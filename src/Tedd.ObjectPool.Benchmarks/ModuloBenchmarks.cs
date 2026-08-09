using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Tedd.ObjectPoolBenchmarks;

[MemoryDiagnoser]
public class ModuloBenchmarks
{
    private int _len = 63;
    private int _startIdx = 1000;

    [Benchmark(Baseline = true)]
    public int Modulo()
    {
        int sum = 0;
        int start = _startIdx % _len;
        int len = _len;
        for (int k = 0; k < len; k++)
        {
            int i = (start + k) % len;
            sum += i;
        }
        return sum;
    }

    [Benchmark]
    public int BranchSub()
    {
        int sum = 0;
        int start = _startIdx % _len;
        int len = _len;
        for (int k = 0; k < len; k++)
        {
            int i = start + k;
            if (i >= len) i -= len;
            sum += i;
        }
        return sum;
    }
}
