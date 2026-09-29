// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// A stack of unit buffers that the elementary functions use for temporary values.
/// </summary>
/// <remarks>
/// <para>
/// The series in <see cref="Decimal128Math"/> are written as expressions over values, and
/// each value's coefficient needs a buffer with an owner. The arena owns them: one block,
/// divided into equal slots, handed out in order and released together.
/// </para>
/// <para>
/// A loop takes its temporaries once, before the loop, and reuses them. Taking slots inside
/// a loop would run past the end of the block. <see cref="Mark"/> and <see cref="Release"/>
/// exist for the few places where a section of work returns its slots.
/// </para>
/// </remarks>
internal unsafe ref struct Decimal128WideArena
{
    /// <summary>
    /// The number of units in one slot. The innermost accumulator of the natural logarithm
    /// reaches about 180 digits in this format, and the exact product of two such values
    /// needs twice that. The rest is headroom for the exact-root check, which raises a
    /// candidate to a high degree.
    /// </summary>
    public const int SlotUnits = 192;

    /// <summary>
    /// The number of slots one call can hold at once, across all the frames it opens. The
    /// functions nest (power calls ln, which calls exp), but each frame keeps only its result
    /// before releasing its slots, so nesting needs far fewer slots than it might seem.
    /// </summary>
    public const int SlotCount = 96;

    /// <summary>The number of units an entry point allocates on the stack for the whole call.</summary>
    public const int TotalUnits = SlotUnits * SlotCount;

    /// <summary>
    /// The same block counted in 64-bit words, which is how an entry point allocates it. The
    /// multiply and divide accumulators are words, and allocating words keeps every slot
    /// aligned for them.
    /// </summary>
    public const int TotalWords = TotalUnits / 2;

    private readonly uint* _buffer;

    private int _taken;

    /// <summary>Creates an arena over a block of <see cref="TotalUnits"/> units.</summary>
    /// <param name="buffer">The block to divide into slots. The caller owns it and keeps it alive.</param>
    public Decimal128WideArena(uint* buffer)
    {
        _buffer = buffer;
        _taken = 0;
    }

    /// <summary>
    /// The number of slots handed out so far. Pass it to <see cref="Release"/> to return the
    /// slots taken after it.
    /// </summary>
    public readonly int Mark => _taken;

    /// <summary>Returns every slot taken after <paramref name="mark"/>.</summary>
    /// <param name="mark">A value previously read from <see cref="Mark"/>.</param>
    public void Release(int mark)
    {
        _taken = mark;
    }

    /// <summary>Takes a new slot holding zero.</summary>
    /// <returns>A zero value whose coefficient is stored in the new slot.</returns>
    public Decimal128WideNumber Take()
    {
        var slot = _buffer + (_taken * SlotUnits);
        _taken++;
        return new Decimal128WideNumber(slot);
    }

    /// <summary>Takes a new slot holding a copy of an existing value.</summary>
    /// <param name="source">The value to copy.</param>
    /// <returns>A copy of <paramref name="source"/> whose coefficient is stored in the new slot.</returns>
    public Decimal128WideNumber TakeCopy(Decimal128WideNumber source)
    {
        var number = Take();
        number.CopyFrom(source);
        return number;
    }

    /// <summary>
    /// Takes raw units, for the work buffers the primitives need besides their result.
    /// Slots are contiguous, so several slots form one run.
    /// </summary>
    /// <param name="slots">The number of slots to take.</param>
    /// <returns>A pointer to the first unit of the run.</returns>
    public uint* TakeUnits(int slots = 1)
    {
        var slot = _buffer + (_taken * SlotUnits);
        _taken += slots;
        return slot;
    }
}
