namespace Needle.Services;

/// <summary>
///     The file was modified after the search. The found positions are no longer valid.
/// </summary>
public class FileChangedException()
    : InvalidOperationException("The file has changed since the search. Please search again.");
