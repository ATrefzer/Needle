using System.IO;

namespace Needle.Models;

public class SearchResult
{
    public string FilePath { get; set; } = string.Empty;
    public IReadOnlyList<MatchLine> Matches { get; set; } = new List<MatchLine>();
    public int MatchCount => Matches?.Count ?? 0;
    public string FileName => Path.GetFileName(FilePath);
}