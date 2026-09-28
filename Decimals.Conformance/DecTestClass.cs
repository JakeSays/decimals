// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Conformance;

/// <summary>
/// The ten results of the corpus's <c>class</c> operation.
/// </summary>
/// <remarks>
/// Laid out in the order every decimal type in the repository uses for its own class
/// enumeration, so a target converts between the two by a cast.
/// </remarks>
public enum DecTestClass
{
    SignalingNaN,
    QuietNaN,
    NegativeInfinity,
    NegativeNormal,
    NegativeSubnormal,
    NegativeZero,
    PositiveZero,
    PositiveSubnormal,
    PositiveNormal,
    PositiveInfinity
}
