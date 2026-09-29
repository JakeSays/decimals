// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

namespace Decimals.Conformance;

/// <summary>
/// The ten results of the corpus's <c>class</c> operation.
/// </summary>
/// <remarks>
/// Each decimal type's own class enum uses the same order, so a target converts between
/// the two with a cast.
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
