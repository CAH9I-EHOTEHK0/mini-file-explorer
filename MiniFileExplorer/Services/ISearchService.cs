using System;
using System.Collections.Generic;
using System.Threading;
using MiniFileExplorer.Models;

namespace MiniFileExplorer.Services;

public record SearchProgressReport(int ScannedFolders, int ScannedFiles, int MatchesCount, string CurrentFolder);

public interface ISearchService
{
    IAsyncEnumerable<FileSystemItem> SearchAsync(
        string rootPath, 
        string query, 
        IProgress<SearchProgressReport>? progress = null, 
        CancellationToken cancellationToken = default);
}
