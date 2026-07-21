using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using LabKom.Shared.Contracts;
using LabKom.Shared.Hub;
using LabKom.Shared.Security;
using LabKom.Teacher.Hub;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace LabKom.Teacher.Services;

public sealed class FileCollectionService
{
    private const int MaximumPendingRequests = 64;
    private readonly ConcurrentDictionary<string, PendingCollection> _pending =
        new(StringComparer.Ordinal);
    private readonly HubContextHolder _holder;
    private readonly TeacherAuthorizationService _authorization;
    private readonly ActivityFeed _activity;
    private readonly ILogger<FileCollectionService> _logger;
    private readonly string _root;

    public event EventHandler<FileCollectionUpdate>? StatusChanged;

    public FileCollectionService(
        HubContextHolder holder,
        TeacherAuthorizationService authorization,
        ActivityFeed activity,
        ILogger<FileCollectionService> logger)
    {
        _holder = holder;
        _authorization = authorization;
        _activity = activity;
        _logger = logger;
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LabKom",
            "CollectedFiles");
    }

    private IHubContext<TeacherHub> Hub =>
        _holder.HubContext
        ?? throw new InvalidOperationException("Hub belum siap.");

    public async Task<FileCollectionRequest> RequestAsync(
        string pcName,
        FileCollectionRoot root,
        string relativePath,
        long maximumBytes = ContractValidation.MaximumFileCollectionBytes)
    {
        await CleanupExpiredAsync();
        if (_pending.Count >= MaximumPendingRequests)
            throw new InvalidOperationException(
                "Terlalu banyak permintaan file collect yang masih berjalan.");

        var now = DateTimeOffset.UtcNow;
        var request = new FileCollectionRequest(
            Guid.NewGuid().ToString("N"),
            pcName,
            root,
            relativePath.Trim(),
            maximumBytes,
            now.ToUnixTimeMilliseconds(),
            now.AddMinutes(3).ToUnixTimeMilliseconds());
        if (!ContractValidation.IsValidFileCollectionRequest(
                request,
                pcName,
                now.ToUnixTimeMilliseconds()))
        {
            throw new ArgumentException(
                "Root, nama PC, path relatif, atau batas file tidak valid.");
        }

        var pendingRoot = Path.Combine(_root, ".pending");
        Directory.CreateDirectory(pendingRoot);
        var state = new PendingCollection(
            request,
            Path.Combine(pendingRoot, request.RequestId + ".part"));
        if (!_pending.TryAdd(request.RequestId, state))
            throw new InvalidOperationException("ID permintaan file collect duplikat.");

        try
        {
            await _authorization.ExecuteAsync(
                TeacherPermission.CollectFiles,
                "file.collect.request",
                pcName,
                () => Hub.Clients
                    .Group(HubRoutes.Groups.ForPcRole(
                        pcName,
                        HubRoutes.Roles.Desktop))
                    .SendAsync(
                        HubRoutes.Methods.ReceiveFileCollectionRequest,
                        request));
            Publish(
                request,
                FileCollectionState.Collecting,
                0,
                "Menunggu persetujuan siswa.",
                null);
            return request;
        }
        catch
        {
            _pending.TryRemove(request.RequestId, out _);
            await state.DisposeAndDeleteAsync();
            throw;
        }
    }

    public async Task<bool> AcceptChunkAsync(
        FileCollectionChunk chunk,
        string connectedPcName,
        CancellationToken cancellationToken = default)
    {
        if (!ContractValidation.IsValidFileCollectionChunk(
                chunk,
                connectedPcName)
            || !_pending.TryGetValue(chunk.RequestId, out var state)
            || !string.Equals(
                state.Request.TargetPcName,
                connectedPcName,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        await state.Gate.WaitAsync(cancellationToken);
        try
        {
            if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                > state.Request.ExpiresAtUnixMs)
            {
                await FailAndRemoveAsync(
                    state,
                    FileCollectionState.Failed,
                    "Permintaan kedaluwarsa.");
                return false;
            }

            var expectedFileName = Path.GetFileName(
                state.Request.RelativePath);
            if (chunk.SequenceNumber != state.NextSequence
                || !string.Equals(
                    chunk.FileName,
                    expectedFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                await FailAndRemoveAsync(
                    state,
                    FileCollectionState.Failed,
                    "Urutan chunk atau nama file tidak cocok.");
                return false;
            }

            if (chunk.State == FileCollectionState.Collecting)
            {
                var nextTotal = state.ReceivedBytes + chunk.Data.Length;
                if (chunk.TotalBytes != nextTotal
                    || nextTotal > state.Request.MaximumBytes)
                {
                    await FailAndRemoveAsync(
                        state,
                        FileCollectionState.Failed,
                        "Ukuran chunk tidak konsisten.");
                    return false;
                }

                await state.Stream.WriteAsync(
                    chunk.Data,
                    cancellationToken);
                state.Hash.AppendData(chunk.Data);
                state.ReceivedBytes = nextTotal;
                state.NextSequence++;
                Publish(
                    state.Request,
                    FileCollectionState.Collecting,
                    state.ReceivedBytes,
                    "Menerima file...",
                    null);
                return true;
            }

            if (chunk.State == FileCollectionState.Completed)
            {
                var hash = Convert.ToHexString(
                    state.Hash.GetHashAndReset());
                if (chunk.TotalBytes != state.ReceivedBytes
                    || !CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(hash),
                        Convert.FromHexString(chunk.Sha256!)))
                {
                    await FailAndRemoveAsync(
                        state,
                        FileCollectionState.Failed,
                        "Hash atau ukuran akhir tidak cocok.");
                    return false;
                }

                await state.Stream.FlushAsync(cancellationToken);
                await state.Stream.DisposeAsync();
                state.StreamClosed = true;
                var destination = BuildDestination(
                    state.Request,
                    chunk.FileName);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(state.TemporaryPath, destination, overwrite: false);
                _pending.TryRemove(chunk.RequestId, out _);
                state.Hash.Dispose();
                Publish(
                    state.Request,
                    FileCollectionState.Completed,
                    state.ReceivedBytes,
                    "File terkumpul dan hash terverifikasi.",
                    destination);
                AuditAndFeed(
                    state.Request,
                    FileCollectionState.Completed,
                    state.ReceivedBytes,
                    destination);
                return true;
            }

            await FailAndRemoveAsync(
                state,
                chunk.State,
                chunk.Message ?? "Permintaan tidak diselesaikan.");
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Pemrosesan file collect {RequestId} gagal",
                chunk.RequestId);
            await FailAndRemoveAsync(
                state,
                FileCollectionState.Failed,
                exception.GetType().Name);
            return false;
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private async Task CleanupExpiredAsync()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var pair in _pending.ToArray())
        {
            var state = pair.Value;
            if (state.Request.ExpiresAtUnixMs >= now)
            {
                continue;
            }

            await state.Gate.WaitAsync();
            try
            {
                if (!_pending.TryRemove(pair.Key, out _))
                {
                    continue;
                }

                await state.DisposeAndDeleteAsync();
                Publish(
                    state.Request,
                    FileCollectionState.Failed,
                    state.ReceivedBytes,
                    "Permintaan kedaluwarsa tanpa respons siswa.",
                    null);
                AuditAndFeed(
                    state.Request,
                    FileCollectionState.Failed,
                    state.ReceivedBytes,
                    "timeout");
            }
            finally
            {
                state.Gate.Release();
            }
        }
    }


    public string CollectedFilesPath => _root;

    private string BuildDestination(
        FileCollectionRequest request,
        string fileName)
    {
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        return Path.Combine(
            _root,
            date,
            request.TargetPcName,
            request.RequestId + "-" + fileName);
    }

    private async Task FailAndRemoveAsync(
        PendingCollection state,
        FileCollectionState status,
        string message)
    {
        _pending.TryRemove(state.Request.RequestId, out _);
        await state.DisposeAndDeleteAsync();
        Publish(state.Request, status, state.ReceivedBytes, message, null);
        AuditAndFeed(
            state.Request,
            status,
            state.ReceivedBytes,
            message);
    }

    private void Publish(
        FileCollectionRequest request,
        FileCollectionState state,
        long bytes,
        string message,
        string? savedPath) =>
        StatusChanged?.Invoke(
            this,
            new FileCollectionUpdate(
                request.RequestId,
                request.TargetPcName,
                state,
                bytes,
                message,
                savedPath,
                DateTimeOffset.UtcNow));

    private void AuditAndFeed(
        FileCollectionRequest request,
        FileCollectionState state,
        long bytes,
        string detail)
    {
        _authorization.RecordSystemEvent(
            "file.collect.result",
            request.TargetPcName,
            state.ToString().ToLowerInvariant(),
            $"{request.RequestId}|{bytes}|{detail}");
        _activity.Push(
            new ActivityRecord(
                request.TargetPcName,
                ActivityRecordKind.FileCollected,
                $"File collect: {state} ({bytes} byte)",
                Path.GetFileName(request.RelativePath),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    private sealed class PendingCollection
    {
        public PendingCollection(
            FileCollectionRequest request,
            string temporaryPath)
        {
            Request = request;
            TemporaryPath = temporaryPath;
            Stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                ContractValidation.MaximumFileCollectionChunkBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }

        public FileCollectionRequest Request { get; }
        public string TemporaryPath { get; }
        public FileStream Stream { get; }
        public IncrementalHash Hash { get; } =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int NextSequence { get; set; } = 1;
        public long ReceivedBytes { get; set; }
        public bool StreamClosed { get; set; }

        public async Task DisposeAndDeleteAsync()
        {
            if (!StreamClosed)
            {
                await Stream.DisposeAsync();
                StreamClosed = true;
            }
            Hash.Dispose();
            if (File.Exists(TemporaryPath))
                File.Delete(TemporaryPath);
        }
    }
}

public sealed record FileCollectionUpdate(
    string RequestId,
    string PcName,
    FileCollectionState State,
    long BytesReceived,
    string Message,
    string? SavedPath,
    DateTimeOffset TimestampUtc);
