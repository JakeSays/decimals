// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Conformance;

/// <summary>
/// The directive state in force for the tests that follow it in a file. A file starts with
/// none of the four required directives set, and a <c>dectest</c> directive does not pass
/// its settings down to the file it names.
/// </summary>
public sealed class DecTestDirectives
{
    public int Precision { get; private set; }

    public DecimalRounding Rounding { get; private set; } = DecimalRounding.HalfEven;

    public int MaxExponent { get; private set; }

    public int MinExponent { get; private set; }

    public bool Extended { get; private set; } = true;

    public bool Clamp { get; private set; }

    public bool RoundingIsKnown { get; private set; } = true;

    public string RoundingName { get; private set; } = string.Empty;

    /// <summary>True once precision, rounding, maxexponent, and minexponent have all been given.</summary>
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

    private static bool TryParseRounding(string name, out DecimalRounding rounding)
    {
        rounding = DecimalRounding.HalfEven;
        switch (name)
        {
            case "ceiling":
                rounding = DecimalRounding.Ceiling;
                return true;
            case "down":
                rounding = DecimalRounding.Down;
                return true;
            case "floor":
                rounding = DecimalRounding.Floor;
                return true;
            case "half_down":
                rounding = DecimalRounding.HalfDown;
                return true;
            case "half_even":
                rounding = DecimalRounding.HalfEven;
                return true;
            case "half_up":
                rounding = DecimalRounding.HalfUp;
                return true;
            case "up":
                rounding = DecimalRounding.Up;
                return true;
            case "05up":
                rounding = DecimalRounding.ZeroFiveUp;
                return true;
            default:
                return false;
        }
    }
}
