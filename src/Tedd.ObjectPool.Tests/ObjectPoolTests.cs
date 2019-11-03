using System;
using System.Linq;
using System.Net.Http.Headers;
using Xunit;

namespace Tedd.ObjectPool.Tests
{
    public class ObjectPoolTests
    {
        public class DummyObject
        {
            public int Id = 0;
        }
        [Fact]
        public void CreateNew()
        {
            var maxCount = 100;
            var oc = 0;
            var objectPool = new ObjectPool<DummyObject>(() => new DummyObject() { Id = ++oc });
            for (var i = 0; i < maxCount; i++)
            {
                var o = objectPool.Allocate();
                Assert.Equal(oc, o.Id);
            }
        }

        [Fact]
        public void Recycle()
        {
            var maxCount = 100;
            var oc = 0;
            var objectPool = new ObjectPool<DummyObject>(() => new DummyObject() { Id = ++oc }, maxCount + 1);
            var os = new DummyObject[maxCount];
            for (var i = 0; i < maxCount; i++)
            {
                os[i] = objectPool.Allocate();
            }
            for (var i = 0; i < maxCount; i++)
            {
                objectPool.Free(os[i]);
                os[i] = null;
            }
            for (var i = 0; i < maxCount; i++)
            {
                var o = objectPool.Allocate();
                Assert.InRange(o.Id,0, maxCount+1);
                //Assert.True(o.Id < maxCount + 1, $"Id: {o.Id} should be < {maxCount + 1}");
            }
        }

        [Fact]
        public void CleanUp()
        {
            var maxCount = 100;
            var oc = 0;
            var objectPool = new ObjectPool<DummyObject>(() => new DummyObject() { Id = ++oc }, o => o.Id += 1000, maxCount + 1);
            var os = new DummyObject[maxCount];
            for (var i = 0; i < maxCount; i++)
            {
                os[i] = objectPool.Allocate();
            }
            for (var i = 0; i < maxCount; i++)
            {
                objectPool.Free(os[i]);
                os[i] = null;
            }
            for (var i = 0; i < maxCount; i++)
            {
                var o = objectPool.Allocate();
                Assert.InRange(o.Id, 1000, 1000 + maxCount + 1);
                //Assert.True(o.Id > 1000 && o.Id <, $"Id: {o.Id} should be > {1000} and < {1000 + maxCount + 1}");
            }
        }

        [Fact]
        public void PoolLimit()
        {
            var maxCount = 100;
            var poolSize = 50;
            var oc = 0;
            var objectPool = new ObjectPool<DummyObject>(() => new DummyObject() { Id = ++oc }, poolSize);
            var os = new DummyObject[maxCount];
            for (var i = 0; i < maxCount; i++)
            {
                os[i] = objectPool.Allocate();
            }
            for (var i = 0; i < maxCount; i++)
            {
                objectPool.Free(os[i]);
                os[i] = null;
            }

            oc += 1000;

            for (var i = 0; i < maxCount; i++)
            {
                os[i] = objectPool.Allocate();
            }

            var reusedObjects = os.Count(o => o.Id < 1000);
            var newObjects = os.Count(o => o.Id > 1000);

            Assert.Equal(50, reusedObjects);
            Assert.Equal(50, newObjects);

        }
    }
}
