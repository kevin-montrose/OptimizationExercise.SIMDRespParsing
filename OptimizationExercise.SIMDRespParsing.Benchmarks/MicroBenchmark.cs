using BenchmarkDotNet.Attributes;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OptimizationExercise.SIMDRespParsing.Benchmarks
{
    [DisassemblyDiagnoser]
    public class MicroBenchmark
    {
        private const int SEED = 2025_06_11_01;

        private const int SIZE = 4 * 1_024;

        private readonly byte[] advance = new byte[SIZE];
        private int count = 0;

        [GlobalSetup]
        public void Setup()
        {
            var rand = new Random(SEED);

            var ix = 0;
            while (true)
            {
                var remaining = advance.Length - ix;
                var available = rand.Next(Math.Min(16, remaining)) + 1;
                advance[ix] = (byte)available;

                ix += available;

                if (ix == advance.Length)
                {
                    break;
                }
            }

            Index();
            var a = count;

            Ref();
            var b = count;

            if (a != b)
            {
                throw new Exception();
            }
        }

        [Benchmark(Baseline = true)]
        public void Index()
        {
            count = 0;

            ref var start = ref MemoryMarshal.GetArrayDataReference(advance);

            var ix = 0;
            while (ix < advance.Length)
            {
                var step = Unsafe.Add(ref start, ix);

                ix += step;
                count++;
            }
        }

        [Benchmark]
        public void Ref()
        {
            count = 0;

            ref var cur = ref MemoryMarshal.GetArrayDataReference(advance);
            ref var cutoff = ref Unsafe.Add(ref cur, advance.Length);

            while (Unsafe.IsAddressLessThan(ref cur, ref cutoff))
            {
                var step = cur;

                cur = ref Unsafe.Add(ref cur, step);
                count++;
            }
        }
    }
}
