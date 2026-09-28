// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// A string builder over a caller's span, for the one formatting path that assembles text
/// from a culture's separators and group sizes rather than writing digits in place. Grows
/// onto the heap only if the culture's symbols outrun the span.
/// </summary>
internal ref struct Decimal32TextBuilder
{
    private Span<char> _buffer;

    private int _length;

    public Decimal32TextBuilder(Span<char> buffer)
    {
        _buffer = buffer;
        _length = 0;
    }

    public void Append(char character)
    {
        if (_length == _buffer.Length)
        {
            Grow(1);
        }

        _buffer[_length++] = character;
    }

    public void Append(char character, int count)
    {
        for (var index = 0; index < count; index++)
        {
            Append(character);
        }
    }

    public void Append(ReadOnlySpan<char> text)
    {
        if (_length + text.Length > _buffer.Length)
        {
            Grow(text.Length);
        }

        text.CopyTo(_buffer[_length..]);
        _length += text.Length;
    }

    public override string ToString()
    {
        return new string(_buffer[.._length]);
    }

    private void Grow(int needed)
    {
        var larger = new char[Math.Max(_buffer.Length * 2, _length + needed)];
        _buffer[.._length].CopyTo(larger);
        _buffer = larger;
    }
}
