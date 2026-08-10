## 2026-08-10 - Architectural Execution Flow & Code Examples

**Observation:** The README.md exhibited documentation drift regarding the Architectural Execution Flow. Specifically, the "Thread-Local Storage (TLS) Cache" and "Rotating Array Slots" tiers of the multi-tiered allocation strategy were missing from the public documentation, requiring developers to speculate on internal mechanics. Additionally, code examples were obsolete, failing to utilize contemporary .NET 9.0/10.0+ target-typed `new()` syntax.

**Strategic Action:** Synchronized the README.md by articulating the precise multi-tiered allocation strategy (TLS Cache -> Fast Slot -> Rotating Array Slots -> Factory Fallback), distinctly separating established framework capabilities from planned future enhancements. Updated all C# code examples to employ target-typed `new()` and structurally validated the examples via compilation prior to submission.
