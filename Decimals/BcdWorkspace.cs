// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals;

/// <summary>
/// The scratch an operation works in: digit buffers for the operands and the result, and
/// the base-billion arrays multiply and divide need.
/// </summary>
/// <remarks>
/// <para>
/// decNumber declares these as locals of each operation, sized from <c>DECPMAX</c>. Here
/// one caller stack-allocates a single block and this carves it up, which keeps the sizes
/// in one place and the allocation at the top of the call rather than in every routine
/// under it.
/// </para>
/// <para>
/// The block is allocated as words so that the limb and accumulator regions are aligned
/// without anything having to arrange it; the digit buffers follow, and bytes need no
/// alignment of their own.
/// </para>
/// <para>
/// A <c>ref struct</c>, because it is nothing but borrowed stack memory: it cannot be
/// boxed, put on the heap, or captured, and it cannot outlive the frame that made it.
/// </para>
/// </remarks>
internal unsafe ref struct BcdWorkspace
{
    /// <summary>
    /// Digits for one decoded operand, sized for the widest format so a single constant
    /// serves every instantiation. Thirty-four digits of coefficient, room to fold and
    /// align against the widest window, and headroom for a carry.
    /// </summary>
    public const int DigitBufferLength = 128;

    /// <summary>Base-billion limbs: four to an operand at the widest.</summary>
    public const int LimbBufferLength = 16;

    /// <summary>Accumulator columns, which division needs three operands' worth of.</summary>
    public const int AccumulatorLength = 32;

    private const int DigitBufferCount = 8;

    private const int LimbBufferCount = 4;

    /// <summary>
    /// Words in the block an entry point allocates. The accumulator is words already; the
    /// limbs are half a word each and the digits an eighth.
    /// </summary>
    public const int ScratchLength = AccumulatorLength
        + ((LimbBufferLength * LimbBufferCount) / 2)
        + ((DigitBufferLength * DigitBufferCount) / 8);

    public byte* Left;

    public byte* Right;

    public byte* Addend;

    public byte* Result;

    public byte* Product;

    public byte* LeftWork;

    public byte* RightWork;

    /// <summary>
    /// Holds the integer quotient while the remainder is formed from it, since forming the
    /// remainder writes over the result buffer the quotient came out of.
    /// </summary>
    public byte* Integer;

    public uint* LeftLimbs;

    public uint* RightLimbs;

    public uint* Quotient;

    public ulong* Accumulator;

    public uint* WideAccumulator;

    public BcdWorkspace(ulong* scratch)
    {
        Accumulator = scratch;

        var limbs = (uint*)(scratch + AccumulatorLength);
        LeftLimbs = limbs;
        RightLimbs = limbs + LimbBufferLength;
        Quotient = limbs + (LimbBufferLength * 2);
        WideAccumulator = limbs + (LimbBufferLength * 3);

        var digits = (byte*)(limbs + (LimbBufferLength * LimbBufferCount));
        Left = digits;
        Right = digits + DigitBufferLength;
        Addend = digits + (DigitBufferLength * 2);
        Result = digits + (DigitBufferLength * 3);
        Product = digits + (DigitBufferLength * 4);
        LeftWork = digits + (DigitBufferLength * 5);
        RightWork = digits + (DigitBufferLength * 6);
        Integer = digits + (DigitBufferLength * 7);
    }
}
