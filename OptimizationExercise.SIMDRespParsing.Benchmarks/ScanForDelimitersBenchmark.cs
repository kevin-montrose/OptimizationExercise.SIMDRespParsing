using BenchmarkDotNet.Attributes;

namespace OptimizationExercise.SIMDRespParsing.Benchmarks
{
    [DisassemblyDiagnoser]
    public class ScanForDelimitersBenchmark
    {
        [ParamsSource(nameof(GetCommandBufferSizes))]
        public int CommandBufferSizeBytes { get; set; }

        private byte[] commandBuffer = [];

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
        }

        [Benchmark]
        public void SIMD()
        {
            var bitmapSize = (commandBuffer.Length / 8) + 1;
            Span<byte> asteriks = stackalloc byte[bitmapSize];
            Span<byte> dollars = stackalloc byte[bitmapSize];
            Span<byte> crs = stackalloc byte[bitmapSize];
            Span<byte> lfs = stackalloc byte[bitmapSize];

            RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);
        }

        public static IEnumerable<int> GetCommandBufferSizes()
        => [512, 512 + 128, 512 + 128 + 64, 512 + 128 + 64 + 32, 512 + 128 + 64 + 32 + 16, 512 + 128 + 64 + 32 + 16 + 8, 512 + 128 + 64 + 32 + 16 + 8 + 4];
    }
}
