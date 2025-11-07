namespace Needle.Models;

public class MatchLine
{
    public int LineNumber { get; set; }
    public string Text { get; set; } = string.Empty;
    public int StartIndex { get; set; }
    public int Length { get; set; }
    public bool IsSelected { get; set; } = true; // For selective replacement
}