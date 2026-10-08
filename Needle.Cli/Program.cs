using System.CommandLine;
using System.Text.Json;
using Needle.Cli;
using Needle.Services;

var patternArgument = new Argument<string?>("pattern")
{
    Description = "Text or regular expression to search for. Overrides the pattern of the options file.",
    Arity = ArgumentArity.ZeroOrOne
};
var pathArgument = new Argument<string?>("path")
{
    Description = "Directory to search in. Default: the current directory.",
    Arity = ArgumentArity.ZeroOrOne
};
var maskOption = new Option<string>("--mask", "-m")
{
    Description = "File masks separated by ';', ',' or '|', e.g. \"*.cs;*.xaml\". Default: all files."
};
var regexOption = new Option<bool>("--regex", "-E") { Description = "The pattern is a .NET regular expression." };
var caseSensitiveOption = new Option<bool>("--case-sensitive", "-s") { Description = "Case sensitive search." };
var noRecurseOption = new Option<bool>("--no-recurse") { Description = "Do not search subdirectories." };
var scopeOption = new Option<SearchScope>("--scope")
{
    Description = "Search in file Content, FileName or Both. Default: Content."
};
var encodingOption = new Option<string>("--encoding")
{
    Description = "Encoding of files without BOM: utf8, ansi, a code page number or name. Default: utf8."
};
var optionsFileOption = new Option<FileInfo>("--options", "-o")
{
    Description = "JSON file with the search options. Command line options override it."
};
var saveOptionsOption = new Option<FileInfo>("--save-options")
{
    Description = "Saves the search options to a JSON file instead of searching."
};
var filesWithMatchesOption = new Option<bool>("--files-with-matches", "-l")
{
    Description = "Prints only the paths of files with matches."
};
var countOption = new Option<bool>("--count", "-c") { Description = "Prints the number of matches per file." };
var sortOption = new Option<bool>("--sort") { Description = "Sorts the output by path. Prints nothing until the search is finished." };
var noColorOption = new Option<bool>("--no-color") { Description = "Disables highlighting. Also disabled by NO_COLOR or redirected output." };
var statsOption = new Option<bool>("--stats") { Description = "Prints a summary to the error output." };

var rootCommand = new RootCommand("Needle - fast text search in files.")
{
    patternArgument, pathArgument, maskOption, regexOption, caseSensitiveOption, noRecurseOption, scopeOption,
    encodingOption, optionsFileOption, saveOptionsOption, filesWithMatchesOption, countOption, sortOption,
    noColorOption, statsOption
};

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    try
    {
        // Defaults, overridden by the options file, overridden by the command line.
        var optionsFile = parseResult.GetValue(optionsFileOption);
        var options = optionsFile != null ? SearchOptions.Load(optionsFile.FullName) : new SearchOptions();

        // Options that are not given have an implicit result, too.
        bool IsSet(Option option) => parseResult.GetResult(option) is { Implicit: false };

        if (parseResult.GetValue(patternArgument) is { } pattern)
        {
            options.Pattern = pattern;
        }

        if (parseResult.GetValue(pathArgument) is { } path)
        {
            // Relative to the current directory, not to the options file.
            options.StartDirectory = Path.GetFullPath(path);
        }

        if (IsSet(maskOption))
        {
            options.FileMasks = parseResult.GetValue(maskOption)!;
        }

        if (IsSet(regexOption))
        {
            options.IsRegex = parseResult.GetValue(regexOption);
        }

        if (IsSet(caseSensitiveOption))
        {
            options.IsCaseSensitive = parseResult.GetValue(caseSensitiveOption);
        }

        if (IsSet(noRecurseOption))
        {
            options.IncludeSubdirectories = !parseResult.GetValue(noRecurseOption);
        }

        if (IsSet(scopeOption))
        {
            options.SearchScope = parseResult.GetValue(scopeOption);
        }

        if (IsSet(encodingOption))
        {
            options.Encoding = parseResult.GetValue(encodingOption)!;
        }

        if (parseResult.GetValue(saveOptionsOption) is { } saveFile)
        {
            options.Save(saveFile.FullName);
            return ExitCodes.Success;
        }

        var output = new OutputOptions
        {
            FilesWithMatches = parseResult.GetValue(filesWithMatchesOption),
            Count = parseResult.GetValue(countOption),
            Sort = parseResult.GetValue(sortOption),
            Color = !parseResult.GetValue(noColorOption),
            Stats = parseResult.GetValue(statsOption)
        };

        return await new SearchRunner(options.ToSearchParameters(), output).RunAsync(cancellationToken);
    }
    catch (OperationCanceledException)
    {
        return ExitCodes.Cancelled;
    }
    catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException
                                   or JsonException)
    {
        // Invalid regex, start directory, encoding or options file.
        await Console.Error.WriteLineAsync($"needle-cli: {ex.Message}");
        return ExitCodes.Error;
    }
});

return await rootCommand.Parse(args).InvokeAsync();
