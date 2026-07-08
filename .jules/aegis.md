## 2026-06-08 - ObjectPool Coverage Expansion
**Observation:** The codebase's `.NET` `Tedd.ObjectPool` coverage was at 76.64% line and 70% branch coverage, specifically missing edge cases in `Dispose()`, `Prefill(int)`, and `AllocateExecuteDeallocate(Action, Action)`. Conditional branches for nullable `_tls` disposal and uninitialized state checks were identified as untested.
**Strategic Action:** Implemented parameterized and boundary testing using xUnit Theories. Utilized `Reflection` conditionally to reset the readonly TLS field for explicit branch testing on disposal. Established inputs (-1, 0, 1, 5, 100) on pool slots for parameterized prefill condition verification to ensure absolute code coverage guarantees deterministic execution.

## 2026-07-08 - ObjectPool Scoped and Tracking Coverage Expansion
**Observation:** Coverage reports indicated that `ForgetTrackedObject` (when freeing un-tracked objects) and the stateful `Scoped<TState>` method lacked verification.
**Strategic Action:** Engineered specific unit tests for `ForgetTrackedObject` verifying the trace behavior for un-tracked instances, and for `Scoped<TState>` asserting correct parameter passing, execution, and deterministic object recycling. This elevated line and branch coverage metrics back to 100%.
