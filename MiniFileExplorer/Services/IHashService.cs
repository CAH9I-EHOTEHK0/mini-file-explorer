using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MiniFileExplorer.Services;

public interface IHashService
{
    Task<string> ComputeSha256Async(string filePath, IProgress<double>? progress = null, CancellationToken ct = default);
}

public class HashService : IHashService
{
    public async Task<string> ComputeSha256Async(string filePath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Файл не знайдено", filePath);

        const int bufferSize = 1024 * 1024; // 1 MB buffer
        var buffer = new byte[bufferSize];
        long totalBytesRead = 0;
        var fileInfo = new FileInfo(filePath);
        long fileLength = fileInfo.Length;

        using var sha256 = SHA256.Create();
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
            totalBytesRead += bytesRead;

            if (fileLength > 0 && progress != null)
            {
                progress.Report((double)totalBytesRead / fileLength * 100.0);
            }
        }

        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var hashBytes = sha256.Hash ?? Array.Empty<byte>();
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}