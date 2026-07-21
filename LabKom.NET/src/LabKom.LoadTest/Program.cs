using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LabKom.Shared.Discovery;
using LabKom.Shared.Contracts;
using LabKom.Shared.Hub;
using LabKom.Shared.Security;
using Microsoft.AspNetCore.SignalR.Client;

namespace LabKom.LoadTest;

internal static class Program
{
    private const string RootSecretEnvironment = "LABKOM_LOADTEST_ROOT_SECRET";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = LoadOptions.Parse(args);
            var rootSecret = Environment.GetEnvironmentVariable(RootSecretEnvironment);
            if (!HubSecurity.IsStrongSecret(rootSecret))
                throw new InvalidOperationException(
                    $"Set {RootSecretEnvironment} dengan root secret minimal 32 karakter.");

            options = await ResolveEndpointAsync(options, rootSecret!);

            var statistics = new LoadStatistics();
            using var cancellation = new CancellationTokenSource(
                TimeSpan.FromSeconds(options.DurationSeconds));
            var started = DateTimeOffset.UtcNow;
            var tasks = Enumerable.Range(1, options.Clients)
                .Select(index => RunClientAsync(
                    index,
                    options,
                    rootSecret!,
                    statistics,
                    cancellation.Token))
                .ToArray();
            await Task.WhenAll(tasks);
            var ended = DateTimeOffset.UtcNow;

            var report = statistics.BuildReport(options, started, ended);
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true,
            });
            var output = Path.GetFullPath(options.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllTextAsync(output, json);
            Console.WriteLine(json);
            Console.WriteLine($"Report: {output}");
            return report.Passed ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Load test gagal: {exception.Message}");
            Console.Error.WriteLine(LoadOptions.Usage);
            return 1;
        }
    }
    private static async Task<LoadOptions> ResolveEndpointAsync(
        LoadOptions options,
        string rootSecret)
    {
        if (!string.IsNullOrWhiteSpace(options.HubUrl)) return options;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var udp = new UdpClient();
        udp.Client.SetSocketOption(
            SocketOptionLevel.Socket,
            SocketOptionName.ReuseAddress,
            true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryProtocol.Port));
        Console.WriteLine(
            $"Menunggu discovery Teacher autentik di UDP {DiscoveryProtocol.Port}...");

        while (true)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "Discovery Teacher tidak diterima dalam 15 detik. " +
                    "Berikan --hub-url dan --certificate-sha256 secara eksplisit.");
            }

            try
            {
                var beacon = JsonSerializer.Deserialize<DiscoveryBeacon>(
                    Encoding.UTF8.GetString(result.Buffer));
                if (beacon is null || !beacon.IsAuthentic(rootSecret)) continue;

                var hubUrl =
                    $"https://{beacon.Ip}:{beacon.HubPort}{HubRoutes.TeacherHubPath}";
                Console.WriteLine($"Teacher ditemukan: {hubUrl}");
                return options with
                {
                    HubUrl = hubUrl,
                    CertificateSha256 = beacon.CertificateSha256,
                };
            }
            catch (JsonException)
            {
                // Abaikan broadcast lain pada port discovery.
            }
        }
    }


    private static async Task RunClientAsync(
        int index,
        LoadOptions options,
        string rootSecret,
        LoadStatistics statistics,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds((index - 1) * options.RampMilliseconds),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        var pcName = $"LOAD-PC-{index:00}";
        var deviceId = Guid.NewGuid().ToString("N");
        var deviceSecret = DeviceCredentialStore.DeriveSecret(
            rootSecret,
            options.ClassroomId,
            deviceId,
            pcName,
            options.KeyVersion);
        var connectionUrl = HubRoutes.BuildClientUrl(
            options.HubUrl,
            HubRoutes.Roles.Agent,
            pcName);
        var connection = new HubConnectionBuilder()
            .WithUrl(connectionUrl, connectionOptions =>
            {
                connectionOptions.Headers[HubSecurity.HeaderName] = deviceSecret;
                connectionOptions.Headers[HubSecurity.PcNameHeaderName] = pcName;
                connectionOptions.Headers[HubSecurity.DeviceIdHeaderName] = deviceId;
                connectionOptions.Headers[HubSecurity.KeyVersionHeaderName] =
                    options.KeyVersion.ToString(CultureInfo.InvariantCulture);
                connectionOptions.HttpMessageHandlerFactory = handler =>
                {
                    if (handler is HttpClientHandler httpHandler)
                    {
                        httpHandler.ServerCertificateCustomValidationCallback =
                            (_, certificate, _, _) => CertificatePin.Matches(
                                certificate,
                                options.CertificateSha256);
                    }
                    return handler;
                };
                connectionOptions.WebSocketConfiguration = webSocket =>
                    webSocket.RemoteCertificateValidationCallback =
                        (_, certificate, _, _) => CertificatePin.Matches(
                            certificate,
                            options.CertificateSha256);
            })
            .Build();

        try
        {
            var connectTimer = Stopwatch.StartNew();
            await connection.StartAsync(cancellationToken);
            connectTimer.Stop();
            statistics.Connected(connectTimer.Elapsed.TotalMilliseconds);
            await connection.InvokeAsync(
                HubRoutes.Methods.Hello,
                StudentPresence.Snapshot(
                    pcName,
                    $"02:00:00:00:{index / 256:X2}:{index % 256:X2}",
                    $"10.254.{index / 254}.{index % 254 + 1}",
                    StudentStatus.Online),
                cancellationToken);

            long sequence = 0;
            var random = new Random(index * 7919);
            while (!cancellationToken.IsCancellationRequested)
            {
                sequence++;
                var sample = SimulatedSample(
                    pcName,
                    sequence,
                    index,
                    random);
                var timer = Stopwatch.StartNew();
                await connection.InvokeAsync(
                    HubRoutes.Methods.PushDeviceTelemetry,
                    sample,
                    cancellationToken);
                timer.Stop();
                statistics.SampleSent(timer.Elapsed.TotalMilliseconds);

                if (sequence % Math.Max(1, 5 / options.IntervalSeconds) == 0)
                {
                    await connection.InvokeAsync(
                        HubRoutes.Methods.Heartbeat,
                        StudentPresence.Snapshot(
                            pcName,
                            $"02:00:00:00:{index / 256:X2}:{index % 256:X2}",
                            $"10.254.{index / 254}.{index % 254 + 1}",
                            StudentStatus.Online),
                        cancellationToken);
                }
                await Task.Delay(
                    TimeSpan.FromSeconds(options.IntervalSeconds),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Durasi uji selesai.
        }
        catch (Exception exception)
        {
            var failure = exception.GetBaseException();
            statistics.Failed(
                pcName, $"{failure.GetType().Name}:{failure.Message}");
        }
        finally
        {
            try
            {
                await connection.DisposeAsync();
            }
            catch (Exception exception)
            {
                statistics.Failed(pcName, $"Dispose:{exception.GetType().Name}");
            }
        }
    }

    private static DeviceTelemetry SimulatedSample(
        string pcName,
        long sequence,
        int clientIndex,
        Random random)
    {
        const long gibibyte = 1024L * 1024 * 1024;
        var cpu = Math.Clamp(
            25 + 20 * Math.Sin((sequence + clientIndex) / 10d) + random.NextDouble() * 5,
            0,
            100);
        return new DeviceTelemetry(
            pcName,
            sequence,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            3_600 + sequence,
            cpu,
            6 * gibibyte + random.Next(0, 512) * 1024L * 1024,
            16 * gibibyte,
            180 * gibibyte,
            500 * gibibyte,
            random.Next(128, 4096) * 1024L,
            random.Next(64, 1024) * 1024L,
            128 * 1024L * 1024,
            24);
    }
}

internal sealed record LoadOptions(
    string HubUrl,
    string ClassroomId,
    string CertificateSha256,
    int Clients,
    int DurationSeconds,
    int IntervalSeconds,
    int KeyVersion,
    int RampMilliseconds,
    int MaximumP95LatencyMs,
    string OutputPath)
{
    public const string Usage =
        "LabKom.LoadTest --classroom-id <32hex> " +
        "[--hub-url https://teacher:41235/hubs/teacher " +
        "--certificate-sha256 <64hex>] " +
        "[--clients 40] [--duration-seconds 60] [--interval-seconds 2] " +
        "[--key-version 1] [--ramp-ms 100] [--max-p95-ms 1500] [--output report.json]";

    public static LoadOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal)
                || index + 1 >= args.Length)
                throw new ArgumentException($"Argumen tidak valid: {args[index]}");
            values[args[index]] = args[index + 1];
        }

        var hubUrl = values.GetValueOrDefault("--hub-url", string.Empty).Trim();
        var certificate = values
            .GetValueOrDefault("--certificate-sha256", string.Empty)
            .Trim();
        if (string.IsNullOrWhiteSpace(hubUrl) != string.IsNullOrWhiteSpace(certificate))
            throw new ArgumentException(
                "--hub-url dan --certificate-sha256 wajib diberikan bersama.");
        if (!string.IsNullOrWhiteSpace(hubUrl)
            && (!Uri.TryCreate(hubUrl, UriKind.Absolute, out var hubUri)
                || hubUri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("--hub-url wajib URL HTTPS absolut.");
        if (!string.IsNullOrWhiteSpace(certificate)
            && !CertificatePin.IsValid(certificate))
            throw new ArgumentException("--certificate-sha256 wajib 64 karakter hex.");

        var classroomId = Required(values, "--classroom-id");
        if (!Guid.TryParseExact(classroomId, "N", out _))
            throw new ArgumentException("--classroom-id wajib GUID format N.");

        return new LoadOptions(
            hubUrl,
            classroomId,
            certificate,
            Integer(values, "--clients", 40, 5, 40),
            Integer(values, "--duration-seconds", 60, 10, 86_400),
            Integer(values, "--interval-seconds", 2, 1, 60),
            Integer(values, "--key-version", 1, 1, 100_000),
            Integer(values, "--ramp-ms", 100, 0, 5_000),
            Integer(values, "--max-p95-ms", 1_500, 100, 30_000),
            values.GetValueOrDefault(
                "--output",
                $"telemetry-load-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"));
    }

    private static string Required(
        IReadOnlyDictionary<string, string> values,
        string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"{name} wajib diberikan.");

    private static int Integer(
        IReadOnlyDictionary<string, string> values,
        string name,
        int fallback,
        int minimum,
        int maximum)
    {
        if (!values.TryGetValue(name, out var raw)) return fallback;
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || value < minimum
            || value > maximum)
            throw new ArgumentException($"{name} harus {minimum}-{maximum}.");
        return value;
    }
}

internal sealed class LoadStatistics
{
    private readonly ConcurrentBag<double> _connectLatencies = new();
    private readonly ConcurrentBag<double> _sampleLatencies = new();
    private readonly ConcurrentQueue<string> _errors = new();
    private long _connected;
    private long _samples;

    public void Connected(double latencyMs)
    {
        Interlocked.Increment(ref _connected);
        _connectLatencies.Add(latencyMs);
    }

    public void SampleSent(double latencyMs)
    {
        Interlocked.Increment(ref _samples);
        _sampleLatencies.Add(latencyMs);
    }

    public void Failed(string pcName, string error)
    {
        const int maximumErrorLength = 512;
        var bounded = error.Length <= maximumErrorLength
            ? error
            : error[..maximumErrorLength];
        _errors.Enqueue($"{pcName}:{bounded}");
    }

    public LoadReport BuildReport(
        LoadOptions options,
        DateTimeOffset started,
        DateTimeOffset ended)
    {
        var connected = Interlocked.Read(ref _connected);
        var samples = Interlocked.Read(ref _samples);
        var expected = (long)Math.Floor(
            options.Clients * options.DurationSeconds / (double)options.IntervalSeconds * 0.75);
        var p95 = Percentile(_sampleLatencies, 0.95);
        var passed = connected == options.Clients
                     && samples >= expected
                     && _errors.IsEmpty
                     && p95 <= options.MaximumP95LatencyMs;
        return new LoadReport(
            started,
            ended,
            options.Clients,
            connected,
            samples,
            expected,
            _errors.Count,
            Percentile(_connectLatencies, 0.95),
            p95,
            options.MaximumP95LatencyMs,
            _errors.ToArray(),
            passed);
    }

    private static double Percentile(IEnumerable<double> values, double percentile)
    {
        var ordered = values.Order().ToArray();
        if (ordered.Length == 0) return 0;
        return ordered[(int)Math.Ceiling(ordered.Length * percentile) - 1];
    }
}

internal sealed record LoadReport(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    int RequestedClients,
    long ConnectedClients,
    long SamplesSent,
    long MinimumExpectedSamples,
    int Errors,
    double P95ConnectLatencyMs,
    double P95SampleRoundTripMs,
    int MaximumP95LatencyMs,
    IReadOnlyList<string> ErrorDetails,
    bool Passed);
