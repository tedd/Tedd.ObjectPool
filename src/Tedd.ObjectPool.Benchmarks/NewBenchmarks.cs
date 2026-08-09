using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using System.Threading;

namespace Tedd.ObjectPoolBenchmarks;

[MemoryDiagnoser]
public class NewBenchmarks
{
    private Tedd.ObjectPool<byte[]> _newPool = null!;
    private Tedd.Legacy.ObjectPool<byte[]> _archivePool = null!;

    [Params(4, 16)]
    public int Threads { get; set; }

    [Params(10_000)]
    public int OperationsPerThread { get; set; }

    [Params(256)]
    public int BufferSize { get; set; }

    [Params(64)]
    public int PoolSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _newPool = new Tedd.ObjectPool<byte[]>(() => new byte[BufferSize], PoolSize);
        _archivePool = new Tedd.Legacy.ObjectPool<byte[]>(() => new byte[BufferSize], PoolSize);
    }

    [Benchmark(Baseline = true)]
    public void ArchivePool()
    {
        var threads = new Thread[Threads];
        for (int i = 0; i < Threads; i++)
        {
            threads[i] = new Thread(() =>
            {
                for (int j = 0; j < OperationsPerThread; j++)
                {
                    var buf = _archivePool.Allocate();
                    _archivePool.Free(buf);
                }
            });
        }

        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();
    }

    [Benchmark]
    public void NewPool()
    {
        var threads = new Thread[Threads];
        for (int i = 0; i < Threads; i++)
        {
            threads[i] = new Thread(() =>
            {
                for (int j = 0; j < OperationsPerThread; j++)
                {
                    var buf = _newPool.Allocate();
                    _newPool.Free(buf);
                }
            });
        }

        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();
    }
}
