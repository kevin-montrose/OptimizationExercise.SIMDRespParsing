using BenchmarkDotNet.Attributes;
using System.Numerics;

namespace OptimizationExercise.SIMDRespParsing.Benchmarks
{
    [DisassemblyDiagnoser]
    public class MultipleCommandsBenchmark
    {
        private const int SEED = 2025_03_30_02;

        [ParamsSource(nameof(GetCommandBufferSizes))]
        public int CommandBufferSizeBytes { get; set; }

        [Params("PING", "GET", "SET", "MGET", "*")]
        public string Command { get; set; } = "";

        private byte[] commandBuffer = [];
        private ParsedRespCommandOrArgument[] parse = [];

        private GarnetParser garnetParser = new();

        [GlobalSetup]
        public void Setup()
        {
            commandBuffer = GC.AllocateArray<byte>(CommandBufferSizeBytes, pinned: true);

            var rand = new Random(SEED);

            var pingCmd = "*1\r\n$4\r\nPING\r\n"u8;
            var getCmd = "*2\r\n$3\r\nGET\r\n$6\r\nfoobar\r\n"u8;
            var setCmd = "*3\r\n$3\r\nSET\r\n$6\r\nfoobar\r\n$8\r\nfizzbuzz\r\n"u8;
            var mgetCmd = "*4\r\n$4\r\nMGET\r\n$3\r\nfoo\r\n$4\r\nfizz\r\n$5\r\nworld\r\n"u8;

            var full = 0;
            var fullBytes = 0;

            var remainingSpace = commandBuffer.AsSpan();
            while (!remainingSpace.IsEmpty)
            {
                var cmd = Command;
                if (cmd == "*")
                {
                    cmd = rand.Next(4) switch
                    {
                        0 => "PING",
                        1 => "GET",
                        2 => "SET",
                        3 => "MGET",
                        _ => throw new Exception(),
                    };
                }

                ReadOnlySpan<byte> buff =
                    cmd switch
                    {
                        "PING" => pingCmd,
                        "GET" => getCmd,
                        "SET" => setCmd,
                        "MGET" => mgetCmd,
                        _ => throw new Exception(),
                    };

                var copyLen = Math.Min(buff.Length, remainingSpace.Length);
                buff[..copyLen].CopyTo(remainingSpace);

                if (copyLen == buff.Length)
                {
                    fullBytes += buff.Length;

                    full +=
                        cmd switch
                        {
                            "PING" => 2,
                            "GET" => 3,
                            "SET" => 4,
                            "MGET" => 5,
                            _ => throw new Exception(),
                        };
                }

                remainingSpace = remainingSpace[copyLen..];
            }

            parse = new ParsedRespCommandOrArgument[BitOperations.RoundUpToPowerOf2((uint)full)];

            RespParser.Parse(commandBuffer, parse, out var parsedCount, out var parsedBytes);

            if (parsedCount != full)
            {
                throw new Exception();
            }

            if (parsedBytes != fullBytes)
            {
                throw new Exception();
            }

            // the unsafe version should match the safe version exactly
            RespParserV2.Parse(commandBuffer, parse, out var parsedV2Count, out var parsedV2Bytes);

            if (parsedCount != parsedV2Count)
            {
                throw new Exception();
            }

            if (parsedBytes != parsedV2Bytes)
            {
                throw new Exception();
            }

            // Garnet is going to be "close" but not exact, since this was a white room re-implementation of parsing
            garnetParser.Parse(commandBuffer, parse, out var garnetParsedCount, out var garnetParsedBytes);

            if (garnetParsedCount >= parsedCount)
            {
                // Garnet parsing combines the first 2 elements
                throw new Exception();
            }

            if (garnetParsedBytes != fullBytes)
            {
                // Garnet should still fully consume the buffer
                throw new Exception();
            }
        }

        [Benchmark(Baseline = true)]
        public void GarnetParse()
        {
            garnetParser.Parse(commandBuffer, parse, out _, out _);
        }

        [Benchmark]
        public void SIMDParse()
        {
            RespParser.Parse(commandBuffer, parse, out _, out _);
        }

        [Benchmark]
        public void UnsafeSIMDParse()
        {
            RespParserV2.Parse(commandBuffer, parse, out _, out _);
        }

        public static IEnumerable<int> GetCommandBufferSizes()
        => [4 * 1_024, 8 * 1_024, 16 * 1_024, 32 * 1_024, 64 * 1_024];
    }
}
