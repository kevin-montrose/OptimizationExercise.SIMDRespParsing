using BenchmarkDotNet.Attributes;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace OptimizationExercise.SIMDRespParsing.Benchmarks
{
    [DisassemblyDiagnoser]
    public class ParseIntBenchmarks
    {
        private const int DATA_COUNT = 1_000_000;
        private const int SEED = 2025_03_30_01;

        [ParamsSource(nameof(GetRanges))]
        public (int MinDigitsInc, int MaxDigitsExc) Range { get; set; }

        public readonly (int EntrySize, ReadOnlyMemory<byte> Memory, int Length)[] data = new (int EntrySize, ReadOnlyMemory<byte> Memory, int Length)[DATA_COUNT];

        public readonly int[] parsed = new int[DATA_COUNT];

        [GlobalSetup]
        public unsafe void Setup()
        {
            var rand = new Random(SEED);

            var minInc = (long)Math.Pow(10L, Range.MinDigitsInc - 1);
            var maxExc = (long)Math.Pow(10L, Range.MaxDigitsExc - 1);

            minInc = Math.Min(minInc, int.MaxValue + 1L);
            maxExc = Math.Min(maxExc, int.MaxValue + 1L);

            for (var i = 0; i < data.Length; i++)
            {
                var num = rand.NextInt64(minInc, maxExc);

                if (num == 0)
                {
                    i--;
                    continue;
                }

                var digits = num.ToString().Length;
                RespParserV3.CalculateByteBufferSizes(digits + 2, out var entrySizeBytes, out _, out _);

                var entry = GC.AllocateArray<byte>(entrySizeBytes, pinned: true);

                if (!num.TryFormat(entry.AsSpan()[..digits], out var written) || written != digits)
                {
                    throw new InvalidOperationException();
                }

                "\r\n"u8.CopyTo(entry.AsSpan()[digits..]);

                data[i] = (entry.Length, new ReadOnlyMemory<byte>(entry)[..digits], digits);

                byte* dataStartPtr = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(data[i].Memory.Span));
                byte* dataEndPtr = dataStartPtr + data[i].Length;

                var r1 = int.TryParse(data[i].Memory.Span, out var p1);
                var r2 = RespParser.TryParsePositiveInt(data[i].Memory.Span, out var p2);
                ref var start = ref MemoryMarshal.GetReference(data[i].Memory.Span);
                ref var end = ref Unsafe.Add(ref start, data[i].Length);
                var p3 = RespParserV3.UnconditionalParsePositiveInt(ref Unsafe.Add(ref start, data[i].EntrySize), ref start, ref end);
                var r4 = GarnetParser.RespReadUtils_TryReadUInt64(ref dataStartPtr, dataEndPtr, out var p4, out _);
                var r3 = p3 > 0;

                if (!r1 || !r2 || !r3 || !r4)
                {
                    throw new Exception();
                }

                if (p1 != p2 || p1 != p3 || p1 != (int)p4)
                {
                    throw new Exception();
                }
            }
        }

        [Benchmark(Baseline = true)]
        public void BuiltIn()
        {
            for (var i = 0; i < data.Length; i++)
            {
                var d = data[i];
                _ = int.TryParse(d.Memory.Span, out parsed[i]);
            }
        }

        [Benchmark]
        public void Custom()
        {
            for (var i = 0; i < data.Length; i++)
            {
                var d = data[i];
                _ = RespParser.TryParsePositiveInt(d.Memory.Span, out var parsed);
            }
        }

        [Benchmark]
        public unsafe void Garnet()
        {
            for (var i = 0; i < data.Length; i++)
            {
                var d = data[i];

                byte* dataStartPtr = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(d.Memory.Span));
                byte* dataEndPtr = dataStartPtr + d.Length;

                GarnetParser.RespReadUtils_TryReadUInt64(ref dataStartPtr, dataEndPtr, out var p, out _);
                parsed[i] = (int)p;
            }
        }

        [Benchmark]
        public void NearlyBranchless()
        {
            for (var i = 0; i < data.Length; i++)
            {
                var d = data[i];
                ref var start = ref MemoryMarshal.GetReference(d.Memory.Span);
                ref var end = ref Unsafe.Add(ref start, d.Length);
                parsed[i] = RespParserV3.UnconditionalParsePositiveInt(ref Unsafe.Add(ref start, d.EntrySize), ref start, ref end);
            }
        }

        public static IEnumerable<(int MinInc, int MaxInc)> GetRanges()
        {
            var maxDigits = int.MaxValue.ToString().Length;

            for (var i = 1; i <= maxDigits; i++)
            {
                for (var j = i + 1; j <= maxDigits + 1; j++)
                {
                    yield return (i, j);
                }
            }
        }
    }
}
