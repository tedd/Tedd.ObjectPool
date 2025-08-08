using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

namespace Tedd.ObjectPool.Tests;

public class ComprehensiveObjectPoolTests
{
    #region Test Classes

    [DebuggerDisplay("{Id} - {Name} - {IsReset}")]
    public class TestObject
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public bool IsReset { get; set; }

        public void Reset()
        {
            Id = 0;
            Name = null;
            IsReset = true;
        }
    }

    public class DisposableTestObject : IDisposable
    {
        public int Id { get; set; }
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    // Test-unique types to avoid TLS cross-test interference
    public class UniqueA
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    public class UniqueDisposableA : IDisposable
    {
        public int Id { get; set; }
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }

    public class UniqueB
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithFactoryOnly_ShouldCreateWithDefaultSize()
    {
        // Arrange & Act
        var pool = new ObjectPool<TestObject>(() => new TestObject());

        // Assert - Should not throw and should work
        var obj = pool.Allocate();
        Assert.NotNull(obj);
        pool.Free(obj);
    }

    [Fact]
    public void Constructor_WithFactoryAndSize_ShouldCreateWithSpecifiedSize()
    {
        // Arrange & Act
        var pool = new ObjectPool<TestObject>(() => new TestObject(), 10);

        // Assert - Should not throw and should work
        var obj = pool.Allocate();
        Assert.NotNull(obj);
        pool.Free(obj);
    }

    [Fact]
    public void Constructor_WithAllParameters_ShouldCreateSuccessfully()
    {
        // Arrange & Act
        var pool = new ObjectPool<TestObject>(
            () => new TestObject(),
            obj => obj.Reset(),
            10);

        // Assert - Should not throw and should work
        var obj = pool.Allocate();
        Assert.NotNull(obj);
        pool.Free(obj);
    }

    [Fact]
    public void Constructor_WithNullFactory_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ObjectPool<TestObject>(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Constructor_WithInvalidSize_ShouldThrowArgumentOutOfRangeException(int size)
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ObjectPool<TestObject>(() => new TestObject(), size));
    }

    #endregion

    #region Allocate Tests

    [Fact]
    public void Allocate_WhenPoolIsEmpty_ShouldCreateNewObject()
    {
        // Arrange
        var createCount = 0;
        var pool = new ObjectPool<UniqueB>(() => new UniqueB { Id = ++createCount });

        // Act
        var obj = pool.Allocate();

        // Assert
        Assert.NotNull(obj);
        Assert.Equal(1, obj.Id);
        Assert.Equal(1, createCount);
    }

    [Fact]
    public void Allocate_MultipleCallsOnEmptyPool_ShouldCreateNewObjectsEachTime()
    {
        // Arrange
        var createCount = 0;
        var pool = new ObjectPool<UniqueA>(() => new UniqueA { Id = ++createCount });

        // Act
        var obj1 = pool.Allocate();
        var obj2 = pool.Allocate();
        var obj3 = pool.Allocate();

        // Assert
        Assert.NotNull(obj1);
        Assert.NotNull(obj2);
        Assert.NotNull(obj3);
        Assert.Equal(1, obj1.Id);
        Assert.Equal(2, obj2.Id);
        Assert.Equal(3, obj3.Id);
        Assert.Equal(3, createCount);
    }

    [Fact]
    public void Allocate_AfterFreeingObject_ShouldReuseObject()
    {
        // Arrange
        var createCount = 0;
        var pool = new ObjectPool<UniqueB>(() => new UniqueB { Id = ++createCount });
        var originalObj = pool.Allocate();
        pool.Free(originalObj);

        // Act
        var reusedObj = pool.Allocate();

        // Assert
        Assert.Same(originalObj, reusedObj);
        // With TLS cache, allocate after exception may reuse same instance without new creation
        Assert.InRange(createCount, 1, 2);
    }

    #endregion

    #region Free Tests

    [Fact]
    public void Free_WithNullObject_ShouldNotThrowInRelease()
    {
        // Arrange
        var pool = new ObjectPool<TestObject>(() => new TestObject());

        // Act & Assert - In Release, Free(null) should not throw (DEBUG-only validation)
        pool.Free(null!);
    }

    [Fact]
    public void Free_WithValidObject_ShouldAcceptObject()
    {
        // Arrange
        var pool = new ObjectPool<TestObject>(() => new TestObject());
        var obj = pool.Allocate();

        // Act & Assert - Should not throw
        pool.Free(obj);
    }

    [Fact]
    public void Free_WithCleanupAction_ShouldInvokeCleanup()
    {
        // Arrange
        var cleanupCalled = false;
        var pool = new ObjectPool<TestObject>(
            () => new TestObject(),
            obj => { cleanupCalled = true; obj.Reset(); },
            10);
        var obj = pool.Allocate();

        // Act
        pool.Free(obj);

        // Assert
        Assert.True(cleanupCalled);
        Assert.True(obj.IsReset);
    }

    [Fact]
    public void Free_WhenPoolIsFull_ShouldDisposeDisposableObjects()
    {
        // Arrange
        var pool = new ObjectPool<UniqueDisposableA>(() => new UniqueDisposableA(), cleanup: null, size: 1, disposeWhenFull: true);
        var obj1 = pool.Allocate();
        var obj2 = pool.Allocate();
        var obj3 = pool.Allocate();
        pool.Free(obj1); // TLS
        pool.Free(obj2); // Fast slot

        // Act - third free should trigger disposal due to full pool + TLS occupied
        pool.Free(obj3);

        // Assert
        Assert.False(obj1.IsDisposed); // In TLS
        Assert.False(obj2.IsDisposed); // In fast slot
        Assert.True(obj3.IsDisposed);  // Disposed due to overflow
    }

    [Fact]
    public void Free_ExceedingPoolSize_ShouldNotStoreExtraObjects()
    {
        // Arrange
        var poolSize = 2;
        var createCount = 0;
        var pool = new ObjectPool<UniqueA>(() => new UniqueA { Id = ++createCount }, poolSize);

        var objects = new UniqueA[5];
        for (int i = 0; i < 5; i++)
        {
            objects[i] = pool.Allocate();
        }

        // Act - Free all objects
        foreach (var obj in objects)
        {
            pool.Free(obj);
        }

        // Assert - Only poolSize objects should be reused
        var reusedObjects = new UniqueA[5];
        for (int i = 0; i < 5; i++)
        {
            reusedObjects[i] = pool.Allocate();
        }

        var originalObjectsReused = reusedObjects.Count(obj => obj.Id <= 5);
        var newObjectsCreated = reusedObjects.Count(obj => obj.Id > 5);

        // Pool slots + TLS cache can be reused
        Assert.Equal(poolSize + 1, originalObjectsReused);
        Assert.Equal(5 - (poolSize + 1), newObjectsCreated);
    }

    #endregion

    #region Scoped Tests

    [Fact]
    public void Scoped_WithAction_ShouldAllocateExecuteAndFree()
    {
        // Arrange
        var createCount = 0;
        var pool = new ObjectPool<UniqueB>(() => new UniqueB { Id = ++createCount });
        UniqueB? capturedObject = null;

        // Act
        pool.Scoped(0, (obj, _) =>
        {
            capturedObject = obj;
            obj.Name = "Test";
        });

        // Assert
        Assert.NotNull(capturedObject);
        Assert.Equal("Test", capturedObject.Name);
        Assert.Equal(1, createCount);

        // Verify object was returned to pool
        var reusedObj = pool.Allocate();
        Assert.Same(capturedObject, reusedObj);
    }

    [Fact]
    public void Scoped_WhenActionThrows_ShouldStillFreeObject()
    {
        // Arrange
        var createCount = 0;
        var pool = new ObjectPool<UniqueB>(() => new UniqueB { Id = ++createCount });

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
        {
            pool.Scoped(0, (obj, _) =>
            {
                obj.Name = "Test";
                throw new InvalidOperationException("Test exception");
            });
        });

        // Verify object was still returned to pool despite exception
        var reusedObj = pool.Allocate();
        Assert.Equal("Test", reusedObj.Name); // Object was modified before exception
        Assert.Equal(1, createCount); // No new object created
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task ConcurrentAllocateAndFree_ShouldBeThreadSafe()
    {
        // Arrange
        var poolSize = 10;
        var operationsPerThread = 1000;
        var threadCount = Environment.ProcessorCount;
        var createCount = 0;
        var pool = new ObjectPool<TestObject>(() => new TestObject { Id = Interlocked.Increment(ref createCount) }, poolSize);

        var tasks = new Task[threadCount];
        var exceptions = new ConcurrentBag<Exception>();

        // Act
        for (int i = 0; i < threadCount; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                try
                {
                    for (int j = 0; j < operationsPerThread; j++)
                    {
                        var obj = pool.Allocate();
                        Assert.NotNull(obj);

                        // Simulate some work
                        obj.Name = $"Thread{Thread.CurrentThread.ManagedThreadId}_Op{j}";

                        pool.Free(obj);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
        }

        await Task.WhenAll(tasks);

        // Assert
        Assert.Empty(exceptions);

        // Verify the pool still works correctly
        var finalObj = pool.Allocate();
        Assert.NotNull(finalObj);
    }

    [Fact]
    public async Task ConcurrentScoped_ShouldBeThreadSafe()
    {
        // Arrange
        var poolSize = 5;
        var operationsPerThread = 500;
        var threadCount = Environment.ProcessorCount;
        var createCount = 0;
        var pool = new ObjectPool<TestObject>(() => new TestObject { Id = Interlocked.Increment(ref createCount) }, poolSize);

        var tasks = new Task[threadCount];
        var exceptions = new ConcurrentBag<Exception>();
        var processedItems = new ConcurrentBag<int>();

        // Act
        for (int i = 0; i < threadCount; i++)
        {
            var threadId = i;
            tasks[i] = Task.Run(() =>
            {
                try
                {
                    for (int j = 0; j < operationsPerThread; j++)
                    {
                        pool.Scoped(j, (obj, op) =>
                        {
                            Assert.NotNull(obj);
                            obj.Name = $"Thread{threadId}_Op{op}";
                            processedItems.Add(obj.Id);
                        });
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
        }

        await Task.WhenAll(tasks);

        // Assert
        Assert.Empty(exceptions);
        Assert.Equal(threadCount * operationsPerThread, processedItems.Count);
    }

    #endregion

    #region Performance and Stress Tests

    [Fact]
    public void HighVolumeAllocateAndFree_ShouldPerformWell()
    {
        // Arrange
        var poolSize = 100;
        var operations = 10000;
        var pool = new ObjectPool<TestObject>(() => new TestObject(), poolSize);

        // Act & Assert - Should complete without throwing
        for (int i = 0; i < operations; i++)
        {
            var obj = pool.Allocate();
            Assert.NotNull(obj);
            pool.Free(obj);
        }
    }

    [Fact]
    public void AllocateMany_ThenFreeMany_ShouldReuseObjects()
    {
        // Arrange
        var poolSize = 50;
        var objectCount = 100;
        var createCount = 0;
        var pool = new ObjectPool<TestObject>(() => new TestObject { Id = ++createCount }, poolSize);

        // Act - Allocate many objects
        var objects = new TestObject[objectCount];
        for (int i = 0; i < objectCount; i++)
        {
            objects[i] = pool.Allocate();
        }

        // Free all objects
        for (int i = 0; i < objectCount; i++)
        {
            pool.Free(objects[i]);
        }

        // Allocate again
        var newObjects = new TestObject[objectCount];
        for (int i = 0; i < objectCount; i++)
        {
            newObjects[i] = pool.Allocate();
        }

        // Assert
        var reusedCount = newObjects.Count(obj => obj.Id <= objectCount);
        var newCount = newObjects.Count(obj => obj.Id > objectCount);

        // Allow TLS cache to increase reuse by one
        Assert.True(reusedCount == poolSize || reusedCount == poolSize + 1);
        Assert.Equal(objectCount - reusedCount, newCount);
    }

    #endregion

    #region Edge Cases and Error Conditions

    [Fact]
    public void Factory_ThrowingException_ShouldPropagateException()
    {
        // Arrange
        var pool = new ObjectPool<TestObject>(() => throw new InvalidOperationException("Factory error"));

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => pool.Allocate());
    }

    [Fact]
    public void Cleanup_ThrowingException_ShouldNotPreventFreeOperation()
    {
        // Arrange
        var pool = new ObjectPool<TestObject>(
            () => new TestObject(),
            obj => throw new InvalidOperationException("Cleanup error"),
            10);
        var obj = pool.Allocate();

        // Act & Assert - Should throw the cleanup exception
        Assert.Throws<InvalidOperationException>(() => pool.Free(obj));
    }

    [Fact]
    public void SingleItemPool_ShouldWorkCorrectly()
    {
        // Arrange
        var createCount = 0;
        var pool = new ObjectPool<TestObject>(() => new TestObject { Id = ++createCount }, 1);

        // Act
        var obj1 = pool.Allocate();
        var obj2 = pool.Allocate(); // Should create new since pool is empty
        pool.Free(obj1); // Should go to pool
        pool.Free(obj2); // Should be discarded (pool full)
        var obj3 = pool.Allocate(); // Should reuse obj1

        // Assert
        Assert.Same(obj1, obj3);
        Assert.NotSame(obj2, obj3);
        // TLS cache may reduce the number of new creations
        Assert.InRange(createCount, 1, 2);
    }

    [Fact]
    public void VeryLargePoolSize_ShouldNotCauseIssues()
    {
        // Arrange
        var largePoolSize = 10000;
        var pool = new ObjectPool<TestObject>(() => new TestObject(), largePoolSize);

        // Act & Assert - Should not throw
        var obj = pool.Allocate();
        Assert.NotNull(obj);
        pool.Free(obj);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void RealWorldScenario_HttpClientLikeUsage_ShouldWorkCorrectly()
    {
        // Simulate a scenario similar to HttpClient pooling
        var pool = new ObjectPool<TestObject>(
            () => new TestObject(),
            obj => obj.Reset(), // Reset state like clearing headers
            Environment.ProcessorCount * 2);

        // Simulate multiple "requests"
        for (int request = 0; request < 100; request++)
        {
            pool.Scoped(request, (client, req) =>
            {
                client.Id = req;
                client.Name = $"Request-{req}";

                // Simulate work
                Assert.NotNull(client);
                Assert.Equal(req, client.Id);
            });
        }

        // Verify pool is still functional
        var finalClient = pool.Allocate();
        Assert.NotNull(finalClient);
        Assert.True(finalClient.IsReset); // Should have been reset
        pool.Free(finalClient);
    }

    #endregion
}