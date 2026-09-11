// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Decimals;

/// <summary>
/// Densely packed decimal: the ten-bit declet that carries three decimal digits, used by
/// the IEEE interchange encodings.
/// </summary>
/// <remarks>
/// <para>
/// The tables come from <see cref="DpdTables"/>, which <c>dpd-tables</c> generates from the
/// bit rules. They sit in the assembly's data section, so there is nothing to build at
/// startup and no class constructor between a caller and a lookup; taking a pointer to one
/// needs no pinning, because a data-section span does not move.
/// </para>
/// <para>
/// Twenty-four of the 1024 declets are non-canonical -- they decode to a value another
/// declet also encodes -- and encoding always produces the canonical one.
/// </para>
/// </remarks>
internal static unsafe class Dpd
{
    public const int DecletCount = 1024;
    public const int ValueCount = 1000;

    /// <summary>Digits in a declet, and so the stride of the unpacking table.</summary>
    public const int DigitsPerDeclet = 3;

    /// <summary>A declet is ten bits, so the packing table's stride is two.</summary>
    private const int BytesPerDeclet = 2;

    /// <summary>
    /// The unpacking table: three digits at <c>declet * 3</c>, most significant first.
    /// </summary>
    private static byte* DecletToBcd
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(DpdTables.DecletToBcd));
    }

    /// <summary>
    /// The packing table: a declet at <c>nibbles * 2</c>, least significant byte first.
    /// </summary>
    private static byte* BcdToDeclet
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(DpdTables.BcdToDeclet));
    }

    /// <summary>
    /// Writes a declet's three digits, one per byte and most significant first. This is the
    /// whole of unpacking: three bytes copied, and no arithmetic anywhere.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteDigits(uint declet, byte* destination)
    {
        var source = DecletToBcd + (declet * DigitsPerDeclet);
        destination[0] = source[0];
        destination[1] = source[1];
        destination[2] = source[2];
    }

    /// <summary>
    /// The canonical declet carrying three digits held one per nibble, most significant
    /// first. This is the whole of packing.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint FromNibbles(uint nibbles)
    {
        var source = BcdToDeclet + (nibbles * BytesPerDeclet);
        return (uint)(source[0] | (source[1] << 8));
    }

    /// <summary>
    /// The base-billion splitting table: three digits at <c>value * 3</c>.
    /// </summary>
    private static byte* BinaryToBcd
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(DpdTables.BinaryToBcd));
    }

    /// <summary>
    /// Writes a value 0 through 999 as three digits, one per byte and most significant
    /// first. Laying a base-billion limb back out as digits goes through this three times.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteTriple(uint value, byte* destination)
    {
        var source = BinaryToBcd + (value * DigitsPerDeclet);
        destination[0] = source[0];
        destination[1] = source[1];
        destination[2] = source[2];
    }

    /// <summary>
    /// Turns a ten-bit declet into the three digits it carries, 0 through 999. This is the
    /// interchange boundary's view, not arithmetic's: it forms a number, which is what the
    /// digit form exists to avoid.
    /// </summary>
    public static uint ToBinary(uint declet)
    {
        var source = DecletToBcd + (declet * DigitsPerDeclet);
        return (uint)((source[0] * 100) + (source[1] * 10) + source[2]);
    }

    /// <summary>
    /// Turns a value 0 through 999 into its canonical ten-bit declet.
    /// </summary>
    public static uint ToDeclet(uint value)
    {
        var high = value / 100;
        var middle = (value / 10) % 10;
        var low = value % 10;
        return FromNibbles((high << 8) | (middle << 4) | low);
    }
}
