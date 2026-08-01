## 2026-06-01 - ObjectPool TFMs and Dependencies Modernized

**Observation:** The package targets `netstandard2.0` and is missing explicit targets for `net8.0` and `net9.0`. Some test packages and benchmark packages are slightly outdated.

**Strategic Action:** Added `net8.0` and `net9.0` to `TargetFrameworks` for `Tedd.ObjectPool`. Updated dependencies in test and benchmark projects to latest stable versions compatible with the targets.

## 2026-06-01 - Used proper argument validation

**Observation:** ArgumentOutOfRangeException and ArgumentNullException could be optimized with new methods under NET8.

**Strategic Action:** Added conditional compilation `#if NET8_0_OR_GREATER` using `ArgumentOutOfRangeException.ThrowIfLessThan` and `ArgumentNullException.ThrowIfNull` to comply with `CA1512` and `CA1510` warnings.

## 2026-06-01 - Cleaned up warnings

**Observation:** Tests and benchmarks had compiler warnings for nullability when `Nullable` was set to `enable` in the projects.

**Strategic Action:** Resolved nullability warnings in tests and legacy benchmark code by using explicit nullability (e.g., `DummyObject?[]`) or null-forgiving operators where required to ensure strict clean builds.
## 2026-06-02 - ObjectPool Dependencies Modernization

**Observation:** Test and benchmark dependencies were slightly outdated.

**Strategic Action:** Updated `Microsoft.NET.Test.Sdk` to 18.8.1 and `Microsoft.Extensions.ObjectPool` to 10.0.10.
