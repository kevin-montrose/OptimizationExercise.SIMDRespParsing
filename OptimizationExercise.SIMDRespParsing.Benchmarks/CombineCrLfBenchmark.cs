using BenchmarkDotNet.Attributes;
using System.Security.Cryptography;

namespace OptimizationExercise.SIMDRespParsing.Benchmarks
{
    [DisassemblyDiagnoser]
    public class CombineCrLfBenchmark
    {
        [ParamsSource(nameof(GetCommandBufferSizes))]
        public int CommandBufferSizeBytes { get; set; }

        private byte[] commandBuffer = [];

        private byte[] crs = [];
        private byte[] lfs = [];

        [GlobalSetup]
        public void GlobalSetup()
        {
            var fill = "*2\r\n$3\r\nGET\r\n$1\r\na\r\n"u8;

            commandBuffer = GC.AllocateArray<byte>(CommandBufferSizeBytes, pinned: true);

            var into = commandBuffer.AsSpan();

            while (!into.IsEmpty)
            {
                var copyLen = Math.Min(fill.Length, into.Length);
                fill[..copyLen].CopyTo(into);

                into = into[copyLen..];
            }

            var bitmapSize = (commandBuffer.Length / 8) + 1;
            Span<byte> asteriks = stackalloc byte[bitmapSize];
            Span<byte> dollars = stackalloc byte[bitmapSize];
            crs = GC.AllocateArray<byte>(bitmapSize, pinned: true);
            lfs = GC.AllocateArray<byte>(bitmapSize, pinned: true);

            RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);
        }

        [Benchmark(Baseline = true)]
        public void Scalar()
        {
            Span<byte> crLfs = stackalloc byte[crs.Length];

            RespParser.CombineCrLf_Scalar(crs, lfs, crLfs);
        }

        [Benchmark]
        public void SIMD()
        {
            Span<byte> crLfs = stackalloc byte[crs.Length];

            RespParser.CombineCrLf_SIMD(crs, lfs, crLfs);
        }

        public static IEnumerable<int> GetCommandBufferSizes()
        => [1 * 1_024, 4 * 1_024, 16 * 1_024, 64 * 1_024, 256 * 1_024, 1_024 * 1_024];
    }
}
