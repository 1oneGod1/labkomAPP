using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using LabKom.Shared.Contracts;
using LabKom.Shared.Devices;
using Microsoft.Extensions.Logging;

namespace LabKom.Student.Desktop.Services;

/// <summary>
/// Pengumpulan satu file yang transparan dan dibatasi. Pengguna harus menyetujui
/// setiap permintaan; path sistem, wildcard, symlink, dan material kunci ditolak.
/// </summary>
public sealed class FileCollectionClient
{
    private static readonly HashSet<string> DeniedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".pfx", ".p12", ".pem", ".key", ".kdbx", ".rdp",
        };

    private readonly MachineIdentity _identity;
    private readonly ILogger<FileCollectionClient> _logger;

    public FileCollectionClient(
        MachineIdentity identity,
        ILogger<FileCollectionClient> logger)
    {
        _identity = identity;
        _logger = logger;
    }

    public async Task CollectAsync(
        FileCollectionRequest request,
        Func<FileCollectionChunk, CancellationToken, Task> send,
        CancellationToken cancellationToken)
    {
        if (!ContractValidation.IsValidFileCollectionRequest(
                request,
                _identity.PcName))
        {
            return;
        }

        var fileName = Path.GetFileName(request.RelativePath);
        var sequence = 1;
        long total = 0;
        try
        {
            if (!Confirm(request))
            {
                await SendTerminalAsync(
                    request,
                    fileName,
                    FileCollectionState.Rejected,
                    0,
                    1,
                    null,
                    "Ditolak oleh pengguna siswa.",
                    send,
                    cancellationToken);
                return;
            }

            var path = ResolvePath(request);
            var info = new FileInfo(path);
            if (!info.Exists)
                throw new FileNotFoundException("File tidak ditemukan.");
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Symlink/reparse point tidak diizinkan.");
            if (DeniedExtensions.Contains(info.Extension))
                throw new InvalidDataException("Tipe file sensitif tidak dapat dikumpulkan.");
            if (info.Length > request.MaximumBytes)
                throw new InvalidDataException("Ukuran file melebihi batas permintaan.");

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                ContractValidation.MaximumFileCollectionChunkBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(
                ContractValidation.MaximumFileCollectionChunkBytes);
            try
            {
                while (true)
                {
                    var read = await stream.ReadAsync(
                        buffer.AsMemory(
                            0,
                            ContractValidation.MaximumFileCollectionChunkBytes),
                        cancellationToken);
                    if (read == 0) break;
                    total += read;
                    if (total > request.MaximumBytes)
                        throw new InvalidDataException(
                            "File berubah dan melewati batas saat dibaca.");

                    hash.AppendData(buffer, 0, read);
                    var payload = buffer.AsSpan(0, read).ToArray();
                    await send(
                        new FileCollectionChunk(
                            request.RequestId,
                            _identity.PcName,
                            FileCollectionState.Collecting,
                            sequence++,
                            total,
                            fileName,
                            payload,
                            null,
                            null,
                            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                        cancellationToken);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }

            await SendTerminalAsync(
                request,
                fileName,
                FileCollectionState.Completed,
                total,
                sequence,
                Convert.ToHexString(hash.GetHashAndReset()),
                null,
                send,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "File collect {RequestId} gagal",
                request.RequestId);
            await SendTerminalAsync(
                request,
                fileName,
                FileCollectionState.Failed,
                total,
                sequence,
                null,
                exception is FileNotFoundException
                    ? "File tidak ditemukan."
                    : exception.Message,
                send,
                cancellationToken);
        }
    }

    private static bool Confirm(FileCollectionRequest request) =>
        Application.Current.Dispatcher.Invoke(() =>
            MessageBox.Show(
                $"Guru meminta file berikut:\n\n" +
                $"{request.Root}\\{request.RelativePath}\n\n" +
                "File akan dikirim ke folder pengumpulan Teacher. Izinkan?",
                "Permintaan Pengumpulan File",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No) == MessageBoxResult.Yes);

    private static string ResolvePath(FileCollectionRequest request)
    {
        var root = request.Root switch
        {
            FileCollectionRoot.Desktop =>
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            FileCollectionRoot.Documents =>
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            FileCollectionRoot.Downloads =>
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads"),
            _ => throw new InvalidDataException("Root file tidak valid."),
        };
        var rootFull = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(
            Path.Combine(rootFull, request.RelativePath));
        if (!candidate.StartsWith(
                rootFull.TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Path keluar dari folder yang diizinkan.");
        }

        return candidate;
    }

    private Task SendTerminalAsync(
        FileCollectionRequest request,
        string fileName,
        FileCollectionState state,
        long total,
        int sequence,
        string? sha256,
        string? message,
        Func<FileCollectionChunk, CancellationToken, Task> send,
        CancellationToken cancellationToken) =>
        send(
            new FileCollectionChunk(
                request.RequestId,
                _identity.PcName,
                state,
                sequence,
                total,
                fileName,
                Array.Empty<byte>(),
                sha256,
                message is null
                    ? null
                    : message.Length <= ContractValidation.MaximumCommandMessageLength
                        ? message
                        : message[..ContractValidation.MaximumCommandMessageLength],
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            cancellationToken);
}
