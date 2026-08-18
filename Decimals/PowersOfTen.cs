// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// Scaling tables. Decimal arithmetic multiplies and divides by powers of ten constantly,
/// and a table lookup beats recomputing one every time.
/// </summary>
internal static class PowersOfTen
{
    /// <summary>Largest power of ten that fits a <see cref="ulong"/>.</summary>
    public const int MaxUInt64Power = 19;

    /// <summary>Largest power of ten that fits a <see cref="UInt128"/>.</summary>
    public const int MaxUInt128Power = 38;

    /// <summary>Largest power of ten that fits a <see cref="UInt256"/>.</summary>
    public const int MaxUInt256Power = 77;

    private static readonly ulong[] UInt64Powers = BuildUInt64Powers();
    private static readonly UInt128[] UInt128Powers = BuildUInt128Powers();
    private static readonly UInt256[] UInt256Powers = BuildUInt256Powers();

    public static ulong UInt64(int power)
    {
        return UInt64Powers[power];
    }

    public static UInt128 UInt128(int power)
    {
        return UInt128Powers[power];
    }

    public static UInt256 UInt256(int power)
    {
        return UInt256Powers[power];
    }

    private static ulong[] BuildUInt64Powers()
    {
        var table = new ulong[MaxUInt64Power + 1];
        table[0] = 1;
        for (var power = 1; power <= MaxUInt64Power; power++)
        {
            table[power] = table[power - 1] * 10;
        }

        return table;
    }

    private static UInt256[] BuildUInt256Powers()
    {
        var table = new UInt256[MaxUInt256Power + 1];
        table[0] = Decimals.UInt256.One;
        for (var power = 1; power <= MaxUInt256Power; power++)
        {
            table[power] = Decimals.UInt256.MultiplyByUInt64(table[power - 1], 10);
        }

        return table;
    }

    private static UInt128[] BuildUInt128Powers()
    {
        var table = new UInt128[MaxUInt128Power + 1];
        table[0] = System.UInt128.One;
        for (var power = 1; power <= MaxUInt128Power; power++)
        {
            table[power] = table[power - 1] * 10;
        }

        return table;
    }
}
