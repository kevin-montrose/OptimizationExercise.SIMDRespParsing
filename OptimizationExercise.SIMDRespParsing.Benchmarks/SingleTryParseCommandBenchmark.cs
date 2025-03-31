using BenchmarkDotNet.Attributes;
using System.Runtime.Intrinsics;
using System.Text;

namespace OptimizationExercise.SIMDRespParsing.Benchmarks
{
    [DisassemblyDiagnoser]
    public class SingleTryParseCommandBenchmark
    {
        [ParamsSource(nameof(GetCommands))]
        public RespCommand Command { get; set; }

        [Params(true, false)]
        public bool UpperCase { get; set; }

        private byte[] buffer = [];

        private int commandLength;

        [GlobalSetup]
        public void Setup()
        {
            var str = $"\r\n{(UpperCase ? Command.ToString().ToUpperInvariant() : Command.ToString().ToLowerInvariant())}\r\n";

            commandLength = Command.ToString().Length;

            var asBytes = Encoding.ASCII.GetBytes(str);

            var padded = new byte[asBytes.Length + Vector256<byte>.Count];

            asBytes.AsSpan().CopyTo(padded);
            buffer = padded;
        }

        [Benchmark(Baseline = true)]
        public void Enum()
        {
            Span<byte> from = stackalloc byte[buffer.Length];
            buffer.CopyTo(from);

            if (!RespParser.TryParseCommand_Enum(from, 2, commandLength, out var cmd) || cmd != Command)
            {
                throw new Exception();
            }
        }

        [Benchmark]
        public void Switch()
        {
            Span<byte> from = stackalloc byte[buffer.Length];
            buffer.CopyTo(from);

            if (!RespParser.TryParseCommand_Switch(from, 2, commandLength, out var cmd) || cmd != Command)
            {
                throw new Exception();
            }
        }

        [Benchmark]
        public void Hash()
        {
            Span<byte> from = stackalloc byte[buffer.Length];
            buffer.CopyTo(from);

            if (!RespParser.TryParseCommand_Hash(from, 2, commandLength, out var cmd) || cmd != Command)
            {
                throw new Exception();
            }
        }

        [Benchmark]
        public void Hash2()
        {
            Span<byte> from = stackalloc byte[buffer.Length];
            buffer.CopyTo(from);

            if (!RespParser.TryParseCommand_Hash2(from, 2, commandLength, out var cmd) || cmd != Command)
            {
                throw new Exception();
            }
        }

        public static IEnumerable<RespCommand> GetCommands()
        {
            var lengthsYielded = new HashSet<int>();

            foreach (var cmd in System.Enum.GetValues<RespCommand>())
            {
                if (cmd is RespCommand.None or RespCommand.Invalid)
                {
                    continue;
                }

                if (lengthsYielded.Add(cmd.ToString().Length))
                {
                    yield return cmd;
                }
            }
        }
    }
}
