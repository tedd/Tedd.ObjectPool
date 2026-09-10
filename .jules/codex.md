## 2026-06-10 - Architectural Execution Flow Articulation
**Observation:** The README.md documentation exhibited a deficit regarding the explicit articulation of the underlying multi-tiered allocation strategy (TLS cache, fast slot, shared array, and fallback). It lacked a clear delineation of the internal mechanics and did not explicitly state the absence of planned hypothetical enhancements, leaving room for speculative assumptions. Code examples were also not utilizing contemporary .NET features like target-typed `new()`.
**Strategic Action:** Synchronized the README.md to explicitly articulate the architectural execution flow, separating implemented capabilities from hypotheses. Updated code examples to leverage modern C# syntax.

## 2026-09-10 - Architectural Execution Flow Articulation (Missing Steps)
**Observation:** The README.md documentation was missing explicit articulation of steps 1 and 3 of the architectural execution flow (TLS cache and Array Probe), and some code examples were still not utilizing target-typed `new()`.
**Strategic Action:** Synchronized the README.md to fully articulate all 4 steps of the architectural execution flow, mapping to `_tls` cache, `_firstItem` fast slot, `_items` array probe, and factory fallback. Updated code examples to use target-typed `new()`.
