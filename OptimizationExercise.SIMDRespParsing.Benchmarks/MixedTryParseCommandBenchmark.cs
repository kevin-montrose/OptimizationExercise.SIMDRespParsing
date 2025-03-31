using BenchmarkDotNet.Attributes;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;

namespace OptimizationExercise.SIMDRespParsing.Benchmarks
{
    [DisassemblyDiagnoser]
    public class MixedTryParseCommandBenchmark
    {
        private const int SEED = 2025_04_08_00;
        private const int COUNT = 1_000_000;

        // Realistic mixes, and then one with all different lengths
        [Params("GET", "GET|SETEX", "GET|SETEX|UNLINK", "HGET|HDEL|HINCRBY|HMSET", "SADD|SDIFF|SELECT|PUBLISH|ZCOLLECT")]
        public string Commands { get; set; } = "";

        [Params(true, false)]
        public bool UpperCase { get; set; }

        private readonly (Memory<byte> Data, int StartIx, int EndIx, int Length)[] toParse = new (Memory<byte> Data, int StartIx, int EndIx, int Length)[COUNT];

        private readonly RespCommand[] parsed = new RespCommand[COUNT];

        [GlobalSetup]
        public void Setup()
        {
            var rand = new Random(SEED);

            var toUse = Commands.Split('|').Select(static x => System.Enum.Parse<RespCommand>(x)).OrderBy(static x => x).ToList();

            for (var i = 0; i < COUNT; i++)
            {
                var cmd = toUse[rand.Next(toUse.Count)];
                var str = $"\r\n{(UpperCase ? cmd.ToString().ToUpperInvariant() : cmd.ToString().ToLowerInvariant())}\r\n";

                var buffer = Encoding.ASCII.GetBytes(str).Concat(new byte[Vector256<byte>.Count]).ToArray();
                var startIx = 2;
                var endIx = startIx + cmd.ToString().Length;
                var length = cmd.ToString().Length;

                toParse[i] = (buffer, startIx, endIx, length);
            }

            parsed.AsSpan().Clear();
            Enum();
            var p0 = parsed.ToList();

            //parsed.AsSpan().Clear();
            //Switch();
            //var p1 = parsed.ToList();

            parsed.AsSpan().Clear();
            Hash();
            var p2 = parsed.ToList();

            parsed.AsSpan().Clear();
            Hash2();
            var p3 = parsed.ToList();

            parsed.AsSpan().Clear();
            Branchless();
            var p4 = parsed.ToList();

            parsed.AsSpan().Clear();
            BitFunc();
            var p5 = parsed.ToList();

            if (/*!p0.SequenceEqual(p1) || */!p0.SequenceEqual(p2) || !p0.SequenceEqual(p3) || !p0.SequenceEqual(p4) || !p0.SequenceEqual(p5))
            {
                throw new Exception();
            }
        }

        [Benchmark(Baseline = true)]
        public void Enum()
        {
            //Span<byte> from = stackalloc byte[256];

            ref var into = ref parsed[0];

            for (var i = 0; i < toParse.Length; i++)
            {
                var raw = toParse[i];
                //raw.Data.Span.CopyTo(from);

                _ = RespParser.TryParseCommand_Enum(raw.Data.Span, raw.StartIx, raw.EndIx, out var cmd);
                into = cmd;
                into = ref Unsafe.Add(ref into, 1);
            }
        }

        // Switch is better than Enum, worse than Hash2
        // but requires this copy because it mutates its inputs
        //[Benchmark]
        //public void Switch()
        //{
        //    Span<byte> from = stackalloc byte[256];

        //    ref var into = ref parsed[0];

        //    for (var i = 0; i < toParse.Length; i++)
        //    {
        //        var raw = toParse[i];
        //        raw.Data.Span.CopyTo(from);

        //        _ = RespParser.TryParseCommand_Switch(from, raw.StartIx, raw.EndIx, out var cmd);
        //        into = cmd;
        //        into = ref Unsafe.Add(ref into, 1);
        //    }
        //}

        [Benchmark]
        public void Hash()
        {
            //Span<byte> from = stackalloc byte[256];

            ref var into = ref parsed[0];

            for (var i = 0; i < toParse.Length; i++)
            {
                var raw = toParse[i];
                //raw.Data.Span.CopyTo(from);

                _ = RespParser.TryParseCommand_Hash(raw.Data.Span, raw.StartIx, raw.EndIx, out var cmd);
                into = cmd;
                into = ref Unsafe.Add(ref into, 1);
            }
        }

        [Benchmark]
        public void Hash2()
        {
            //Span<byte> from = stackalloc byte[256];

            ref var into = ref parsed[0];

            for (var i = 0; i < toParse.Length; i++)
            {
                var raw = toParse[i];
                //raw.Data.Span.CopyTo(from);

                _ = RespParser.TryParseCommand_Hash2(raw.Data.Span, raw.StartIx, raw.EndIx, out var cmd);
                into = cmd;
                into = ref Unsafe.Add(ref into, 1);
            }
        }

        [Benchmark]
        public void Branchless()
        {
            Span<byte> from = stackalloc byte[256];

            ref var into = ref parsed[0];

            for (var i = 0; i < toParse.Length; i++)
            {
                var raw = toParse[i];
                //raw.Data.Span.CopyTo(from);

                ref var start = ref MemoryMarshal.GetReference(raw.Data.Span);
                var cmd = UnconditionalParseRespCommandImpl.Parse(ref Unsafe.Add(ref start, raw.Data.Span.Length), ref start, raw.StartIx, raw.Length);
                into = cmd;
                into = ref Unsafe.Add(ref into, 1);
            }
        }

        [Benchmark]
        public void BitFunc()
        {
            Span<byte> from = stackalloc byte[256];

            ref var into = ref parsed[0];

            for (var i = 0; i < toParse.Length; i++)
            {
                var raw = toParse[i];
                //raw.Data.Span.CopyTo(from);

                ref var start = ref MemoryMarshal.GetReference(raw.Data.Span);
                var cmd = BitFuncParseRespCommandImpl.Calculate(ref Unsafe.Add(ref start, raw.Data.Span.Length), ref start, raw.StartIx, raw.Length);
                into = cmd;
                into = ref Unsafe.Add(ref into, 1);
            }
        }
    }
}
