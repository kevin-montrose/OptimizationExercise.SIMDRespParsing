using BenchmarkDotNet.Attributes;

namespace OptimizationExercise.SIMDRespParsing.Benchmarks
{
    [DisassemblyDiagnoser]
    public class FindNext24Benchmark
    {
        private const int SEED = 2025_03_30_00;

        [ParamsSource(nameof(GetCommandBufferSizes))]
        public int CommandBufferSizeBytes { get; set; }

        [Params(0, 5, 10, 25, 50, 100)]
        public int Density { get; set; }

        private byte[] bitmapBytes = [];

        [GlobalSetup]
        public void GlobalSetup()
        {
            var size = (CommandBufferSizeBytes / 8) + 1;

            bitmapBytes = GC.AllocateArray<byte>(size, pinned: true);

            var rand = new Random(SEED);

            for (var i = 0; i < bitmapBytes.Length; i++)
            {
                bitmapBytes[i] = 0;

                for (var j = 0; j < 8; j++)
                {
                    byte bit;
                    if (Density == 0)
                    {
                        bit = 0;
                    }
                    else if (Density == 100)
                    {
                        bit = 1;
                    }
                    else if (Density == 5)
                    {
                        bit = (byte)(rand.Next(20) == 0 ? 1 : 0);
                    }
                    else if (Density == 10)
                    {
                        bit = (byte)(rand.Next(10) == 0 ? 1 : 0);
                    }
                    else if (Density == 25)
                    {
                        bit = (byte)(rand.Next(4) == 0 ? 1 : 0);
                    }
                    else if (Density == 50)
                    {
                        bit = (byte)(rand.Next(2) == 0 ? 1 : 0);
                    }
                    else
                    {
                        throw new InvalidOperationException();
                    }

                    bitmapBytes[i] = (byte)(bit << j);
                }
            }
        }

        [Benchmark]
        public void SWAR()
        {
            var bitsToSkip = (byte)0;

            var remaining = (ReadOnlySpan<byte>)bitmapBytes;

            while (!remaining.IsEmpty)
            {
                var res = RespParser.FindNext24(bitsToSkip, remaining);

                if (res == -1)
                {
                    break;
                }

                var totalBitsToSkip = bitsToSkip + res;

                if (totalBitsToSkip > 8)
                {
                    var bytesToSkip = totalBitsToSkip / 8;
                    bitsToSkip = (byte)(totalBitsToSkip % 8);

                    remaining = remaining[bytesToSkip..];
                }
                else
                {
                    bitsToSkip = (byte)totalBitsToSkip;
                }
            }
        }

        public static IEnumerable<int> GetCommandBufferSizes()
        => [1, 2, 3, 4];
    }
}
