using System;
using System.Threading;
using Xunit;

namespace Tedd.ObjectPool.Tests;

public class AegisCoverageTests
{
    public class DisposableObject : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public int Id { get; set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    [Fact]
    public void Dispose_ShouldNotThrow_WhenPoolDisposed()
    {
        var pool = new ObjectPool<DisposableObject>(() => new DisposableObject());
        pool.Allocate(); // Initialize TLS
        var exception = Record.Exception(() => pool.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_WhenTlsIsNull_ShouldNotThrow()
    {
        var pool = new ObjectPool<DisposableObject>(() => new DisposableObject());
        var field = typeof(ObjectPool<DisposableObject>).GetField("_tls", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null)
        {
            var oldTls = (IDisposable?)field.GetValue(pool);
            oldTls?.Dispose();
            field.SetValue(pool, null);
        }
        var exception = Record.Exception(() => pool.Dispose());
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    [InlineData(100, 10)]
    public void Prefill_ShouldPopulateExpectedSlots(int prefillCount, int expectedCreates)
    {
        // 1 fast slot + 9 array slots = 10 capacity
        var poolSize = 10;
        var createCount = 0;
        var pool = new ObjectPool<DisposableObject>(() => new DisposableObject { Id = ++createCount }, poolSize);

        pool.Prefill(prefillCount);
        Assert.Equal(expectedCreates, createCount);
    }

    [Fact]
    public void Prefill_WhenTotallyFull_ShouldNotCreateNewInstances()
    {
        var poolSize = 10;
        var createCount = 0;
        var pool = new ObjectPool<DisposableObject>(() => new DisposableObject { Id = ++createCount }, poolSize);

        pool.Prefill(100);
        Assert.Equal(10, createCount);

        // Pre-fill when totally full
        pool.Prefill(1);
        Assert.Equal(10, createCount);
    }

    [Fact]
    public void Prefill_WhenFastSlotOccupied_ShouldPopulateArraySlots()
    {
        var createCount = 0;
        var pool = new ObjectPool<DisposableObject>(() => new DisposableObject { Id = ++createCount }, 10);

        // Allocate and free to occupy the fast slot
        var obj = pool.Allocate();
        pool.Free(obj);

        // Pre-fill should skip the fast slot and start populating array slots
        pool.Prefill(5);

        // 1 object from manual allocation + 5 from prefill
        Assert.Equal(6, createCount);
    }

    [Fact]
    public void Prefill_WhenSomeArraySlotsOccupied_ShouldSkipOccupiedSlots()
    {
        var createCount = 0;
        var pool = new ObjectPool<DisposableObject>(() => new DisposableObject { Id = ++createCount }, 10);

        // Allocate multiple items to ensure some array slots get occupied when freed
        var obj1 = pool.Allocate();
        var obj2 = pool.Allocate();
        var obj3 = pool.Allocate();

        pool.Free(obj1); // TLS
        pool.Free(obj2); // Fast Slot
        pool.Free(obj3); // Array Slot 0

        pool.Prefill(10);

        // Ensure that prefill correctly skipped occupied slots and filled the rest
        Assert.Equal(11, createCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllocateExecuteDeallocate_ShouldCleanupAndFree(bool useCleanup)
    {
        var createCount = 0;
        var cleanupCount = 0;
        var pool = new ObjectPool<DisposableObject>(() => new DisposableObject { Id = ++createCount });

        DisposableObject? executingObject = null;
        Action<DisposableObject> action = obj => { executingObject = obj; };
        Action<DisposableObject>? cleanup = useCleanup ? obj => cleanupCount++ : null;

        pool.AllocateExecuteDeallocate(action, cleanup);

        Assert.NotNull(executingObject);
        Assert.Equal(1, createCount);
        if (useCleanup)
        {
            Assert.Equal(1, cleanupCount);
        }

        // Should be reused
        var newObj = pool.Allocate();
        Assert.Same(executingObject, newObj);
    }

    [Fact]
    public void AllocateExecuteDeallocate_NullAction_ShouldThrowArgumentNullException()
    {
        var pool = new ObjectPool<DisposableObject>(() => new DisposableObject());
        var exception = Record.Exception(() => pool.AllocateExecuteDeallocate(null!));
        Assert.NotNull(exception);
        Assert.IsType<ArgumentNullException>(exception);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Free_DisposeWhenFull_ShouldDisposeExtraObjects(bool disposeWhenFull)
    {
        var poolSize = 2; // TLS + Fast Slot + 1 Array Slot
        var pool = new ObjectPool<DisposableObject>(() => new DisposableObject(), cleanup: null, size: poolSize, disposeWhenFull: disposeWhenFull);

        var obj1 = pool.Allocate();
        var obj2 = pool.Allocate();
        var obj3 = pool.Allocate();
        var obj4 = pool.Allocate();

        pool.Free(obj1); // TLS
        pool.Free(obj2); // Fast Slot
        pool.Free(obj3); // Array Slot 0
        pool.Free(obj4); // Full -> drop or dispose

        if (disposeWhenFull)
        {
            Assert.True(obj4.IsDisposed);
        }
        else
        {
            Assert.False(obj4.IsDisposed);
        }
    }
}
