# Decimals

IEEE 754 decimal floating point for .NET: `Decimal32`, `Decimal64`, and `Decimal128`, in
managed C# with no native dependency.

These are real decimal floating-point types. Unlike `System.Decimal` they carry an exponent
rather than a fixed scale, so they span the full IEEE range and behave the way `double`
does about overflow, underflow, infinities, and NaNs -- in base ten, where `0.1 + 0.2` is
exactly `0.3`. Unlike `double` they hold decimal fractions exactly, and they keep the
*quantum*: `1.0` and `1.00` are the same number written to different precision, and the
arithmetic says which one you get back.

```csharp
using Decimals;

var price = Decimal64.Parse("19.99");
var quantity = Decimal64.Parse("3");
var total = price * quantity;

Console.WriteLine(total);             // 59.97
Console.WriteLine(total / 7);         // 8.567142857142857
Console.WriteLine(Decimal64.Sqrt(2)); // 1.414213562373095
```

Requires .NET 10.

## Origins

The arithmetic implemented here is Mike Cowlishaw's **decimal arithmetic specification**,
the document that became the decimal half of IEEE 754-2008 and IEEE 754-2019. The
specification, the encodings, the reference implementation, and the test suite all live at
**<https://speleotrove.com/decimal/>**.

The reference implementation is **decNumber**, a C library by the same author:
**<https://speleotrove.com/decimal/#decNumber>**. It offers two routes to the same
arithmetic -- an arbitrary-precision engine (`decNumber`) and fixed-format types operating
directly on the encodings (`decSingle`, `decDouble`, `decQuad`). This library is modeled on
the second: a fixed-size value the JIT can keep in registers, with no allocation on any
arithmetic path.

Correctness is measured against the same corpus decNumber uses, the `.decTest` files
published on that site. Every case for the three formats runs on every build. See
[Testing](#testing) below.

The specification uses its own vocabulary, and this library keeps the concepts while
renaming them for .NET: `to-number` is `Parse`, `to-scientific-string` is `ToString`,
`remainder-near` is `RemainderNear`, and `decFloatOrOp` is `Or`.

## The three formats

| | `Decimal32` | `Decimal64` | `Decimal128` |
| --- | --- | --- | --- |
| size | 4 bytes | 8 bytes | 16 bytes |
| precision | 7 digits | 16 digits | 34 digits |
| Emax | +96 | +384 | +6144 |
| Emin | -95 | -383 | -6143 |
| Etiny | -101 | -398 | -6176 |
| largest finite | 9.999999E+96 | 9.999999999999999E+384 | 9.999999999999999999999999999999999E+6144 |
| smallest subnormal | 1E-101 | 1E-398 | 1E-6176 |

A value is `(-1)^sign x coefficient x 10^exponent`. `Decimal32` is a storage and interchange
format in the specification rather than an arithmetic one -- decNumber gives it no
arithmetic at all -- but it has full arithmetic here.

## Using the types

### Making values

```csharp
var fromText = Decimal64.Parse("1.05");
var fromInt = (Decimal64)42;                  // implicit for int and narrower
var fromLong = (Decimal64)9223372036854775807L; // explicit: 19 digits into 16
var widened = (Decimal128)fromText;           // implicit: narrow to wide is exact
var narrowed = (Decimal32)fromText;           // explicit: rounds
```

Widening between the three formats is implicit and exact, quantum included -- a `Decimal32`
`1.000` arrives as `1.000`, not `1`. Narrowing is explicit, because it rounds and can
overflow to an infinity or underflow to a subnormal.

Integer types up to ten digits convert implicitly; `long`, `ulong`, `Int128`, and `UInt128`
are explicit because they can carry more digits than the format holds. Conversions *to* an
integer truncate toward zero and throw `OverflowException` if the value will not fit, which
is what `System.Decimal` does. `System.Decimal` itself has no cast in either direction on
purpose: it holds 28 digits against a single scale, so neither direction is a clean fit. Use
`Decimal64.CreateChecked(someDecimal)` when you want it.

### Binary floats have two readings

```csharp
var shortest = (Decimal128)0.1;   // 0.1
var exact = Decimal128.FromBinary(0.1, BinaryConversion.ExactValue);
// 0.1000000000000000055511151231257827
```

The cast takes the shortest text that round-trips the `double`, so `0.1` becomes the decimal
one tenth -- almost always what the writer meant. `BinaryConversion.ExactValue` gives IEEE
754's `convertFormat` instead: the binary value itself, which is a whole number times a power
of two and therefore has an exact decimal form. It takes all 34 digits to see it -- the same
call on `Decimal64` rounds to `0.1000000000000000`, since sixteen digits cannot show where a
`double` stops being a tenth.

### Arithmetic

Operators round half to even and discard the status flags, which is IEEE 754 default
exception handling:

```csharp
var sum = a + b;
var quotient = a / b;
var scaled = Decimal64.FusedMultiplyAdd(a, b, c);   // rounded once, not twice
var root = Decimal64.Sqrt(a);
var power = Decimal64.Pow(a, b);
```

Every operation also has an overload taking a `DecimalContext` by reference, which is how
you choose a rounding mode and find out what happened:

```csharp
var context = new DecimalContext(DecimalRounding.Ceiling);
var quotient = Decimal64.Divide(dividend, divisor, ref context);

if (context.HasRaised(DecimalStatus.Inexact))
{
    // the quotient did not divide evenly
}
```

`DecimalRounding` has all eight of the specification's modes: `Ceiling`, `Down`, `Floor`,
`HalfDown`, `HalfEven`, `HalfUp`, `Up`, and `ZeroFiveUp`.

`DecimalStatus` accumulates across calls until you clear it, so you can run a whole
calculation and ask once at the end. It carries the five IEEE exceptions -- `InvalidOperation`,
`DivisionByZero`, `Overflow`, `Underflow`, `Inexact` -- and the specification's additional
conditions: `ConversionSyntax`, `DivisionImpossible`, `DivisionUndefined`, `Clamped`,
`Rounded`, and `Subnormal`.

### The quantum is part of the value

This is the thing that surprises people arriving from `double`:

```csharp
var one = Decimal64.Parse("1");
var alsoOne = Decimal64.Parse("1.00");

one == alsoOne             // true  -- same number
one.Equals(alsoOne)        // true
one.ToString()             // "1"
alsoOne.ToString()         // "1.00"
one.ToBits() == alsoOne.ToBits()          // false -- different encodings
Decimal64.CompareTotal(one, alsoOne)      // > 0   -- the total order separates them
```

Those two are members of one *cohort*: the same value at different precision. Equality,
ordering, and hashing are numeric, so a cohort is one key in a dictionary and sorts as one
value. `CompareTotal` is the IEEE total order, which separates them, and `ToBits` shows why.

Arithmetic decides which cohort member comes out, by rules the specification lays down
exactly -- `1.20 + 0.30` is `1.50`, and `1.2 * 1.5` is `1.80`. `Quantize` sets the quantum
deliberately, and `Reduce` and `Trim` strip trailing zeros:

```csharp
var money = Decimal64.Quantize(total, Decimal64.Parse("0.01"), ref context);  // two places
```

### Comparison

`==`, `<`, `>`, `<=`, `>=` are numeric and leave NaN unordered, exactly as IEEE 754 says --
every one of them is false when either side is a NaN, including a NaN against itself.

`Equals`, `GetHashCode`, and `CompareTo` have to place every value somewhere, so they follow
.NET convention instead: a NaN equals a NaN and sorts below everything. That is the same
split `double` makes, and it is what lets these types work in collections.

`CompareTotal` and `CompareTotalMagnitude` are the IEEE total order, which orders NaNs,
signs, and cohort members. `Compare` and `CompareSignal` are the specification's numeric
comparison, returning a decimal that may itself be NaN.

### Text

```csharp
value.ToString()                                 // the specification's scientific form
value.ToEngineeringString()                      // exponent a multiple of three
value.ToString("F2", CultureInfo.InvariantCulture)
value.ToString("G", CultureInfo.GetCultureInfo("de-DE"))   // 1234,5

Decimal64.Parse("1.05");                         // the specification's grammar
Decimal64.Parse("1.234,5", CultureInfo.GetCultureInfo("de-DE"));
Decimal64.TryParse("1.05"u8, out var fromUtf8);  // UTF-8 too
```

There are two grammars, and which one runs is decided by whether you passed an
`IFormatProvider` -- never by ambient state.

**Without a provider** you get the specification's, which is invariant by definition and
unaffected by `CultureInfo.CurrentCulture`: `ToString()`, `ToScientificString`,
`ToEngineeringString`, and the no-provider `Parse` and `TryParse`. This is the canonical text
of a value, and it is the one departure from .NET convention worth knowing about --
`ToString()` here is *not* `ToString(null, CurrentCulture)`, because a thread's locale must
not change what a value looks like on the wire.

**With a provider**, separators, signs, currency symbols, and `NumberStyles` are all honored,
defaulting to `Float | AllowThousands` like the built-in types. The specification's own
spellings survive that path too, since no culture has words for them: `Infinity`, `sNaN`, and
NaN payloads all still parse.

Note that `"E"` is the *engineering* form here rather than .NET's exponential one -- the
specification has an engineering notation and .NET has no specifier for it.

### Encoding

```csharp
var bits = value.ToBits();                  // uint, ulong, or UInt128
var restored = Decimal64.FromBits(bits);

var interchange = value.ToDpdBits();        // densely packed decimal, the stored form
var fromWire = Decimal64.FromDpdBits(interchange);

var binary = value.ToBidBits();             // binary integer decimal, a conversion
var fromBinary = Decimal64.FromBidBits(binary);
```

The in-memory encoding is **DPD** (densely packed decimal), three digits to a ten-bit
declet, which is what decimal hardware and decNumber exchange -- so `ToBits` and
`ToDpdBits` agree and neither costs anything. `ToBidBits` and `FromBidBits` reach the
**BID** (binary integer decimal) form, where the coefficient is a plain binary integer.
IEEE 754 permits either for decimal interchange formats; both are implemented and the
corpus checks the conversion bit for bit.

Storing the interchange form means a non-canonical encoding survives being held, which is
what IEEE `copy` requires of it, and makes `Canonical` a real operation.

### Elementary functions and generic math

`Sqrt`, `Exp`, `Log`, `Log10`, and `Pow` are implemented directly and checked against the
corpus. `Exp2`, `Exp10`, `Cbrt`, `RootN`, `Hypot`, `Log2`, and the `M1`/`P1` variants reduce
to those.

Each type implements `IFloatingPoint<T>`, `INumber<T>`, `INumberBase<T>`, `ISignedNumber<T>`,
`IFloatingPointConstants<T>`, `IMinMaxValue<T>`, `IExponentialFunctions<T>`,
`ILogarithmicFunctions<T>`, `IPowerFunctions<T>`, `IRootFunctions<T>`, and the text and
ordering contracts including `IUtf8SpanFormattable` and `IUtf8SpanParsable<T>`. So generic
numeric code works:

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

## Where this differs from IEEE 754

The required operations are all here, and the corpus checks them. What follows is what a
reader of the standard should know before relying on this.

**No alternate exception handling.** IEEE 754 clause 8 describes trap handlers that run
instead of returning a default result. Only default exception handling is implemented:
operations return the standard result and record what happened in `DecimalContext.Status`.
There is nothing to install and nothing that throws from arithmetic.

**No trigonometric or hyperbolic functions**, and therefore no `IFloatingPointIeee754<T>`.
IEEE 754-2019 clause 9 lists these as *recommended* rather than required, decNumber supplies
none of them, and the corpus has no cases for them. Everything else that interface declares
is provided as ordinary statics with the same names -- `Epsilon`, `NaN`, the infinities,
`NegativeZero`, `FusedMultiplyAdd`, `Ieee754Remainder`, `ILogB`, `ScaleB`, `BitIncrement`,
`BitDecrement` -- so adding the interface later is a pure addition.

**Two readings of max and min.** IEEE 754-2019's `maximum` gives a NaN when either operand is
one; the specification's `max` hands back the number standing beside a quiet NaN. Both are
here, and .NET's names decide which you get: `Max` and `Min` are the IEEE reading, `MaxNumber`
and `MinNumber` are the specification's -- and so are the overloads taking a
`DecimalContext`, because that is what the corpus is written against. The same split runs
through `MaxMagnitude` and `MinMagnitude`.

**`ILogB` reports out-of-range results the .NET way**, giving `int.MinValue` for a zero and
`int.MaxValue` for a NaN or an infinity, where the specification's `logb` gives -Infinity, a
NaN, and +Infinity. `LogB` is the specification's and returns a decimal.

**`ScaleB` shifts by powers of ten.** That is IEEE's definition -- scaleB uses the format's
radix -- but it differs from `double.ScaleB`, which shifts by powers of two. A scale too
large to be an operand is an invalid operation giving a NaN, where `double` overflows to an
infinity.

**`Round(value, digits)` rounds and does not quantize.** Asking for more places than the
value carries leaves it exactly as it is rather than padding the coefficient with zeros to
reach the requested exponent. Padding is `Quantize`, and it is a separate operation because
the quantum is part of what a decimal value records.

**Equality and ordering split along .NET lines**, as described above: the operators are IEEE,
while `Equals`/`GetHashCode`/`CompareTo` place NaN so that collections work.

**Three recommended operations are absent**: `quantum(x)` -- though `sameQuantum` and
`quantize` are both here -- and the NaN payload accessors `getPayload`, `setPayload`, and
`setPayloadSignaling`. Payloads themselves are fully supported: they propagate through
arithmetic, survive format conversion, and parse and format as `NaN255`.

## Performance

Measured against decNumber's `decDouble` and `decQuad` -- the fixed-format C routes, which
are the fastest thing decNumber offers and so the right thing to compare against.
Nanoseconds per operation, same machine, same values, BenchmarkDotNet's default job.

| operation | `Decimal64` | `decDouble` | ratio |
| --- | --- | --- | --- |
| add | 60.5 | 42.3 | 1.43x |
| multiply | 47.9 | 35.1 | 1.36x |
| divide | 120.3 | 119.7 | 1.01x |
| fma | 96.5 | 88.5 | 1.09x |
| compare | 12.1 | 12.5 | 0.97x |
| to string | 63.6 | 10.3 | 6.17x |
| from string | 54.3 | 28.1 | 1.93x |

Broadly: managed code lands within about 1.1x to 1.4x of optimized C on arithmetic, and
matches it on divide and compare -- the latter because classification and sign handling read
the packed bits without ever unpacking the coefficient. `Decimal128` runs 1.1x to 1.8x
against `decQuad`, and is faster than it on multiply.

The C figures are medians of several runs. `decbench` times one pass per operation with no
warmup or statistics, and its longer rows vary by a good deal more between runs than the
managed numbers beside them do, so read a ratio within about ten percent of another as a tie.

Formatting is the outlier at roughly 6x, and the reason is known rather than mysterious:
decNumber emits three digits at a time from its declet tables, and this still divides the
coefficient down one digit at a time. Parsing is within 2x. Neither has been optimized;
arithmetic was the priority.

For scale on the other side, `System.Decimal` adds in about 6ns against `Decimal64`'s 24ns
on the same 8-digit operands, and `double` in under 1ns. Decimal floating point costs
something; what it buys is exactness in base ten across the full IEEE range.

The benchmark suite that produces these is in `Benchmarks/`; run it with
`dotnet run --project Benchmarks -c Release -- --filter '*'`.

## Testing

```
dotnet test                                   # unit tests and the corpus
dotnet run --project DecTest -- <files>       # the corpus runner on its own
```

518 unit tests, plus every applicable case from Cowlishaw's `.decTest` corpus: **29,212**
cases from the distributed files and **21,437** generated ones. The generated set exists
because `decSingle` ships no arithmetic cases at all, so `Decimal32` coverage is produced by
cross-checking against decNumber through a C++ harness.

Cases are matched on the full set of status flags, not just the result value. Most cohort
and subnormal bugs show up as a missing `Rounded` or a spurious `Clamped` long before they
change a digit.

## Repository layout

```
Decimals/               the library
Decimals.Conformance/   corpus reader and runner
DecTest/                console front end for the runner
Tests/                  unit tests and the corpus harness
Benchmarks/             BenchmarkDotNet suite
TestData/               vendored .decTest corpus
```

## Attribution

The arithmetic specification, the encodings, and the testcases are Mike Cowlishaw's, offered
on an as-is basis; see <https://speleotrove.com/decimal/>. The corpus files vendored under
`TestData/` keep their original headers and that notice intact. This library is an
independent implementation and shares no code with decNumber.
