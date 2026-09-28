// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Internal;

/// <summary>
/// The square root on the engine's unit arrays, which the hypotenuse needs at a width past
/// the format's own. The format-width root lives in <see cref="Decimal32SquareRoot"/>.
/// </summary>
/// <remarks>
/// The coefficient is scaled until its integer square root carries a digit past the
/// precision -- that digit is the guard -- and whatever the root leaves over becomes the
/// sticky. Scaling has to keep the exponent even, since halving it is what makes the root's
/// exponent. An exact root is then shortened toward the exponent the specification prefers,
/// which is half the operand's.
/// </remarks>
internal static unsafe class Decimal32WideSquareRoot
{
    /// <summary>
    /// Work buffers the root needs, each sized for the radicand: the running guess, the
    /// quotient of each Newton step, the division's remainder, and a square to check
    /// exactness with.
    /// </summary>
    public const int WorkBuffers = 6;

    public static void SquareRoot(ref Decimal32WideNumber result, Decimal32WideNumber value, Decimal32WideContext context,
        ref Decimal32Status status, uint* work, int bufferLength, ulong* accumulator)
    {
        if (!value.IsFinite)
        {
            if (value.IsInfinity && value.IsNegative)
            {
                status |= Decimal32Status.InvalidOperation;
                result.SetZero();
                result.Kind = Decimal32Kind.QuietNaN;
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
            Decimal32WideRounding.Finalize(ref result, 0, context, ref status);
            return;
        }

        if (value.IsNegative)
        {
            status |= Decimal32Status.InvalidOperation;
            result.SetZero();
            result.Kind = Decimal32Kind.QuietNaN;
            result.IsNegative = false;
            return;
        }

        // Halving the exponent is what makes the root's, so the scaling has to leave it even.
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

        radicandLength = Decimal32WideUnits.ShiftUp(radicand, radicandLength, shift);
        var radicandDigits = value.Digits + shift;

        var rootLength = IntegerSquareRoot(radicand, radicandLength, radicandDigits,
            guess, quotient, remainder, sum);

        var exponent = (value.Exponent - shift) / 2;
        var residue = 0;

        // Exact when the root squared is the radicand again.
        var rootNumber = default(Decimal32WideNumber);
        rootNumber.Lsu = guess;
        rootNumber.Units = rootLength;
        rootNumber.Kind = Decimal32Kind.Finite;

        var squareNumber = default(Decimal32WideNumber);
        squareNumber.Lsu = square;
        squareNumber.Kind = Decimal32Kind.Finite;

        Decimal32WideMultiply.Multiply(ref squareNumber, rootNumber, rootNumber, accumulator);

        var exact = Decimal32WideUnits.Compare(square, squareNumber.Units, radicand, radicandLength) == 0;

        for (var index = 0; index < rootLength; index++)
        {
            result.Lsu[index] = guess[index];
        }

        result.Units = rootLength;
        result.Kind = Decimal32Kind.Finite;
        result.IsNegative = false;
        result.Exponent = exponent;
        result.CountDigits();

        if (exact)
        {
            // Shorten toward the exponent the specification prefers, which is half the
            // operand's, giving back the trailing zeros the scaling introduced.
            while (result.Exponent < idealExponent
                && Decimal32WideUnits.DigitAt(result.Lsu, result.Units, 0) == 0
                && !result.IsZero)
            {
                result.Units = Decimal32WideUnits.ShiftDown(result.Lsu, result.Units, 1);
                result.Exponent++;
                result.CountDigits();
            }
        }
        else
        {
            // The root carries a guard digit, so all the remainder says is that something
            // non-zero lies below it: a sticky bit, which is residue 1.
            residue = 1;
        }

        Decimal32WideRounding.SetCoefficient(ref result, context.Digits, ref residue, ref status);
        Decimal32WideRounding.Finalize(ref result, residue, context, ref status);
    }

    /// <summary>
    /// The largest integer whose square does not exceed the value, by Newton's method. The
    /// starting guess is the power of ten just above the root, which is certain to be high
    /// and keeps the whole iteration in the decimal world.
    /// </summary>
    /// <returns>Units in the root, which is left in <paramref name="guess"/>.</returns>
    private static int IntegerSquareRoot(uint* value, int valueLength, int valueDigits,
        uint* guess, uint* quotient, uint* remainder, uint* sum)
    {
        var guessLength = Decimal32WideUnits.SetPowerOfTen(guess, (valueDigits + 1) / 2);

        while (true)
        {
            var quotientLength = Decimal32WideDivide.DivRem(value, valueLength, guess, guessLength,
                remainder, quotient, out _);

            // The next guess is the mean of this one and what it divides into.
            var sumLength = Decimal32WideUnits.AddSub(guess, guessLength, quotient, quotientLength, 0,
                sum, 1);

            sumLength = Decimal32WideUnits.Halve(sum, Math.Abs(sumLength));

            if (Decimal32WideUnits.Compare(sum, sumLength, guess, guessLength) >= 0)
            {
                // No longer decreasing, so the previous guess is the root.
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
