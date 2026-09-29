// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Decimals.Internal;

/// <summary>
/// Powers of ten in one, two, and four words, the constants used to divide by them, and
/// the digit count at each width. Every operation uses these, so each takes only a few
/// instructions and no lookup has a bounds check.
/// </summary>
/// <remarks>
/// <para>
/// Every table here is a span over the assembly's data section. Nothing is allocated, and
/// no static constructor runs before a lookup.
/// </para>
/// <para>
/// A one-word value is divided by a power of ten as in Decimal64: a right shift by the
/// power removes the factors of 2, and one multiply by the reciprocal of the remaining 5^p
/// is exact for every numerator. A wider value is divided one word at a time from the top,
/// with the 2-by-1 method of Möller and Granlund. The divisor is shifted so its top bit is
/// set, and its reciprocal is one word. Each step turns the remainder and the next word
/// into a quotient word and a new remainder, with two multiplies and at most two
/// corrections. For powers above 10^19, four words are divided with the 3-by-2 method from
/// the same paper, which gets one quotient word from three dividend words the same way.
/// All the constants were generated, and the tests recompute them.
/// </para>
/// </remarks>
internal static class Decimal128Tables
{
    /// <summary>The largest power of ten that fits in one word.</summary>
    public const int MaxPower = 19;

    /// <summary>The largest power of ten that fits in two words.</summary>
    public const int MaxWidePower = 38;

    /// <summary>The largest power of ten that fits in four words.</summary>
    public const int MaxLongPower = 77;

    private static ReadOnlySpan<ulong> Powers =>
    [
        1,
        10,
        100,
        1000,
        10000,
        100000,
        1000000,
        10000000,
        100000000,
        1000000000,
        10000000000,
        100000000000,
        1000000000000,
        10000000000000,
        100000000000000,
        1000000000000000,
        10000000000000000,
        100000000000000000,
        1000000000000000000,
        10000000000000000000
    ];

    /// <summary>
    /// <c>ceil(2^(64 + shift) / 5^p)</c> for each power. Power zero would divide by one,
    /// which no caller does.
    /// </summary>
    private static ReadOnlySpan<ulong> Multipliers =>
    [
        0,
        0xCCCCCCCCCCCCCCCD,
        0xA3D70A3D70A3D70B,
        0x83126E978D4FDF3C,
        0xD1B71758E219652C,
        0xA7C5AC471B478424,
        0x8637BD05AF6C69B6,
        0xD6BF94D5E57A42BD,
        0xABCC77118461CEFD,
        0x89705F4136B4A598,
        0xDBE6FECEBDEDD5BF,
        0xAFEBFF0BCB24AAFF,
        0x8CBCCC096F5088CC,
        0xE12E13424BB40E14,
        0xB424DC35095CD810,
        0x901D7CF73AB0ACDA,
        0xE69594BEC44DE15C,
        0xB877AA3236A4B44A,
        0x9392EE8E921D5D08,
        0xEC1E4A7DB69561A6
    ];

    /// <summary><c>floor(log2 5^p)</c>: the right shift applied to the high half of the product.</summary>
    private static ReadOnlySpan<byte> Shifts =>
    [
        0, 2, 4, 6, 9, 11, 13, 16, 18, 20, 23, 25, 27, 30, 32, 34, 37, 39, 41, 44
    ];

    /// <summary>10 to each power from 0 to 38, as a high word and a low word.</summary>
    private static ReadOnlySpan<ulong> WidePowers =>
    [
        0x0000000000000000, 0x0000000000000001,
        0x0000000000000000, 0x000000000000000A,
        0x0000000000000000, 0x0000000000000064,
        0x0000000000000000, 0x00000000000003E8,
        0x0000000000000000, 0x0000000000002710,
        0x0000000000000000, 0x00000000000186A0,
        0x0000000000000000, 0x00000000000F4240,
        0x0000000000000000, 0x0000000000989680,
        0x0000000000000000, 0x0000000005F5E100,
        0x0000000000000000, 0x000000003B9ACA00,
        0x0000000000000000, 0x00000002540BE400,
        0x0000000000000000, 0x000000174876E800,
        0x0000000000000000, 0x000000E8D4A51000,
        0x0000000000000000, 0x000009184E72A000,
        0x0000000000000000, 0x00005AF3107A4000,
        0x0000000000000000, 0x00038D7EA4C68000,
        0x0000000000000000, 0x002386F26FC10000,
        0x0000000000000000, 0x016345785D8A0000,
        0x0000000000000000, 0x0DE0B6B3A7640000,
        0x0000000000000000, 0x8AC7230489E80000,
        0x0000000000000005, 0x6BC75E2D63100000,
        0x0000000000000036, 0x35C9ADC5DEA00000,
        0x000000000000021E, 0x19E0C9BAB2400000,
        0x000000000000152D, 0x02C7E14AF6800000,
        0x000000000000D3C2, 0x1BCECCEDA1000000,
        0x0000000000084595, 0x161401484A000000,
        0x000000000052B7D2, 0xDCC80CD2E4000000,
        0x00000000033B2E3C, 0x9FD0803CE8000000,
        0x00000000204FCE5E, 0x3E25026110000000,
        0x00000001431E0FAE, 0x6D7217CAA0000000,
        0x0000000C9F2C9CD0, 0x4674EDEA40000000,
        0x0000007E37BE2022, 0xC0914B2680000000,
        0x000004EE2D6D415B, 0x85ACEF8100000000,
        0x0000314DC6448D93, 0x38C15B0A00000000,
        0x0001ED09BEAD87C0, 0x378D8E6400000000,
        0x0013426172C74D82, 0x2B878FE800000000,
        0x00C097CE7BC90715, 0xB34B9F1000000000,
        0x0785EE10D5DA46D9, 0x00F436A000000000,
        0x4B3B4CA85A86C47A, 0x098A224000000000
    ];

    /// <summary>10 to each power from 0 to 77, as four words, highest first.</summary>
    private static ReadOnlySpan<ulong> LongPowers =>
    [
        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x0000000000000001,
        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x000000000000000A,
        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x0000000000000064,
        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x00000000000003E8,
        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x0000000000002710,
        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x00000000000186A0,
        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x00000000000F4240,
        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x0000000000989680,
        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x0000000005F5E100,
        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x000000003B9ACA00,
        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x00000002540BE400,
        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x000000174876E800,
        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x000000E8D4A51000,
        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x000009184E72A000,
        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x00005AF3107A4000,
        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x00038D7EA4C68000,
        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x002386F26FC10000,
        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x016345785D8A0000,
        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x0DE0B6B3A7640000,
        0x0000000000000000, 0x0000000000000000, 0x0000000000000000, 0x8AC7230489E80000,
        0x0000000000000000, 0x0000000000000000, 0x0000000000000005, 0x6BC75E2D63100000,
        0x0000000000000000, 0x0000000000000000, 0x0000000000000036, 0x35C9ADC5DEA00000,
        0x0000000000000000, 0x0000000000000000, 0x000000000000021E, 0x19E0C9BAB2400000,
        0x0000000000000000, 0x0000000000000000, 0x000000000000152D, 0x02C7E14AF6800000,
        0x0000000000000000, 0x0000000000000000, 0x000000000000D3C2, 0x1BCECCEDA1000000,
        0x0000000000000000, 0x0000000000000000, 0x0000000000084595, 0x161401484A000000,
        0x0000000000000000, 0x0000000000000000, 0x000000000052B7D2, 0xDCC80CD2E4000000,
        0x0000000000000000, 0x0000000000000000, 0x00000000033B2E3C, 0x9FD0803CE8000000,
        0x0000000000000000, 0x0000000000000000, 0x00000000204FCE5E, 0x3E25026110000000,
        0x0000000000000000, 0x0000000000000000, 0x00000001431E0FAE, 0x6D7217CAA0000000,
        0x0000000000000000, 0x0000000000000000, 0x0000000C9F2C9CD0, 0x4674EDEA40000000,
        0x0000000000000000, 0x0000000000000000, 0x0000007E37BE2022, 0xC0914B2680000000,
        0x0000000000000000, 0x0000000000000000, 0x000004EE2D6D415B, 0x85ACEF8100000000,
        0x0000000000000000, 0x0000000000000000, 0x0000314DC6448D93, 0x38C15B0A00000000,
        0x0000000000000000, 0x0000000000000000, 0x0001ED09BEAD87C0, 0x378D8E6400000000,
        0x0000000000000000, 0x0000000000000000, 0x0013426172C74D82, 0x2B878FE800000000,
        0x0000000000000000, 0x0000000000000000, 0x00C097CE7BC90715, 0xB34B9F1000000000,
        0x0000000000000000, 0x0000000000000000, 0x0785EE10D5DA46D9, 0x00F436A000000000,
        0x0000000000000000, 0x0000000000000000, 0x4B3B4CA85A86C47A, 0x098A224000000000,
        0x0000000000000000, 0x0000000000000002, 0xF050FE938943ACC4, 0x5F65568000000000,
        0x0000000000000000, 0x000000000000001D, 0x6329F1C35CA4BFAB, 0xB9F5610000000000,
        0x0000000000000000, 0x0000000000000125, 0xDFA371A19E6F7CB5, 0x4395CA0000000000,
        0x0000000000000000, 0x0000000000000B7A, 0xBC627050305ADF14, 0xA3D9E40000000000,
        0x0000000000000000, 0x00000000000072CB, 0x5BD86321E38CB6CE, 0x6682E80000000000,
        0x0000000000000000, 0x0000000000047BF1, 0x9673DF52E37F2410, 0x011D100000000000,
        0x0000000000000000, 0x00000000002CD76F, 0xE086B93CE2F768A0, 0x0B22A00000000000,
        0x0000000000000000, 0x0000000001C06A5E, 0xC5433C60DDAA1640, 0x6F5A400000000000,
        0x0000000000000000, 0x00000000118427B3, 0xB4A05BC8A8A4DE84, 0x5986800000000000,
        0x0000000000000000, 0x00000000AF298D05, 0x0E4395D69670B12B, 0x7F41000000000000,
        0x0000000000000000, 0x00000006D79F8232, 0x8EA3DA61E066EBB2, 0xF88A000000000000,
        0x0000000000000000, 0x000000446C3B15F9, 0x926687D2C40534FD, 0xB564000000000000,
        0x0000000000000000, 0x000002AC3A4EDBBF, 0xB8014E3BA83411E9, 0x15E8000000000000,
        0x0000000000000000, 0x00001ABA4714957D, 0x300D0E549208B31A, 0xDB10000000000000,
        0x0000000000000000, 0x00010B46C6CDD6E3, 0xE0828F4DB456FF0C, 0x8EA0000000000000,
        0x0000000000000000, 0x000A70C3C40A64E6, 0xC51999090B65F67D, 0x9240000000000000,
        0x0000000000000000, 0x006867A5A867F103, 0xB2FFFA5A71FBA0E7, 0xB680000000000000,
        0x0000000000000000, 0x04140C78940F6A24, 0xFDFFC78873D4490D, 0x2100000000000000,
        0x0000000000000000, 0x28C87CB5C89A2571, 0xEBFDCB54864ADA83, 0x4A00000000000000,
        0x0000000000000001, 0x97D4DF19D6057673, 0x37E9F14D3EEC8920, 0xE400000000000000,
        0x000000000000000F, 0xEE50B7025C36A080, 0x2F236D04753D5B48, 0xE800000000000000,
        0x000000000000009F, 0x4F2726179A224501, 0xD762422C946590D9, 0x1000000000000000,
        0x0000000000000639, 0x17877CEC0556B212, 0x69D695BDCBF7A87A, 0xA000000000000000,
        0x0000000000003E3A, 0xEB4AE1383562F4B8, 0x2261D969F7AC94CA, 0x4000000000000000,
        0x0000000000026E4D, 0x30ECCC3215DD8F31, 0x57D27E23ACBDCFE6, 0x8000000000000000,
        0x0000000000184F03, 0xE93FF9F4DAA797ED, 0x6E38ED64BF6A1F01, 0x0000000000000000,
        0x0000000000F31627, 0x1C7FC3908A8BEF46, 0x4E3945EF7A25360A, 0x0000000000000000,
        0x00000000097EDD87, 0x1CFDA3A5697758BF, 0x0E3CBB5AC5741C64, 0x0000000000000000,
        0x000000005EF4A747, 0x21E864761EA97776, 0x8E5F518BB6891BE8, 0x0000000000000000,
        0x00000003B58E88C7, 0x5313EC9D329EAAA1, 0x8FB92F75215B1710, 0x0000000000000000,
        0x00000025179157C9, 0x3EC73E23FA32AA4F, 0x9D3BDA934D8EE6A0, 0x0000000000000000,
        0x00000172EBAD6DDC, 0x73C86D67C5FAA71C, 0x245689C107950240, 0x0000000000000000,
        0x00000E7D34C64A9C, 0x85D4460DBBCA8719, 0x6B61618A4BD21680, 0x0000000000000000,
        0x000090E40FBEEA1D, 0x3A4ABC8955E946FE, 0x31CDCF66F634E100, 0x0000000000000000,
        0x0005A8E89D752524, 0x46EB5D5D5B1CC5ED, 0xF20A1A059E10CA00, 0x0000000000000000,
        0x003899162693736A, 0xC531A5A58F1FBB4B, 0x746504382CA7E400, 0x0000000000000000,
        0x0235FADD81C2822B, 0xB3F07877973D50F2, 0x8BF22A31BE8EE800, 0x0000000000000000,
        0x161BCCA7119915B5, 0x0764B4ABE8652979, 0x7775A5F171951000, 0x0000000000000000,
        0xDD15FE86AFFAD912, 0x49EF0EB713F39EBE, 0xAA987B6E6FD2A000, 0x0000000000000000
    ];

    /// <summary>
    /// The left shift that moves the top bit of 10^p to the top of the word, for each
    /// power. No caller divides by power zero.
    /// </summary>
    private static ReadOnlySpan<byte> DivisorShifts =>
    [
        0, 60, 57, 54, 50, 47, 44, 40, 37, 34, 30, 27, 24, 20, 17, 14, 10, 7, 4, 0
    ];

    /// <summary>10 to each power, shifted left by <see cref="DivisorShifts"/>.</summary>
    private static ReadOnlySpan<ulong> NormalizedDivisors =>
    [
        0,
        0xA000000000000000,
        0xC800000000000000,
        0xFA00000000000000,
        0x9C40000000000000,
        0xC350000000000000,
        0xF424000000000000,
        0x9896800000000000,
        0xBEBC200000000000,
        0xEE6B280000000000,
        0x9502F90000000000,
        0xBA43B74000000000,
        0xE8D4A51000000000,
        0x9184E72A00000000,
        0xB5E620F480000000,
        0xE35FA931A0000000,
        0x8E1BC9BF04000000,
        0xB1A2BC2EC5000000,
        0xDE0B6B3A76400000,
        0x8AC7230489E80000
    ];

    /// <summary>
    /// <c>floor((2^128 - 1) / d') - 2^64</c> for each normalized divisor d'. The 2-by-1
    /// division step multiplies by this word.
    /// </summary>
    private static ReadOnlySpan<ulong> Inverses =>
    [
        0,
        0x9999999999999999,
        0x47AE147AE147AE14,
        0x0624DD2F1A9FBE76,
        0xA36E2EB1C432CA57,
        0x4F8B588E368F0846,
        0x0C6F7A0B5ED8D36B,
        0xAD7F29ABCAF48578,
        0x5798EE2308C39DF9,
        0x12E0BE826D694B2E,
        0xB7CDFD9D7BDBAB7D,
        0x5FD7FE17964955FD,
        0x19799812DEA11197,
        0xC25C268497681C26,
        0x6849B86A12B9B01E,
        0x203AF9EE756159B2,
        0xCD2B297D889BC2B6,
        0x70EF54646D496892,
        0x2725DD1D243ABA0E,
        0xD83C94FB6D2AC34A
    ];

    /// <summary>
    /// The left shift that moves the top bit of 10^p to the top of its high word, for each
    /// power from 20 to 38.
    /// </summary>
    private static ReadOnlySpan<byte> WideDivisorShifts =>
    [
        61, 58, 54, 51, 48, 44, 41, 38, 34, 31, 28, 25, 21, 18, 15, 11, 8, 5, 1
    ];

    /// <summary>
    /// 10 to each power from 20 to 38, shifted left by <see cref="WideDivisorShifts"/>, as a
    /// high word and a low word.
    /// </summary>
    private static ReadOnlySpan<ulong> WideNormalizedDivisors =>
    [
        0xAD78EBC5AC620000, 0x0000000000000000,
        0xD8D726B7177A8000, 0x0000000000000000,
        0x878678326EAC9000, 0x0000000000000000,
        0xA968163F0A57B400, 0x0000000000000000,
        0xD3C21BCECCEDA100, 0x0000000000000000,
        0x84595161401484A0, 0x0000000000000000,
        0xA56FA5B99019A5C8, 0x0000000000000000,
        0xCECB8F27F4200F3A, 0x0000000000000000,
        0x813F3978F8940984, 0x4000000000000000,
        0xA18F07D736B90BE5, 0x5000000000000000,
        0xC9F2C9CD04674EDE, 0xA400000000000000,
        0xFC6F7C4045812296, 0x4D00000000000000,
        0x9DC5ADA82B70B59D, 0xF020000000000000,
        0xC5371912364CE305, 0x6C28000000000000,
        0xF684DF56C3E01BC6, 0xC732000000000000,
        0x9A130B963A6C115C, 0x3C7F400000000000,
        0xC097CE7BC90715B3, 0x4B9F100000000000,
        0xF0BDC21ABB48DB20, 0x1E86D40000000000,
        0x96769950B50D88F4, 0x1314448000000000
    ];

    /// <summary>
    /// <c>floor((2^192 - 1) / d') - 2^64</c> for each normalized two-word divisor d'. The
    /// 3-by-2 division step multiplies by this word.
    /// </summary>
    private static ReadOnlySpan<ulong> WideInverses =>
    [
        0x79CA10C9242235D5,
        0x2E3B40A0E9B4F7DD,
        0xE392010175EE5962,
        0x82DB34012B25144E,
        0x357C299A88EA76A5,
        0xEF2D0F5DA7DD8AA2,
        0x8C240C4AECB13BB5,
        0x3CE9A36F23C0FC90,
        0xFB0F6BE50601941B,
        0x95A5EFEA6B34767C,
        0x4484BFEEBC29F863,
        0x039D66589687F9E9,
        0x9F623D5A8A732974,
        0x4C4E977BA1F5BAC3,
        0x09D8792FB4C49569,
        0xA95A5B7F87A0EF0F,
        0x54484932D2E725A5,
        0x1039D428A8B8EAEA,
        0xB38FB9DAA78E44AB
    ];

    /// <summary>
    /// The reciprocal seed for each value of the divisor's top 9 bits, from 256 up:
    /// <c>floor((2^19 - 3 * 2^8) / d9)</c>. The seed has 11 correct bits, and three
    /// refinements bring it to 64.
    /// </summary>
    private static ReadOnlySpan<ushort> ReciprocalSeeds =>
    [
        2045, 2037, 2029, 2021, 2013, 2005, 1998, 1990, 1983, 1975, 1968, 1960, 1953, 1946, 1938, 1931,
        1924, 1917, 1910, 1903, 1896, 1889, 1883, 1876, 1869, 1863, 1856, 1849, 1843, 1836, 1830, 1824,
        1817, 1811, 1805, 1799, 1792, 1786, 1780, 1774, 1768, 1762, 1756, 1750, 1745, 1739, 1733, 1727,
        1722, 1716, 1710, 1705, 1699, 1694, 1688, 1683, 1677, 1672, 1667, 1661, 1656, 1651, 1646, 1641,
        1636, 1630, 1625, 1620, 1615, 1610, 1605, 1600, 1596, 1591, 1586, 1581, 1576, 1572, 1567, 1562,
        1558, 1553, 1548, 1544, 1539, 1535, 1530, 1526, 1521, 1517, 1513, 1508, 1504, 1500, 1495, 1491,
        1487, 1483, 1478, 1474, 1470, 1466, 1462, 1458, 1454, 1450, 1446, 1442, 1438, 1434, 1430, 1426,
        1422, 1418, 1414, 1411, 1407, 1403, 1399, 1396, 1392, 1388, 1384, 1381, 1377, 1374, 1370, 1366,
        1363, 1359, 1356, 1352, 1349, 1345, 1342, 1338, 1335, 1332, 1328, 1325, 1322, 1318, 1315, 1312,
        1308, 1305, 1302, 1299, 1295, 1292, 1289, 1286, 1283, 1280, 1276, 1273, 1270, 1267, 1264, 1261,
        1258, 1255, 1252, 1249, 1246, 1243, 1240, 1237, 1234, 1231, 1228, 1226, 1223, 1220, 1217, 1214,
        1211, 1209, 1206, 1203, 1200, 1197, 1195, 1192, 1189, 1187, 1184, 1181, 1179, 1176, 1173, 1171,
        1168, 1165, 1163, 1160, 1158, 1155, 1153, 1150, 1148, 1145, 1143, 1140, 1138, 1135, 1133, 1130,
        1128, 1125, 1123, 1121, 1118, 1116, 1113, 1111, 1109, 1106, 1104, 1102, 1099, 1097, 1095, 1092,
        1090, 1088, 1086, 1083, 1081, 1079, 1077, 1074, 1072, 1070, 1068, 1066, 1064, 1061, 1059, 1057,
        1055, 1053, 1051, 1049, 1047, 1044, 1042, 1040, 1038, 1036, 1034, 1032, 1030, 1028, 1026, 1024
    ];

    /// <summary>10 to the power <paramref name="power"/>, which must be at most <see cref="MaxPower"/>.</summary>
    /// <param name="power">The power of ten, from 0 to <see cref="MaxPower"/>. It is not range-checked.</param>
    /// <returns>10^<paramref name="power"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong PowerOfTen(int power)
    {
        return Unsafe.Add(ref MemoryMarshal.GetReference(Powers), power);
    }

    /// <summary>10 to the power <paramref name="power"/> in two words. The power must be at most <see cref="MaxWidePower"/>.</summary>
    /// <param name="power">The power of ten, from 0 to <see cref="MaxWidePower"/>. It is not range-checked.</param>
    /// <returns>10^<paramref name="power"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer WidePowerOfTen(int power)
    {
        ref var entry = ref Unsafe.Add(ref MemoryMarshal.GetReference(WidePowers), power * 2);
        return new Decimal128Integer(entry, Unsafe.Add(ref entry, 1));
    }

    /// <summary>10 to the power <paramref name="power"/> in four words. The power must be at most <see cref="MaxLongPower"/>.</summary>
    /// <param name="power">The power of ten, from 0 to <see cref="MaxLongPower"/>. It is not range-checked.</param>
    /// <returns>10^<paramref name="power"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128LongInteger LongPowerOfTen(int power)
    {
        ref var entry = ref Unsafe.Add(ref MemoryMarshal.GetReference(LongPowers), power * 4);
        return new Decimal128LongInteger(entry, Unsafe.Add(ref entry, 1), Unsafe.Add(ref entry, 2),
            Unsafe.Add(ref entry, 3));
    }

    /// <summary>The multiplier for a power. Tests use it to check the table.</summary>
    /// <param name="power">The power of ten, from 0 to <see cref="MaxPower"/>.</param>
    /// <returns><c>ceil(2^(64 + shift) / 5^power)</c>, or 0 for power zero.</returns>
    public static ulong Multiplier(int power)
    {
        return Multipliers[power];
    }

    /// <summary>The post-shift for a power. Tests use it to check the table.</summary>
    /// <param name="power">The power of ten, from 0 to <see cref="MaxPower"/>.</param>
    /// <returns><c>floor(log2 5^power)</c>.</returns>
    public static int Shift(int power)
    {
        return Shifts[power];
    }

    /// <summary>The normalization shift for a power. Tests use it to check the table.</summary>
    /// <param name="power">The power of ten, from 0 to <see cref="MaxPower"/>.</param>
    /// <returns>The left shift that moves the top bit of 10^<paramref name="power"/> to bit 63.</returns>
    public static int DivisorShift(int power)
    {
        return DivisorShifts[power];
    }

    /// <summary>The normalized divisor for a power. Tests use it to check the table.</summary>
    /// <param name="power">The power of ten, from 0 to <see cref="MaxPower"/>.</param>
    /// <returns>10^<paramref name="power"/> shifted left by <see cref="DivisorShift"/>.</returns>
    public static ulong NormalizedDivisor(int power)
    {
        return NormalizedDivisors[power];
    }

    /// <summary>The reciprocal word for a power. Tests use it to check the table.</summary>
    /// <param name="power">The power of ten, from 0 to <see cref="MaxPower"/>.</param>
    /// <returns><c>floor((2^128 - 1) / d') - 2^64</c> for the normalized divisor d'.</returns>
    public static ulong Inverse(int power)
    {
        return Inverses[power];
    }

    /// <summary>The normalization shift for a power from 20 up. Tests use it to check the table.</summary>
    /// <param name="power">The power of ten, from 20 to <see cref="MaxWidePower"/>.</param>
    /// <returns>The left shift that moves the top bit of 10^<paramref name="power"/> to the top of its high word.</returns>
    public static int WideDivisorShift(int power)
    {
        return WideDivisorShifts[power - MaxPower - 1];
    }

    /// <summary>The normalized divisor for a power from 20 up. Tests use it to check the table.</summary>
    /// <param name="power">The power of ten, from 20 to <see cref="MaxWidePower"/>.</param>
    /// <returns>10^<paramref name="power"/> shifted left by <see cref="WideDivisorShift"/>.</returns>
    public static Decimal128Integer WideNormalizedDivisor(int power)
    {
        var index = (power - MaxPower - 1) * 2;
        return new Decimal128Integer(WideNormalizedDivisors[index], WideNormalizedDivisors[index + 1]);
    }

    /// <summary>The 3-by-2 reciprocal word for a power from 20 up. Tests use it to check the table.</summary>
    /// <param name="power">The power of ten, from 20 to <see cref="MaxWidePower"/>.</param>
    /// <returns><c>floor((2^192 - 1) / d') - 2^64</c> for the normalized two-word divisor d'.</returns>
    public static ulong WideInverse(int power)
    {
        return WideInverses[power - MaxPower - 1];
    }

    /// <summary>The reciprocal seed for a normalized divisor's top 9 bits, which are at least 256.</summary>
    /// <param name="topBits">The divisor's top 9 bits, from 256 to 511. It is not range-checked.</param>
    /// <returns>An 11-bit estimate of the reciprocal.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ReciprocalSeed(int topBits)
    {
        return Unsafe.Add(ref MemoryMarshal.GetReference(ReciprocalSeeds), topBits - 256);
    }

    /// <summary>Half of 10 to the power <paramref name="power"/>, used to classify a discarded part.</summary>
    /// <param name="power">The power of ten, from 0 to <see cref="MaxPower"/>. It is not range-checked.</param>
    /// <returns>10^<paramref name="power"/> / 2, which is 0 for power zero.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong HalfPowerOfTen(int power)
    {
        return PowerOfTen(power) >> 1;
    }

    /// <summary>Half of 10 to the power <paramref name="power"/>, in two words. The power must be at most <see cref="MaxWidePower"/>.</summary>
    /// <param name="power">The power of ten, from 0 to <see cref="MaxWidePower"/>. It is not range-checked.</param>
    /// <returns>10^<paramref name="power"/> / 2, which is 0 for power zero.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer WideHalfPowerOfTen(int power)
    {
        return WidePowerOfTen(power).Halved();
    }

    /// <summary>
    /// A two-word value times 10 to the power <paramref name="power"/>. The power must be at
    /// most <see cref="MaxWidePower"/>, and the caller must know the product fits in two
    /// words.
    /// </summary>
    /// <remarks>
    /// This always uses the two-word multiply. Choosing a one-word multiply when the power
    /// fits in a word needed a branch that depends on the data, and its mispredictions cost
    /// more than the multiply saved.
    /// </remarks>
    /// <param name="value">The value to scale.</param>
    /// <param name="power">The power of ten, from 0 to <see cref="MaxWidePower"/>.</param>
    /// <returns><paramref name="value"/> * 10^<paramref name="power"/>, truncated to two words.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer Scale(Decimal128Integer value, int power)
    {
        return value.Multiply(WidePowerOfTen(power));
    }

    /// <summary>
    /// A two-word value times 10 to the power <paramref name="power"/>, in four words. The
    /// power must be at most <see cref="MaxLongPower"/>, and the caller must know the product
    /// fits in four words. It is one multiply of two words by the power's four words,
    /// truncated to four words.
    /// </summary>
    /// <param name="value">The value to scale.</param>
    /// <param name="power">The power of ten, from 0 to <see cref="MaxLongPower"/>.</param>
    /// <returns><paramref name="value"/> * 10^<paramref name="power"/>, truncated to four words.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128LongInteger ScaleLong(Decimal128Integer value, int power)
    {
        return Decimal128LongInteger.Multiply(value, LongPowerOfTen(power));
    }

    /// <summary>
    /// Divides one word by 10 to the power <paramref name="power"/> and returns the
    /// remainder too. The power must be between 1 and <see cref="MaxPower"/>.
    /// </summary>
    /// <param name="value">The dividend.</param>
    /// <param name="power">The power of ten to divide by, from 1 to <see cref="MaxPower"/>. It is not range-checked.</param>
    /// <param name="remainder">Receives the remainder.</param>
    /// <returns>The quotient.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong DivRemPowerOfTen(ulong value, int power, out ulong remainder)
    {
        var shifted = value >> power;
        var multiplier = Unsafe.Add(ref MemoryMarshal.GetReference(Multipliers), power);
        var shift = Unsafe.Add(ref MemoryMarshal.GetReference(Shifts), power);
        var quotient = Math.BigMul(shifted, multiplier, out _) >> shift;
        remainder = value - (quotient * PowerOfTen(power));
        return quotient;
    }

    /// <summary>
    /// Divides two words by 10 to the power <paramref name="power"/> and returns the
    /// remainder too. The power must be between 1 and <see cref="MaxPower"/>.
    /// </summary>
    /// <remarks>
    /// The high word is divided alone, exactly, with the reciprocal. Its remainder and the
    /// low word are then divided with one step of the 2-by-1 division. The pair is shifted
    /// left by the divisor's normalization shift, and the high part is below the normalized
    /// divisor because the remainder was below the power. The final remainder is shifted
    /// back.
    /// </remarks>
    /// <param name="value">The dividend.</param>
    /// <param name="power">The power of ten to divide by, from 1 to <see cref="MaxPower"/>. It is not range-checked.</param>
    /// <param name="remainder">Receives the remainder, which is below 10^<paramref name="power"/>.</param>
    /// <returns>The quotient.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Decimal128Integer DivRemPowerOfTen(Decimal128Integer value, int power, out ulong remainder)
    {
        var high = DivRemPowerOfTen(value.High, power, out var rest);

        var shift = Unsafe.Add(ref MemoryMarshal.GetReference(DivisorShifts), power);
        var divisor = Unsafe.Add(ref MemoryMarshal.GetReference(NormalizedDivisors), power);
        var inverse = Unsafe.Add(ref MemoryMarshal.GetReference(Inverses), power);

        // A shift by 64 is undefined for a 64-bit word, so the bits that cross the word
        // boundary are moved with two shifts, neither larger than 63.
        var top = (rest << shift) | ((value.Low >> 1) >> (63 - shift));
        var bottom = value.Low << shift;

        var low = DivideWord(ref top, bottom, divisor, inverse);
        remainder = top >> shift;
        return new Decimal128Integer(high, low);
    }

    /// <summary>
    /// Divides four words by 10 to the power <paramref name="power"/> and returns the
    /// remainder too. The power must be between 1 and <see cref="MaxPower"/>. As for two
    /// words, the top word is a one-word division and the three words below it are steps of
    /// the 2-by-1 division.
    /// </summary>
    /// <param name="value">The dividend.</param>
    /// <param name="power">The power of ten to divide by, from 1 to <see cref="MaxPower"/>. It is not range-checked.</param>
    /// <param name="remainder">Receives the remainder, which is below 10^<paramref name="power"/>.</param>
    /// <returns>The quotient.</returns>
    public static Decimal128LongInteger DivRemPowerOfTen(Decimal128LongInteger value, int power, out ulong remainder)
    {
        var word3 = DivRemPowerOfTen(value.Word3, power, out var rest);

        var shift = Unsafe.Add(ref MemoryMarshal.GetReference(DivisorShifts), power);
        var divisor = Unsafe.Add(ref MemoryMarshal.GetReference(NormalizedDivisors), power);
        var inverse = Unsafe.Add(ref MemoryMarshal.GetReference(Inverses), power);

        var top = (rest << shift) | ((value.Word2 >> 1) >> (63 - shift));
        var upper = (value.Word2 << shift) | ((value.Word1 >> 1) >> (63 - shift));
        var lower = (value.Word1 << shift) | ((value.Word0 >> 1) >> (63 - shift));
        var bottom = value.Word0 << shift;

        var word2 = DivideWord(ref top, upper, divisor, inverse);
        var word1 = DivideWord(ref top, lower, divisor, inverse);
        var word0 = DivideWord(ref top, bottom, divisor, inverse);
        remainder = top >> shift;
        return new Decimal128LongInteger(word3, word2, word1, word0);
    }

    /// <summary>
    /// Divides two words by 10 to the power <paramref name="power"/> and returns the
    /// two-word remainder too. The power can be up to <see cref="MaxWidePower"/>. For powers
    /// above 19, it does two divisions and combines their remainders.
    /// </summary>
    /// <param name="value">The dividend.</param>
    /// <param name="power">The power of ten to divide by, from 1 to <see cref="MaxWidePower"/>.</param>
    /// <param name="remainder">Receives the remainder, which is below 10^<paramref name="power"/>.</param>
    /// <returns>The quotient.</returns>
    public static Decimal128Integer DivRemWidePowerOfTen(Decimal128Integer value, int power,
        out Decimal128Integer remainder)
    {
        if (power <= MaxPower)
        {
            var quotient = DivRemPowerOfTen(value, power, out var rest);
            remainder = Decimal128Integer.FromUInt64(rest);
            return quotient;
        }

        var first = DivRemPowerOfTen(value, MaxPower, out var lowRest);
        var second = DivRemPowerOfTen(first, power - MaxPower, out var highRest);
        remainder = Decimal128Integer.FromUInt64(highRest).MultiplyBy(PowerOfTen(MaxPower)) + lowRest;
        return second;
    }

    /// <summary>
    /// Divides four words by 10 to the power <paramref name="power"/>, when the quotient
    /// fits in two words. The power must be between 20 and <see cref="MaxWidePower"/>. The
    /// division is two steps of the 3-by-2 division by the normalized power. If the quotient
    /// does not fit, nothing is divided and the method returns false. No value produced by
    /// the arithmetic causes that.
    /// </summary>
    /// <param name="value">The dividend.</param>
    /// <param name="power">The power of ten to divide by, from 20 to <see cref="MaxWidePower"/>. It is not range-checked.</param>
    /// <param name="quotient">Receives the quotient, or zero if it does not fit in two words.</param>
    /// <param name="remainder">Receives the remainder, or zero if the quotient does not fit in two words.</param>
    /// <returns>True if the quotient fits in two words and the division was done.</returns>
    public static bool DivRemWidePowerOfTenIfFits(Decimal128LongInteger value, int power,
        out Decimal128Integer quotient, out Decimal128Integer remainder)
    {
        var index = power - MaxPower - 1;
        var shift = Unsafe.Add(ref MemoryMarshal.GetReference(WideDivisorShifts), index);
        ref var divisor = ref Unsafe.Add(ref MemoryMarshal.GetReference(WideNormalizedDivisors), index * 2);
        var divisorHigh = divisor;
        var divisorLow = Unsafe.Add(ref divisor, 1);
        var inverse = Unsafe.Add(ref MemoryMarshal.GetReference(WideInverses), index);

        var word3 = (value.Word3 << shift) | ((value.Word2 >> 1) >> (63 - shift));
        var word2 = (value.Word2 << shift) | ((value.Word1 >> 1) >> (63 - shift));
        var word1 = (value.Word1 << shift) | ((value.Word0 >> 1) >> (63 - shift));
        var word0 = value.Word0 << shift;

        // The quotient fits in two words if and only if the shifted value's top two words
        // are below the shifted divisor and no bits were shifted out above them.
        var lost = (value.Word3 >> 1) >> (63 - shift);
        if (lost != 0 || new Decimal128Integer(word3, word2) >= new Decimal128Integer(divisorHigh, divisorLow))
        {
            quotient = default;
            remainder = default;
            return false;
        }

        var high = DivideThreeByTwo(ref word3, ref word2, word1, divisorHigh, divisorLow, inverse);
        var low = DivideThreeByTwo(ref word3, ref word2, word0, divisorHigh, divisorLow, inverse);
        remainder = new Decimal128Integer(word3 >> shift, (word2 >> shift) | ((word3 << 1) << (63 - shift)));
        quotient = new Decimal128Integer(high, low);
        return true;
    }

    /// <summary>
    /// One step of the 2-by-1 division: divides the remainder and the next word by the
    /// normalized divisor. The estimate is the reciprocal times the high word, plus the
    /// dividend, plus 1. It is at most 1 too high or 1 too low, and two comparisons correct
    /// it.
    /// </summary>
    /// <remarks>
    /// The estimate is 1 too high about half the time, so that correction uses a mask
    /// instead of a branch. 1 too low is rare, so its branch predicts well.
    /// </remarks>
    /// <param name="remainder">The high word of the dividend, which must be below <paramref name="divisor"/>. Receives the new remainder.</param>
    /// <param name="word">The low word of the dividend.</param>
    /// <param name="divisor">The normalized divisor, with its top bit set.</param>
    /// <param name="inverse">The divisor's reciprocal word, as <see cref="Inverse"/> gives.</param>
    /// <returns>The quotient word.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong DivideWord(ref ulong remainder, ulong word, ulong divisor, ulong inverse)
    {
        var high = remainder;
        var quotientHigh = Math.BigMul(inverse, high, out var quotientLow);
        quotientLow += word;
        quotientHigh += high + Unsafe.BitCast<bool, byte>(quotientLow < word);
        quotientHigh++;

        var rest = word - (quotientHigh * divisor);

        var over = 0UL - Unsafe.BitCast<bool, byte>(rest > quotientLow);
        quotientHigh += over;
        rest += divisor & over;

        if (rest >= divisor)
        {
            quotientHigh++;
            rest -= divisor;
        }

        remainder = rest;
        return quotientHigh;
    }

    /// <summary>
    /// One step of the 3-by-2 division: divides the two-word remainder and the next word by
    /// the normalized two-word divisor, and leaves the new remainder in place. The estimate
    /// is the reciprocal times the top word, plus the top two words, plus 1. It is at most 1
    /// too high or 1 too low. The divisor is subtracted once in advance for the +1, and the
    /// first correction adds it back.
    /// </summary>
    /// <remarks>
    /// As in the one-word step, the estimate is 1 too high about half the time, so that
    /// correction uses a mask instead of a branch. 1 too low is rare.
    /// </remarks>
    /// <param name="remainderHigh">The top word of the dividend. Receives the high word of the new remainder.</param>
    /// <param name="remainderLow">The middle word of the dividend. Receives the low word of the new remainder.</param>
    /// <param name="word">The bottom word of the dividend.</param>
    /// <param name="divisorHigh">The high word of the normalized divisor, with its top bit set.</param>
    /// <param name="divisorLow">The low word of the normalized divisor.</param>
    /// <param name="inverse">The divisor's reciprocal word, as <see cref="WideInverse"/> gives.</param>
    /// <returns>The quotient word.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong DivideThreeByTwo(ref ulong remainderHigh, ref ulong remainderLow, ulong word,
        ulong divisorHigh, ulong divisorLow, ulong inverse)
    {
        var top = remainderHigh;
        var middle = remainderLow;

        var quotient = Math.BigMul(inverse, top, out var quotientLow);
        quotientLow += middle;
        quotient += top + Unsafe.BitCast<bool, byte>(quotientLow < middle);

        var high = middle - (quotient * divisorHigh);

        var productHigh = Math.BigMul(quotient, divisorLow, out var productLow);
        var low = word - productLow;
        high -= productHigh + Unsafe.BitCast<bool, byte>(word < productLow);

        var borrow = Unsafe.BitCast<bool, byte>(low < divisorLow);
        low -= divisorLow;
        high -= divisorHigh + borrow;

        quotient++;

        var over = 0UL - Unsafe.BitCast<bool, byte>(high >= quotientLow);
        quotient += over;
        var restoredLow = divisorLow & over;
        low += restoredLow;
        high += (divisorHigh & over) + Unsafe.BitCast<bool, byte>(low < restoredLow);

        if ((high > divisorHigh) | ((high == divisorHigh) & (low >= divisorLow)))
        {
            quotient++;
            var borrowAgain = Unsafe.BitCast<bool, byte>(low < divisorLow);
            low -= divisorLow;
            high -= divisorHigh + borrowAgain;
        }

        remainderHigh = high;
        remainderLow = low;
        return quotient;
    }

    /// <summary>
    /// The number of decimal digits in a word. Zero has one digit.
    /// </summary>
    /// <remarks>
    /// The bit length gives the digit count to within one, because each bit is worth
    /// log10(2) of a digit. One comparison with a power of ten gives the exact count. OR-ing
    /// in a 1 makes zero count as one digit without a separate branch.
    /// </remarks>
    /// <param name="value">The value to measure.</param>
    /// <returns>The number of decimal digits, from 1 to 20.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CountDigits(ulong value)
    {
        var nonZero = value | 1;
        var bitLength = 64 - BitOperations.LeadingZeroCount(nonZero);
        var digits = ((bitLength * 1233) >> 12) + 1;

        // The estimate is one too high for values below the power it names. That is random
        // for mixed values, so it is corrected with a comparison instead of a branch.
        return digits - Unsafe.BitCast<bool, byte>(nonZero < PowerOfTen(digits - 1));
    }

    /// <summary>The number of decimal digits in a two-word value. Zero has one digit.</summary>
    /// <remarks>
    /// The bit length comes from the high word if it is non-zero, and from the low word
    /// otherwise. The choice uses a mask, because a coefficient is as likely to fit in one
    /// word as not, so a branch would mispredict half the time. The 1 OR-ed into the low
    /// word makes zero count as one digit. It cannot change any other comparison, because
    /// every power of ten above 1 is even.
    /// </remarks>
    /// <param name="value">The value to measure. It must be below 10^38.</param>
    /// <returns>The number of decimal digits, from 1 to 38.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CountDigits(Decimal128Integer value)
    {
        var nonZero = new Decimal128Integer(value.High, value.Low | 1);
        var highZero = -(int)Unsafe.BitCast<bool, byte>(value.High == 0);
        var lowLeadingZeros = BitOperations.LeadingZeroCount(nonZero.Low);
        var bitLength = 128 - BitOperations.LeadingZeroCount(value.High) - (lowLeadingZeros & highZero);
        var digits = ((bitLength * 1233) >> 12) + 1;
        return digits - Unsafe.BitCast<bool, byte>(nonZero < WidePowerOfTen(digits - 1));
    }

    /// <summary>The number of decimal digits in a four-word value. Zero has one digit.</summary>
    /// <param name="value">The value to measure. It must be below 10^77.</param>
    /// <returns>The number of decimal digits, from 1 to 77.</returns>
    public static int CountDigits(Decimal128LongInteger value)
    {
        if (value.IsInteger)
        {
            return CountDigits(value.ToInteger());
        }

        var bitLength = value.Word3 != 0
            ? 256 - BitOperations.LeadingZeroCount(value.Word3)
            : 192 - BitOperations.LeadingZeroCount(value.Word2);

        var digits = ((bitLength * 1233) >> 12) + 1;
        return digits - Unsafe.BitCast<bool, byte>(value.CompareTo(LongPowerOfTen(digits - 1)) < 0);
    }
}
