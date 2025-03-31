using BenchmarkDotNet.Attributes;
using System.Security.Cryptography;

namespace OptimizationExercise.SIMDRespParsing.Benchmarks
{
    [DisassemblyDiagnoser]
    public class InPlaceCombineCrLfBenchmark
    {
        [ParamsSource(nameof(GetCommandBufferSizes))]
        public int CommandBufferSizeBytes { get; set; }

        private byte[] commandBuffer = [];

        private byte[] oldCrs = [];

        private byte[] crs = [];
        private byte[] lfs = [];

        private byte[] crLfs = [];

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
            Span<byte> asteriks = new byte[bitmapSize];
            Span<byte> dollars = new byte[bitmapSize];
            crs = GC.AllocateArray<byte>(bitmapSize, pinned: true);
            oldCrs = GC.AllocateArray<byte>(bitmapSize, pinned: true);
            lfs = GC.AllocateArray<byte>(bitmapSize, pinned: true);
            crLfs = GC.AllocateArray<byte>(bitmapSize, pinned: true);

            RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);

            
            crs.AsSpan().CopyTo(oldCrs);            
        }

        [IterationSetup]
        public void IterSetup()
        {
            oldCrs.AsSpan().CopyTo(crs);
        }

        [Benchmark(Baseline = true)]
        public void ThreeSpans()
        {
            // move into new span
            RespParser.CombineCrLf_SIMD(crs, lfs, crLfs);
        }

        [Benchmark]
        public void TwoSpans()
        {
            // overwrite crs
            RespParser.CombineCrLf_SIMD(crs, lfs, crs);
        }

        public static IEnumerable<int> GetCommandBufferSizes()
        => [100 * 1_024 * 1_024];
    }
}
