using System.CommandLine;
using System.CommandLine.Help;
using System.Text.Json;
using Needle.Cli;
using Needle.Services;

const string DefaultOptionsFileName = "needle-cli.json";

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
    Description = $"JSON file with the options. Overrides {DefaultOptionsFileName} next to the executable, " +
                  "command line options override it."
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
var maxColumnsOption = new Option<int>("--max-columns", "-M")
{
    Description = "Cuts lines longer than this around the matches. Default: 0, lines are not cut."
};

var rootCommand = new RootCommand("Needle - fast text search in files.")
{
    patternArgument, pathArgument, maskOption, regexOption, caseSensitiveOption, noRecurseOption, scopeOption,
    encodingOption, optionsFileOption, saveOptionsOption, filesWithMatchesOption, countOption, sortOption,
    noColorOption, statsOption, maxColumnsOption
};

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    try
    {
        // Options that are not given have an implicit result, too.
        bool IsSet(Option option) => parseResult.GetResult(option) is { Implicit: false };
        T? Given<T>(Option<T> option) where T : struct => IsSet(option) ? parseResult.GetValue(option) : null;
        string? GivenText(Option<string> option) => IsSet(option) ? parseResult.GetValue(option) : null;

        var commandLine = new SearchOptions
        {
            Pattern = parseResult.GetValue(patternArgument),

            // Relative to the current directory, not to an options file.
            StartDirectory = parseResult.GetValue(pathArgument) is { } path ? Path.GetFullPath(path) : null,
            FileMasks = GivenText(maskOption),
            IsRegex = Given(regexOption),
            IsCaseSensitive = Given(caseSensitiveOption),
            IncludeSubdirectories = IsSet(noRecurseOption) ? !parseResult.GetValue(noRecurseOption) : null,
            SearchScope = Given(scopeOption),
            Encoding = GivenText(encodingOption),
            MaxColumns = Given(maxColumnsOption)
        };

        // Each level overrides the previous one: defaults, the file next to the executable,
        // the file given with --options, the command line.
        var options = new SearchOptions();
        var defaultFile = Path.Combine(AppContext.BaseDirectory, DefaultOptionsFileName);
        if (File.Exists(defaultFile))
        {
            options = options.Merge(LoadOptions(defaultFile));
        }

        if (parseResult.GetValue(optionsFileOption) is { } optionsFile)
        {
            options = options.Merge(LoadOptions(optionsFile.FullName));
        }

        options = options.Merge(commandLine);

        if (options.MaxColumns < 0)
        {
            throw new ArgumentException("--max-columns must not be negative.");
        }

        if (parseResult.GetValue(saveOptionsOption) is { } saveFile)
        {
            options.Save(saveFile.FullName);
            return ExitCodes.Success;
        }

        if (string.IsNullOrWhiteSpace(options.Pattern))
        {
            if (args.Length == 0)
            {
                new HelpAction().Invoke(parseResult);
                return ExitCodes.Error;
            }

            throw new ArgumentException(
                "No search pattern given, neither on the command line nor in an options file. See needle-cli --help.");
        }

        if (commandLine.Pattern != null && commandLine.StartDirectory == null && Directory.Exists(commandLine.Pattern))
        {
            // A common mistake: needle-cli <path> searches for the path as text in the current directory.
            await Console.Error.WriteLineAsync(
                $"needle-cli: Searching for the text '{commandLine.Pattern}' in '{Path.GetFullPath(options.StartDirectory ?? ".")}'. " +
                "To search in that directory, use: needle-cli <pattern> <path>");
        }

        var output = new OutputOptions
        {
            FilesWithMatches = parseResult.GetValue(filesWithMatchesOption),
            Count = parseResult.GetValue(countOption),
            Sort = parseResult.GetValue(sortOption),
            Color = !parseResult.GetValue(noColorOption),
            Stats = parseResult.GetValue(statsOption),
            MaxColumns = options.MaxColumns ?? 0
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

static SearchOptions LoadOptions(string filePath)
{
    try
    {
        return SearchOptions.Load(filePath);
    }
    catch (JsonException ex)
    {
        // Which of the files is wrong.
        throw new JsonException($"{filePath}: {ex.Message}", ex);
    }
}
