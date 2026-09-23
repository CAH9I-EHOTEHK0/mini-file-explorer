namespace MiniFileExplorer.ViewModels;

public class SidebarItemViewModel : ViewModelBase
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string IconGlyph { get; init; }
    public string? Subtitle { get; init; }
    public bool IsDrive { get; init; }
}
