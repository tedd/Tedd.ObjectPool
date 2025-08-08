## Tedd.ObjectPool

High-performance, thread-safe object pool for reference types in .NET. Optimized for ultra-low overhead under contention with per-thread fast paths, avoiding locks on hot paths.

## Thread-safety

- Designed for multi-threaded use. Most hot-path operations avoid locks.
- A per-thread single-item cache greatly reduces interlocked traffic for common allocate/free pairs.
- Allocation attempts spread contention across slots using rotating indices.

## Implementation and performance details

- **Fast paths kept tiny**: `Allocate`/`Free` are aggressively inlined; slow paths are marked `NoInlining` to keep the I-cache hot.
- **Per-thread 1-slot cache**: Most allocate/free pairs complete without touching shared memory.
- **Fast slot + array**: One hot fast slot (`_firstItem`) plus an array of elements (`size - 1`).
- **Correct memory publication**: Uses `Volatile.Read/Write` for unsynchronized accesses and a single CAS (`Interlocked.CompareExchange`) to claim/release.
- **Contention spreading**: Rotating probe indices on allocate/free reduce CAS collisions and cache-line ping-pong.
- **Optional prefill**: `Prefill` reduces first-use latency when the factory is expensive.
- **Overflow policy**: Configurable disposal of `IDisposable` items when the pool is full; default is drop-on-floor to let GC reclaim.
- **DEBUG diagnostics**: Optional leak tracking warns via `Debug.WriteLine` if an allocated item is never returned; `ForgetTrackedObject` lets you intentionally replace an item (e.g., upsizing a buffer) without spurious warnings.

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