using BenchmarkDotNet.Running;

namespace Tedd.ObjectPoolBenchmarks;

class Program
{
    static void Main(string[] args)
    {
        BenchmarkRunner.Run<NewBenchmarks>();
    }
}
