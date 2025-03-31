using BenchmarkDotNet.Attributes;

namespace OptimizationExercise.SIMDRespParsing.Benchmarks
{
    [DisassemblyDiagnoser]
    public class BoolToMaskBenchmark
    {
        private const int SEED = 2025_06_08_00;

        private const int ITERS = 1_000_000;

        public struct Triple
        {
            public uint Input0;
            public uint Input1;
            public uint Output;
        }

        private Triple[] data = new Triple[ITERS];

        [GlobalSetup]
        public void Setup()
        {
            var rand = new Random(SEED);

            for (var i = 0; i < ITERS; i++)
            {
                var shouldMatch = rand.Next(2) == 1;
                if (shouldMatch)
                {
                    data[i].Input0 = data[i].Input1 = (byte)rand.Next();
                }
                else
                {
                    do
                    {
                        data[i].Input0 = (byte)rand.Next();
                        data[i].Input1 = (byte)rand.Next();
                    }
                    while (data[i].Input0 == data[i].Input1);
                }
            }

#if DEBUG
            var oldData = data;

            data = oldData.ToArray();
            Naive();
            var r0 = data.ToArray();

            data = oldData.ToArray();
            Naive();
            var r1 = data.ToArray();

            data = oldData.ToArray();
            SubtractExtend();
            var r2 = data.ToArray();

            data = oldData;

            for (var i = 0; i < r0.Length; i++)
            {
                if (r0[i].Output != r1[i].Output || r0[i].Output != r2[i].Output)
                {
                    throw new Exception();
                }
            }
#endif
        }

        [Benchmark(Baseline = true)]
        public void Naive()
        {
            for (var i = 0; i < data.Length; i++)
            {
                ref var d = ref data[i];
                d.Output = d.Input0 != d.Input1 ? RespParserV3.TRUE : RespParserV3.FALSE;
            }
        }

        [Benchmark]
        public void CompareNegate()
        {
            for (var i = 0; i < data.Length; i++)
            {
                ref var d = ref data[i];
                var b = d.Input0 != d.Input1;
                d.Output = (uint)-System.Runtime.CompilerServices.Unsafe.As<bool, byte>(ref b);
            }
        }

        [Benchmark]
        public void SubtractExtend()
        {
            for (var i = 0; i < data.Length; i++)
            {
                ref var d = ref data[i];
                var s = (int)(0 - (d.Input0 ^ d.Input1));
                d.Output = (uint)(s >> 31);
            }
        }
    }
}
