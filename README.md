## Tedd.ObjectPool

High-performance, thread-safe object pool for reference types in .NET. Optimized for ultra-low overhead under contention with per-thread fast paths, avoiding locks on hot paths.

Under concurrent workloads, Tedd.ObjectPool delivers between 11× and 111× higher throughput than Microsoft.Extensions.ObjectPool, with the largest gains at high thread counts.

### Install

- .NET CLI:
  ```bash
  dotnet add package Tedd.ObjectPool
  ```
- PackageReference:
  ```xml
  <ItemGroup>
    <PackageReference Include="Tedd.ObjectPool" Version="x.y.z" />
  </ItemGroup>
  ```
- Package Manager Console:
  ```powershell
  Install-Package Tedd.ObjectPool -Version x.y.z
  ```

Replace `x.y.z` with the desired version.

## Quick start

Basic pooling of an object, e.g., `StringBuilder`:

```csharp
using Tedd;
using System.Text;

var pool = new ObjectPool<StringBuilder>(
    factory: () => new StringBuilder(capacity: 256),
    cleanup: sb => sb.Clear(),           // reset before publishing back
    size: 64                              // total pool slots
);

var sb = pool.Allocate();
try
{
    sb.Append("Hello, world!");
    // ... use sb ...
}
finally
{
    pool.Free(sb); // cleanup runs, then it goes back to the pool
}
```

## API overview

- **Constructors**
  - `new ObjectPool<T>(Factory factory)`
  - `new ObjectPool<T>(Factory factory, int size)`
  - `new ObjectPool<T>(Factory factory, Action<T> cleanup, int size)`
  - `new ObjectPool<T>(Factory factory, Action<T>? cleanup, int size, bool disposeWhenFull)`

  Where `Factory` is `delegate T Factory()` and `T : class`.

- **Core methods**
  - `T Allocate()`
  - `void Free(T obj)` (no-op if `obj` is `null`)
  - `void Prefill(int count)` – optionally pre-create items to reduce cold-start latency
  - `void AllocateExecuteDeallocate(Action<T> action, Action<T>? cleanupAction = null)`
  - `void Scoped<TState>(TState state, Action<T,TState> action)` – allocation-free scoped usage
  - `void Dispose()` – disposes internal TLS cache; pooled items are not disposed

- **Diagnostics (DEBUG builds)**
  - `void ForgetTrackedObject(T old, T? replacement = null)` – mark an object as intentionally not returned

### Defaults and behavior

- Default size is `Math.Max(1, Environment.ProcessorCount * 2)` when only the factory is provided.
- If `Free` is called when the pool is full:
  - Default: the object is dropped (eligible for GC).
  - If constructed with `disposeWhenFull: true` and `T : IDisposable`, the object is disposed.
- The optional `cleanup` action runs on every `Free` before the object is published back to the pool.
- `Free(null)` is a no-op; useful for simplifying caller logic.

## Usage patterns

### 1) Simple allocate/free

```csharp
var pool = new ObjectPool<byte[]>(
    factory: () => new byte[4096],
    cleanup: _ => { /* optional reset */ },
    size: 128
);

var buffer = pool.Allocate();
try
{
    // use buffer
}
finally
{
    pool.Free(buffer);
}
```

### 2) Scoped usage (auto-free)

```csharp
pool.Scoped(Unit.Value, (sb, _) =>
{
    sb.AppendLine("Scoped work");
});

// Or with state:
pool.Scoped("hello", (sb, text) =>
{
    sb.Append(text);
});

readonly struct Unit { public static readonly Unit Value = default; }
```

### 3) Execute and deallocate helper

```csharp
pool.AllocateExecuteDeallocate(
    action: sb => { sb.Append("work"); },
    cleanupAction: sb => sb.Clear() // optional extra cleanup for this call
);
```

### 4) Prefill to warm the pool

```csharp
pool.Prefill(count: 32);
```

### 5) Dispose overflow when full (for IDisposable)

```csharp
var socketPool = new ObjectPool<System.Net.Sockets.Socket>(
    factory: () => new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp),
    cleanup: s => { /* reset if applicable */ },
    size: 32,
    disposeWhenFull: true // overflowed sockets are disposed instead of dropped
);
```

## Thread-safety

- Designed for multi-threaded use. Most hot-path operations avoid locks.
- A per-thread single-item cache greatly reduces interlocked traffic for common allocate/free pairs.
- Allocation attempts spread contention across slots using rotating indices.

## When to use

- Reuse of expensive-to-create reference types (e.g., `StringBuilder`, buffers, serializers, `MemoryStream`).
- High-throughput services needing minimal allocation and contention.

## Notes and limitations

- `T` must be a reference type (`class`).
- The pool does not own lifetime of items except when `disposeWhenFull: true` is enabled for overflow.
- `Dispose()` only tears down internal thread-local storage; it does not dispose pooled items.

## Implementation and performance details

- **Fast paths kept tiny**: `Allocate`/`Free` are aggressively inlined; slow paths are marked `NoInlining` to keep the I-cache hot.
- **Per-thread 1-slot cache**: Most allocate/free pairs complete without touching shared memory.
- **Fast slot + array**: One hot fast slot (`_firstItem`) plus an array of elements (`size - 1`).
- **Correct memory publication**: Uses `Volatile.Read/Write` for unsynchronized accesses and a single CAS (`Interlocked.CompareExchange`) to claim/release.
- **Contention spreading**: Rotating probe indices on allocate/free reduce CAS collisions and cache-line ping-pong.
- **Optional prefill**: `Prefill` reduces first-use latency when the factory is expensive.
- **Overflow policy**: Configurable disposal of `IDisposable` items when the pool is full; default is drop-on-floor to let GC reclaim.
- **DEBUG diagnostics**: Optional leak tracking warns via `Debug.WriteLine` if an allocated item is never returned; `ForgetTrackedObject` lets you intentionally replace an item (e.g., upsizing a buffer) without spurious warnings.

These choices aim to deliver predictable, low-latency operation under contention while keeping the API simple and allocation-free on the hot path.

### Benchmarks

#### Overview
Tedd.ObjectPool is consistently faster than Microsoft.Extensions.ObjectPool across all tested thread counts.

#### Relative Speed Advantage
* 2 threads: Tedd.ObjectPool is 11.28× faster
* 4 threads: 19.12× faster
* 8 threads: 36.30× faster
* 16 threads: ~95× faster
* 32 threads: ~111× faster

#### Observation
* Tedd.ObjectPool sustains low, stable latencies as concurrency increases (8.8 µs → 35.9 µs).
* The performance advantage grows with thread count, indicating better scalability and reduced contention effects.

#### Conclusion:
Under concurrent workloads, Tedd.ObjectPool delivers between 11× and 111× higher throughput than Microsoft.Extensions.ObjectPool, with the largest gains at high thread counts.

#### Benchmark Results
// * Summary *

BenchmarkDotNet v0.15.2, Windows 11 (10.0.26100.4652/24H2/2024Update/HudsonValley)
Unknown processor
.NET SDK 10.0.100-preview.6.25358.103
  [Host]     : .NET 9.0.7 (9.0.725.31616), X64 RyuJIT AVX2
  DefaultJob : .NET 9.0.7 (9.0.725.31616), X64 RyuJIT AVX2


| Method                                            | Threads | OperationsPerThread | BufferSize | PoolSize | Mean         | Error      | StdDev     | Median       | Ratio  | RatioSD | Gen0     | Completed Work Items | Lock Contentions | Gen1   | Allocated  | Alloc Ratio |
|------------------------------------ |-------- |-------------------- |----------- |--------- |-------------:|-----------:|-----------:|-------------:|-------:|--------:|---------:|---------------------:|-----------------:|-------:|-----------:|------------:|
| Tedd.ObjectPool                     | 2       | 1000                | 256        | 64       |     8.827 us |  0.1760 us |  0.1646 us |     8.865 us |   1.00 |    0.03 |   0.0916 |               1.0000 |           0.0000 |      - |    1.66 KB |        1.00 |
| Microsoft.Extensions.ObjectPool     | 2       | 1000                | 256        | 64       |    99.532 us |  1.8606 us |  1.7404 us |    99.644 us |  11.28 |    0.28 |        - |               1.0000 |           0.0001 |      - |    1.66 KB |        1.00 |
| Tedd.ObjectPool_v1                  | 2       | 1000                | 256        | 64       |    40.741 us |  0.7680 us |  0.7184 us |    40.656 us |   4.62 |    0.11 |   2.1973 |               1.0000 |           0.0002 |      - |   36.06 KB |       21.77 |
|                                     |         |                     |            |          |              |            |            |              |        |         |          |                      |                  |        |            |             |
| Tedd.ObjectPool                     | 4       | 1000                | 256        | 64       |    14.222 us |  0.4897 us |  1.3893 us |    14.608 us |   1.01 |    0.17 |   0.1221 |               2.9998 |           0.0002 |      - |    2.08 KB |        1.00 |
| Microsoft.Extensions.ObjectPool     | 4       | 1000                | 256        | 64       |   268.482 us |  3.0884 us |  4.4294 us |   267.717 us |  19.12 |    2.55 |        - |               2.9995 |           0.0054 |      - |     2.1 KB |        1.01 |
| Tedd.ObjectPool_v1                  | 4       | 1000                | 256        | 64       |   157.382 us |  2.5749 us |  3.1622 us |   156.239 us |  11.21 |    1.50 |  15.3809 |               2.9998 |           0.0027 |      - |  248.64 KB |      119.65 |
|                                     |         |                     |            |          |              |            |            |              |        |         |          |                      |                  |        |            |             |
| Tedd.ObjectPool                     | 8       | 1000                | 256        | 64       |    17.623 us |  0.3764 us |  1.1098 us |    17.864 us |   1.01 |    0.10 |   0.1678 |               6.9634 |           0.0003 |      - |    2.92 KB |        1.00 |
| Microsoft.Extensions.ObjectPool     | 8       | 1000                | 256        | 64       |   636.495 us |  8.2262 us | 10.1025 us |   633.216 us |  36.30 |    2.97 |        - |               6.9971 |           0.0029 |      - |    2.98 KB |        1.02 |
| Tedd.ObjectPool_v1                  | 8       | 1000                | 256        | 64       |   438.754 us |  4.0612 us |  6.8961 us |   436.242 us |  25.02 |    2.05 |  51.7578 |               6.9990 |           0.0039 |      - |  844.28 KB |      289.43 |
|                                     |         |                     |            |          |              |            |            |              |        |         |          |                      |                  |        |            |             |
| Tedd.ObjectPool                     | 16      | 1000                | 256        | 64       |    21.821 us |  0.3818 us |  0.8695 us |    21.776 us |   1.00 |    0.05 |   0.2441 |              12.0614 |           0.0002 |      - |    4.16 KB |        1.00 |
| Microsoft.Extensions.ObjectPool     | 16      | 1000                | 256        | 64       | 2,068.935 us | 39.3110 us | 46.7970 us | 2,086.189 us |  94.95 |    4.14 |        - |              15.0000 |           0.0078 |      - |    4.71 KB |        1.13 |
| Tedd.ObjectPool_v1                  | 16      | 1000                | 256        | 64       | 1,089.897 us | 18.9815 us | 30.6516 us | 1,078.430 us |  50.02 |    2.34 | 119.1406 |              14.9629 |           0.0117 | 1.9531 |  1955.6 KB |      470.63 |
|                                     |         |                     |            |          |              |            |            |              |        |         |          |                      |                  |        |            |             |
| Tedd.ObjectPool                     | 32      | 1000                | 256        | 64       |    35.892 us |  0.1476 us |  0.1232 us |    35.851 us |   1.00 |    0.00 |   0.3052 |              16.7516 |           0.0001 |      - |    5.78 KB |        1.00 |
| Microsoft.Extensions.ObjectPool     | 32      | 1000                | 256        | 64       | 3,974.344 us | 69.3458 us | 64.8661 us | 4,003.417 us | 110.73 |    1.79 |        - |              30.9922 |           0.0156 |      - |    8.23 KB |        1.42 |
| Tedd.ObjectPool_v1                  | 32      | 1000                | 256        | 64       | 2,798.889 us | 21.8024 us | 19.3273 us | 2,793.363 us |  77.98 |    0.58 | 253.9063 |              30.5508 |           0.0156 | 7.8125 | 4137.25 KB |      716.00 |
