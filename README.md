# Needle

A simple and fast text search and replace tool for Windows.

## Prerequisites

- .NET 10 Runtime ([download](https://dotnet.microsoft.com/download/dotnet/10.0))

![Screenshot](Images/screenshot.png)

## Searching

| Option | Description |
|---|---|
| Start directory | Directory to search in. |
| File mask | One or more masks separated by `;`, `,` or `\|`, e.g. `*.cs;*.xaml`. The last 10 masks are kept in the drop-down. |
| Text | Plain text, or a .NET regular expression if *Regex* is checked. |
| Search in | *File content*, *File name* or both. |
| Encoding | Encoding for files without byte order mark (BOM), see [Encoding](#encoding). |
| Case sensitive | Applies to plain text and regular expressions. |
| Include subdirectories | Searches the whole directory tree. |

All files matching the file mask are searched, including hidden, system and binary files. The `.git` directory is skipped.

Click a file to expand its matches. The context menu opens a file in Explorer or a match in Notepad++.

### File Names

A match in a file name is listed as the first row of the file's matches, marked with a blue **Name** badge instead of a line number. When searching in both content and file name, each file is listed once with all its matches.

### ZIP Archives

You can search within ZIP files by including `*.zip` in your file mask. The file masks are applied to each file within the archive, allowing you to search large archives without prior extraction.

Nested archives are ignored. Matches inside archives cannot be replaced.

### Encoding

Files with a BOM are read with the encoding of the BOM (UTF-8, UTF-16 LE/BE, UTF-32 LE/BE).

Files without BOM are read with the encoding selected in *Encoding*: UTF-8 (default) or the ANSI code page of the system (e.g. Windows-1252). The encoding of such files cannot be detected reliably, so you have to choose.

The search tolerates a wrong encoding: ASCII text is still found, only non-ASCII characters may not match.

## Replacing

*Replace* replaces all matches in the current result with the text in *Replace with*. With *Regex*, the replacement can refer to capture groups, e.g. `$1`.

Replacing never guesses, it refuses files instead of risking to corrupt them. A file is not modified and an error is shown if

- the file was modified after the search, so the found positions are no longer valid,
- the file is not valid in its encoding (e.g. an ANSI file searched as UTF-8),
- the replacement text contains characters that cannot be stored in the file's encoding (e.g. `→` in Windows-1252).

The encoding is preserved: files with BOM keep their BOM, files without BOM are written in the selected encoding.

After replacing, the result list is not updated. Search again before replacing a second time.

### Renaming Files

Matches in file names rename the file. The content is modified first and the file is renamed afterwards. A rename is refused if the new name is invalid or a file with that name already exists. Only files are renamed, not directories.

### Limitations

Replacing processes the file line by line:

- All line endings are written as CRLF, and a line break is appended at the end of the file.
- **Do not replace in binary files.** Searching them is fine, but replacing changes every line break byte and corrupts the file.

## Development

Run the tests with:

```
dotnet test
```

## Contributing

Issues and pull requests are welcome.
