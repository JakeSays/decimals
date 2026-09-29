// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace Decimals.Tests;

/// <summary>
/// Random decimal64 operands as text, for tests that need many values. The values favor
/// the cases where arithmetic tends to fail: full-width coefficients, runs of nines and
/// zeros, exponents at both ends of the range, and special values.
/// </summary>
public sealed class Decimal64TestValues
{
    private readonly Random _random;

    public Decimal64TestValues(int seed)
    {
        _random = new Random(seed);
    }

    /// <summary>A value of any kind, including the specials.</summary>
    public string Next()
    {
        var choice = _random.Next(0, 100);
        if (choice < 2)
        {
            return _random.Next(0, 2) == 0 ? "NaN" : "-NaN";
        }

        if (choice < 3)
        {
            return "sNaN" + _random.Next(0, 1000).ToString(CultureInfo.InvariantCulture);
        }

        if (choice < 4)
        {
            return "NaN" + _random.NextInt64(1, 999999999999999).ToString(CultureInfo.InvariantCulture);
        }

        if (choice < 6)
        {
            return _random.Next(0, 2) == 0 ? "Infinity" : "-Infinity";
        }

        if (choice < 10)
        {
            return (_random.Next(0, 2) == 0 ? "0E" : "-0E") + NextExponent().ToString(CultureInfo.InvariantCulture);
        }

        return NextFinite();
    }

    /// <summary>A finite non-zero value.</summary>
    public string NextFinite()
    {
        var sign = _random.Next(0, 2) == 0 ? string.Empty : "-";
        return sign + NextCoefficient() + "E" + NextExponent().ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A finite value whose magnitude sits between a thousandth and a thousand.</summary>
    public string NextModerate()
    {
        var sign = _random.Next(0, 4) == 0 ? "-" : string.Empty;
        var coefficient = NextCoefficient();
        var exponent = _random.Next(-3, 4) - (coefficient.Length - 1);
        return sign + coefficient + "E" + exponent.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A small integer, for the operations that take a count or a scale.</summary>
    public string NextSmallInteger(int limit)
    {
        return _random.Next(-limit, limit + 1).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A string of ones and zeros, for the logical operations.</summary>
    public string NextLogical()
    {
        var length = _random.Next(1, 17);
        var digits = new char[length];
        for (var index = 0; index < length; index++)
        {
            digits[index] = _random.Next(0, 2) == 0 ? '0' : '1';
        }

        return new string(digits);
    }

    public int NextInt32(int minimum, int maximum)
    {
        return _random.Next(minimum, maximum);
    }

    public long NextInt64()
    {
        Span<byte> bytes = stackalloc byte[8];
        _random.NextBytes(bytes);
        return BitConverter.ToInt64(bytes) >> _random.Next(0, 64);
    }

    public double NextDouble()
    {
        var choice = _random.Next(0, 4);
        if (choice == 0)
        {
            return _random.NextDouble() * 1000;
        }

        if (choice == 1)
        {
            return (_random.NextDouble() - 0.5) * 1E+20;
        }

        if (choice == 2)
        {
            return _random.Next(-1000000, 1000000) / 1000.0;
        }

        return Math.ScaleB(_random.NextDouble(), _random.Next(-1000, 1000));
    }

    private string NextCoefficient()
    {
        var choice = _random.Next(0, 10);
        var length = _random.Next(1, 17);

        if (choice == 0)
        {
            return new string('9', length);
        }

        if (choice == 1)
        {
            return "1" + new string('0', length - 1);
        }

        if (choice == 2)
        {
            length = 16;
        }

        var digits = new char[length];
        for (var index = 0; index < length; index++)
        {
            digits[index] = index == 0
                ? (char)('1' + _random.Next(0, 9))
                : (char)('0' + _random.Next(0, 10));
        }

        if (choice == 3 && length > 3)
        {
            // Trailing zeros, to test the rules for result exponents.
            var zeros = _random.Next(1, length - 1);
            for (var index = length - zeros; index < length; index++)
            {
                digits[index] = '0';
            }
        }

        return new string(digits);
    }

    private int NextExponent()
    {
        var choice = _random.Next(0, 10);
        if (choice < 4)
        {
            return _random.Next(-20, 21);
        }

        if (choice < 6)
        {
            return _random.Next(-400, 401);
        }

        if (choice < 8)
        {
            return _random.Next(-410, -370);
        }

        return _random.Next(355, 400);
    }
}
