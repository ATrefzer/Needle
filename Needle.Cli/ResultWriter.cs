using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using Needle.Models;

namespace Needle.Cli;

/// <summary>
///     Writes the results in grep format: path:line:text. Matches in file names are written as the path only.
/// </summary>
internal sealed class ResultWriter
{
    private const string PathColor = "\e[35m";
    private const string LineNumberColor = "\e[32m";
    private const string MatchColor = "\e[1;31m";
    private const string Reset = "\e[0m";

    // ASCII, the Windows console code pages have no ellipsis character.
    private const string Ellipsis = "...";

    private readonly OutputOptions _options;
    private readonly bool _isTerminal;
    private readonly bool _useColor;
    private readonly string _currentDirectory = Environment.CurrentDirectory;

    public ResultWriter(OutputOptions options)
    {
        _options = options;
        _isTerminal = !Console.IsOutputRedirected;
        _useColor = options.Color && _isTerminal &&
                    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")) &&
                    VirtualTerminal.Enable();
    }

    public int FileCount { get; private set; }
    public int MatchCount { get; private set; }

    /// <summary>
    ///     The reader of the output stopped, like head. The search is cancelled then.
    /// </summary>
    public bool IsOutputClosed { get; private set; }

    public async Task WriteAllAsync(ChannelReader<SearchResult> results, CancellationTokenSource searchCancellation)
    {
        await using var writer = new StreamWriter(Console.OpenStandardOutput(), OutputEncoding(), 64 * 1024);
        try
        {
            if (_options.Sort)
            {
                var all = new List<SearchResult>();
                await foreach (var result in results.ReadAllAsync())
                {
                    all.Add(result);
                }

                foreach (var result in all.OrderBy(DisplayPath, StringComparer.Ordinal))
                {
                    Write(writer, result);
                }

                await writer.FlushAsync();
                return;
            }

            // Flushed when no result is waiting, so the output appears while searching.
            while (await results.WaitToReadAsync())
            {
                while (results.TryRead(out var result))
                {
                    Write(writer, result);
                }

                await writer.FlushAsync();
            }
        }
        catch (IOException)
        {
            IsOutputClosed = true;
            await searchCancellation.CancelAsync();

            // Drain, so the search does not wait.
            while (await results.WaitToReadAsync())
            {
                while (results.TryRead(out _))
                {
                }
            }
        }
    }

    private void Write(TextWriter writer, SearchResult result)
    {
        FileCount++;
        MatchCount += result.MatchCount;
        var path = DisplayPath(result);

        if (_options.FilesWithMatches)
        {
            WritePath(writer, path);
            writer.WriteLine();
            return;
        }

        if (_options.Count)
        {
            WritePath(writer, path);
            writer.Write(':');
            writer.WriteLine(result.MatchCount);
            return;
        }

        foreach (var match in result.Matches.Where(m => m.IsFileName))
        {
            // The path ends with the file name, the match is highlighted there.
            var nameStart = path.Length - match.Text.Length;
            WriteColored(writer, PathColor, path[..nameStart]);
            WriteHighlighted(writer, match.Text, [match], PathColor, 0, match.Text.Length);
            writer.WriteLine();
        }

        // A line with several matches is written once, all matches are highlighted.
        foreach (var line in result.Matches.Where(m => !m.IsFileName).GroupBy(m => m.LineNumber))
        {
            WritePath(writer, path);
            writer.Write(':');
            WriteColored(writer, LineNumberColor, line.Key.ToString());
            writer.Write(':');
            WriteLine(writer, line.First().Text, line.ToList());
            writer.WriteLine();
        }
    }

    private void WritePath(TextWriter writer, string path)
    {
        WriteColored(writer, PathColor, path);
    }

    /// <summary>
    ///     Long lines are cut with --max-columns.
    /// </summary>
    private void WriteLine(TextWriter writer, string text, IReadOnlyList<MatchLine> matches)
    {
        var window = LineWindow.Create(text, matches, _options.MaxColumns);
        if (window.Start > 0)
        {
            writer.Write(Ellipsis);
        }

        WriteHighlighted(writer, text, matches, null, window.Start, window.End);

        if (window.End < text.Length)
        {
            writer.Write(Ellipsis);
        }

        if (window.HiddenMatches > 0)
        {
            writer.Write(window.HiddenMatches == 1 ? " [+1 match]" : $" [+{window.HiddenMatches} matches]");
        }
    }

    /// <summary>
    ///     Writes text[from..to] with the matches in it highlighted.
    /// </summary>
    private void WriteHighlighted(TextWriter writer, string text, IReadOnlyList<MatchLine> matches, string? color,
        int from, int to)
    {
        var position = from;
        foreach (var match in matches.OrderBy(m => m.StartIndex))
        {
            // Regex matches can be empty or, for safety, overlap. Matches are cut at the end of the window.
            var start = Math.Max(match.StartIndex, position);
            var end = Math.Min(match.StartIndex + match.Length, to);
            if (end <= start)
            {
                continue;
            }

            WriteColored(writer, color, text[position..start]);
            WriteColored(writer, MatchColor, text[start..end]);
            position = end;
        }

        WriteColored(writer, color, text[position..to]);
    }

    private void WriteColored(TextWriter writer, string? color, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (_isTerminal)
        {
            // Control characters of binary files would garble the terminal.
            text = ReplaceControlCharacters(text);
        }

        if (_useColor && color != null)
        {
            writer.Write(color);
            writer.Write(text);
            writer.Write(Reset);
        }
        else
        {
            writer.Write(text);
        }
    }

    private static string ReplaceControlCharacters(string text)
    {
        if (!text.Any(c => char.IsControl(c) && c != '\t'))
        {
            return text;
        }

        return string.Create(text.Length, text, (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                span[i] = char.IsControl(c) && c != '\t' ? '.' : c;
            }
        });
    }

    /// <summary>
    ///     Relative to the current directory if the file is below it, like grep.
    /// </summary>
    private string DisplayPath(SearchResult result)
    {
        var path = result.FilePath;
        var relative = Path.GetRelativePath(_currentDirectory, path);
        var isOutside = relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar) ||
                        Path.IsPathRooted(relative);
        if (!isOutside)
        {
            path = relative;
        }

        return result.IsArchive ? path + "/" + result.ArchiveEntryName : path;
    }

    private static Encoding OutputEncoding()
    {
        // Without BOM, it would be written before the first line.
        var encoding = Console.OutputEncoding;
        return encoding.CodePage == Encoding.UTF8.CodePage ? new UTF8Encoding(false) : encoding;
    }
}

/// <summary>
///     The Windows console needs to be switched to interpret the ANSI color codes.
/// </summary>
internal static partial class VirtualTerminal
{
    private const int StdOutputHandle = -11;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    public static bool Enable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        var handle = GetStdHandle(StdOutputHandle);
        return GetConsoleMode(handle, out var mode) &&
               SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr handle, uint mode);
}
