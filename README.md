# Decimals

IEEE 754 decimal floating-point types for .NET: `Decimal32`, `Decimal64`, and `Decimal128`.
Written in C# with no native dependencies. Requires .NET 10.

Each value has a sign, an integer coefficient, and a base-ten exponent. Decimal fractions
such as `0.1` are stored exactly, so `0.1 + 0.2` equals `0.3`. The exponent range covers
the full IEEE 754 range, including subnormals, infinities, and NaNs. This differs from
`System.Decimal`, which has a fixed scale and no special values.

```csharp
using Decimals;

var price = Decimal64.Parse("19.99");
var quantity = Decimal64.Parse("3");
var total = price * quantity;

Console.WriteLine(total);             // 59.97
Console.WriteLine(total / 7);         // 8.567142857142857
Console.WriteLine(Decimal64.Sqrt(2)); // 1.414213562373095
```

## Background

The arithmetic follows Mike Cowlishaw's General Decimal Arithmetic Specification, which
became the decimal part of IEEE 754-2008 and IEEE 754-2019. The specification, the
encodings, and the test suite are published at <https://speleotrove.com/decimal/>.

The reference implementation is decNumber, a C library by the same author
(<https://speleotrove.com/decimal/#decNumber>). It provides an arbitrary-precision engine
and three fixed-size types: `decSingle`, `decDouble`, and `decQuad`. The types in this
repository follow the fixed-size design. A value is a small struct, and the arithmetic
does not allocate.

Correctness is tested against decNumber's `.decTest` files. See [Testing](#testing).

Some operations are renamed to follow .NET conventions. For example, `to-number` is
`Parse`, `to-scientific-string` is `ToString`, `remainder-near` is `RemainderNear`, and
`or` is `Or`.

## Formats

| | `Decimal32` | `Decimal64` | `Decimal128` |
| --- | --- | --- | --- |
| size | 4 bytes | 8 bytes | 16 bytes |
| precision | 7 digits | 16 digits | 34 digits |
| Emax | +96 | +384 | +6144 |
| Emin | -95 | -383 | -6143 |
| Etiny | -101 | -398 | -6176 |
| largest finite | 9.999999E+96 | 9.999999999999999E+384 | 9.999999999999999999999999999999999E+6144 |
| smallest subnormal | 1E-101 | 1E-398 | 1E-6176 |

A value equals `(-1)^sign x coefficient x 10^exponent`. The specification defines
decimal32 as a storage format only, and decNumber has no arithmetic for it. `Decimal32`
here supports the same arithmetic as the other two types.

Each format is a separate project (`Decimal32/`, `Decimal64/`, `Decimal128/`) with no
dependency on the others. Each has its own context, rounding, status, and class types,
for example `Decimal64Context` and `Decimal64Rounding`. The examples below use
`Decimal64`. The other two types have the same members.

## Usage

### Creating values

```csharp
var fromText = Decimal64.Parse("1.05");
var fromInt = (Decimal64)42;                        // implicit
var fromLong = (Decimal64)9223372036854775807L;     // explicit, rounds to 16 digits
var widened = Decimal128.CreateChecked(fromText);   // exact
var narrowed = Decimal32.CreateChecked(fromText);   // may round
```

Conversion from an integer type is implicit when every value of that type fits in the
format's precision:

| format | implicit from |
| --- | --- |
| `Decimal32` | `sbyte`, `byte`, `short`, `ushort`, `char` |
| `Decimal64` | the above, plus `int` and `uint` |
| `Decimal128` | the above, plus `long` and `ulong` |

Conversion from any other integer type is explicit. Conversion to an integer truncates
toward zero and throws `OverflowException` if the result does not fit.

There are no cast operators between the three formats, and none to or from
`System.Decimal`. Use `CreateChecked`, `CreateSaturating`, or `CreateTruncating` instead.
Converting to a wider decimal format is exact and keeps the exponent. Converting to a
narrower one rounds.

### Converting from binary floating point

```csharp
var shortest = (Decimal128)0.1;   // 0.1
var exact = Decimal128.FromBinary(0.1, Decimal128BinaryConversion.ExactValue);
// 0.1000000000000000055511151231257827
```

The cast operator uses the shortest decimal string that converts back to the same
`double`. For `0.1`, that is `0.1`.

`FromBinary` with `ExactValue` converts the exact binary value instead, rounded to the
format's precision. This is IEEE 754 `convertFormat`. `Decimal128` shows the binary error
in `0.1` because it has 34 digits. `Decimal64` gives `0.1000000000000000` because the
error is beyond its 16 digits.

### Arithmetic

Operators round half to even and do not report status. This is IEEE 754 default exception
handling.

```csharp
var sum = a + b;
var quotient = a / b;
var scaled = Decimal64.FusedMultiplyAdd(a, b, c);   // one rounding
var root = Decimal64.Sqrt(a);
var power = Decimal64.Pow(a, b);
```

Every operation has an overload that takes a context by reference. Use it to set the
rounding mode or to read the status flags:

```csharp
var context = new Decimal64Context(Decimal64Rounding.Ceiling);
var quotient = Decimal64.Divide(dividend, divisor, ref context);

if (context.HasRaised(Decimal64Status.Inexact))
{
    // the result was rounded
}
```

Rounding modes: `Ceiling`, `Down`, `Floor`, `HalfDown`, `HalfEven`, `HalfUp`, `Up`, and
`ZeroFiveUp`.

Status flags stay set until `ClearStatus` is called, so one check after a sequence of
operations reports everything raised during it. The flags are the five IEEE 754
exceptions (`InvalidOperation`, `DivisionByZero`, `Overflow`, `Underflow`, `Inexact`) and
the specification's additional conditions (`ConversionSyntax`, `DivisionImpossible`,
`DivisionUndefined`, `Clamped`, `Rounded`, `Subnormal`).

### Exponent and quantum

A value is stored as a coefficient and an exponent, and the same number can be stored in
more than one way. `1` is stored as coefficient 1 with exponent 0. `1.00` is stored as
coefficient 100 with exponent -2. Both equal one. The value of the last digit, 10 to the
power of the exponent, is called the *quantum*: 1 for `1`, and 0.01 for `1.00`.

The set of all encodings of one number is called a *cohort*. The type keeps the exponent,
so the two members of this cohort behave differently in some operations:

```csharp
var one = Decimal64.Parse("1");        // coefficient 1, exponent 0
var alsoOne = Decimal64.Parse("1.00"); // coefficient 100, exponent -2

one == alsoOne                          // true: the numbers are equal
one.Equals(alsoOne)                     // true
one.GetHashCode() == alsoOne.GetHashCode()   // true
one.ToString()                          // "1"
alsoOne.ToString()                      // "1.00": the text shows the exponent
one.ToBits() == alsoOne.ToBits()        // false: the stored coefficient and exponent differ
Decimal64.CompareTotal(one, alsoOne)    // > 0: the total order puts 1.00 before 1
```

`==`, `Equals`, `GetHashCode`, and `CompareTo` compare numeric value only. All members of a
cohort are therefore the same dictionary key and sort as equal. `CompareTotal` is the
IEEE 754 total order. It also compares exponents: for positive numbers in the same
cohort, the one with the smaller exponent comes first.

Each operation's result exponent is defined by the specification. For example,
`1.20 + 0.30` is `1.50`, and `1.2 * 1.5` is `1.80`. `Quantize` sets the exponent
explicitly. `Reduce` and `Trim` remove trailing zeros.

```csharp
var money = Decimal64.Quantize(total, Decimal64.Parse("0.01"), ref context);  // two places
```

### Comparison

The operators `==`, `!=`, `<`, `>`, `<=`, and `>=` compare numeric value and follow
IEEE 754. Every comparison involving a NaN is false, except `!=`, which is true.

`Equals`, `GetHashCode`, and `CompareTo` follow .NET conventions instead. A NaN equals
another NaN and sorts before all other values. `double` works the same way, and this is
what allows the types to be used in collections.

`CompareTotal` and `CompareTotalMagnitude` implement the IEEE 754 total order, which
orders NaNs, signs, and cohort members. `Compare` and `CompareSignal` implement the
specification's comparison and return a decimal: -1, 0, 1, or NaN.

### Text

```csharp
value.ToString()                                    // scientific form
value.ToEngineeringString()                         // exponent is a multiple of three
value.ToString("F2", CultureInfo.InvariantCulture)
value.ToString("G", CultureInfo.GetCultureInfo("de-DE"))   // 1234,5

Decimal64.Parse("1.05");
Decimal64.Parse("1.234,5", CultureInfo.GetCultureInfo("de-DE"));
Decimal64.TryParse("1.05"u8, out var fromUtf8);     // UTF-8 input
```

Methods without an `IFormatProvider` use the specification's format. It does not depend
on `CultureInfo.CurrentCulture`. This applies to `ToString()`, `ToScientificString`,
`ToEngineeringString`, and `Parse` and `TryParse` without a provider. Note that this
differs from other .NET numeric types, where `ToString()` uses the current culture.

Methods with an `IFormatProvider` use that provider's separators, signs, and currency
symbols, and accept `NumberStyles`. The default style is `Float | AllowThousands`.
`Infinity`, `sNaN`, and NaN payloads are also accepted with a provider.

The `"E"` format specifier produces engineering notation, not .NET exponential notation.

### Encoding

```csharp
var bits = value.ToBits();                  // uint, ulong, or UInt128
var restored = Decimal64.FromBits(bits);

var binary = value.ToBidBits();             // BID, same as ToBits
var fromBinary = Decimal64.FromBidBits(binary);

var interchange = value.ToDpdBits();        // DPD
var fromWire = Decimal64.FromDpdBits(interchange);
```

Values are stored in the BID (binary integer decimal) encoding, where the coefficient is
a binary integer. `ToBits` and `ToBidBits` return the stored bits. `ToDpdBits` and
`FromDpdBits` convert to and from the DPD (densely packed decimal) encoding, which stores
three decimal digits in each 10-bit group. decNumber and decimal hardware use DPD. IEEE
754 allows either encoding.

A BID encoding whose coefficient exceeds the format's maximum is non-canonical. IEEE 754
defines its value as zero. `FromBits` stores such an encoding unchanged, and arithmetic
treats it as zero. `IsCanonical` detects it, and `Canonical` converts it to the canonical
encoding. `FromDpdBits` always produces a canonical encoding.

### Elementary functions and generic math

`Sqrt`, `Exp`, `Log`, `Log10`, and `Pow` are implemented directly and tested against the
corpus. `Exp2`, `Exp10`, `Cbrt`, `RootN`, `Hypot`, `Log2`, `ExpM1`, `LogP1`, and the
related functions are computed from those.

Each type implements:

- `IFloatingPoint<T>`, which includes `INumber<T>`, `INumberBase<T>`, `ISignedNumber<T>`,
  and `IFloatingPointConstants<T>`
- `IMinMaxValue<T>`
- `IExponentialFunctions<T>`, `ILogarithmicFunctions<T>`, `IPowerFunctions<T>`, and
  `IRootFunctions<T>`
- `IUtf8SpanFormattable` and `IUtf8SpanParsable<T>`

Generic numeric code works with all three types:

```csharp
static T Mean<T>(ReadOnlySpan<T> values)
    where T : INumber<T>
{
    var total = T.Zero;
    foreach (var value in values)
    {
        total += value;
    }

    return total / T.CreateChecked(values.Length);
}

var average = Mean<Decimal64>(samples);
```

## Differences from IEEE 754

All required IEEE 754 operations are implemented and tested. The differences are:

- **No alternate exception handling.** IEEE 754 clause 8 describes trap handlers. Only
  default exception handling is implemented: each operation returns the default result
  and sets flags in the context's `Status`. Arithmetic never throws.
- **No trigonometric or hyperbolic functions.** IEEE 754-2019 lists these as recommended,
  not required. decNumber does not implement them, and the corpus does not test them. As
  a result, the types do not implement `IFloatingPointIeee754<T>`. The other members of
  that interface are available as static methods with the same names: `Epsilon`, `NaN`,
  the infinities, `NegativeZero`, `FusedMultiplyAdd`, `Ieee754Remainder`, `ILogB`,
  `ScaleB`, `BitIncrement`, and `BitDecrement`.
- **Two definitions of max and min.** IEEE 754-2019 `maximum` returns NaN if either
  operand is NaN. The specification's `max` returns the other operand when one is a
  quiet NaN. `Max` and `Min` use the IEEE 754-2019 definition. `MaxNumber`, `MinNumber`,
  and the overloads that take a context use the specification's definition. The same
  applies to `MaxMagnitude` and `MinMagnitude`.
- **`ILogB` uses .NET conventions for special values.** It returns `int.MinValue` for zero
  and `int.MaxValue` for NaN and infinity. The specification's `logb` returns -Infinity,
  NaN, and +Infinity. `LogB` implements the specification's version and returns a
  decimal.
- **`ScaleB` scales by powers of ten**, as IEEE 754 defines it for decimal formats.
  `double.ScaleB` scales by powers of two. A scale value outside the valid range is an
  invalid operation and returns NaN. `double.ScaleB` returns infinity in that case.
- **`Round(value, digits)` does not add trailing zeros.** If the value already has
  `digits` or fewer decimal places, it is returned unchanged. Use `Quantize` to set an
  exact number of places.
- **Equality and ordering follow .NET conventions for NaN**, as described under
  [Comparison](#comparison).
- **Three recommended operations are missing:** `quantum(x)`, `getPayload`, `setPayload`,
  and `setPayloadSignaling`. `sameQuantum` and `quantize` are implemented. NaN payloads
  are preserved through arithmetic and conversion, and are parsed and formatted, for
  example `NaN255`.

## Implementation

**`Decimal32`** stores its BID encoding in a `uint` and computes in 64-bit integers. With
7 digits, every intermediate result fits in 64 bits: a product has at most 14 digits, an
aligned sum at most 20, a scaled dividend 14, and a square-root radicand 16. Division
uses one `double` division followed by an integer correction. Square root uses
`Math.Sqrt` followed by an exact check. See `design/Decimal32.md`.

**`Decimal64`** stores its BID encoding in a `ulong` and computes in 64-bit integers,
without `UInt128` or `BigInteger`. Products are formed from 8-digit halves into two
base-10^16 limbs. Division by a power of ten uses multiplication by a precomputed
reciprocal. See `design/Decimal64.md`.

**`Decimal128`** stores its BID encoding in two 64-bit words and computes with two- and
four-word integers. `UInt128` is used only by `ToBits` and `FromBits`, and `BigInteger`
is not used. Division uses the Möller–Granlund 2-by-1 and 3-by-2 methods with a
reciprocal computed from a table, without a hardware divide instruction. See
`design/Decimal128.md`.

The elementary functions in all three types use a port of decNumber's
arbitrary-precision engine. They need more digits than the format holds in order to
round correctly. The engine's working storage is a fixed-size arena.

## Performance

Nanoseconds per operation compared with decNumber's fixed-size C types. The operands are
decbench's set: 1 to 16 digits, exponents from -40 to +40. Measured on one machine with
BenchmarkDotNet's short job.

| operation | `decDouble` | `Decimal64` | `decQuad` | `Decimal128` |
| --- | --- | --- | --- | --- |
| add | 41.0 | 12.7 | 62.7 | 24.4 |
| multiply | 36.0 | 11.2 | 50.1 | 15.3 |
| divide | 121.1 | 31.0 | 192.2 | 68.7 |
| fma | 88.8 | 23.8 | 114.6 | 34.1 |
| compare | 12.8 | 7.4 | 25.3 | 12.9 |
| format into a span | 10.5 | 19.8 | 17.5 | 25.4 |
| from string | 27.5 | 18.1 | 31.9 | 33.0 |

decbench measures only formatting and parsing for `decSingle`. On the same operands,
`decSingle` formats in 7.7 ns and parses in 29.0 ns; `Decimal32` takes 17.1 ns and
24.3 ns. On 1-to-7-digit operands, `Decimal32` adds in 12.2 ns, multiplies in 7.3 ns, and
divides in 18.1 ns.

Summary:

- Add, multiply, divide, and fma are 2.5 to 4 times faster than the C.
- Compare is about 2 times faster.
- Parsing is about the same speed or faster.
- Formatting is 1.5 to 2.2 times slower. The C types store DPD and convert each 10-bit
  group to three digits with a table lookup. These types store a binary coefficient and
  must divide it to produce digits.

The C figures are the median of several runs. decbench times one pass per operation
without warmup, so its results vary more between runs than the BenchmarkDotNet results.
Treat ratios within about 10% of each other as equal.

To run the benchmarks:

```
dotnet run --project Benchmarks -c Release -- --filter '*'
```

## Testing

```
dotnet test                                   # unit tests and the corpus
dotnet run --project DecTest -- <files>       # the corpus runner only
```

There are 665 tests. They include every applicable case from the `.decTest` corpus:
10,562 cases for `Decimal32`, 19,850 for `Decimal64`, and 20,237 for `Decimal128`.

The corpus has two parts: the files distributed with decNumber, and a generated set. The
distributed files have no arithmetic cases for decimal32 and no square-root cases for
the fixed-size formats. The generated set covers these. It was produced by running
decNumber on the operands of the general test files through a C++ harness.

Each case checks both the result and the complete set of status flags.

## Repository layout

```
Decimal32/              Decimal32 type
Decimal64/              Decimal64 type
Decimal128/             Decimal128 type
Decimals.Conformance/   .decTest reader and runner
DecTest/                command-line front end for the runner
Tests/                  unit tests and corpus tests
Benchmarks/             BenchmarkDotNet benchmarks
TestData/               .decTest corpus
```

## Attribution

The arithmetic specification, the encodings, and the test cases are by Mike Cowlishaw and
are provided as-is; see <https://speleotrove.com/decimal/>. The corpus files in
`TestData/` keep their original headers and notices. This implementation shares no code
with decNumber.
