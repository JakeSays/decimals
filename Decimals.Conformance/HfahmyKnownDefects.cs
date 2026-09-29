// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Conformance;

/// <summary>
/// Lines in the published vector files that the runner skips.
/// </summary>
/// <remarks>
/// <para>
/// 22 lines are damaged in the published files. Some are broken in two, some are missing
/// the format or an operand, and some are runs of <c>#</c> characters. They are skipped
/// instead of reported as errors, so that any other line that fails to parse still shows
/// up as an error.
/// </para>
/// <para>
/// One vector in the July 2010 decimal64 fused multiply-add set has a wrong result. The
/// exact product of <c>-5064047626626594E276</c> and <c>9999999999990028E64</c> is
/// <c>-50640476266215441317067279604632E340</c>. Rounded toward positive infinity, that is
/// <c>-5064047626621544E356</c>. The vector gives <c>-4131706727960463E341</c>. The
/// February 2011 set regenerates the same models and does not contain this vector.
/// </para>
/// </remarks>
public static class HfahmyKnownDefects
{
    private const string Damaged = "line damaged in the published file";

    private const string WrongResult = "vector gives a wrong result";

    private static readonly HfahmyDefect[] Defects =
    [
        new("2010_07_d64_add_cancel1.txt", 2911, "0E369 ->", Damaged),
        new("2010_07_d64_add_cancel1.txt", 2912, "113323E-398 ", Damaged),
        new("2010_07_d64_add_cancel1.txt", 2915, "91360022097E-398 ", Damaged),
        new("2010_07_d64_add_clamping.txt", 10753, "04478144E369", Damaged),
        new("2010_07_d64_add_clamping.txt", 10757, "52E369", Damaged),
        new("2010_07_d64_add_overflow.txt", 1587, "7412059208726113E-301 ", Damaged),
        new("2010_07_d64_add_overflow.txt", 1783, "E369 ->", Damaged),
        new("2010_07_d64_add_overflow.txt", 1786, "f xo", Damaged),
        new("2010_07_d64_add_result.txt", 2747, "0E369", Damaged),
        new("2010_07_d64_add_result.txt", 2772, "69", Damaged),
        new("2010_07_d64_add_rounding1.txt", 4201, "9674179E245 ", Damaged),
        new("2010_07_d64_add_rounding1.txt", 4202, "99999E308", Damaged),
        new("2010_07_d64_add_rounding2.txt", 8401, "-9279062095039181E276 ", Damaged),
        new("2010_07_d64_add_type2.txt", 9097, "9725E369", Damaged),
        new("2010_07_d64_add_type2.txt", 9099, "3143410E370 ", Damaged),
        new("2010_07_d64_add_type2.txt", 9100, "788052680E369", Damaged),
        new("2010_07_d64_fma_shift1.txt", 15457, "-> -1241399718205089E369", Damaged),
        new("2010_07_d64_fma_type3.txt", 37946, "d64*+ > -5064047626626594E276 ", WrongResult),
        new("2011_02_d64_fma_type1.txt", 35795, "Q Q -> Q i", Damaged),
        new("2011_02_d64_fma_type1.txt", 35797, "S Q S -> Q i", Damaged),
        new("2011_04_d128_sqrt_type1.txt", 726, "d128V =0 -> Q i", Damaged),
        new("2011_09_d128_fma_type4.txt", 233509, "d128*+ > +135611477298866693835828130923706#", Damaged),
        new("2011_09_d128_fma_type4.txt", 233510, "####", Damaged)
    ];

    public static IReadOnlyList<HfahmyDefect> All => Defects;

    /// <summary>Returns true if the line is a known defect, with the reason it is skipped.</summary>
    public static bool TryFind(string fileName, int lineNumber, string line, out string reason)
    {
        foreach (var defect in Defects)
        {
            if (defect.LineNumber == lineNumber
                && string.Equals(defect.FileName, fileName, StringComparison.Ordinal)
                && line.StartsWith(defect.Start, StringComparison.Ordinal))
            {
                reason = defect.Reason;
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }
}
