// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// A string builder that writes into a caller's span. It is used by the one formatting
/// path that builds text from a culture's separators and group sizes. It moves to a heap
/// array only if the text does not fit in the span.
/// </summary>
internal ref struct Decimal64TextBuilder
{
    private Span<char> _buffer;

    private int _length;

    /// <summary>Creates a builder that writes into the given span first.</summary>
    /// <param name="buffer">The initial buffer, usually on the stack.</param>
    public Decimal64TextBuilder(Span<char> buffer)
    {
        _buffer = buffer;
        _length = 0;
    }

    /// <summary>Appends one character.</summary>
    /// <param name="character">The character to append.</param>
    public void Append(char character)
    {
        if (_length == _buffer.Length)
        {
            Grow(1);
        }

        _buffer[_length++] = character;
    }

    /// <summary>Appends a character several times.</summary>
    /// <param name="character">The character to append.</param>
    /// <param name="count">The number of times to append it. Zero or less appends nothing.</param>
    public void Append(char character, int count)
    {
        for (var index = 0; index < count; index++)
        {
            Append(character);
        }
    }

    /// <summary>Appends a run of characters.</summary>
    /// <param name="text">The characters to append.</param>
    public void Append(ReadOnlySpan<char> text)
    {
        if (_length + text.Length > _buffer.Length)
        {
            Grow(text.Length);
        }

        text.CopyTo(_buffer[_length..]);
        _length += text.Length;
    }

    /// <summary>Creates a string from the characters appended so far.</summary>
    /// <returns>The built text.</returns>
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
