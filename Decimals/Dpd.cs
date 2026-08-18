// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// Densely packed decimal: the ten-bit declet that carries three decimal digits, used by
/// the IEEE interchange encodings.
/// </summary>
/// <remarks>
/// The tables are built at startup from the bit rules rather than written out, so the rules
/// are what gets reviewed. Twenty-four of the 1024 declets are non-canonical -- they decode
/// to a value another declet also encodes -- and encoding always produces the canonical
/// one.
/// </remarks>
internal static class Dpd
{
    public const int DecletCount = 1024;
    public const int ValueCount = 1000;

    private static readonly ushort[] DecletToBinaryTable = BuildDecletToBinary();
    private static readonly ushort[] BinaryToDecletTable = BuildBinaryToDeclet();

    /// <summary>
    /// Turns a ten-bit declet into the three digits it carries, 0 through 999.
    /// </summary>
    public static uint ToBinary(uint declet)
    {
        return DecletToBinaryTable[declet];
    }

    /// <summary>
    /// Turns a value 0 through 999 into its canonical ten-bit declet.
    /// </summary>
    public static uint ToDeclet(uint value)
    {
        return BinaryToDecletTable[value];
    }

    private static ushort[] BuildDecletToBinary()
    {
        var table = new ushort[DecletCount];
        for (var declet = 0u; declet < DecletCount; declet++)
        {
            table[declet] = (ushort)DecodeDeclet(declet);
        }

        return table;
    }

    private static ushort[] BuildBinaryToDeclet()
    {
        var table = new ushort[ValueCount];
        for (var value = 0u; value < ValueCount; value++)
        {
            table[value] = (ushort)EncodeDeclet(value);
        }

        return table;
    }

    /// <summary>
    /// The declet bit layout is <c>p q r s t u v w x y</c> from bit 9 down to bit 0. When
    /// <c>v</c> is clear all three digits are 0 through 7 and sit in <c>pqr</c>, <c>stu</c>,
    /// and <c>wxy</c>; otherwise <c>wx</c>, and then <c>st</c>, select which digits are 8 or
    /// 9 and where the survivors moved to.
    /// </summary>
    private static uint DecodeDeclet(uint declet)
    {
        var p = (declet >> 9) & 1;
        var q = (declet >> 8) & 1;
        var r = (declet >> 7) & 1;
        var s = (declet >> 6) & 1;
        var t = (declet >> 5) & 1;
        var u = (declet >> 4) & 1;
        var v = (declet >> 3) & 1;
        var w = (declet >> 2) & 1;
        var x = (declet >> 1) & 1;
        var y = declet & 1;

        var pqr = (p << 2) | (q << 1) | r;
        var stu = (s << 2) | (t << 1) | u;
        var wxy = (w << 2) | (x << 1) | y;
        var pqu = (p << 2) | (q << 1) | u;
        var sty = (s << 2) | (t << 1) | y;
        var pqy = (p << 2) | (q << 1) | y;

        if (v == 0)
        {
            return Combine(pqr, stu, wxy);
        }

        var wx = (w << 1) | x;
        if (wx == 0)
        {
            return Combine(pqr, stu, 8 + y);
        }

        if (wx == 1)
        {
            return Combine(pqr, 8 + u, sty);
        }

        if (wx == 2)
        {
            return Combine(8 + r, stu, pqy);
        }

        var st = (s << 1) | t;
        if (st == 0)
        {
            return Combine(8 + r, 8 + u, pqy);
        }

        if (st == 1)
        {
            return Combine(8 + r, pqu, 8 + y);
        }

        if (st == 2)
        {
            return Combine(pqr, 8 + u, 8 + y);
        }

        // st == 3: p and q carry nothing here, which is where the non-canonical declets
        // come from -- four encodings of the same three digits, and only pq == 00 is written.
        return Combine(8 + r, 8 + u, 8 + y);
    }

    private static uint EncodeDeclet(uint value)
    {
        var high = value / 100;
        var middle = (value / 10) % 10;
        var low = value % 10;

        // The high bit of each digit selects the layout; the low three bits are what has to
        // be placed. Digits 8 and 9 need only their last bit carried.
        var a = high >> 3;
        var bcd = high & 7;
        var e = middle >> 3;
        var fgh = middle & 7;
        var i = low >> 3;
        var jkm = low & 7;

        var selector = (a << 2) | (e << 1) | i;
        var d = bcd & 1;
        var h = fgh & 1;
        var m = jkm & 1;
        var jk = jkm >> 1;
        var fg = fgh >> 1;

        return selector switch
        {
            0 => Pack(bcd, fgh, 0, jkm),
            1 => Pack(bcd, fgh, 1, m),
            2 => Pack(bcd, (jk << 1) | h, 1, 2 | m),
            3 => Pack(bcd, 4 | h, 1, 6 | m),
            4 => Pack((jk << 1) | d, fgh, 1, 4 | m),
            5 => Pack((fg << 1) | d, 2 | h, 1, 6 | m),
            6 => Pack((jk << 1) | d, h, 1, 6 | m),
            _ => Pack(d, 6 | h, 1, 6 | m)
        };
    }

    private static uint Combine(uint high, uint middle, uint low)
    {
        return (high * 100) + (middle * 10) + low;
    }

    private static uint Pack(uint pqr, uint stu, uint v, uint wxy)
    {
        return (pqr << 7) | (stu << 4) | (v << 3) | wxy;
    }
}
