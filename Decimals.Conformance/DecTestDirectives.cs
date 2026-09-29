// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Conformance;

/// <summary>
/// The directive settings that apply to the test lines after them in a file.
/// </summary>
/// <remarks>
/// Each file starts with none of the four required directives set. A <c>dectest</c>
/// directive does not pass its settings to the file it names.
/// </remarks>
public sealed class DecTestDirectives
{
    public int Precision { get; private set; }

    public DecTestRounding Rounding { get; private set; } = DecTestRounding.HalfEven;

    public int MaxExponent { get; private set; }

    public int MinExponent { get; private set; }

    public bool Extended { get; private set; } = true;

    public bool Clamp { get; private set; }

    public bool RoundingIsKnown { get; private set; } = true;

    public string RoundingName { get; private set; } = string.Empty;

    /// <summary>True once precision, rounding, maxexponent, and minexponent are all set.</summary>
    public bool IsComplete { get; private set; }

    private bool _hasPrecision;
    private bool _hasRounding;
    private bool _hasMaxExponent;
    private bool _hasMinExponent;

    public bool TryApply(string keyword, string value, out string error)
    {
        error = string.Empty;

        switch (keyword.ToLowerInvariant())
        {
            case "precision":
                if (!int.TryParse(value, out var precision) || precision < 1)
                {
                    error = $"bad precision '{value}'";
                    return false;
                }

                Precision = precision;
                _hasPrecision = true;
                break;

            case "rounding":
                RoundingName = value.ToLowerInvariant();
                RoundingIsKnown = TryParseRounding(RoundingName, out var rounding);
                if (RoundingIsKnown)
                {
                    Rounding = rounding;
                }

                _hasRounding = true;
                break;

            case "maxexponent":
                if (!int.TryParse(value, out var maxExponent) || maxExponent < 0)
                {
                    error = $"bad maxexponent '{value}'";
                    return false;
                }

                MaxExponent = maxExponent;
                _hasMaxExponent = true;
                break;

            case "minexponent":
                if (!int.TryParse(value, out var minExponent) || minExponent > 0)
                {
                    error = $"bad minexponent '{value}'";
                    return false;
                }

                MinExponent = minExponent;
                _hasMinExponent = true;
                break;

            case "extended":
                if (value is not ("0" or "1"))
                {
                    error = $"bad extended '{value}'";
                    return false;
                }

                Extended = value == "1";
                break;

            case "clamp":
                if (value is not ("0" or "1"))
                {
                    error = $"bad clamp '{value}'";
                    return false;
                }

                Clamp = value == "1";
                break;

            case "version":
                break;

            default:
                error = $"unknown directive '{keyword}'";
                return false;
        }

        IsComplete = _hasPrecision && _hasRounding && _hasMaxExponent && _hasMinExponent;
        return true;
    }

    private static bool TryParseRounding(string name, out DecTestRounding rounding)
    {
        rounding = DecTestRounding.HalfEven;
        switch (name)
        {
            case "ceiling":
                rounding = DecTestRounding.Ceiling;
                return true;
            case "down":
                rounding = DecTestRounding.Down;
                return true;
            case "floor":
                rounding = DecTestRounding.Floor;
                return true;
            case "half_down":
                rounding = DecTestRounding.HalfDown;
                return true;
            case "half_even":
                rounding = DecTestRounding.HalfEven;
                return true;
            case "half_up":
                rounding = DecTestRounding.HalfUp;
                return true;
            case "up":
                rounding = DecTestRounding.Up;
                return true;
            case "05up":
                rounding = DecTestRounding.ZeroFiveUp;
                return true;
            default:
                return false;
        }
    }
}
