// Enable during development to diagnose misuse; keep disabled for benchmarks/release builds.
// #define TRACE_LEAKS
// #define DETECT_LEAKS  // typically on in DEBUG

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

// ReSharper disable once CheckNamespace
namespace Tedd;

/// <summary>
/// High-performance, thread-safe object pool for reference types.
///
/// Optimizations:
/// - True fast paths: Allocate/Free are inlined; slow paths are NoInlining (hot code stays tiny).
/// - Correct memory publication: Volatile.Read/Write on unsynchronized accesses; CAS only when claiming.
/// - Contention spreading: rotating probe indices for allocate/free instead of hammering slot 0.
/// - Per-thread 1-slot cache: most Allocate/Free pairs avoid interlocked traffic entirely.
/// - Optional prefill: reduce cold-start latency if factory is expensive.
/// - Configurable overflow behavior via optional overloads (kept off by default to match original behavior).
///
/// Diagnostics:
/// - Optional leak tracking in DEBUG (TRACE_LEAKS adds stack traces).
/// </summary>
[DebuggerDisplay("Size={_items.Length + 1}, DisposeWhenFull={_disposeWhenFull}")]
public sealed class ObjectPool<T> : IDisposable where T : class
{
    [DebuggerDisplay("{Value,nq}")]
    private struct Element
    {
        public T? Value;
    }

    /// <remarks>
    /// Using a delegate rather than new T() allows callers to initialize instances
    /// and is often faster than Activator.CreateInstance.
    /// </remarks>
    public delegate T Factory();

    private T? _firstItem;                // Hot fast slot.
    private readonly Element[] _items;   // Remaining slots.
    private readonly Factory _factory;
    private readonly Action<T>? _cleanup;
    private readonly bool _disposeWhenFull;

    // Rotating cursors to spread contention across the array (reduced cache-line ping-pong).
    private int _freeIdx;
    private int _allocIdx;

    // Per-thread single-item cache, scoped per pool instance to avoid cross-pool contamination.
    private readonly ThreadLocal<T?> _tls = new(() => null);

#if DETECT_LEAKS
    private static readonly ConditionalWeakTable<T, LeakTracker> LeakTrackers = new();

    private sealed class LeakTracker : IDisposable
    {
        private volatile bool _disposed;

#if TRACE_LEAKS
        public volatile object? Trace;
#endif

        public void Dispose()
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private string GetTrace()
        {
#if TRACE_LEAKS
            return Trace == null ? "" : Trace.ToString();
#else
                return "Define TRACE_LEAKS to include stack traces in leak diagnostics.\n";
#endif
        }

        ~LeakTracker()
        {
            if (!_disposed && !Environment.HasShutdownStarted)
            {
                Debug.WriteLine(
                    $"TRACEOBJECTPOOLLEAKS_BEGIN\nPool detected potential leaking of {typeof(T)}.\n" +
                    $"Location of the leak:\n{GetTrace()}TRACEOBJECTPOOLLEAKS_END");
            }
        }
    }

#if TRACE_LEAKS
    // ReSharper disable once StaticMemberInGenericType
    private static readonly Lazy<Type> StackTraceType = new(() => Type.GetType("System.Diagnostics.StackTrace"));
    private static object CaptureStackTrace() => Activator.CreateInstance(StackTraceType.Value);
#endif
#endif // DETECT_LEAKS

    /// <summary>
    /// Initializes a new instance of the pool using the specified <paramref name="factory"/>
    /// and a default size based on the number of processors.
    /// </summary>
    public ObjectPool(Factory factory)
        : this(factory, cleanup: null, size: Math.Max(1, Environment.ProcessorCount * 2), disposeWhenFull: false)
    { }

    /// <summary>
    /// Initializes a new instance of the pool using the specified <paramref name="factory"/>
    /// and <paramref name="size"/>.
    /// </summary>
    public ObjectPool(Factory factory, int size)
        : this(factory, cleanup: null, size: size, disposeWhenFull: false)
    { }

    /// <summary>
    /// Initializes a new instance of the pool using the specified <paramref name="factory"/>,
    /// a per-item <paramref name="cleanup"/> action executed before returning items to the pool,
    /// and the given <paramref name="size"/>.
    /// </summary>
    public ObjectPool(Factory factory, Action<T> cleanup, int size)
        : this(factory, cleanup, size, disposeWhenFull: false)
    { }

    /// <summary>
    /// Initializes a new instance of the pool using the specified <paramref name="factory"/>, optional
    /// <paramref name="cleanup"/> action, <paramref name="size"/>, and overflow behavior controlled by
    /// <paramref name="disposeWhenFull"/>.
    /// </summary>
    public ObjectPool(Factory factory, Action<T>? cleanup, int size, bool disposeWhenFull)
    {
        if (size < 1) throw new ArgumentOutOfRangeException(nameof(size));

        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _cleanup = cleanup;
        _disposeWhenFull = disposeWhenFull;

        // One fast slot + (size - 1) array slots. Access pattern favors low indices (cache-friendly).
        _items = new Element[size - 1];
    }

    public void Dispose()
    {
        _tls?.Dispose();
    }

    /// <summary>
    /// Optional: prefill up to <paramref name="count"/> items to reduce first-hit latency when factory is expensive.
    /// </summary>
    public void Prefill(int count)
    {
        if (count <= 0) return;

        // Fill fast slot first — cheapest future hit.
        if (Volatile.Read(ref _firstItem) == null)
        {
            Volatile.Write(ref _firstItem, _factory());
            if (--count == 0) return;
        }

        var items = _items;
        int len = items.Length;
        for (int i = 0; i < len && count > 0; i++)
        {
            if (Volatile.Read(ref items[i].Value) == null)
            {
                Volatile.Write(ref items[i].Value, _factory());
                count--;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private T CreateInstance() => _factory(); // Kept tiny for branch-prediction friendliness.

    /// <summary>
    /// Allocate from TLS cache → fast slot → array → new.
    /// Hot path is aggressively inlined; slow path is NoInlining to keep I-cache hot.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Allocate()
    {
        // 1) TLS cache: zero contention, no interlocked; fastest path.
        var t = _tls.Value;
        if (t != null)
        {
            _tls.Value = null;
            return PostAllocate(t);
        }

        // 2) Fast slot: optimistic read, claim with a single CAS.
        var inst = Volatile.Read(ref _firstItem);
        if (inst != null && Interlocked.CompareExchange(ref _firstItem, null, inst) == inst)
            return PostAllocate(inst);

        // 3) Slow path: probe array starting at a rotating index to reduce contention.
        inst = AllocateSlow();
        return PostAllocate(inst);
    }

    /// <summary>
    /// Return to TLS cache → fast slot → array. Optionally dispose when full (off by default).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Free(T obj)
    {
        // Gracefully ignore null inputs (common no-op pattern for pools)
        if (obj is null) return;

        Validate(obj);
        ForgetTrackedObject(obj);   // DEBUG-only (compiled out in Release).

        // Run caller-provided cleanup before publishing.
        _cleanup?.Invoke(obj);

        // 1) TLS cache: cheapest store; avoids shared contention.
        if (_tls.Value == null)
        {
            _tls.Value = obj;
            return;
        }

        // 2) Fast slot: quick publish if empty.
        if (Volatile.Read(ref _firstItem) == null)
        {
            Volatile.Write(ref _firstItem, obj);
            return;
        }

        // 3) Array path; if full, optionally dispose (configurable).
        FreeSlow(obj);
    }

    /// <summary>
    /// Convenience wrapper: allocates, executes, cleans, and frees.
    /// </summary>
    public void AllocateExecuteDeallocate(Action<T> action, Action<T>? cleanupAction = null)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        var obj = Allocate();
        try
        {
            action(obj);
        }
        finally
        {
            cleanupAction?.Invoke(obj);
            Free(obj);
        }
    }

    // ---- Slow paths: NoInlining keeps hot paths smaller and faster ----

    [MethodImpl(MethodImplOptions.NoInlining)]
    private T AllocateSlow()
    {
        var items = _items;
        int len = items.Length;
        if (len != 0)
        {
            // Rotating start reduces CAS collisions and cache-line ping-pong.
            int start = Interlocked.Increment(ref _allocIdx);
            for (int k = 0; k < len; k++)
            {
                int i = (start + k) % len;
                var candidate = Volatile.Read(ref items[i].Value);
                if (candidate != null &&
                    Interlocked.CompareExchange(ref items[i].Value, null, candidate) == candidate)
                {
                    return candidate;
                }
            }
        }

        // Empty pool → create new instance.
        return CreateInstance();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void FreeSlow(T obj)
    {
        var items = _items;
        int len = items.Length;
        if (len != 0)
        {
            int start = Interlocked.Increment(ref _freeIdx);
            for (int k = 0; k < len; k++)
            {
                int i = (start + k) % len;
                if (Volatile.Read(ref items[i].Value) == null)
                {
                    Volatile.Write(ref items[i].Value, obj);
                    return;
                }
            }
        }

        // Pool is full. Original behavior: drop on the floor (let GC reclaim).
        // Optional (new): dispose if explicitly requested via the new overload.
        if (_disposeWhenFull && obj is IDisposable d)
        {
            d.Dispose();
        }
    }

    // ---- Diagnostics hooks (compiled out in Release) ----

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T PostAllocate(T inst)
    {
#if DETECT_LEAKS
#pragma warning disable CA2000
        var tracker = new LeakTracker();
#pragma warning restore CA2000
        LeakTrackers.Add(inst, tracker);
#if TRACE_LEAKS
        tracker.Trace = CaptureStackTrace();
#endif
#endif
        return inst;
    }

    /// <summary>
    /// Removes an object from leak tracking (called on Free). Can also be called explicitly
    /// if a pooled object is intentionally not returned (e.g., replacement with a larger array).
    /// </summary>
    [Conditional("DEBUG")]
    public void ForgetTrackedObject(T old, T? replacement = null)
    {
#if DETECT_LEAKS
        if (LeakTrackers.TryGetValue(old, out var tracker))
        {
            tracker.Dispose();
            LeakTrackers.Remove(old);
        }
        else
        {
            Debug.WriteLine(
                $"TRACEOBJECTPOOLLEAKS_BEGIN\nObject of type {typeof(T)} was freed but not tracked as pooled.\n" +
                $"Enable TRACE_LEAKS for call stacks.\nTRACEOBJECTPOOLLEAKS_END");
        }

        if (replacement is not null)
        {
#pragma warning disable CA2000
            var t = new LeakTracker();
#pragma warning restore CA2000
            LeakTrackers.Add(replacement, t);
#if TRACE_LEAKS
            t.Trace = CaptureStackTrace();
#endif
        }
#endif
    }

    [Conditional("DEBUG")]
    private void Validate(object obj)
    {
        Debug.Assert(obj != null, "freeing null?");

        // Optional double-free detection (DEBUG-only to avoid scan costs in Release).
        var items = _items;
        for (int i = 0; i < items.Length; i++)
        {
            var value = items[i].Value;
            if (value is null) return;
            Debug.Assert(!ReferenceEquals(value, obj), "freeing twice?");
        }
    }

    /// <summary>
    /// (Allocation-free). Automates executing a stateful action with a pooled object.
    /// </summary>
    /// <typeparam name="TState">The type of the state to pass to the action.</typeparam>
    /// <param name="state">The state to pass to the action.</param>
    /// <param name="action">The action to execute with the allocated object and the provided state.</param>
    public void Scoped<TState>(TState state, Action<T, TState> action)
    {
        var obj = Allocate();
        try
        {
#pragma warning disable CA1062
            action(obj, state);
#pragma warning restore CA1062
        }
        finally
        {
            Free(obj);
        }
    }
}

