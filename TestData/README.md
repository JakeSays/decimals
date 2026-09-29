# Test data

This directory holds three sets of test data. None of it was written for this repository.
Each set's source, license, and attribution are below.

| directory | source | contents | size |
| --- | --- | --- | --- |
| `DecTest/` | Mike Cowlishaw's General Decimal Arithmetic testcases | 89 `.decTest` files for decSingle, decDouble, and decQuad | 2.0 MB |
| `Generated/` | decNumber 3.68, run on operands from Cowlishaw's general testcases | 18 `.decTest` files for fixed formats | 1.3 MB |
| `Hfahmy/Sample/` | Amr Sayed-Ahmed and Hossam A. H. Fahmy's test vectors | every hundredth vector, 111,177 in 228 files | 12 MB |
| `Hfahmy/Full/` | the same | all 11,108,857 vectors; not committed | 1.2 GB unpacked, 395 MB to download |

## DecTest

**Source:** the General Decimal Arithmetic testcases, version 2.62, by Mike Cowlishaw.
Published at <https://speleotrove.com/decimal/dectest.html>, part of the General Decimal
Arithmetic site at <https://speleotrove.com/decimal/>.

**Contents:** the `dd`, `dq`, and `ds` groups, which test the decDouble, decQuad, and
decSingle formats. `decDouble.decTest`, `decQuad.decTest`, and `decSingle.decTest` list the
other files of each group. The format is described in the document "General Decimal
Arithmetic Testcases" on the same site.

**License:** each file carries this notice, which is kept unchanged:

> Copyright (c) Mike Cowlishaw, 1981, 2010. All rights reserved.
> Parts copyright (c) IBM Corporation, 1981, 2008.
>
> These testcases are experimental ('beta' versions), and they may contain errors. They
> are offered on an as-is basis. In particular, achieving the same results as the tests
> here is not a guarantee that an implementation complies with any Standard or
> specification. The tests are not exhaustive.

**Changes:** none. The files are stored exactly as distributed, including their CRLF line
endings; `.gitattributes` turns off line-ending conversion for this directory.

## Generated

**Source:** written by `decgen`, a C++ program built against decNumber 3.68. `decgen` is
not part of this repository.

**Why it exists:** the distributed testcases have no arithmetic for decSingle and no square
root for any fixed format. The general testcases do cover these operations, but they are
written for an arbitrary-precision context, so they cannot be run against a fixed format
directly. `decgen` takes the operands from the general testcases (`add.decTest`,
`squareroot.decTest`, and so on), converts them to the fixed format, and records what
decNumber gives for them under that format's precision and exponent range. Each file's
header names its source group.

**License:** the operands come from Cowlishaw's testcases, under the notice above. The
results come from decNumber, which is distributed under the ICU License; see
<https://speleotrove.com/decimal/#decNumber>.

**Known issues in decNumber 3.68:** its errata list off-by-one errors in fused multiply-add
and in square root under rounding modes other than half-even
(<https://speleotrove.com/decimal/decnumerr.html>). This set has no fused multiply-add
cases. Its square-root cases all agree with the types in this repository, which pass every
square-root vector in the Hfahmy set.

## Hfahmy

**Source:** the decimal floating-point test vectors of Amr Sayed-Ahmed and Hossam A. H.
Fahmy, Electronics and Electrical Communications Department, Faculty of Engineering, Cairo
University. Published at <http://eece.cu.edu.eg/~hfahmy/arith_debug/>. The site serves
plain HTTP only.

**Contents:** 13 sets of decimal64 and decimal128 vectors for addition, subtraction,
multiplication, division, fused multiply-add, fused multiply-subtract, and square root, in
five rounding modes. Each set was generated to cover particular models, such as
cancellation, rounding, sticky digits, trailing zeros, overflow, and underflow, rather than
at random. Each vector gives the operands, the rounding mode, the exact result including
its exponent, and the IEEE 754 flags raised. The format is described on the source page and
in `Decimals.Conformance/HfahmyVector.cs`. There are no decimal32 vectors.

**License:** the source page states:

> The files provided below with test vectors for the decimal FP operations are copyrighted
> to Amr Sayed-Ahmed and Hossam A. H. Fahmy. They are provided as is without any implied
> warranties. The persons who download the files are free to use them for any purpose under
> their own responsibilities. The copyright owners provide the files in the hope that they
> will be useful to others but give no guarantees. We would appreciate if you reference our
> work and give us credit for it.

**Credit:** the vectors and the models behind them are described in:

- Amr Sayed-Ahmed, MSc thesis, Cairo University, June 2011:
  <http://eece.cu.edu.eg/~hfahmy/arith_debug/amr_thesis_final.pdf>
- Amr Sayed-Ahmed, Hossam A. H. Fahmy, and Rodina Samy. "Verification of decimal
  floating-point fused-multiply-add operation." The Ninth ACS/IEEE International
  Conference on Computer Systems and Applications (AICCSA), Sharm El-Sheikh, Egypt,
  December 2011.
- Amr Sayed-Ahmed, Hossam A. H. Fahmy, and Mahmoud Hassan. "Three engines to solve
  verification constraints of decimal floating-point operations." Forty-Fourth Asilomar
  Conference on Signals, Systems, and Computers, Asilomar, California, USA, November 2010.

The line format follows IBM's FPGen test vectors:
<https://www.research.ibm.com/haifa/projects/verification/fpgen/ieeets.html>.

### Full and Sample

The commands below use `dectest`, the runner in `DecTest/`, which `publish-dectest.sh` at
the repository root builds into `~/bin`.

`Full/` holds the complete set, as published. It is too large to commit, so it is listed in
`.gitignore`. To download it, check it against the published md5 sums, and unpack it:

```
dectest --fetch-hfahmy TestData/Hfahmy/Full
```

To run it:

```
dectest --vectors TestData/Hfahmy/Full
```

`Sample/` holds every hundredth vector of each file in the full set, starting with the
first, in a file of the same name. It is committed, and `dotnet test` runs it. It was
written with the following command, which produces the same files each time it is run on
the same full set:

```
dectest --sample-hfahmy TestData/Hfahmy/Full TestData/Hfahmy/Sample
```

The sample differs from the full set in two ways: its line endings are LF, and it leaves
out the lines listed below.

### Known defects

The runner skips these lines, which are listed in
`Decimals.Conformance/HfahmyKnownDefects.cs`:

- **22 damaged lines.** In the published files, some lines are broken in two, some are
  missing their format or an operand, and two are mostly `#` characters. 17 are in the
  July 2010 decimal64 addition and fused multiply-add sets, and 5 in the 2011 fused
  multiply-add and square-root sets.
- **1 wrong result.** `2010_07_d64_fma_type3.txt` line 37946 expects
  `-4131706727960463E341` for `-5064047626626594E276 × 9999999999990028E64 + 1E-398`,
  rounded toward positive infinity. The exact product is
  `-50640476266215441317067279604632E340`, which rounds to `-5064047626621544E356`. The
  February 2011 fused multiply-add set, which regenerates the same models, does not
  contain this vector.
