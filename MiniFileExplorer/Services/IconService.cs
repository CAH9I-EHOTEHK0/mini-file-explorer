using System;
using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace MiniFileExplorer.Services;

public static class IconService
{
    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new();

    public static Bitmap? LoadBitmap(string relativeAssetPath)
    {
        return Cache.GetOrAdd(relativeAssetPath, path =>
        {
            try
            {
                var uri = new Uri($"avares://MiniFileExplorer/Assets/Icons/{path}");
                if (AssetLoader.Exists(uri))
                {
                    return new Bitmap(AssetLoader.Open(uri));
                }
            }
            catch
            {
                // Fallback to null if icon missing or fails to load
            }
            return null;
        });
    }

    public static Bitmap? GetIconForFile(string? extension, bool isDirectory, bool isParentDir = false, bool isDrive = false)
    {
        if (isDrive)
        {
            return LoadBitmap("icons8-ssd-64.png");
        }

        if (isParentDir)
        {
            return LoadBitmap("icons8-opened-folder-64.png");
        }

        if (isDirectory)
        {
            return LoadBitmap("icons8-folder-64.png");
        }

        var ext = extension?.ToLowerInvariant();
        var iconFile = ext switch
        {
            ".txt" or ".rtf" => "icons8-text-file-64.png",
            ".log" => "icons8-log-64.png",
            ".doc" or ".docx" or ".odt" => "icons8-doc-64.png",
            ".pdf" => "icons8-pdf-64.png",
            ".xls" or ".xlsx" or ".csv" => "icons8-google-sheets-64.png",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" or ".ico" => "icons8-image-64.png",
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" => "icons8-video-file-64.png",
            ".mp3" or ".wav" or ".flac" or ".ogg" or ".aac" or ".m4a" => "icons8-audio-file-64.png",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "icons8-archive-folder-64.png",
            ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1" => "icons8-exe-64.png",
            ".cs" or ".js" or ".ts" or ".py" or ".json" or ".xml" or ".axaml" or ".html" or ".css" or ".cpp" or ".h" or ".c" => "icons8-code-64.png",
            ".bin" or ".dat" or ".dll" or ".iso" => "icons8-binary-file-64.png",
            _ => "icons8-file-64.png"
        };

        return LoadBitmap(iconFile);
    }
}
