using System.Diagnostics;
using System.Threading.Channels;
using Needle.Models;
using Needle.Services;

namespace Needle.Cli;

internal static class ExitCodes
{
    // Like grep.
    public const int Success = 0;
    public const int NoMatches = 1;
    public const int Error = 2;
    public const int Cancelled = 130;
}

internal sealed class OutputOptions
{
    public bool FilesWithMatches { get; init; }
    public bool Count { get; init; }
    public bool Sort { get; init; }
    public bool Color { get; init; } = true;
    public bool Stats { get; init; }

    /// <summary>
    ///     0 for whole lines.
    /// </summary>
    public int MaxColumns { get; init; }
}

internal sealed class SearchRunner(SearchParameters parameters, OutputOptions options)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stopwatch = Stopwatch.StartNew();

        // The search raises the events from parallel workers. A single consumer writes the output, so the
        // lines of different files are not mixed.
        var channel = Channel.CreateUnbounded<SearchResult>(new UnboundedChannelOptions { SingleReader = true });
        var service = new FileSearchService();
        service.FileCompleted += (_, result) => channel.Writer.TryWrite(result);

        var writer = new ResultWriter(options);
        var writerTask = writer.WriteAllAsync(channel.Reader, cancellation);

        try
        {
            await service.SearchAsync(parameters, cancellation.Token);
        }
        catch (OperationCanceledException) when (writer.IsOutputClosed)
        {
            // Output piped into a tool that stopped reading, like head. Not an error.
        }
        finally
        {
            channel.Writer.Complete();
            await writerTask;
        }

        if (options.Stats)
        {
            await Console.Error.WriteLineAsync(
                $"{writer.MatchCount} matches in {writer.FileCount} files, " +
                $"skipped {service.SkippedFiles} files and {service.SkippedDirectories} directories, " +
                $"{stopwatch.Elapsed.TotalSeconds:F2} s");
        }

        return writer.FileCount > 0 ? ExitCodes.Success : ExitCodes.NoMatches;
    }
}
