using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MiniFileExplorer.Models;

namespace MiniFileExplorer.Services;

public class SearchService : ISearchService
{
    public async IAsyncEnumerable<FileSystemItem> SearchAsync(
        string rootPath, 
        string query, 
        IProgress<SearchProgressReport>? progress = null, 
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath) || string.IsNullOrWhiteSpace(query))
            yield break;

        var channel = Channel.CreateBounded<FileSystemItem>(new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = true
        });

        // Run entire directory crawling in a background worker task off UI thread
        _ = Task.Run(async () =>
        {
            var patternRegex = CreatePatternRegex(query.Trim());
            var queue = new Queue<string>();
            queue.Enqueue(rootPath);

            int scannedFolders = 0;
            int scannedFiles = 0;
            int matchesCount = 0;
            var lastReportTime = DateTime.UtcNow;

            try
            {
                while (queue.Count > 0)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var currentDir = queue.Dequeue();
                    scannedFolders++;

                    if ((DateTime.UtcNow - lastReportTime).TotalMilliseconds > 150)
                    {
                        progress?.Report(new SearchProgressReport(scannedFolders, scannedFiles, matchesCount, currentDir));
                        lastReportTime = DateTime.UtcNow;
                    }

                    DirectoryInfo dirInfo;
                    try
                    {
                        dirInfo = new DirectoryInfo(currentDir);
                    }
                    catch
                    {
                        continue;
                    }

                    // Enumerate files
                    try
                    {
                        foreach (var file in dirInfo.EnumerateFiles())
                        {
                            if (cancellationToken.IsCancellationRequested)
                                break;

                            scannedFiles++;

                            bool isMatch;
                            try
                            {
                                isMatch = patternRegex.IsMatch(file.Name);
                            }
                            catch
                            {
                                isMatch = file.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase);
                            }

                            if (isMatch)
                            {
                                matchesCount++;
                                var matchItem = new FileSystemItem
                                {
                                    Name = file.Name,
                                    FullPath = file.FullName,
                                    IsDirectory = false,
                                    SizeBytes = file.Length,
                                    ModifiedDate = file.LastWriteTime,
                                    Extension = file.Extension.ToLowerInvariant()
                                };

                                await channel.Writer.WriteAsync(matchItem, cancellationToken);
                            }
                        }
                    }
                    catch
                    {
                        // Inaccessible folder or disappeared files
                    }

                    // Enumerate subdirectories
                    try
                    {
                        foreach (var subDir in dirInfo.EnumerateDirectories())
                        {
                            if (cancellationToken.IsCancellationRequested)
                                break;

                            // Skip reparse points (symlinks / junctions)
                            if ((subDir.Attributes & FileAttributes.ReparsePoint) != 0)
                                continue;

                            if (patternRegex.IsMatch(subDir.Name))
                            {
                                matchesCount++;
                                var dirMatch = new FileSystemItem
                                {
                                    Name = subDir.Name,
                                    FullPath = subDir.FullName,
                                    IsDirectory = true,
                                    SizeBytes = null,
                                    ModifiedDate = subDir.LastWriteTime,
                                    Extension = null
                                };

                                await channel.Writer.WriteAsync(dirMatch, cancellationToken);
                            }

                            queue.Enqueue(subDir.FullName);
                        }
                    }
                    catch
                    {
                        // Inaccessible directory
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled
            }
            finally
            {
                progress?.Report(new SearchProgressReport(scannedFolders, scannedFiles, matchesCount, "Завершено"));
                channel.Writer.Complete();
            }
        }, cancellationToken);

        // Read results from channel
        while (await channel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (channel.Reader.TryRead(out var item))
            {
                yield return item;
            }
        }
    }

    private static Regex CreatePatternRegex(string pattern)
    {
        if (pattern.Contains('*') || pattern.Contains('?'))
        {
            var regexPattern = "^" + Regex.Escape(pattern)
                .Replace(@"\*", ".*")
                .Replace(@"\?", ".") + "$";
            return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return new Regex(Regex.Escape(pattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
