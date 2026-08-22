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

    /// <summary>
    /// One entry per power, standing in for the division by it. See
    /// <see cref="PowerOfTenReciprocal"/> for what the pairs are and why they are exact; the
    /// generator that found them is checked by <c>PowerOfTenReciprocalTests</c>, which
    /// re-derives every one and compares the result against real division.
    /// </summary>
    private static readonly PowerOfTenReciprocal[] Reciprocals =
    [
        // Power zero divides by one, which the callers handle without reaching here.
        default,
        new(new UInt128(0x6666666666666666, 0x6666666666666667), 129, 1, UInt128Powers[1]),
        new(new UInt128(0x28F5C28F5C28F5C2, 0x8F5C28F5C28F5C29), 130, 2, UInt128Powers[2]),
        new(new UInt128(0x20C49BA5E353F7CE, 0xD916872B020C49BB), 132, 3, UInt128Powers[3]),
        new(new UInt128(0x0D1B71758E219652, 0xBD3C36113404EA4B), 133, 4, UInt128Powers[4]),
        new(new UInt128(0x053E2D6238DA3C21, 0x187E7C06E19B90EB), 134, 5, UInt128Powers[5]),
        new(new UInt128(0x0431BDE82D7B634D, 0xAD31FCD24E160D89), 136, 6, UInt128Powers[6]),
        new(new UInt128(0x01AD7F29ABCAF485, 0x787A6520EC08D237), 137, 7, UInt128Powers[7]),
        new(new UInt128(0x015798EE2308C39D, 0xF9FB841A566D74F9), 139, 8, UInt128Powers[8]),
        new(new UInt128(0x0089705F4136B4A5, 0x9731680A88F89531), 140, 9, UInt128Powers[9]),
        new(new UInt128(0x000DBE6FECEBDEDD, 0x5BEB573440E5A885), 139, 10, UInt128Powers[10]),
        new(new UInt128(0x0015FD7FE1796495, 0x5FDEF1ED34A2A73B), 142, 11, UInt128Powers[11]),
        new(new UInt128(0x00119799812DEA11, 0x197F27F0F6E885C9), 144, 12, UInt128Powers[12]),
        new(new UInt128(0x0001C25C26849768, 0x1C2650CB4BE40D61), 143, 13, UInt128Powers[13]),
        new(new UInt128(0x0005A126E1A84AE6, 0xC07A9C24260CF79D), 147, 14, UInt128Powers[14]),
        new(new UInt128(0x00024075F3DCEAC2, 0xB3643E74DC052FD9), 148, 15, UInt128Powers[15]),
        new(new UInt128(0x0000734ACA5F6226, 0xF0ADA6175F343CC5), 148, 16, UInt128Powers[16]),
        new(new UInt128(0x0000B877AA3236A4, 0xB44909BEFEB9FAD5), 151, 17, UInt128Powers[17]),
        new(new UInt128(0x000024E4BBA3A487, 0x5741CEBFCC8B9891), 151, 18, UInt128Powers[18]),
        new(new UInt128(0x00000EC1E4A7DB69, 0x561A52B31E9E3D07), 152, 19, UInt128Powers[19]),
        new(new UInt128(0x000002F394219248, 0x446BAA23D2EC729B), 152, 20, UInt128Powers[20]),
        new(new UInt128(0x00000971DA05074D, 0xA7BEED3F6FC16EBD), 156, 21, UInt128Powers[21]),
        new(new UInt128(0x000000F1C90080BA, 0xF72CB15324C68B13), 155, 22, UInt128Powers[22]),
        new(new UInt128(0x00000305B6680256, 0x4A289DD6DC14F03D), 159, 23, UInt128Powers[23]),
        new(new UInt128(0x0000009ABE14CD44, 0x753B52C4926A9673), 159, 24, UInt128Powers[24]),
        new(new UInt128(0x0000003DE5A1EBB4, 0xFBB1544EA0F76F61), 160, 25, UInt128Powers[25]),
        new(new UInt128(0x000000318481895D, 0x962776A54D92BF81), 162, 26, UInt128Powers[26]),
        new(new UInt128(0x00000009E74D1B79, 0x1E07E48775EA264D), 162, 27, UInt128Powers[27]),
        new(new UInt128(0x00000007EC3DAF94, 0x1806506C5E54EB71), 164, 28, UInt128Powers[28]),
        new(new UInt128(0x000000065697BFA9, 0xACD1D9F04B7722C1), 166, 29, UInt128Powers[29]),
        new(new UInt128(0x0000000289097FDD, 0x7853F0C684960DE7), 167, 30, UInt128Powers[30]),
        new(new UInt128(0x00000002073ACCB1, 0x2D0FF3D203AB3E53), 169, 31, UInt128Powers[31]),
        new(new UInt128(0x0000000067D88F56, 0xA29CCA5D33EF0C77), 169, 32, UInt128Powers[32]),
        new(new UInt128(0x000000002989D2EF, 0x743EB7587B2C6B63), 170, 33, UInt128Powers[33]),
        new(new UInt128(0x00000000213B0F25, 0xF69892AD2F56BC4F), 172, 34, UInt128Powers[34]),
        new(new UInt128(0x000000001A95A5B7, 0xF87A0EF0F2ABC9D9), 174, 35, UInt128Powers[35]),
        new(new UInt128(0x0000000015448493, 0x2D2E725A5BBCA17B), 176, 36, UInt128Powers[36]),
        new(new UInt128(0x000000000881CEA1, 0x4545C75757E50D65), 177, 37, UInt128Powers[37]),
        new(new UInt128(0x0000000003671F73, 0xB54F1C89565B9EF5), 178, 38, UInt128Powers[38])
    ];

    /// <summary>
    /// Divides by 10^<paramref name="power"/> without dividing. The power must be between 1
    /// and <see cref="MaxUInt128Power"/>; zero is the caller's to skip.
    /// </summary>
    public static UInt128 DivideByPowerOfTen(UInt128 value, int power)
    {
        return Reciprocals[power].Divide(value);
    }

    /// <summary>The reciprocal itself, for a caller dividing by one power repeatedly.</summary>
    public static PowerOfTenReciprocal Reciprocal(int power)
    {
        return Reciprocals[power];
    }

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
