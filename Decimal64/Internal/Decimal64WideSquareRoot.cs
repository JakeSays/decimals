// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The square root on wide numbers, which the hypotenuse needs at a precision wider than
/// the format's. The square root at the format's precision is in
/// <see cref="Decimal64SquareRoot"/>.
/// </summary>
/// <remarks>
/// The coefficient is scaled until its integer square root has one digit more than the
/// precision. That digit is the guard digit, and any remainder becomes the sticky bit. The
/// scaling must keep the exponent even, because the root's exponent is half of it. An
/// exact root is then shortened toward the exponent the specification prefers, which is
/// half the operand's.
/// </remarks>
internal static unsafe class Decimal64WideSquareRoot
{
    /// <summary>
    /// The number of work buffers the root needs, each sized for the radicand: the scaled
    /// radicand, the current guess, the quotient of each Newton step, the division's
    /// remainder, the sum for the next guess, and the square used to check exactness.
    /// </summary>
    public const int WorkBuffers = 6;

    /// <summary>The square root of a value, rounded to the context.</summary>
    /// <param name="result">Receives the rounded root.</param>
    /// <param name="value">The operand.</param>
    /// <param name="context">The precision, rounding, and exponent limits to apply.</param>
    /// <param name="status">Receives the conditions the operation raises.</param>
    /// <param name="work">A block of <see cref="WorkBuffers"/> buffers, each <paramref name="bufferLength"/> units long.</param>
    /// <param name="bufferLength">The length of each work buffer, in units.</param>
    /// <param name="accumulator">A 64-bit work buffer for squaring the root, as <see cref="Decimal64WideMultiply.Multiply"/> requires.</param>
    public static void SquareRoot(ref Decimal64WideNumber result, Decimal64WideNumber value, Decimal64WideContext context,
        ref Decimal64Status status, uint* work, int bufferLength, ulong* accumulator)
    {
        if (!value.IsFinite)
        {
            if (value.IsInfinity && value.IsNegative)
            {
                status |= Decimal64Status.InvalidOperation;
                result.SetZero();
                result.Kind = Decimal64Kind.QuietNaN;
                result.IsNegative = false;
                return;
            }

            result.CopyFrom(value);
            return;
        }

        var idealExponent = value.Exponent >= 0 ? value.Exponent / 2 : (value.Exponent - 1) / 2;

        if (value.IsZero)
        {
            result.SetZero();
            result.IsNegative = value.IsNegative;
            result.Exponent = idealExponent;
            Decimal64WideRounding.Finalize(ref result, 0, context, ref status);
            return;
        }

        if (value.IsNegative)
        {
            status |= Decimal64Status.InvalidOperation;
            result.SetZero();
            result.Kind = Decimal64Kind.QuietNaN;
            result.IsNegative = false;
            return;
        }

        // The root's exponent is half the operand's, so the scaling must leave the exponent
        // even.
        var shift = (2 * (context.Digits + 1)) - value.Digits;
        if (((value.Exponent - shift) & 1) != 0)
        {
            shift++;
        }

        var radicand = work;
        var guess = work + bufferLength;
        var quotient = work + (bufferLength * 2);
        var remainder = work + (bufferLength * 3);
        var sum = work + (bufferLength * 4);
        var square = work + (bufferLength * 5);

        var radicandLength = value.Units;
        for (var index = 0; index < radicandLength; index++)
        {
            radicand[index] = value.Lsu[index];
        }

        radicandLength = Decimal64WideUnits.ShiftUp(radicand, radicandLength, shift);
        var radicandDigits = value.Digits + shift;

        var rootLength = IntegerSquareRoot(radicand, radicandLength, radicandDigits,
            guess, quotient, remainder, sum);

        var exponent = (value.Exponent - shift) / 2;
        var residue = 0;

        // The root is exact if its square equals the radicand.
        var rootNumber = default(Decimal64WideNumber);
        rootNumber.Lsu = guess;
        rootNumber.Units = rootLength;
        rootNumber.Kind = Decimal64Kind.Finite;

        var squareNumber = default(Decimal64WideNumber);
        squareNumber.Lsu = square;
        squareNumber.Kind = Decimal64Kind.Finite;

        Decimal64WideMultiply.Multiply(ref squareNumber, rootNumber, rootNumber, accumulator);

        var exact = Decimal64WideUnits.Compare(square, squareNumber.Units, radicand, radicandLength) == 0;

        for (var index = 0; index < rootLength; index++)
        {
            result.Lsu[index] = guess[index];
        }

        result.Units = rootLength;
        result.Kind = Decimal64Kind.Finite;
        result.IsNegative = false;
        result.Exponent = exponent;
        result.CountDigits();

        if (exact)
        {
            // Shorten toward the exponent the specification prefers, which is half the
            // operand's, by removing the trailing zeros the scaling added.
            while (result.Exponent < idealExponent
                && Decimal64WideUnits.DigitAt(result.Lsu, result.Units, 0) == 0
                && !result.IsZero)
            {
                result.Units = Decimal64WideUnits.ShiftDown(result.Lsu, result.Units, 1);
                result.Exponent++;
                result.CountDigits();
            }
        }
        else
        {
            // The root has a guard digit, so the remainder only shows that something non-zero
            // is below it. That is a sticky bit, which is residue 1.
            residue = 1;
        }

        Decimal64WideRounding.SetCoefficient(ref result, context.Digits, ref residue, ref status);
        Decimal64WideRounding.Finalize(ref result, residue, context, ref status);
    }

    /// <summary>
    /// The largest integer whose square does not exceed the value, by Newton's method. The
    /// starting guess is the power of ten just above the root, which is certain to be too
    /// high and needs no binary conversion.
    /// </summary>
    /// <returns>The number of units in the root, which is left in <paramref name="guess"/>.</returns>
    private static int IntegerSquareRoot(uint* value, int valueLength, int valueDigits,
        uint* guess, uint* quotient, uint* remainder, uint* sum)
    {
        var guessLength = Decimal64WideUnits.SetPowerOfTen(guess, (valueDigits + 1) / 2);

        while (true)
        {
            var quotientLength = Decimal64WideDivide.DivRem(value, valueLength, guess, guessLength,
                remainder, quotient, out _);

            // The next guess is the mean of this guess and the value divided by it.
            var sumLength = Decimal64WideUnits.AddSub(guess, guessLength, quotient, quotientLength, 0,
                sum, 1);

            sumLength = Decimal64WideUnits.Halve(sum, Math.Abs(sumLength));

            if (Decimal64WideUnits.Compare(sum, sumLength, guess, guessLength) >= 0)
            {
                // The guess stopped decreasing, so the previous guess is the root.
                return guessLength;
            }

            for (var index = 0; index < sumLength; index++)
            {
                guess[index] = sum[index];
            }

            guessLength = sumLength;
        }
    }
}
