using BenchmarkDotNet.Attributes;

namespace OptimizationExercise.SIMDRespParsing.Benchmarks
{
    [DisassemblyDiagnoser]
    public class SetMSBsWhereZero64Benchmark
    {
        private const int SEED = 2025_03_31_00;

        private readonly ulong[] data = new ulong[1_000_000];

        [GlobalSetup]
        public void Setup()
        {
            var rand = new Random(SEED);

            for (var i = 0; i < data.Length; i++)
            {
                data[i] = ~0UL;

                var count = rand.Next(8);
                for (var j = 0; j < count; j++)
                {
                    var ix = rand.Next(8);

                    var mask = (0xFFUL << (ix * 8));

                    data[i] &= ~mask;
                }
            }
        }

        [Benchmark(Baseline = true)]
        public void Scalar()
        {
            foreach (var d in data)
            {
                RespParser.SetMSBsWhereZero64(d);
            }
        }

        [Benchmark]
        public void Mult()
        {
            foreach (var d in data)
            {
                RespParser.SetMSBsWhereZero64_Mult(d);
            }
        }
    }
}
