 using BenchmarkDotNet.Attributes;
 using BenchmarkDotNet.Configs;
 using BenchmarkDotNet.Exporters;
 using BenchmarkDotNet.Exporters.Csv;

using Microsoft.Extensions.ObjectPool;

using System;
using System.Threading;
using System.Threading.Tasks;

using LegacyPool = Tedd.ObjectPool.Benchmarks.Legacy.ObjectPool1<byte[]>;

namespace Tedd.ObjectPool.Benchmarks;

 [MemoryDiagnoser]
 [ThreadingDiagnoser]
 [Config(typeof(BenchConfig))]
 public class MultiThreadBenchmarks
{
    private Tedd.ObjectPool<byte[]> _teddPool = null!;
    private DefaultObjectPool<byte[]> _msPool = null!;
    private LegacyPool _legacyPool = null!;

    private long _sink; // prevent JIT elision

    [Params(2, 4, 8, 16, 32)]
    //[Params(32)]
    public int Threads { get; set; } = Math.Max(2, Environment.ProcessorCount);

    [Params(1_000)]
    public int OperationsPerThread { get; set; }

    [Params(256)]
    public int BufferSize { get; set; }

    [Params(64)]
    public int PoolSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _teddPool = new Tedd.ObjectPool<byte[]>(() => new byte[BufferSize], PoolSize);
        _msPool = new DefaultObjectPool<byte[]>(new FixedArrayPolicy(BufferSize), PoolSize);
        _legacyPool = new LegacyPool(() => new byte[BufferSize], PoolSize);

        // Prefill/warmup: try to retain items inside each pool to reduce cold-start effects.
        WarmupTedd();
        WarmupMs();
        WarmupLegacy();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _teddPool.Dispose();
    }

    [Benchmark(Baseline = true)]
    public long Tedd_ObjectPool_AllocateFree_Parallel()
    {
        long sum = 0;
        Parallel.For(0, Threads, new ParallelOptions { MaxDegreeOfParallelism = Threads }, _ =>
        {
            long local = 0;
            for (int i = 0; i < OperationsPerThread; i++)
            {
                var buf = _teddPool.Allocate();
                buf[0] = (byte)(i & 0xFF);
                local += buf[0];
                _teddPool.Free(buf);
            }
            Interlocked.Add(ref sum, local);
        });
        Volatile.Write(ref _sink, sum);
        return sum;
    }

    [Benchmark]
    public long Microsoft_DefaultObjectPool_AllocateFree_Parallel()
    {
        long sum = 0;
        Parallel.For(0, Threads, new ParallelOptions { MaxDegreeOfParallelism = Threads }, _ =>
        {
            long local = 0;
            for (int i = 0; i < OperationsPerThread; i++)
            {
                var buf = _msPool.Get();
                buf[0] = (byte)(i & 0xFF);
                local += buf[0];
                _msPool.Return(buf);
            }
            Interlocked.Add(ref sum, local);
        });
        Volatile.Write(ref _sink, sum);
        return sum;
    }

    [Benchmark]
    public long Legacy_ObjectPool1_AllocateFree_Parallel()
    {
        long sum = 0;
        Parallel.For(0, Threads, new ParallelOptions { MaxDegreeOfParallelism = Threads }, _ =>
        {
            long local = 0;
            for (int i = 0; i < OperationsPerThread; i++)
            {
                var buf = _legacyPool.Allocate();
                buf[0] = (byte)(i & 0xFF);
                local += buf[0];
                _legacyPool.Free(buf);
            }
            Interlocked.Add(ref sum, local);
        });
        Volatile.Write(ref _sink, sum);
        return sum;
    }

    private void WarmupTedd()
    {
        for (int i = 0; i < PoolSize * 2; i++)
        {
            var b = _teddPool.Allocate();
            _teddPool.Free(b);
        }
    }

    private void WarmupMs()
    {
        for (int i = 0; i < PoolSize * 2; i++)
        {
            var b = _msPool.Get();
            _msPool.Return(b);
        }
    }

    private void WarmupLegacy()
    {
        for (int i = 0; i < PoolSize * 2; i++)
        {
            var b = _legacyPool.Allocate();
            _legacyPool.Free(b);
        }
    }

    private sealed class FixedArrayPolicy : PooledObjectPolicy<byte[]>
    {
        private readonly int _size;
        public FixedArrayPolicy(int size) => _size = size;
        public override byte[] Create() => new byte[_size];
        public override bool Return(byte[] obj) => obj.Length == _size; // keep; do not clear
    }

     private sealed class BenchConfig : ManualConfig
     {
         public BenchConfig()
         {
             AddExporter(MarkdownExporter.GitHub);
             AddExporter(HtmlExporter.Default);
             AddExporter(CsvExporter.Default);
             // Graphs (requires R on PATH). If R is missing, BDN will report a warning and skip.
             AddExporter(RPlotExporter.Default);
         }
     }
}


