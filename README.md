# Needle

A simple and fast text search and replace tool for Windows.

## Prerequisites

- .NET 10 Runtime ([download](https://dotnet.microsoft.com/download/dotnet/10.0))


![Screenshot](Images/screenshot.png)

## Searching in ZIP Archives

You can search within ZIP files by including `*.zip` in your file pattern list. The specified file patterns are applied to each file within the archive, allowing you to search large archives without prior extraction.

**Note:** Nested archives are currently ignored.

## Searching in File Names

The *Search in* option selects where the pattern is searched: the file content, the file name, or both. A match in a file name is listed as the first row of the file's matches, marked with a blue **Name** badge instead of a line number.

When replacing, the content is modified first and the file is renamed afterwards. Regex capture groups (`$1`) work for file names as well. A rename is skipped with an error if the new name is invalid or a file with that name already exists. Only file names are renamed, not directories. Files inside ZIP archives are never renamed.

## Text Replacement Behavior

### Encoding
When replacing text in files, the output encoding depends on the input file:
- Files with a byte order mark (BOM): Original encoding is preserved
- Files without BOM: Output is written as UTF-8

### Line Endings
Text replacement is processed line by line, which normalizes line endings in the output. If preserving exact line endings is critical for your use case, be aware of this behavior.

## Contributing

Issues and pull requests are welcome.
