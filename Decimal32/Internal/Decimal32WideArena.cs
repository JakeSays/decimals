// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// A stack of unit buffers the elementary functions draw their temporaries from.
/// </summary>
/// <remarks>
/// <para>
/// The series in <see cref="Decimal32Math"/> are written as expressions over values, and a
/// value here is a coefficient in a buffer somebody has to own. The arena owns them: one
/// block, carved into equal slots, handed out in order and released in bulk.
/// </para>
/// <para>
/// A loop takes its temporaries once, outside itself, and reuses them; taking inside a loop
/// would walk off the end. <see cref="Mark"/> and <see cref="Release"/> exist for the few
/// places where a stretch of work wants its slots back.
/// </para>
/// </remarks>
internal unsafe ref struct Decimal32WideArena
{
    /// <summary>
    /// Units in one slot. The innermost accumulator of power runs to some fifty digits at
    /// this format, and the exact product of two of those needs twice that again; the rest
    /// is headroom for the exact-root check, which raises a candidate to a high degree.
    /// </summary>
    public const int SlotUnits = 96;

    /// <summary>
    /// Slots one call may hold at once, across every frame it opens. The functions nest --
    /// power calls ln, which calls exp -- and each frame keeps only its result before
    /// releasing, so the depth costs far less than the count suggests.
    /// </summary>
    public const int SlotCount = 96;

    /// <summary>Units an entry point stack-allocates for the whole call.</summary>
    public const int TotalUnits = SlotUnits * SlotCount;

    /// <summary>
    /// The same block counted in words, which is what an entry point allocates it as: the
    /// accumulators the multiply and divide want are words, and starting from one keeps
    /// every slot aligned for them without anything having to arrange it.
    /// </summary>
    public const int TotalWords = TotalUnits / 2;

    private readonly uint* _buffer;

    private int _taken;

    public Decimal32WideArena(uint* buffer)
    {
        _buffer = buffer;
        _taken = 0;
    }

    /// <summary>Slots handed out so far, which <see cref="Release"/> winds back to.</summary>
    public readonly int Mark => _taken;

    public void Release(int mark)
    {
        _taken = mark;
    }

    /// <summary>A fresh zero in its own slot.</summary>
    public Decimal32WideNumber Take()
    {
        var slot = _buffer + (_taken * SlotUnits);
        _taken++;
        return new Decimal32WideNumber(slot);
    }

    /// <summary>A slot holding a copy of an existing value.</summary>
    public Decimal32WideNumber TakeCopy(Decimal32WideNumber source)
    {
        var number = Take();
        number.CopyFrom(source);
        return number;
    }

    /// <summary>
    /// Raw units, for the work buffers the primitives need beside their result. Slots are
    /// contiguous, so asking for several gives one run of that many.
    /// </summary>
    public uint* TakeUnits(int slots = 1)
    {
        var slot = _buffer + (_taken * SlotUnits);
        _taken += slots;
        return slot;
    }
}
