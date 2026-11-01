## 2026-06-01 - ObjectPool TFMs and Dependencies Modernized

**Observation:** The package targets `netstandard2.0` and is missing explicit targets for `net8.0` and `net9.0`. Some test packages and benchmark packages are slightly outdated.

**Strategic Action:** Added `net8.0` and `net9.0` to `TargetFrameworks` for `Tedd.ObjectPool`. Updated dependencies in test and benchmark projects to latest stable versions compatible with the targets.

## 2026-06-01 - Used proper argument validation

**Observation:** ArgumentOutOfRangeException and ArgumentNullException could be optimized with new methods under NET8.

**Strategic Action:** Added conditional compilation `#if NET8_0_OR_GREATER` using `ArgumentOutOfRangeException.ThrowIfLessThan` and `ArgumentNullException.ThrowIfNull` to comply with `CA1512` and `CA1510` warnings.

## 2026-06-01 - Cleaned up warnings

**Observation:** Tests and benchmarks had compiler warnings for nullability when `Nullable` was set to `enable` in the projects.

**Strategic Action:** Resolved nullability warnings in tests and legacy benchmark code by using explicit nullability (e.g., `DummyObject?[]`) or null-forgiving operators where required to ensure strict clean builds.

## 2025-03-09 - Dependency Modernization

**Observation:** The codebase has some outdated dependencies:
- In `Tedd.ObjectPool.Tests.csproj`: `coverlet.collector` (10.0.1 -> 10.1.0), `Microsoft.NET.Test.Sdk` (18.6.0 -> 18.11.0), `xunit.runner.visualstudio` (3.1.5 -> 4.0.1).
- In `Tedd.ObjectPool.Benchmarks.csproj`: `Microsoft.Extensions.ObjectPool` (10.0.8 -> 10.0.14).
The test and benchmark projects were missing `net10.0` as a target framework, which is present in the main project.

**Strategic Action:** Updated these dependencies to their latest compatible stable versions. Added `net10.0` to the test and benchmark projects to ensure the build/tests/pack still pass for all target frameworks.
