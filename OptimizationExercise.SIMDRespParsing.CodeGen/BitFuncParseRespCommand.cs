using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;

namespace OptimizationExercise.SIMDRespParsing.CodeGen
{
    internal static class BitFuncParseRespCommand
    {
        public static string Generate()
        {
            // minimum is $3\r\nGET\r\n = 9
            var bulkStrings = GetBulkStrings();
            var upperCasedWithAnds = bulkStrings.ToDictionary(static kv => kv.Key, static kv => (ReadOnlyMemory<byte>)kv.Value.ToArray().Select(static b => (byte)(b & 0xDF)).ToArray().AsMemory());

            var asUIntPairs = bulkStrings.ToDictionary(static kv => kv.Key, static kv => (Low: MemoryMarshal.Read<uint>(kv.Value.Span[(1 + (kv.Key.ToString().Length.ToString().Length) + 2)..]), High: MemoryMarshal.Read<uint>(kv.Value.Span[^6..])));
            var relevantBits = new List<byte>();
            for (byte i = 0; i < 32; i++)
            {
                var mask = 1U << i;

                var highSet = false;
                var highClear = false;
                var lowSet = false;
                var lowClear = false;

                foreach (var kv in asUIntPairs)
                {
                    if ((kv.Value.High & mask) != 0)
                    {
                        highSet = true;
                    }
                    else
                    {
                        highClear = true;
                    }

                    if ((kv.Value.Low & mask) != 0)
                    {
                        lowSet = true;
                    }
                    else
                    {
                        lowClear = true;
                    }
                }

                var relevant =
                    (highSet && highClear) ||
                    (lowSet && lowClear);

                if (relevant)
                {
                    relevantBits.Add(i);
                }
            }

            var uniqueValues = asUIntPairs.Select(static kv => kv.Value).Distinct().ToList();
            if (uniqueValues.Count != asUIntPairs.Count)
            {
                throw new Exception("Impossible");
            }

            var bitsToFunctionResults = new Dictionary<byte, Dictionary<string, Dictionary<RespCommand, bool>>>();
            foreach (var bit in relevantBits)
            {
                bitsToFunctionResults.Add(bit, new());

                foreach (var func in BinaryFunctions(bit))
                {
                    bitsToFunctionResults[bit].Add(func.Desc, new());

                    foreach (var (cmd, (low, high)) in asUIntPairs)
                    {
                        bitsToFunctionResults[bit][func.Desc][cmd] = func.DoIt(low, high);
                    }
                }
            }

            var choices = bitsToFunctionResults.SelectMany(static a => a.Value.Select(b => (Bit: a.Key, Desc: b.Key, ValuesForCommands: b.Value))).ToArray();

            var remaining = new List<List<RespCommand>>() { bulkStrings.Keys.ToList() };

            var candidates = new HashSet<string>();
            var res = SearchLevel(remaining, choices, []);

            if (res == null)
            {
                throw new Exception("No solution!");
            }

            var usedBits = res.Select(static x => x.Bit).Distinct().ToArray();
            var usedFunctions = res.Select(static x => x.Desc).Distinct().ToArray();

            var sb = new StringBuilder();
            sb.AppendLine("      [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("    public static RespCommand Calculate(ref byte commandBufferAllocatedEnd, ref byte commandBuffer, int commandStartIx, int commandLength)");
            sb.AppendLine("    {");
            sb.AppendLine("      Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref commandBuffer, commandStartIx + sizeof(uint)), ref commandBufferAllocatedEnd), \"About to read past end of allocated command buffer\");");
            sb.AppendLine("      var lo = Unsafe.As<byte, uint>(ref Unsafe.Add(ref commandBuffer, commandStartIx));");
            sb.AppendLine("      Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref commandBuffer, commandStartIx + commandLength - 4 + sizeof(uint)), ref commandBufferAllocatedEnd), \"About to read past end of allocated command buffer\");");
            sb.AppendLine("      var hi = Unsafe.As<byte, uint>(ref Unsafe.Add(ref commandBuffer, commandStartIx + commandLength - 4));");
            sb.AppendLine();
            foreach (var f in usedFunctions)
            {
                switch (f)
                {
                    // already available
                    case "L":
                    case "H":
                        break;
                    case "'L":
                        sb.AppendLine("      var nLo = ~lo;");
                        break;
                    case "'H":
                        sb.AppendLine("      var nHi = ~hi;");
                        break;
                    case "L & H":
                        sb.AppendLine("      var loAndHi = lo & hi;");
                        break;
                    case "'L & H":
                        sb.AppendLine("      var nloAndHi = ~lo & hi;");
                        break;
                    case "L & 'H":
                        sb.AppendLine("      var loAndNHi = lo & ~hi;");
                        break;
                    case "'L & 'H":
                        sb.AppendLine("      var nloAndNHi = ~lo & ~hi;");
                        break;
                    case "L | H":
                        sb.AppendLine("      var loOrHi = lo | hi;");
                        break;
                    case "'L | H":
                        sb.AppendLine("      var nloOrHi = ~lo | hi;");
                        break;
                    case "L | 'H":
                        sb.AppendLine("      var loOrNHi = lo | ~hi;");
                        break;
                    case "'L | 'H":
                        sb.AppendLine("      var nloOrNHi = ~lo | ~hi;");
                        break;
                    case "(L & 'H) | ('L & H)":
                        sb.AppendLine("      var loXorHi = lo ^ hi;");
                        break;
                    case "('L & 'H) | (L & H)":
                        sb.AppendLine("      var loNXorHi = ~(lo ^ hi);");
                        break;
                    default:
                        throw new Exception("Unexpected value needed");
                }
            }

            var resultBitsAssigned = new bool[32];

            var stepCalculators = new List<Func<uint, uint, uint>>();

            var stepNum = 0;
            foreach (var grp in res.GroupBy(static x => x.Desc))
            {
                sb.AppendLine();

                var bits = grp.Select(static g => g.Bit).OrderBy(static x => x).ToArray();
                var mask = new bool[32];
                foreach (var b in bits)
                {
                    mask[b] = true;
                }

                var maskUInt = 0U;
                for (var i = 0; i < mask.Length; i++)
                {
                    if (mask[i])
                    {
                        maskUInt |= 1U << i;
                    }
                }

                var rotate = 0;
                while (rotate < 32)
                {
                    var noCollision = true;
                    for (var i = 0; i < resultBitsAssigned.Length; i++)
                    {
                        var effectiveIx = (i + rotate) % 32;
                        if (mask[i] && resultBitsAssigned[effectiveIx])
                        {
                            noCollision = false;
                            break;
                        }
                    }

                    if (noCollision)
                    {
                        break;
                    }

                    rotate++;
                }

                if (rotate == 32)
                {
                    throw new Exception("Not possible");
                }

                for (var i = 0; i < resultBitsAssigned.Length; i++)
                {
                    var effectiveIx = (i + rotate) % 32;
                    resultBitsAssigned[effectiveIx] |= mask[i];
                }

                var maskStr = $"0b{string.Join("", mask.Reverse().Select(static c => c ? "1" : "0"))}U";

                Func<uint, uint, uint> step;

                switch (grp.Key)
                {
                    case "L":
                        sb.AppendLine($"      var step{stepNum} = lo & {maskStr};");
                        step = (lo, hi) => lo & maskUInt;
                        break;
                    case "H":
                        sb.AppendLine($"      var step{stepNum} = hi & {maskStr};");
                        step = (lo, hi) => hi & maskUInt;
                        break;
                    case "'L":
                        sb.AppendLine($"      var step{stepNum} = nLo & {maskStr};");
                        step = (lo, hi) => ~lo & maskUInt;
                        break;
                    case "'H":
                        sb.AppendLine($"      var step{stepNum} = nHi & {maskStr};");
                        step = (lo, hi) => ~hi & maskUInt;
                        break;
                    case "L & H":
                        sb.AppendLine($"      var step{stepNum} = loAndHi & {maskStr};");
                        step = (lo, hi) => lo & hi & maskUInt;
                        break;
                    case "'L & H":
                        sb.AppendLine($"      var step{stepNum} = nloAndHi & {maskStr};");
                        step = (lo, hi) => ~lo & hi & maskUInt;
                        break;
                    case "L & 'H":
                        sb.AppendLine($"      var step{stepNum} = loAndNHi & {maskStr};");
                        step = (lo, hi) => lo & ~hi & maskUInt;
                        break;
                    case "'L & 'H":
                        sb.AppendLine($"      var step{stepNum} = nloAndNHi & {maskStr};");
                        step = (lo, hi) => ~lo & ~hi & maskUInt;
                        break;
                    case "L | H":
                        sb.AppendLine($"      var step{stepNum} = loOrHi & {maskStr};");
                        step = (lo, hi) => (lo | hi) & maskUInt;
                        break;
                    case "'L | H":
                        sb.AppendLine($"      var step{stepNum} = nloOrHi & {maskStr};");
                        step = (lo, hi) => (~lo | hi) & maskUInt;
                        break;
                    case "L | 'H":
                        sb.AppendLine($"      var step{stepNum} = loOrNHi & {maskStr};");
                        step = (lo, hi) => (lo | ~hi) & maskUInt;
                        break;
                    case "'L | 'H":
                        sb.AppendLine($"      var step{stepNum} = nloOrNHi & {maskStr};");
                        step = (lo, hi) => (~lo | ~hi) & maskUInt;
                        break;
                    case "(L & 'H) | ('L & H)":
                        sb.AppendLine($"      var step{stepNum} = loXorHi & {maskStr};");
                        step = (lo, hi) => (lo ^ hi) & maskUInt;
                        break;
                    case "('L & 'H) | (L & H)":
                        sb.AppendLine($"      var step{stepNum} = loNXorHi & {maskStr};");
                        step = (lo, hi) => ~(lo ^ hi) & maskUInt;
                        break;
                    default:
                        throw new Exception("Unexpected value needed");
                }

                if (rotate != 0)
                {
                    sb.AppendLine($"      step{stepNum} = BitOperations.RotateLeft(step{stepNum}, {rotate});");
                    var oldRef = step;
                    step = (lo, hi) => BitOperations.RotateLeft(oldRef(lo, hi), rotate);
                }

                stepCalculators.Add(step);

                stepNum++;
            }

            sb.AppendLine();
            sb.AppendLine($"      var result = {string.Join(" | ", Enumerable.Range(0, stepNum).Select(static x => $"step{x}"))};");

            Func<uint, uint, uint> finalCalc =
                (lo, hi) =>
                {
                    var ret = 0U;
                    foreach (var step in stepCalculators)
                    {
                        ret |= step(lo, hi);
                    }

                    return ret;
                };

            var mappedValue = asUIntPairs.ToDictionary(static kv => kv.Key, kv => finalCalc(kv.Value.Low, kv.Value.High));

            var uniqueMappedValues = mappedValue.Values.Distinct().ToList();
            if (uniqueMappedValues.Count != bulkStrings.Count)
            {
                throw new InvalidCastException("Uhhh, shouldn't happen?");
            }

            var modCandidates = Enumerable.Range(uniqueMappedValues.Count, 4098 - uniqueMappedValues.Count + 2);
            var rotateCandidates = Enumerable.Range(0, 32);

            var modAndRotateCandidates = modCandidates.SelectMany(mod => rotateCandidates.Select(rot => (Mod: mod, Rot: (byte)rot)));

            var validMods =
                modAndRotateCandidates
                    .Where(
                        t =>
                        {
                            var (mod, rot) = t;

                            var afterMod = uniqueMappedValues.Select(x => BitOperations.RotateLeft(x, rot) % mod).Distinct().Count();

                            return afterMod == uniqueMappedValues.Count;
                        }
                    )
                    .OrderBy(static x => x.Mod)
                    .ThenBy(static x => x.Rot)
                    .ToList();

            var (chosenMod, chosenRot) = validMods.First();

            var modedValues = mappedValue.ToDictionary(static kv => kv.Key, kv => BitOperations.RotateLeft(kv.Value, chosenRot) % chosenMod);

            sb.AppendLine();
            if (chosenRot == 0)
            {
                sb.AppendLine($"      var index = result % {chosenMod}");
            }
            else
            {
                sb.AppendLine($"      var index = BitOperations.RotateLeft(result, {chosenRot}) % {chosenMod};");
            }

            sb.AppendLine();
            sb.AppendLine("      Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref commandBuffer, commandStartIx + Vector256<byte>.Count), ref commandBufferAllocatedEnd), \"About to read past end of allocated command buffer\");");
            sb.AppendLine("      var actualStringValue = Vector256.LoadUnsafe(ref Unsafe.Add(ref commandBuffer, commandStartIx));");
            sb.AppendLine($"      var expectedStringValue = Vector256.LoadUnsafe(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(ExpectedStrings), index * {Vector256<byte>.Count}));");
            sb.AppendLine("      var expectedMask = Vector256.GreaterThan(expectedStringValue, Vector256<byte>.Zero);");
            sb.AppendLine("      actualStringValue = Vector256.BitwiseAnd(actualStringValue, expectedMask);");
            sb.AppendLine("      var stringOutOfRange = Vector256.GreaterThanAny(actualStringValue, Vector256.Create((byte)'z'));");
            sb.AppendLine("      actualStringValue = Vector256.BitwiseAnd(actualStringValue, Vector256.Create((byte)0xDF));");
            sb.AppendLine("      var stringsMatch = Vector256.EqualsAll(expectedStringValue, actualStringValue) & !stringOutOfRange;");
            sb.AppendLine("      var stringsMatchMask = (ushort)(-Unsafe.As<bool, byte>(ref stringsMatch) >> 31);");
            sb.AppendLine();
            sb.AppendLine("      var ret = CommandLookup[index];");
            sb.AppendLine("      ret = (RespCommand)(stringsMatchMask & (ushort)ret);");
            sb.AppendLine();
            sb.AppendLine("      return ret;");
            sb.Append("    }");

            var mtd = sb.ToString();
            sb.Clear();

            var lookupBytes = new Dictionary<int, byte>();
            for (var i = 0; i <= modedValues.Values.Max(); i++)
            {
                var insertAt = i * Vector256<byte>.Count;

                var actual = modedValues.SingleOrDefault(kv => kv.Value == i);
                if (actual.Key == RespCommand.None)
                {
                    for (var j = 0; j < Vector256<byte>.Count; j++)
                    {
                        lookupBytes.Add(insertAt + j, 0);
                    }
                }
                else
                {
                    var expected = upperCasedWithAnds[actual.Key].Span[(1 + actual.Key.ToString().Length.ToString().Length + 2)..]; // skip $\d\r\n
                    for (var j = 0; j < Vector256<byte>.Count; j++)
                    {
                        byte x;
                        if (j < expected.Length)
                        {
                            x = expected[j];
                        }
                        else
                        {
                            x = 0;
                        }

                        lookupBytes.Add(insertAt + j, x);
                    }
                }
            }

            sb.Append("    private static readonly byte[] ExpectedStrings = [");
            for (var i = 0; i <= lookupBytes.Keys.Max(); i++)
            {
                sb.Append($"0x{lookupBytes[i]:X2}, ");
            }
            sb.AppendLine("];");

            sb.AppendLine();
            sb.Append("    private static readonly RespCommand[] CommandLookup = [");
            for (var i = 0; i < chosenMod; i++)
            {
                var actual = modedValues.SingleOrDefault(kv => kv.Value == i);
                if (actual.Key == RespCommand.None)
                {
                    sb.Append("0, ");
                }
                else
                {
                    sb.Append($"RespCommand.{actual.Key}, ");
                }
            }
            sb.Append("];");

            var lookups = sb.ToString();

            var generated = $@"using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace OptimizationExercise.SIMDRespParsing
{{
  public static class BitFuncParseRespCommandImpl
  {{
{lookups}

{mtd}
  }}
}}";

            return generated;

            static List<(byte Bit, string Desc)>? SearchLevel(List<List<RespCommand>> groups, (byte Bit, string Desc, Dictionary<RespCommand, bool> ValuesForCommands)[] options, HashSet<(byte Bit, string Desc)> except)
            {
                if (groups.All(static g => g.Count == 1))
                {
                    return [];
                }

                var previousChoices = new HashSet<(byte Bit, string Desc)>();

                while (true)
                {
                    var bestChoice = BestDivides(groups, options, [.. previousChoices.Union(except)]);
                    if (bestChoice == null)
                    {
                        return null;
                    }

                    var lookup = options.Single(x => x.Bit == bestChoice.Value.Bit && x.Desc == bestChoice.Value.Desc).ValuesForCommands;

                    var regrouped = new List<List<RespCommand>>();
                    foreach (var group in groups)
                    {
                        regrouped.AddRange(group.GroupBy(x => lookup[x]).Select(static g => g.ToList()));
                    }

                    var nextLevelRes = SearchLevel(regrouped, options, [.. except.Append(bestChoice.Value)]);
                    if (nextLevelRes != null)
                    {
                        return [bestChoice.Value, .. nextLevelRes];
                    }

                    previousChoices.Add(bestChoice.Value);
                }
            }

            static (byte Bit, string Desc)? BestDivides(List<List<RespCommand>> groups, (byte Bit, string Desc, Dictionary<RespCommand, bool> ValuesForCommands)[] options, HashSet<(byte Bit, string Desc)> except)
            {
                var bestScore = 0;
                (byte Bit, string Desc)? bestOption = null;

                foreach (var option in options)
                {
                    if (except.Contains((option.Bit, option.Desc)))
                    {
                        continue;
                    }

                    var score = 0;

                    foreach (var group in groups)
                    {
                        if (group.Count == 1)
                        {
                            continue;
                        }

                        var divided = group.GroupBy(x => option.ValuesForCommands[x]);
                        if (divided.Count() == 1)
                        {
                            continue;
                        }

                        var smallerGroupSize = divided.Min(static g => g.Count());
                        score += smallerGroupSize;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestOption = (option.Bit, option.Desc);
                    }
                }

                return bestOption;
            }

            static IEnumerable<(byte Bit, string Desc, Func<uint, uint, bool> DoIt)> BinaryFunctions(byte bit)
            {
                var mask = 1U << bit;

                // unique functions
                // ################
                // l h | o (0)
                // =======
                // 0 0 | 0
                // 0 1 | 0
                // 1 0 | 0
                // 1 1 | 0
                //
                // l h | o ('L & 'H)
                // =======
                // 0 0 | 1
                // 0 1 | 0
                // 1 0 | 0
                // 1 1 | 0
                //
                // l h | o (`L & H)
                // =======
                // 0 0 | 0
                // 0 1 | 1 
                // 1 0 | 0
                // 1 1 | 0
                //
                // l h | o (`L)
                // =======
                // 0 0 | 1
                // 0 1 | 1
                // 1 0 | 0
                // 1 1 | 0
                //
                // l h | o (L & `H)
                // =======
                // 0 0 | 0
                // 0 1 | 0
                // 1 0 | 1
                // 1 1 | 0
                //
                // l h | o (`H)
                // =======
                // 0 0 | 1
                // 0 1 | 0
                // 1 0 | 1
                // 1 1 | 0
                // 
                // l h | o (L ^ H)
                // =======
                // 0 0 | 0
                // 0 1 | 1
                // 1 0 | 1
                // 1 1 | 0
                //
                // l h | o (`L | `H)
                // =======
                // 0 0 | 1
                // 0 1 | 1
                // 1 0 | 1
                // 1 1 | 0
                //
                // l h | o (L & H)
                // =======
                // 0 0 | 0
                // 0 1 | 0
                // 1 0 | 0
                // 1 1 | 1
                //
                // l h | o (`(L ^ H))
                // =======
                // 0 0 | 1
                // 0 1 | 0
                // 1 0 | 0
                // 1 1 | 1
                // 
                // l h | o (H)
                // =======
                // 0 0 | 0
                // 0 1 | 1
                // 1 0 | 0
                // 1 1 | 1
                //
                // l h | o (`L | H)
                // =======
                // 0 0 | 1
                // 0 1 | 1
                // 1 0 | 0
                // 1 1 | 1
                //
                // l h | o (L) 
                // =======
                // 0 0 | 0
                // 0 1 | 0
                // 1 0 | 1
                // 1 1 | 1
                //
                // l h | o (L | `H)
                // =======
                // 0 0 | 1
                // 0 1 | 0
                // 1 0 | 1
                // 1 1 | 1
                //
                // l h | o (L | H)
                // =======
                // 0 0 | 0
                // 0 1 | 1
                // 1 0 | 1
                // 1 1 | 1
                //
                // l h | o (1)
                // =======
                // 0 0 | 1
                // 0 1 | 1
                // 1 0 | 1
                // 1 1 | 1

                // constants
                //yield return (bit, "0", (low, high) => false);
                //yield return (bit, "1", (low, high) => true);

                // identity (2)
                yield return (bit, "L", (low, high) => (low & mask) != 0);
                yield return (bit, "H", (low, high) => (high & mask) != 0);

                // not (2)
                yield return (bit, "'L", (low, high) => (~low & mask) != 0);
                yield return (bit, "'H", (low, high) => (~high & mask) != 0);

                // and (4)
                yield return (bit, "L & H", (low, high) => ((low & mask) & (high & mask)) != 0);
                yield return (bit, "L & 'H", (low, high) => ((low & mask) & (~high & mask)) != 0);
                yield return (bit, "'L & H", (low, high) => ((~low & mask) & (high & mask)) != 0);
                yield return (bit, "'L & 'H", (low, high) => ((~low & mask) & (~high & mask)) != 0);

                // or (4)
                yield return (bit, "L | H", (low, high) => ((low & mask) | (high & mask)) != 0);
                yield return (bit, "L | 'H", (low, high) => ((low & mask) | (~high & mask)) != 0);
                yield return (bit, "'L | H", (low, high) => ((~low & mask) | (high & mask)) != 0);
                yield return (bit, "'L | 'H", (low, high) => ((~low & mask) | (~high & mask)) != 0);

                // xor = (L & `H) | (`L & H)
                yield return (bit, "(L & 'H) | ('L & H)", (low, high) => ((low & mask) ^ (high & mask)) != 0);

                // not xor = (`L & `H) | (L & H)
                yield return (bit, "`('L & 'H) | (L & H)", (low, high) => ((low & mask) ^ (high & mask)) == 0);
            }

            static IEnumerable<List<byte>> Permutate(IEnumerable<byte> items)
            {
                if (items.Count() == 1)
                {
                    yield return items.ToList();
                    yield break;
                }

                for (var i = 0; i < items.Count(); i++)
                {
                    var item = items.ElementAt(i);
                    var iCopy = i;
                    var withoutI = items.Where((i, ix) => ix != iCopy);

                    foreach (var next in Permutate(withoutI))
                    {
                        yield return (new byte[] { item }.Concat(next)).ToList();
                    }
                }
            }
        }

        private static Dictionary<RespCommand, ReadOnlyMemory<byte>> GetBulkStrings()
        {
            var bulkStringEquivalents = new Dictionary<RespCommand, ReadOnlyMemory<byte>>();

            foreach (var cmd in Enum.GetValues<RespCommand>())
            {
                if (cmd is RespCommand.None or RespCommand.Invalid)
                {
                    continue;
                }

                var asString = cmd.ToString().ToUpperInvariant();
                var asBytes = Encoding.ASCII.GetBytes(asString);
                var len = asBytes.Length;

                var encoded = new List<byte> { (byte)'$' };
                encoded.AddRange(Encoding.ASCII.GetBytes(len.ToString()));
                encoded.AddRange("\r\n"u8);
                encoded.AddRange(asBytes);
                encoded.AddRange("\r\n"u8);

                bulkStringEquivalents[cmd] = encoded.ToArray();
            }

            return bulkStringEquivalents;
        }
    }
}
