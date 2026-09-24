using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace MiniFileExplorer.Services;

public record BookmarkDto(string Name, string Path, string IconGlyph);

public interface IBookmarkService
{
    Task<List<BookmarkDto>> LoadBookmarksAsync();
    Task SaveBookmarksAsync(IEnumerable<BookmarkDto> bookmarks);
}

public class BookmarkService : IBookmarkService
{
    private readonly string _storagePath;

    public BookmarkService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "MiniFileExplorer");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        _storagePath = Path.Combine(dir, "bookmarks.json");
    }

    public async Task<List<BookmarkDto>> LoadBookmarksAsync()
    {
        try
        {
            if (!File.Exists(_storagePath))
                return new List<BookmarkDto>();

            var json = await File.ReadAllTextAsync(_storagePath);
            return JsonSerializer.Deserialize<List<BookmarkDto>>(json) ?? new List<BookmarkDto>();
        }
        catch
        {
            return new List<BookmarkDto>();
        }
    }

    public async Task SaveBookmarksAsync(IEnumerable<BookmarkDto> bookmarks)
    {
        try
        {
            var json = JsonSerializer.Serialize(bookmarks, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_storagePath, json);
        }
        catch
        {
            // Suppress error
        }
    }
}