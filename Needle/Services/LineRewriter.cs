using System.Buffers;
using System.IO;
using System.Text;

namespace Needle.Services;

/// <summary>
///     Copies a stream line by line on byte level. Only the lines selected by the caller are passed to a callback
///     that returns their new content. All other bytes, including all line breaks, are copied unchanged.
///     <para>
///         Line breaks are found on byte level. This is safe because the code units for CR and LF never occur inside
///         other characters: In UTF-8 all bytes of multibyte characters are &gt;= 0x80, in the Windows code pages
///         trail bytes are &gt;= 0x40, and in UTF-16/32 the units are compared as a whole.
///     </para>
///     <para>
///         Line breaks are CR LF, LF and a single CR. This matches <see cref="StreamReader.ReadLine" />,
///         so the line numbers are the same as in the search.
///     </para>
/// </summary>
internal sealed class LineRewriter
{
    private const int BufferSize = 81920;
    private const int Cr = 0x0D;
    private const int Lf = 0x0A;

    private readonly Func<int, bool> _isLineToRewrite;
    private readonly ArrayBufferWriter<byte> _line = new();
    private readonly Stream _output;
    private readonly Func<int, ReadOnlyMemory<byte>, byte[]> _rewriteLine;
    private readonly byte[] _crBytes;
    private readonly byte[] _lfBytes;
    private readonly byte[] _crLfBytes;
    private readonly bool _bigEndian;
    private readonly int _unitSize;

    private int _lineNumber = 1;
    private bool _pendingCr;

    /// <param name="encoding">Determines the size of the code units: 1 byte, or 2 and 4 bytes for UTF-16 and UTF-32.</param>
    /// <param name="isLineToRewrite">Gets the 1-based line number.</param>
    /// <param name="rewriteLine">Gets the 1-based line number and the line's bytes without line break.</param>
    private LineRewriter(Stream output, Encoding encoding, Func<int, bool> isLineToRewrite,
        Func<int, ReadOnlyMemory<byte>, byte[]> rewriteLine)
    {
        _output = output;
        _isLineToRewrite = isLineToRewrite;
        _rewriteLine = rewriteLine;

        (_unitSize, _bigEndian) = encoding.CodePage switch
        {
            1200 => (2, false), // UTF-16 LE
            1201 => (2, true), // UTF-16 BE
            12000 => (4, false), // UTF-32 LE
            12001 => (4, true), // UTF-32 BE
            _ => (1, false)
        };

        _crBytes = EncodeUnit(Cr);
        _lfBytes = EncodeUnit(Lf);
        _crLfBytes = [.. _crBytes, .. _lfBytes];
    }

    /// <summary>
    ///     The input must be positioned after the BOM.
    /// </summary>
    public static void Rewrite(Stream input, Stream output, Encoding encoding, Func<int, bool> isLineToRewrite,
        Func<int, ReadOnlyMemory<byte>, byte[]> rewriteLine, CancellationToken cancellationToken)
    {
        var rewriter = new LineRewriter(output, encoding, isLineToRewrite, rewriteLine);
        rewriter.Run(input, cancellationToken);
    }

    private void Run(Stream input, CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        var count = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var read = input.Read(buffer, count, buffer.Length - count);
            count += read;
            var isEnd = read == 0;

            // Process complete code units only. An incomplete unit is kept for the next read.
            // At the end of the file, it is part of the last line.
            var usable = isEnd ? count : count - count % _unitSize;
            Process(buffer.AsSpan(0, usable), isEnd);

            var remainder = count - usable;
            buffer.AsSpan(usable, remainder).CopyTo(buffer);
            count = remainder;

            if (isEnd)
            {
                break;
            }
        }

        // The last line has no line break.
        if (_isLineToRewrite(_lineNumber))
        {
            _output.Write(_rewriteLine(_lineNumber, _line.WrittenMemory));
        }
    }

    private void Process(ReadOnlySpan<byte> span, bool isEnd)
    {
        var index = 0;

        if (_pendingCr)
        {
            // The previous chunk ended with CR. It is either CR LF or a single CR.
            if (span.Length >= _unitSize)
            {
                _pendingCr = false;
                if (IsUnit(span, 0, Lf))
                {
                    EndLine(_crLfBytes);
                    index = _unitSize;
                }
                else
                {
                    EndLine(_crBytes);
                }
            }
            else if (isEnd)
            {
                _pendingCr = false;
                EndLine(_crBytes);
            }
            else
            {
                return;
            }
        }

        var lineStart = index;

        while ((index = IndexOfLineBreak(span, index)) >= 0)
        {
            AppendContent(span[lineStart..index]);

            if (IsUnit(span, index, Lf))
            {
                index += _unitSize;
                EndLine(_lfBytes);
            }
            else if (index + 2 * _unitSize <= span.Length)
            {
                var isCrLf = IsUnit(span, index + _unitSize, Lf);
                index += isCrLf ? 2 * _unitSize : _unitSize;
                EndLine(isCrLf ? _crLfBytes : _crBytes);
            }
            else if (isEnd)
            {
                // CR at the end of the file, possibly followed by an incomplete unit.
                index += _unitSize;
                EndLine(_crBytes);
            }
            else
            {
                // CR at the end of the chunk. The next chunk decides whether LF follows.
                _pendingCr = true;
                return;
            }

            lineStart = index;
        }

        AppendContent(span[lineStart..]);
    }

    /// <param name="lineBreak">The original line break, it is written unchanged.</param>
    private void EndLine(byte[] lineBreak)
    {
        if (_isLineToRewrite(_lineNumber))
        {
            _output.Write(_rewriteLine(_lineNumber, _line.WrittenMemory));
            _line.Clear();
        }

        _output.Write(lineBreak);
        _lineNumber++;
    }

    private void AppendContent(ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
        {
            return;
        }

        // Only the lines to rewrite are kept in memory.
        if (_isLineToRewrite(_lineNumber))
        {
            _line.Write(content);
        }
        else
        {
            _output.Write(content);
        }
    }

    /// <summary>
    ///     Returns the index of the next CR or LF unit at or after start, or -1.
    /// </summary>
    private int IndexOfLineBreak(ReadOnlySpan<byte> span, int start)
    {
        if (_unitSize == 1)
        {
            var found = span[start..].IndexOfAny((byte)Cr, (byte)Lf);
            return found < 0 ? -1 : start + found;
        }

        for (var i = start; i + _unitSize <= span.Length; i += _unitSize)
        {
            if (IsUnit(span, i, Cr) || IsUnit(span, i, Lf))
            {
                return i;
            }
        }

        return -1;
    }

    private bool IsUnit(ReadOnlySpan<byte> span, int index, int value)
    {
        return span.Slice(index, _unitSize).SequenceEqual(value == Cr ? _crBytes : _lfBytes);
    }

    private byte[] EncodeUnit(int value)
    {
        var bytes = new byte[_unitSize];
        bytes[_bigEndian ? _unitSize - 1 : 0] = (byte)value;
        return bytes;
    }
}
