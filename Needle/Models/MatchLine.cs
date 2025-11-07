namespace Needle.Models;

public class MatchLine
{
    public const int MaxDisplayLength = 100;
    public int LineNumber { get; set; }
    public string Text { get; set; } = string.Empty;
    public int StartIndex { get; set; }
    public int Length { get; set; }
    public bool IsSelected { get; set; } = true; // For selective replacement

    private string Truncate()
    {
        return string.Concat("(truncated) ... ",
            Text.AsSpan(StartIndex, Math.Min(MaxDisplayLength, Text.Length - StartIndex)));
    }
    
    public string SafeText => Text.Length < MaxDisplayLength ? Text : Truncate();
}