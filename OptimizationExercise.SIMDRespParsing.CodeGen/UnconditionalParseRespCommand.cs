using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Text;

namespace OptimizationExercise.SIMDRespParsing.CodeGen
{
    internal static class UnconditionalParseRespCommand
    {
        public static string GenerateAddXorRotate()
        {
            const int CmdSpacingBytes = 64;

            // how the command will be represented as a Resp bulk string
            var bulkStringEquivalents = GetBulkStrings();

            var model = FindConstantModAndLengthXorsFor(bulkStringEquivalents);

            var lookupSizeNextPow2 = BitOperations.RoundUpToPowerOf2(model.LookupSize);

            var allLengths = bulkStringEquivalents.Select(static kv => kv.Key.ToString().Length).ToHashSet();
            var maxLength = allLengths.Max();

            var maxLenPow2 = BitOperations.IsPow2(maxLength) ? maxLength : (int)BitOperations.RoundUpToPowerOf2((uint)maxLength);

            var sb = new StringBuilder();
            sb.AppendLine("using System.Diagnostics;");
            sb.AppendLine("using System.Runtime.CompilerServices;");
            sb.AppendLine("using System.Runtime.InteropServices;");
            sb.AppendLine("using System.Runtime.Intrinsics;");
            sb.AppendLine();
            sb.AppendLine("namespace OptimizationExercise.SIMDRespParsing");
            sb.AppendLine("{");
            sb.AppendLine("  public static class UnconditionalParseRespCommandImpl");
            sb.AppendLine("  {");

            sb.AppendLine($"    // {maxLenPow2 * CmdSpacingBytes:N0} bytes, {(maxLenPow2 * CmdSpacingBytes) / 1_024}K bytes, {(maxLenPow2 * CmdSpacingBytes) / (1_024 * 1_024)}M bytes");
            sb.Append("    private static readonly byte[] LengthMaskAndHashXors = [");


            var andToLimitLength = maxLenPow2 - 1;

            for (var len = 0; len <= maxLenPow2; len++)
            {
                if (allLengths.Contains(len))
                {
                    for (var i = 0; i < Vector256<byte>.Count; i++)
                    {
                        if (i < len)
                        {
                            sb.Append(" 0xDF,");
                        }
                        else
                        {
                            sb.Append(" 0x00,");
                        }
                    }

                    var xorForLen = model.LengthToXorConstant[len];

                    var xorBytes = new byte[sizeof(uint)];
                    BinaryPrimitives.WriteUInt32LittleEndian(xorBytes, xorForLen);

                    for (var i = 0; i < xorBytes.Length; i++)
                    {
                        sb.Append($" 0x{xorBytes[i]:X2},");
                    }

                    for (var i = Vector256<byte>.Count + sizeof(uint); i < CmdSpacingBytes; i++)
                    {
                        sb.Append(" 0x00,");
                    }
                }
                else
                {
                    for (var i = 0; i < CmdSpacingBytes; i++)
                    {
                        sb.Append(" 0x00,");
                    }
                }
            }

            sb.AppendLine(" ];");
            sb.AppendLine();

            var commandAndExpectedValuesDict = new Dictionary<int, byte>();
            foreach (var kv in bulkStringEquivalents)
            {
                var len = kv.Key.ToString().Length;

                var commandStart = kv.Value.Span.IndexOf((byte)'\n') + 1;

                var asUpper = new byte[Vector256<byte>.Count];
                for (var i = 0; i < len; i++)
                {
                    asUpper[i] = (byte)(kv.Value.Span[i + commandStart] & 0xDF);
                }

                var commandVector = Vector256.LoadUnsafe(ref asUpper[0]);
                var xor = Vector256.Create(model.LengthToXorConstant[len]);

                var xored = Vector256.Xor(commandVector, Vector256.As<uint, byte>(xor));
                var mixed = Vector256.Dot(Vector256.As<byte, uint>(xored), Vector256<uint>.One);
                var moded = mixed % model.LookupSize;

                var cmdIx = len * (lookupSizeNextPow2 * CmdSpacingBytes) + moded * CmdSpacingBytes;

                var cmdBuff = new byte[sizeof(int)];
                BinaryPrimitives.WriteInt32LittleEndian(cmdBuff, (int)kv.Key);

                for (var i = 0; i < cmdBuff.Length; i++)
                {
                    commandAndExpectedValuesDict.Add((int)(cmdIx + i), cmdBuff[i]);
                }

                var expectedValueBuff = new byte[Vector256<byte>.Count];
                commandVector.StoreUnsafe(ref expectedValueBuff[0]);
                for (var i = 0; i < expectedValueBuff.Length; i++)
                {
                    commandAndExpectedValuesDict.Add((int)(cmdIx + sizeof(int) + i), expectedValueBuff[i]);
                }
            }

            var commandAndExpectedValuesSize = lookupSizeNextPow2 * andToLimitLength * CmdSpacingBytes;
            sb.AppendLine($"    // {commandAndExpectedValuesSize:N0} bytes, {commandAndExpectedValuesSize / 1_024}K bytes, {commandAndExpectedValuesSize / (1_024 * 1_024)}M bytes");
            sb.Append("    private static readonly byte[] CommandAndExpectedValues = [");

            for (var i = 0; i <= commandAndExpectedValuesSize; i++)
            {
                if (commandAndExpectedValuesDict.TryGetValue(i, out var data))
                {
                    sb.Append($" 0x{data:X2},");
                }
                else
                {
                    sb.Append(" 0x00,");
                }
            }
            sb.AppendLine(" ];");
            sb.AppendLine();

            sb.AppendLine("    [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("    public static RespCommand Parse(ref byte commandBufferAllocatedEnd, ref byte commandBuffer, int commandStartIx, int commandLength)");
            sb.AppendLine("    {");
            sb.AppendLine("      Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref commandBuffer, commandStartIx + Vector256<byte>.Count), ref commandBufferAllocatedEnd), \"About to read past end of allocated command buffer\");");
            sb.AppendLine("      var commandVector = Vector256.LoadUnsafe(ref Unsafe.Add(ref commandBuffer, commandStartIx));");
            sb.AppendLine();
            sb.AppendLine($"      var effectiveLength = commandLength & {andToLimitLength};");
            sb.AppendLine($"      var maskIx = effectiveLength * {CmdSpacingBytes};");
            sb.AppendLine($"      var xorIx = maskIx + {Vector256<byte>.Count};");
            sb.AppendLine();
            sb.AppendLine("      ref var lengthMaskAndHashXorsRef = ref MemoryMarshal.GetArrayDataReference(LengthMaskAndHashXors);");
            sb.AppendLine("      Debug.Assert(xorIx + sizeof(uint) <= LengthMaskAndHashXors.Length, \"About to read past end of LengthMaskAndHashXors\");");
            sb.AppendLine("      var maskVector = Vector256.LoadUnsafe(ref Unsafe.Add(ref lengthMaskAndHashXorsRef, maskIx));");
            sb.AppendLine("      var xorVector = Vector256.Create(Unsafe.As<byte, uint>(ref Unsafe.Add(ref lengthMaskAndHashXorsRef, xorIx)));");
            sb.AppendLine();
            sb.AppendLine("      var truncatedCommandVector = Vector256.BitwiseAnd(commandVector, Vector256.GreaterThan(maskVector, Vector256<byte>.Zero));");
            sb.AppendLine("      var upperCommandVector = Vector256.BitwiseAnd(truncatedCommandVector, maskVector);");
            sb.AppendLine();
            sb.AppendLine("      var invalid = Vector256.GreaterThanAny(truncatedCommandVector, Vector256.Create((byte)'z'));");
            sb.AppendLine("      var allZerosIfInvalid = Unsafe.As<bool, byte>(ref invalid) - 1;");
            sb.AppendLine();
            sb.AppendLine("      var xored = Vector256.Xor(upperCommandVector, Vector256.As<uint, byte>(xorVector));");
            sb.AppendLine("      var mixed = Vector256.Dot(Vector256.As<byte, uint>(xored), Vector256<uint>.One);");
            sb.AppendLine($"      var moded = mixed % {model.LookupSize};");
            sb.AppendLine();
            sb.AppendLine($"      var dataStartIx = {lookupSizeNextPow2 * CmdSpacingBytes} * effectiveLength;");
            sb.AppendLine($"      dataStartIx += (int)(moded * {CmdSpacingBytes});");
            sb.AppendLine();
            sb.AppendLine("      ref var commandAndExpectedValuesRef = ref MemoryMarshal.GetArrayDataReference(CommandAndExpectedValues);");
            sb.AppendLine("      Debug.Assert(dataStartIx + sizeof(uint) + Vector256<byte>.Count <= CommandAndExpectedValues.Length, \"About to read past end of CommandAndExpectedValues\");");
            sb.AppendLine("      var cmd = Unsafe.As<byte, int>(ref Unsafe.Add(ref commandAndExpectedValuesRef, dataStartIx));");
            sb.AppendLine($"      var expectedCommandVector = Vector256.LoadUnsafe(ref Unsafe.Add(ref commandAndExpectedValuesRef, dataStartIx + {sizeof(int)}));");
            sb.AppendLine();
            sb.AppendLine("      var matches = Vector256.EqualsAll(upperCommandVector, expectedCommandVector);");
            sb.AppendLine("      var allOnesIfMatches = -Unsafe.As<bool, byte>(ref matches);");
            sb.AppendLine();
            sb.AppendLine("      var ret = (RespCommand)(cmd & allOnesIfMatches & allZerosIfInvalid);");
            sb.AppendLine("      return ret;");
            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine("}");

            var ret = sb.ToString();

            return ret;
        }

        public static (uint LookupSize, Dictionary<int, uint> LengthToXorConstant) FindConstantModAndLengthXorsFor(
            Dictionary<RespCommand, ReadOnlyMemory<byte>> bulkStringEquivalents
        )
        {
            Span<uint> allPrimes = [2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71, 73, 79, 83, 89, 97, 101, 103, 107, 109, 113, 127, 131, 137, 139, 149, 151, 157, 163, 167, 173, 179, 181, 191, 193, 197, 199, 211, 223, 227, 229, 233, 239, 241, 251, 257, 263, 269, 271, 277, 281, 283, 293, 307, 311, 313, 317, 331, 337, 347, 349, 353, 359, 367, 373, 379, 383, 389, 397, 401, 409, 419, 421, 431, 433, 439, 443, 449, 457, 461, 463, 467, 479, 487, 491, 499, 503, 509, 521, 523, 541, 547, 557, 563, 569, 571, 577, 587, 593, 599, 601, 607, 613, 617, 619, 631, 641, 643, 647, 653, 659, 661, 673, 677, 683, 691, 701, 709, 719, 727, 733, 739, 743, 751, 757, 761, 769, 773, 787, 797, 809, 811, 821, 823, 827, 829, 839, 853, 857, 859, 863, 877, 881, 883, 887, 907, 911, 919, 929, 937, 941, 947, 953, 967, 971, 977, 983, 991, 997, 1009, 1013, 1019, 1021, 1031, 1033, 1039, 1049, 1051, 1061, 1063, 1069, 1087, 1091, 1093, 1097, 1103, 1109, 1117, 1123, 1129, 1151, 1153, 1163, 1171, 1181, 1187, 1193, 1201, 1213, 1217, 1223, 1229, 1231, 1237, 1249, 1259, 1277, 1279, 1283, 1289, 1291, 1297, 1301, 1303, 1307, 1319, 1321, 1327, 1361, 1367, 1373, 1381, 1399, 1409, 1423, 1427, 1429, 1433, 1439, 1447, 1451, 1453, 1459, 1471, 1481, 1483, 1487, 1489, 1493, 1499, 1511, 1523, 1531, 1543, 1549, 1553, 1559, 1567, 1571, 1579, 1583, 1597, 1601, 1607, 1609, 1613, 1619, 1621, 1627, 1637, 1657, 1663, 1667, 1669, 1693, 1697, 1699, 1709, 1721, 1723, 1733, 1741, 1747, 1753, 1759, 1777, 1783, 1787, 1789, 1801, 1811, 1823, 1831, 1847, 1861, 1867, 1871, 1873, 1877, 1879, 1889, 1901, 1907, 1913, 1931, 1933, 1949, 1951, 1973, 1979, 1987, 1993, 1997, 1999, 2003, 2011, 2017, 2027, 2029, 2039, 2053, 2063, 2069, 2081, 2083, 2087, 2089, 2099, 2111, 2113, 2129, 2131, 2137, 2141, 2143, 2153, 2161, 2179, 2203, 2207, 2213, 2221, 2237, 2239, 2243, 2251, 2267, 2269, 2273, 2281, 2287, 2293, 2297, 2309, 2311, 2333, 2339, 2341, 2347, 2351, 2357, 2371, 2377, 2381, 2383, 2389, 2393, 2399, 2411, 2417, 2423, 2437, 2441, 2447, 2459, 2467, 2473, 2477, 2503, 2521, 2531, 2539, 2543, 2549, 2551, 2557, 2579, 2591, 2593, 2609, 2617, 2621, 2633, 2647, 2657, 2659, 2663, 2671, 2677, 2683, 2687, 2689, 2693, 2699, 2707, 2711, 2713, 2719, 2729, 2731, 2741, 2749, 2753, 2767, 2777, 2789, 2791, 2797, 2801, 2803, 2819, 2833, 2837, 2843, 2851, 2857, 2861, 2879, 2887, 2897, 2903, 2909, 2917, 2927, 2939, 2953, 2957, 2963, 2969, 2971, 2999, 3001, 3011, 3019, 3023, 3037, 3041, 3049, 3061, 3067, 3079, 3083, 3089, 3109, 3119, 3121, 3137, 3163, 3167, 3169, 3181, 3187, 3191, 3203, 3209, 3217, 3221, 3229, 3251, 3253, 3257, 3259, 3271, 3299, 3301, 3307, 3313, 3319, 3323, 3329, 3331, 3343, 3347, 3359, 3361, 3371, 3373, 3389, 3391, 3407, 3413, 3433, 3449, 3457, 3461, 3463, 3467, 3469, 3491, 3499, 3511, 3517, 3527, 3529, 3533, 3539, 3541, 3547, 3557, 3559, 3571, 3581, 3583, 3593, 3607, 3613, 3617, 3623, 3631, 3637, 3643, 3659, 3671, 3673, 3677, 3691, 3697, 3701, 3709, 3719, 3727, 3733, 3739, 3761, 3767, 3769, 3779, 3793, 3797, 3803, 3821, 3823, 3833, 3847, 3851, 3853, 3863, 3877, 3881, 3889, 3907, 3911, 3917, 3919, 3923, 3929, 3931, 3943, 3947, 3967, 3989, 4001, 4003, 4007, 4013, 4019, 4021, 4027, 4049, 4051, 4057, 4073, 4079, 4091, 4093, 4099];

            var minSize = bulkStringEquivalents.GroupBy(static kv => kv.Key.ToString().Length).Select(static g => g.Count()).OrderByDescending(static x => x).First();

            var candidateLookupSizes = allPrimes.ToArray().Where(p => p >= (uint)minSize).TakeWhile(static p => p <= 1_024).ToArray();

            candidateLookupSizes = candidateLookupSizes.OrderBy(static x => x == 71 ? 0 : x).ToArray();

            var lengthsToVectors = new Dictionary<int, List<Vector256<byte>>>();

            foreach (var kv in bulkStringEquivalents)
            {
                var len = kv.Key.ToString().Length;

                var commandStart = kv.Value.Span.IndexOf((byte)'\n') + 1;

                var asUpper = new byte[Vector256<byte>.Count];
                for (var i = 0; i < len; i++)
                {
                    asUpper[i] = (byte)(kv.Value.Span[i + commandStart] & 0xDF);
                }

                if (!lengthsToVectors.TryGetValue(len, out var vecs))
                {
                    lengthsToVectors[len] = vecs = new();
                }

                vecs.Add(Vector256.LoadUnsafe(ref asUpper[0]));
            }

            var candidates = new Dictionary<int, uint>();
            var uniqueVals = new HashSet<uint>();

            foreach (var lookupSize in candidateLookupSizes)
            {
                candidates.Clear();

                foreach (var commandGroup in lengthsToVectors.OrderByDescending(static kv => kv.Value.Count))
                {
                    for (var xor = 0L; xor <= uint.MaxValue; xor++)
                    {
                        uniqueVals.Clear();

                        var toXor = Vector256.Create((uint)xor);

                        foreach (var commandVec in commandGroup.Value)
                        {
                            var xored = Vector256.Xor(commandVec, Vector256.As<uint, byte>(toXor));

                            var mixed = Vector256.Dot(Vector256.As<byte, uint>(xored), Vector256<uint>.One);
                            var moded = mixed % lookupSize;

                            if (!uniqueVals.Add(moded))
                            {
                                break;
                            }
                        }

                        if (uniqueVals.Count == commandGroup.Value.Count)
                        {
                            candidates[commandGroup.Key] = (uint)xor;
                            break;
                        }
                    }

                    if (!candidates.ContainsKey(commandGroup.Key))
                    {
                        candidates.Clear();
                        break;
                    }
                }

                if (candidates.Count != 0)
                {
                    // todo: maybe we could minimize the XORs?
                    //       presumably only a couple of the bits actually matter, and we just stumble across them due to increments
                    //       and if they're all in common (definitely possible due to the commands being, well, English)
                    //       we could hard code them and remove a (cheap, but non-zero) load from memory
                    return (lookupSize, candidates);
                }
            }

            throw new Exception("Nope!");
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
