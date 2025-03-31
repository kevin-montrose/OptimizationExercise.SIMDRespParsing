using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using System.Diagnostics;
using System.Text;

namespace OptimizationExercise.SIMDRespParsing.Benchmarks
{
    public record ClosedOpenRange(int MinInc, int MaxExc)
    {
        public override string ToString()
        => $"[{MinInc}, {MaxExc})";
    }

    public record Config(string AllowedCommands, ClosedOpenRange CommandsPerBatch, ClosedOpenRange? KeySize, ClosedOpenRange? ValueSize, ClosedOpenRange? SubItemCount, int PartialPerc)
    {
        public override string ToString()
        {
            List<string> parts = [AllowedCommands];

            if (CommandsPerBatch is not null)
            {
                parts.Add($"CPB={CommandsPerBatch}");
            }

            if (KeySize is not null)
            {
                parts.Add($"KS={KeySize}");
            }

            if (ValueSize is not null)
            {
                parts.Add($"VS={ValueSize}");
            }

            if (SubItemCount is not null)
            {
                parts.Add($"SIC={SubItemCount}");
            }

            parts.Add($"P%={PartialPerc}");

            return string.Join(",", parts);
        }
    }

    [DisassemblyDiagnoser]
    [EventPipeProfiler(EventPipeProfile.Jit)]
    public class RespRequestBatchBenchmark
    {
        private const int SEED = 2025_06_07_00;
        private const int BATCH_COUNT = 10_000;

        [ParamsSource(nameof(GetConfigs))]
        public Config? Config { get; set; }

        public readonly (int AllocatedBufferSize, Memory<byte> Buffer, int ReadBytes, int AllocatedBitmapSize, Memory<byte> Bitmap)[] batches = new (int AllocatedBufferSize, Memory<byte> Buffer, int ReadBytes, int AllocatedBitmapSize, Memory<byte> Bitmap)[BATCH_COUNT];

        private Memory<ParsedRespCommandOrArgument> into = Array.Empty<ParsedRespCommandOrArgument>();
        private int consumedInInto;

        private readonly GarnetParser garnetParser = new();



        [GlobalSetup]
        public void Setup()
        {
            if (Config is null)
            {
                throw new Exception();
            }

            var rand = new Random(SEED);
            var commands = Config.AllowedCommands.Split("|");

            var totalParsedEntries = 0;

            var cmdBytes = new List<byte>();

            for (var i = 0; i < BATCH_COUNT; i++)
            {
                cmdBytes.Clear();
                var cmdCount = rand.Next(Config.CommandsPerBatch.MinInc, Config.CommandsPerBatch.MaxExc);

                for (var j = 0; j < cmdCount; j++)
                {
                    var cmd = Enum.Parse<RespCommand>(commands[rand.Next(commands.Length)]);
                    cmdBytes.AddRange(GenerateCommand(cmd).SelectMany(static x => x));
                }

                // truncate some of the time, to simulate incomplete request batches
                var truncated = rand.Next(100) < Config.PartialPerc;
                if (truncated)
                {
                    var toDiscard = rand.Next(cmdBytes.Count);
                    do
                    {
                        toDiscard = rand.Next(cmdBytes.Count);
                    } while (toDiscard == 0 || toDiscard == cmdBytes.Count);

                    cmdBytes.RemoveRange(cmdBytes.Count - toDiscard, toDiscard);
                }

                RespParserFinal.CalculateByteBufferSizes(cmdBytes.Count, out var allocateSize, out var useableSize, out var bitmapSize);

                var pinnedCmdBytes = GC.AllocateArray<byte>(allocateSize, pinned: true);
                cmdBytes.CopyTo(pinnedCmdBytes);

                var pinnedBitmapBytes = GC.AllocateArray<byte>(bitmapSize, pinned: true);

                batches[i] = (allocateSize, pinnedCmdBytes.AsMemory()[..useableSize], cmdBytes.Count, bitmapSize, pinnedBitmapBytes.AsMemory());
            }

            // exactly size, as typically we'll be over sized so we're not benchmarking that
            into = GC.AllocateArray<ParsedRespCommandOrArgument>(totalParsedEntries, pinned: true);

            // check initial state
            foreach (var i in into.Span)
            {
                if (!i.IsMalformed)
                {
                    throw new Exception();
                }
            }

            Validate(this);

            [Conditional("DEBUG")]
            static void Validate(RespRequestBatchBenchmark self)
            {
                self.into.Span.Clear();
                self.Naive();
                var i0 = self.into.ToArray();
                var c0 = self.consumedInInto;

                self.into.Span.Clear();
                self.Garnet();
                var i1 = self.into.ToArray();
                var c1 = self.consumedInInto;

                self.into.Span.Clear();
                self.SIMD();
                var i2 = self.into.ToArray();
                var c2 = self.consumedInInto;

                if (c0 != c1 || c0 != c2)
                {
                    throw new InvalidOperationException();
                }

                for (var i = 0; i < c0; i++)
                {
                    if (i0[i].IsMalformed)
                    {
                        if (self.Config!.PartialPerc == 0 || !i1[i].IsMalformed || !i2[i].IsMalformed)
                        {
                            throw new InvalidOperationException();
                        }
                    }
                    else if (i0[i].IsCommand)
                    {
                        if (!i1[i].IsCommand || !i2[i].IsCommand)
                        {
                            throw new InvalidOperationException();
                        }

                        if (i0[i].Command != i1[i].Command || i0[i].Command != i2[i].Command)
                        {
                            throw new InvalidOperationException();
                        }

                        if (i0[i].ArgumentCount != i1[i].ArgumentCount || i0[i].ArgumentCount != i2[i].ArgumentCount)
                        {
                            throw new InvalidOperationException();
                        }
                    }
                    else if (i0[i].IsArgument)
                    {
                        if (!i1[i].IsArgument || !i2[i].IsArgument)
                        {
                            throw new InvalidOperationException();
                        }

                        if (i0[i].ByteStart != i1[i].ByteStart || i0[i].ByteStart != i2[i].ByteStart)
                        {
                            throw new InvalidOperationException();
                        }

                        if (i0[i].ByteEnd != i1[i].ByteEnd || i0[i].ByteEnd != i2[i].ByteEnd)
                        {
                            throw new InvalidOperationException();
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException();
                    }
                }
            }

            IEnumerable<IEnumerable<byte>> GenerateCommand(RespCommand cmd)
            {
                switch (cmd)
                {
                    case RespCommand.PING:
                        {
                            yield return "*1\r\n$4\r\nPING\r\n"u8.ToArray();
                            totalParsedEntries += 1;
                        }
                        yield break;
                    case RespCommand.GET:
                        {
                            yield return "*2\r\n$3\r\nGET\r\n"u8.ToArray();
                            yield return GenerateKeyString();
                            totalParsedEntries += 2;
                        }
                        yield break;
                    case RespCommand.SET:
                        {
                            yield return "*3\r\n$3\r\nSET\r\n"u8.ToArray();
                            yield return GenerateKeyString();
                            yield return GenerateValueString();
                            totalParsedEntries += 3;
                        }
                        yield break;
                    case RespCommand.UNLINK:
                        {
                            yield return "*2\r\n$6\r\nUNLINK\r\n"u8.ToArray();
                            yield return GenerateKeyString();
                            totalParsedEntries += 2;
                        }
                        yield break;
                    case RespCommand.HMGET:
                        {
                            if (Config.SubItemCount is null)
                            {
                                throw new Exception();
                            }

                            var subKeyCount = rand.Next(Config.SubItemCount.MinInc, Config.SubItemCount.MaxExc);

                            yield return Encoding.ASCII.GetBytes($"*{2 + subKeyCount}\r\n$5\r\nHMGET\r\n");
                            yield return GenerateKeyString();
                            for (var i = 0; i < subKeyCount; i++)
                            {
                                yield return GenerateKeyString();
                            }

                            totalParsedEntries += 2 + subKeyCount;
                        }
                        yield break;
                    case RespCommand.HMSET:
                        {
                            if (Config.SubItemCount is null)
                            {
                                throw new Exception();
                            }

                            var subPairCount = rand.Next(Config.SubItemCount.MinInc, Config.SubItemCount.MaxExc);

                            yield return Encoding.ASCII.GetBytes($"*{2 + (2 * subPairCount)}\r\n$5\r\nHMSET\r\n");
                            yield return GenerateKeyString();
                            for (var i = 0; i < subPairCount; i++)
                            {
                                yield return GenerateKeyString();
                                yield return GenerateValueString();
                            }

                            totalParsedEntries += 2 + (2 * subPairCount);
                        }
                        yield break;
                    case RespCommand.HDEL:
                        {
                            if (Config.SubItemCount is null)
                            {
                                throw new Exception();
                            }

                            var subKeyCount = rand.Next(Config.SubItemCount.MinInc, Config.SubItemCount.MaxExc);

                            yield return Encoding.ASCII.GetBytes($"*{2 + subKeyCount}\r\n$4\r\nHDEL\r\n");
                            yield return GenerateKeyString();
                            for (var i = 0; i < subKeyCount; i++)
                            {
                                yield return GenerateKeyString();
                            }

                            totalParsedEntries += 2 + subKeyCount;
                        }
                        yield break;
                    case RespCommand.SADD:
                        {
                            if (Config.SubItemCount is null)
                            {
                                throw new Exception();
                            }

                            var subValueCount = rand.Next(Config.SubItemCount.MinInc, Config.SubItemCount.MaxExc);

                            yield return Encoding.ASCII.GetBytes($"*{2 + subValueCount}\r\n$4\r\nSADD\r\n");
                            yield return GenerateKeyString();
                            for (var i = 0; i < subValueCount; i++)
                            {
                                yield return GenerateValueString();
                            }

                            totalParsedEntries += 2 + subValueCount;
                        }
                        yield break;
                    case RespCommand.SINTER:
                        {
                            if (Config.SubItemCount is null)
                            {
                                throw new Exception();
                            }

                            var subKeyCount = rand.Next(Config.SubItemCount.MinInc, Config.SubItemCount.MaxExc);

                            yield return Encoding.ASCII.GetBytes($"*{1 + subKeyCount}\r\n$6\r\nSINTER\r\n");
                            for (var i = 0; i < subKeyCount; i++)
                            {
                                yield return GenerateKeyString();
                            }

                            totalParsedEntries += 1 + subKeyCount;
                        }
                        yield break;
                    default:
                        throw new Exception($"Unexpected command: {cmd}");
                }
            }

            IEnumerable<byte> GenerateKeyString()
            {
                if (Config.KeySize is null)
                {
                    throw new Exception();
                }

                var size = rand.Next(Config.KeySize.MinInc, Config.KeySize.MaxExc);

                return GenerateRespString(size);
            }

            IEnumerable<byte> GenerateValueString()
            {
                if (Config.ValueSize is null)
                {
                    throw new Exception();
                }

                var size = rand.Next(Config.ValueSize.MinInc, Config.ValueSize.MaxExc);

                return GenerateRespString(size);
            }

            IEnumerable<byte> GenerateRespString(int size)
            {
                var ret = new byte[size];
                rand.NextBytes(ret);

                return Encoding.ASCII.GetBytes($"${ret.Length}\r\n").Concat(ret).Concat([(byte)'\r', (byte)'\n']);
            }
        }

        [Benchmark(Baseline = true)]
        public void Naive()
        {
            consumedInInto = 0;
            var remainingInto = into.Span;

            foreach (var batch in batches)
            {
                NaiveParser.Parse(batch.Buffer.Span[..batch.ReadBytes], remainingInto, out var usedItems, out _);
                remainingInto = remainingInto[usedItems..];
                consumedInInto += usedItems;
            }
        }

        [Benchmark]
        public void Garnet()
        {
            consumedInInto = 0;
            var remainingInto = into.Span;

            foreach (var batch in batches)
            {
                garnetParser.Parse(batch.Buffer.Span[..batch.ReadBytes], remainingInto, out var usedItems, out _);
                remainingInto = remainingInto[usedItems..];
                consumedInInto += usedItems;
            }
        }

        [Benchmark]
        public void SIMD()
        {
            consumedInInto = 0;
            var remainingInto = into.Span;

            foreach (var batch in batches)
            {
                RespParserFinal.Parse(batch.AllocatedBufferSize, batch.Buffer.Span, batch.ReadBytes, batch.AllocatedBitmapSize, batch.Bitmap.Span, remainingInto, out var usedItems, out _);
                remainingInto = remainingInto[usedItems..];
                consumedInInto += usedItems;
            }
        }

        public static IEnumerable<Config> GetConfigs()
        {
            foreach (var cmds in GetAllowedCommands())
            {
                var hasKeys = false;
                var hasValues = false;
                var hasSubItems = false;

                foreach (var cmd in cmds.Split("|"))
                {
                    switch (Enum.Parse<RespCommand>(cmd))
                    {
                        case RespCommand.PING: break;
                        case RespCommand.GET:
                            hasKeys |= true;
                            break;
                        case RespCommand.SET:
                            hasKeys |= true;
                            hasValues |= true;
                            break;
                        case RespCommand.UNLINK:
                            hasKeys |= true;
                            break;
                        case RespCommand.HDEL:
                            hasKeys |= true;
                            hasSubItems |= true;
                            break;
                        case RespCommand.HMGET:
                            hasKeys |= true;
                            hasSubItems |= true;
                            break;
                        case RespCommand.HMSET:
                            hasKeys |= true;
                            hasValues |= true;
                            hasSubItems |= true;
                            break;
                        case RespCommand.SADD:
                            hasKeys |= true;
                            hasValues |= true;
                            hasSubItems |= true;
                            break;
                        case RespCommand.SINTER:
                            hasKeys |= true;
                            hasSubItems |= true;
                            break;
                        default:
                            throw new Exception();
                    }
                }

                var keyCounts = hasKeys ? GetKeySizes() : [null];
                var valueCounts = hasValues ? GetValueSizes() : [null];
                var subItemCounts = hasSubItems ? GetSubItemCount() : [null];

                foreach (var batchSize in GetCommandsPerBatch())
                {
                    foreach (var k in keyCounts)
                    {
                        foreach (var v in valueCounts)
                        {
                            foreach (var s in subItemCounts)
                            {
                                foreach (var p in GetPartialPercents())
                                {
                                    yield return new(cmds, batchSize, k, v, s, p);
                                }
                            }
                        }
                    }

                }
            }
        }

        private static IEnumerable<string> GetAllowedCommands()
        {
            const string JustPing = "PING";
            const string JustReads = "GET";
            const string JustStrings = "GET|SET|UNLINK";
            const string SomeHash = "HDEL|HMGET|HMSET";
            const string SomeSets = "SADD|SINTER";

            string[] all = [JustPing, JustReads, JustStrings, SomeHash, SomeSets];

            foreach (var r in all)
            {
                yield return r;
            }

            yield return string.Join("|", all.SelectMany(static x => x.Split('|')));
        }

        public static IEnumerable<ClosedOpenRange> GetCommandsPerBatch()
        {
            // exactly 1
            yield return new(1, 2);

            // ranging from 4-8
            yield return new(4, 9);

            // exactly 8
            yield return new(8, 9);
        }

        public static IEnumerable<ClosedOpenRange?> GetKeySizes()
        {
            yield return new(6, 11);
            yield return new(10, 51);
        }

        public static IEnumerable<ClosedOpenRange?> GetValueSizes()
        {
            //yield return new(0, 100);
            yield return new(100, 500);
            yield return new(500, 1001);
        }

        public static IEnumerable<ClosedOpenRange?> GetSubItemCount()
        {
            yield return new(1, 2);
            yield return new(3, 11);
            //yield return new(11, 21);
        }

        public static IEnumerable<int> GetPartialPercents()
        {
            yield return 0;
            yield return 50;
            yield return 100;
        }
    }
}
