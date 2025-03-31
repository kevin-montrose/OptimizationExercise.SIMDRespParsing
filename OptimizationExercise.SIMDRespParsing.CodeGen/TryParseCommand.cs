using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;

namespace OptimizationExercise.SIMDRespParsing.CodeGen
{
    /// <summary>
    /// Generate code for complicated <see cref="RespParser.TryParseCommand_Enum(Span{byte}, int, int, out RespCommand)"/> equivalents.
    /// </summary>
    public static class TryParseCommand
    {
        /// <summary>
        /// Generate a version which uses AES intrinsics to do weird hash-y things.
        /// </summary>
        public static string GenerateHash()
        {
            Console.WriteLine($"[{DateTime.UtcNow:u}] {nameof(TryParseCommand)}.{nameof(GenerateHash)}");

            var byLength = new Dictionary<int, List<RespCommand>>();

            foreach (var cmd in Enum.GetValues<RespCommand>())
            {
                if (cmd is RespCommand.None or RespCommand.Invalid)
                {
                    continue;
                }

                var strLen = cmd.ToString().Length;

                if (!byLength.TryGetValue(strLen, out var cmds))
                {
                    byLength[strLen] = cmds = new();
                }

                cmds.Add(cmd);
            }


            var lookupDecls = new List<string>();
            var sb = new StringBuilder();

            sb.AppendLine("[MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("public static bool TryParseCommand_Hash(Span<byte> cmdBuffer, int commandStart, int commandEnd, out RespCommand parsed)");
            sb.AppendLine("{");
            sb.AppendLine("  ref var cmdStartRef = ref Unsafe.Add(ref MemoryMarshal.GetReference(cmdBuffer), commandStart);");
            sb.AppendLine("  var firstPass = true;");
            sb.AppendLine("  var cmdLen = commandEnd - commandStart;");
            sb.AppendLine();
            sb.AppendLine("tryAgain:");
            sb.AppendLine("  switch(cmdLen)");
            sb.AppendLine("  {");

            foreach (var len in byLength.Keys.OrderBy(static k => k))
            {
                Console.WriteLine($"[{DateTime.UtcNow:u}]\t len={len}, count={byLength[len].Count}");
                HandleCase(len, byLength[len], sb, lookupDecls);
            }

            sb.AppendLine("    default:");
            sb.AppendLine("      Unsafe.SkipInit(out parsed);");
            sb.AppendLine("      return false;");
            sb.AppendLine("  }");
            sb.AppendLine();
            sb.AppendLine("  if (firstPass)");
            sb.AppendLine("  {");
            sb.AppendLine("    Ascii.ToUpperInPlace(cmdBuffer[commandStart..commandEnd], out _);");
            sb.AppendLine("    firstPass = false;");
            sb.AppendLine("    goto tryAgain;");
            sb.AppendLine("  }");
            sb.AppendLine();
            sb.AppendLine("  Unsafe.SkipInit(out parsed);");
            sb.AppendLine("  return false;");
            sb.AppendLine("}");
            sb.AppendLine();

            foreach (var decl in lookupDecls)
            {
                sb.AppendLine(decl);
            }

            return sb.ToString();

            static void HandleCase(int len, List<RespCommand> cmds, StringBuilder into, List<string> lookupDecls)
            {
                if (cmds.Count == 1)
                {
                    Console.WriteLine($"[{DateTime.UtcNow:u}] Single case for {len}");

                    var cmd = cmds.Single();

                    into.AppendLine($"    case {len}:");
                    into.AppendLine($"      parsed = RespCommand.{cmd.ToString().ToUpperInvariant()};");

                    if (len <= 4)
                    {

                        into.AppendLine($"      var len{len}p0 = Unsafe.As<byte, uint>(ref cmdStartRef);");
                        into.AppendLine($"      if(len{len}p0 == {cmd.ToString().ToUpperInvariant()})");
                        into.AppendLine("      {");
                        into.AppendLine("        return true;");
                        into.AppendLine("      }");
                        into.AppendLine("      break;");
                    }
                    else if (len == 5)
                    {
                        into.AppendLine($"      var len{len}p0 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, -1));");
                        into.AppendLine($"      var len{len}p0 = Unsafe.As<byte, uint>(ref cmdStartRef);");
                        into.AppendLine($"      if(len{len}p0 == {cmd.ToString().ToUpperInvariant()})");
                        into.AppendLine("      {");
                        into.AppendLine("        return true;");
                        into.AppendLine("      }");
                        into.AppendLine("      break;");
                    }
                    else if (len <= 8)
                    {
                        into.AppendLine($"      var len{len}p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);");
                        into.AppendLine($"      var len{len}p0 = Unsafe.As<byte, uint>(ref cmdStartRef);");
                        into.AppendLine($"      if(len{len}p0 == {cmd.ToString().ToUpperInvariant()})");
                        into.AppendLine("      {");
                        into.AppendLine("        return true;");
                        into.AppendLine("      }");
                        into.AppendLine("      break;");
                    }
                    else if (len <= 12)
                    {
                        var tailOffset = 8 - (12 - len);
                        var ulongStrPart = cmd.ToString().ToUpperInvariant()[..8];
                        var uintStrPart = cmd.ToString()[tailOffset..];

                        into.AppendLine($"      var len{len}p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);");
                        into.AppendLine($"      var len{len}p1 = Unsafe.As<byte, uint>(ref Unsafe.Add(ref cmdStartRef, {tailOffset}));");
                        into.AppendLine($"      if(len{len}p0 == {ulongStrPart} && len{len}p1 == {uintStrPart})");
                        into.AppendLine("      {");
                        into.AppendLine("        return true;");
                        into.AppendLine("      }");
                        into.AppendLine("      break;");
                    }
                    else if (len <= 16)
                    {
                        var tailOffset = 8 - (16 - len);
                        var ulong0StrPart = cmd.ToString().ToUpperInvariant()[..8];
                        var ulong1StrPart = cmd.ToString()[tailOffset..];

                        into.AppendLine($"      var len{len}p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);");
                        into.AppendLine($"      var len{len}p1 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, {tailOffset}));");
                        into.AppendLine($"      if(len{len}p0 == {ulong0StrPart} && len{len}p1 == {ulong1StrPart})");
                        into.AppendLine("      {");
                        into.AppendLine("        return true;");
                        into.AppendLine("      }");
                        into.AppendLine("      break;");
                    }
                    else if (len <= 20)
                    {
                        // len == 17, tailOffset == 13
                        // len == 18, tailOffset == 14
                        // len == 19, tailOffset == 15
                        // len == 20, tailOffset == 16
                        var tailOffset = 20 - (24 - len);

                        var ulong0StrPart = cmd.ToString().ToUpperInvariant()[..8];
                        var ulong1StrPart = cmd.ToString().ToUpperInvariant()[8..16];
                        var uintStrPtr = cmd.ToString().ToUpperInvariant()[tailOffset..];

                        into.AppendLine($"      var len{len}p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);");
                        into.AppendLine($"      var len{len}p1 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, 8));");
                        into.AppendLine($"      var len{len}p2 = Unsafe.As<byte, uint>(ref Unsafe.Add(ref cmdStartRef, {tailOffset}));");
                        into.AppendLine($"      if(len{len}p0 == {ulong0StrPart} && len{len}p1 == {ulong1StrPart} && len{len}p2 == {uintStrPtr})");
                        into.AppendLine("      {");
                        into.AppendLine("        return true;");
                        into.AppendLine("      }");
                        into.AppendLine("      break;");
                    }
                    else
                    {
                        throw new NotImplementedException($"Single element not implemented for length == {len}");
                    }
                }
                else
                {
                    if (len <= 4)
                    {
                        var uintEquivs = new Dictionary<RespCommand, uint>();
                        foreach (var cmd in cmds)
                        {
                            var field = typeof(RespParser).GetField(cmd.ToString().ToUpperInvariant(), BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                            var asUint = (uint)field!.GetValue(null)!;

                            uintEquivs[cmd] = asUint;
                        }

                        //var (bitChurn, uShortIndex, mod) = FindBestModUInt(uintEquivs);

                        var next = FindBestModGeneric(uintEquivs, []);
                        if (next is not null)
                        {
                            Console.WriteLine($"[{DateTime.UtcNow:u}] Simple works for {len}");
                            Console.WriteLine($"[{DateTime.UtcNow:u}]\t{next.Value}");
                        }
                        else
                        {
                            Console.WriteLine($"[{DateTime.UtcNow:u}] Simple DOES NOT work for {len}");
                            Environment.Exit(-1);
                        }

                        var (maskCommonBits, _, bitChurn, _, mod) = next.Value;

                        var map = new Dictionary<byte, RespCommand>();
                        foreach (var cmd in cmds)
                        {
                            var u = uintEquivs[cmd];

                            if (maskCommonBits)
                            {
                                u &= ~(0b0110_0000_0110_0000__0110_0000_0110_0000U);
                            }

                            u ^= bitChurn;
                            var calculatedValue = (byte)(u % mod);

                            map.Add(calculatedValue, cmd);
                        }

                        into.AppendLine($"    case {len}:");
                        into.AppendLine($"      var len{len}p0 = Unsafe.As<byte, uint>(ref cmdStartRef);");
                        into.AppendLine($"      var calculatedValue{len} = len{len}p0;");
                        if (maskCommonBits)
                        {
                            into.AppendLine($"      calculatedValue{len} &= ~(0b0110_0000_0110_0000__0110_0000_0110_0000U);");
                        }
                        if (bitChurn != 0)
                        {
                            into.AppendLine($"      calculatedValue{len} ^= {bitChurn}U;");
                        }
                        into.AppendLine($"      var len{len}Ix = (byte)(calculatedValue{len} % {mod});");
                        into.AppendLine($"      var len{len}Value = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash{len}Lookup), len{len}Ix);");
                        into.AppendLine($"      parsed = (RespCommand)len{len}Value;");
                        into.AppendLine($"      if ((len{len}Value >> 32) == len{len}p0)");
                        into.AppendLine("      {");
                        into.AppendLine("        return true;");
                        into.AppendLine("      }");
                        into.AppendLine("    break;");

                        var decl = new StringBuilder();
                        decl.Append($"private static readonly ulong[] Hash{len}Lookup = [");
                        for (var val = 0; val <= mod; val++)
                        {
                            if (!map.TryGetValue((byte)val, out var cmd))
                            {
                                decl.Append("0, ");
                                continue;
                            }

                            decl.Append($"((ulong){cmd.ToString().ToUpperInvariant()} << 32) | (ulong)RespCommand.{cmd}, ");
                        }
                        decl.Append("];");
                        lookupDecls.Add(decl.ToString());
                    }
                    else if (len <= 8)
                    {
                        var ulongEquivs = new Dictionary<RespCommand, ulong>();
                        foreach (var cmd in cmds)
                        {
                            var field = typeof(RespParser).GetField(cmd.ToString().ToUpperInvariant(), BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                            var asULong = (ulong)field!.GetValue(null)!;

                            ulongEquivs[cmd] = asULong;
                        }

                        //var (bitChurn0, bitChurn1, uShortIndex0, uShortIndex1, mod) = FindBestModULong(ulongEquivs);

                        var next = FindBestModGeneric(null, [ulongEquivs]);
                        if (next is not null)
                        {
                            Console.WriteLine($"[{DateTime.UtcNow:u}] Simple works for {len}");
                            Console.WriteLine($"[{DateTime.UtcNow:u}]\t{next.Value}");
                        }
                        else
                        {
                            Console.WriteLine($"[{DateTime.UtcNow:u}] Simple DOES NOT work for {len}");
                            Environment.Exit(-1);
                        }

                        var (maskCommonBits, includeLongHighBits, bitChurn, ulongShifts, mod) = next.Value;

                        var map = new Dictionary<byte, RespCommand>();
                        foreach (var cmd in cmds)
                        {
                            var originalVal = ulongEquivs[cmd];
                            if (maskCommonBits)
                            {
                                originalVal &= ~(0b0110_0000_0110_0000__0110_0000_0110_0000__0110_0000_0110_0000__0110_0000_0110_0000UL);
                            }

                            ulong processedVal;
                            if (ulongShifts[0] == 0)
                            {
                                processedVal = originalVal;
                            }
                            else
                            {
                                processedVal = originalVal >> ulongShifts[0];
                            }

                            if (includeLongHighBits)
                            {
                                processedVal ^= (originalVal >> (32 - ulongShifts[0]));
                            }

                            processedVal ^= bitChurn;

                            var calculatedValue = (byte)((uint)processedVal % mod);

                            map.Add(calculatedValue, cmd);
                        }

                        into.AppendLine($"    case {len}:");
                        if (len == 5)
                        {
                            // special case for reading 1 before start of command
                            into.AppendLine($"      var len{len}p0 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, -1));");
                        }
                        else
                        {
                            into.AppendLine($"      var len{len}p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);");
                        }

                        if (maskCommonBits)
                        {
                            into.AppendLine($"      len{len}p0 &= ~(0b0110_0000_0110_0000__0110_0000_0110_0000__0110_0000_0110_0000__0110_0000_0110_0000UL);");
                        }

                        into.AppendLine($"      var len{len}Calc = len{len}p0;");
                        if (ulongShifts[0] != 0)
                        {
                            into.AppendLine($"      len{len}Calc >>= {ulongShifts[0]};");
                        }

                        if (includeLongHighBits)
                        {
                            into.AppendLine($"      len{len}Calc ^= (len{len}p0 >> (32 - {ulongShifts[0]}));");
                        }

                        if (bitChurn != 0)
                        {
                            into.AppendLine($"      len{len}Calc ^= {bitChurn}U;");
                        }

                        into.AppendLine($"      var len{len}Ix = (byte)((uint)len{len}Calc % (byte){mod});");
                        into.AppendLine($"      ref var len{len}ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash{len}Lookup), len{len}Ix * 2);");
                        into.AppendLine();
                        into.AppendLine($"      parsed = (RespCommand)len{len}ValueRef;");
                        into.AppendLine($"      if (Unsafe.Add(ref len{len}ValueRef, 1) == len{len}p0)");
                        into.AppendLine("      {");
                        into.AppendLine("        return true;");
                        into.AppendLine("      }");
                        into.AppendLine("    break;");

                        var decl = new StringBuilder();
                        decl.Append($"private static readonly ulong[] Hash{len}Lookup = [");
                        for (var val = 0; val <= mod; val++)
                        {
                            if (!map.TryGetValue((byte)val, out var cmd))
                            {
                                decl.Append("0, 0,");
                                continue;
                            }

                            decl.Append($"(ulong)RespCommand.{cmd}, {cmd.ToString().ToUpperInvariant()}, ");
                        }
                        decl.Append("];");
                        lookupDecls.Add(decl.ToString());
                    }
                    else if (len <= 12)
                    {
                        // len ==  9, tailOffset = 5
                        // len == 10, tailOffset = 6
                        // len == 11, tailOffset = 7
                        // len == 12, tailOffset = 8
                        var tailOffset = 8 - (12 - len);

                        var ulongPartEquivs = new Dictionary<RespCommand, ulong>();
                        var ulongPartNames = new Dictionary<RespCommand, string>();

                        var uintPartEquivs = new Dictionary<RespCommand, uint>();
                        var uintPartNames = new Dictionary<RespCommand, string>();
                        foreach (var cmd in cmds)
                        {
                            var ulongStrPart = cmd.ToString().ToUpperInvariant()[..8];
                            var uintStrPart = cmd.ToString()[tailOffset..];

                            var field0 = typeof(RespParser).GetField(ulongStrPart, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                            var asULong = (ulong)field0!.GetValue(null)!;

                            var field1 = typeof(RespParser).GetField(uintStrPart, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                            var asUInt = (uint)field1!.GetValue(null)!;

                            ulongPartEquivs[cmd] = asULong;
                            ulongPartNames[cmd] = ulongStrPart;

                            uintPartEquivs[cmd] = asUInt;
                            uintPartNames[cmd] = uintStrPart;
                        }

                        //var (bitChurn0, bitChurn1, uShortIndex0, uShortIndex1, mod) = FindBestModULongUInt(ulongPartEquivs, uintPartEquivs);


                        var next = FindBestModGeneric(uintPartEquivs, [ulongPartEquivs]);
                        if (next is not null)
                        {
                            Console.WriteLine($"[{DateTime.UtcNow:u}] Simple works for {len}");
                            Console.WriteLine($"[{DateTime.UtcNow:u}]\t{next.Value}");
                        }
                        else
                        {
                            Console.WriteLine($"[{DateTime.UtcNow:u}] Simple DOES NOT work for {len}");
                            Environment.Exit(-1);
                        }

                        var (maskCommonBits, includeLongHighBits, bitChurn, ulongShifts, mod) = next.Value;

                        var map = new Dictionary<byte, RespCommand>();
                        foreach (var cmd in cmds)
                        {
                            var ulongVal = ulongPartEquivs[cmd];
                            var uintVal = uintPartEquivs[cmd];

                            if (maskCommonBits)
                            {
                                uintVal &= ~(0b0110_0000_0110_0000__0110_0000_0110_0000U);
                                ulongVal &= ~(0b0110_0000_0110_0000__0110_0000_0110_0000__0110_0000_0110_0000__0110_0000_0110_0000UL);
                            }

                            var interimVal = uintVal;

                            interimVal ^= (uint)(ulongVal >> ulongShifts[0]);

                            if (includeLongHighBits)
                            {
                                interimVal ^= (uint)(ulongVal >> (32 - ulongShifts[0]));
                            }

                            interimVal ^= bitChurn;

                            var calculatedValue = (byte)(interimVal % mod);

                            map.Add(calculatedValue, cmd);
                        }

                        into.AppendLine($"    case {len}:");
                        into.AppendLine($"      var len{len}p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);");
                        into.AppendLine($"      var len{len}p1 = Unsafe.As<byte, uint>(ref Unsafe.Add(ref cmdStartRef, {tailOffset}));");
                        into.AppendLine();
                        if (maskCommonBits)
                        {
                            into.AppendLine($"      len{len}p0 &= 0b0110_0000_0110_0000__0110_0000_0110_0000__0110_0000_0110_0000__0110_0000_0110_0000UL;");
                            into.AppendLine($"      len{len}p1 &= ~0b0110_0000_0110_0000__0110_0000_0110_0000U;");
                        }
                        into.AppendLine($"      var len{len}Val = len{len}p1;");
                        into.AppendLine($"      len{len}Val ^= (uint)(len{len}p0 >> {ulongShifts[0]});");
                        if (includeLongHighBits)
                        {
                            into.AppendLine($"      len{len}Val ^= (uint)(len{len}p0 >> (32 - {ulongShifts[0]}));");
                        }
                        if (bitChurn != 0)
                        {
                            into.AppendLine($"      len{len}Val ^= {bitChurn}U;");
                        }
                        into.AppendLine($"      var len{len}Ix = (byte)(len{len}Val % {mod});");
                        into.AppendLine($"      ref var len{len}ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash{len}Lookup), len{len}Ix * 2);");
                        into.AppendLine();
                        into.AppendLine($"      var len{len}Val0 = len{len}ValueRef;");
                        into.AppendLine($"      var len{len}Val1 = Unsafe.Add(ref len{len}ValueRef, 1);");
                        into.AppendLine();
                        into.AppendLine($"      parsed = (RespCommand)len{len}Val0;");
                        into.AppendLine($"      if(len{len}Val1 == len{len}p0 && (uint)(len{len}Val0 >> 32) == len{len}p1)");
                        into.AppendLine("      {");
                        into.AppendLine("        return true;");
                        into.AppendLine("      }");
                        into.AppendLine("    break;");

                        var decl = new StringBuilder();
                        decl.Append($"private static readonly ulong[] Hash{len}Lookup = [");
                        for (var val = 0; val <= mod; val++)
                        {
                            if (!map.TryGetValue((byte)val, out var cmd))
                            {
                                decl.Append("0, 0,");
                                continue;
                            }

                            var uintPart = uintPartNames[cmd];
                            var ulongPart = ulongPartNames[cmd];

                            decl.Append($"((ulong){uintPart} << 32) | (ulong)RespCommand.{cmd}, {ulongPart}, ");
                        }
                        decl.Append("];");
                        lookupDecls.Add(decl.ToString());
                    }
                    else if (len <= 16)
                    {
                        // len == 13, tailOffset = 5
                        // len == 14, tailOffset = 6
                        // len == 15, tailOffset = 7
                        // len == 16, tailOffset = 8
                        var tailOffset = 8 - (16 - len);

                        var ulong0PartEquivs = new Dictionary<RespCommand, ulong>();
                        var ulong0PartNames = new Dictionary<RespCommand, string>();

                        var ulong1PartEquivs = new Dictionary<RespCommand, ulong>();
                        var ulong1PartNames = new Dictionary<RespCommand, string>();
                        foreach (var cmd in cmds)
                        {
                            var ulong0StrPart = cmd.ToString().ToUpperInvariant()[..8];
                            var ulong1StrPart = cmd.ToString()[tailOffset..];

                            var field0 = typeof(RespParser).GetField(ulong0StrPart, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                            var asULong0 = (ulong)field0!.GetValue(null)!;

                            var field1 = typeof(RespParser).GetField(ulong1StrPart, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                            var asULong1 = (ulong)field1!.GetValue(null)!;

                            ulong0PartEquivs[cmd] = asULong0;
                            ulong0PartNames[cmd] = ulong0StrPart;

                            ulong1PartEquivs[cmd] = asULong1;
                            ulong1PartNames[cmd] = ulong1StrPart;
                        }

                        //var (bitChurn0, bitChurn1, uShortIndex0, uShortIndex1, mod) = FindBestModULongULong(ulong0PartEquivs, ulong1PartEquivs);

                        var next = FindBestModGeneric(null, [ulong0PartEquivs, ulong1PartEquivs]);
                        if (next is not null)
                        {
                            Console.WriteLine($"[{DateTime.UtcNow:u}] Simple works for {len}");
                            Console.WriteLine($"[{DateTime.UtcNow:u}]\t{next.Value}");
                        }
                        else
                        {
                            Console.WriteLine($"[{DateTime.UtcNow:u}] Simple DOES NOT work for {len}");
                            Environment.Exit(-1);
                        }

                        var (maskCommonBits, includeLongHighBits, bitChurn, ulongShifts, mod) = next.Value;

                        var map = new Dictionary<byte, RespCommand>();
                        foreach (var cmd in cmds)
                        {
                            var ulong0Val = ulong0PartEquivs[cmd];
                            var ulong1Val = ulong1PartEquivs[cmd];

                            if (maskCommonBits)
                            {
                                ulong0Val &= ~(0b0110_0000_0110_0000__0110_0000_0110_0000__0110_0000_0110_0000__0110_0000_0110_0000UL);
                                ulong1Val &= ~(0b0110_0000_0110_0000__0110_0000_0110_0000__0110_0000_0110_0000__0110_0000_0110_0000UL);
                            }

                            var val = (uint)(ulong0Val >> ulongShifts[0]);
                            val ^= (uint)(ulong1Val >> ulongShifts[1]);

                            if (includeLongHighBits)
                            {
                                val ^= (uint)(ulong0Val >> (32 - ulongShifts[0]));
                                val ^= (uint)(ulong1Val >> (32 - ulongShifts[1]));
                            }

                            if (bitChurn != 0)
                            {
                                val ^= bitChurn;
                            }

                            var calculatedValue = (byte)(val % mod);

                            map.Add(calculatedValue, cmd);
                        }

                        into.AppendLine($"    case {len}:");
                        into.AppendLine($"      var len{len}p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);");
                        into.AppendLine($"      var len{len}p1 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, {tailOffset}));");

                        if (maskCommonBits)
                        {
                            into.AppendLine($"      len{len}p0 &= ~(0b0110_0000_0110_0000__0110_0000_0110_0000__0110_0000_0110_0000__0110_0000_0110_0000UL);");
                            into.AppendLine($"      len{len}p1 &= ~(0b0110_0000_0110_0000__0110_0000_0110_0000__0110_0000_0110_0000__0110_0000_0110_0000UL);");
                        }

                        into.AppendLine($"      var len{len}Val = (uint)(len{len}p0 >> {ulongShifts[0]});");
                        into.AppendLine($"      len{len}Val ^= (uint)(len{len}p1 >> {ulongShifts[1]});");

                        if (includeLongHighBits)
                        {
                            into.AppendLine($"      len{len}Val ^= (uint)(len{len}p0 >> (32 - {ulongShifts[0]}));");
                            into.AppendLine($"      len{len}Val ^= (uint)(len{len}p1 >> (32 - {ulongShifts[1]}));");
                        }

                        if (bitChurn != 0)
                        {
                            into.AppendLine($"      len{len}Val ^= {bitChurn}U;");
                        }

                        into.AppendLine($"      var len{len}Ix = (byte)(len{len}Val % {mod});");
                        into.AppendLine($"      ref var len{len}ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash{len}Lookup), len{len}Ix * 3);");
                        into.AppendLine();
                        into.AppendLine($"      var len{len}Val0 = len{len}ValueRef;");
                        into.AppendLine($"      var len{len}Val1 = Unsafe.Add(ref len{len}ValueRef, 1);");
                        into.AppendLine($"      var len{len}Val2 = Unsafe.Add(ref len{len}ValueRef, 2);");
                        into.AppendLine();
                        into.AppendLine($"      parsed = (RespCommand)len{len}Val0;");
                        into.AppendLine($"      if(len{len}Val1 == len{len}p0 && len{len}Val2 == len{len}p1)");
                        into.AppendLine("      {");
                        into.AppendLine("        return true;");
                        into.AppendLine("      }");
                        into.AppendLine("    break;");

                        var decl = new StringBuilder();
                        decl.Append($"private static readonly ulong[] Hash{len}Lookup = [");
                        for (var val = 0; val <= mod; val++)
                        {
                            if (!map.TryGetValue((byte)val, out var cmd))
                            {
                                decl.Append("0, 0, 0,");
                                continue;
                            }

                            var ulong0Part = ulong0PartNames[cmd];
                            var ulong1Part = ulong1PartNames[cmd];

                            decl.Append($"(ulong)RespCommand.{cmd}, {ulong0Part}, {ulong1Part}, ");
                        }
                        decl.Append("];");
                        lookupDecls.Add(decl.ToString());
                    }
                    else
                    {
                        throw new NotImplementedException($"Not implemented for length = {len}, add it yourself?");
                    }
                }
            }

            static (uint BitChurn0, uint BitChurn1, int UShortIndex0, int UShortIndex1, byte Mod) FindBestModULongULong(Dictionary<RespCommand, ulong> ulong0Vals, Dictionary<RespCommand, ulong> ulong1Vals)
            {
                Span<byte> modBuff = stackalloc byte[4];

                var commands = ulong0Vals.Keys.Concat(ulong1Vals.Keys).Distinct().ToList();

                var uniqueValues = new HashSet<byte>();

                var sw = Stopwatch.StartNew();

                var bitChurn0 = 0U;
                var bitChurn1 = 0U;

                (uint BitChurn0, uint BitChurn1, int UIntIndex0, int UIntIndex1, byte Mod)? bestFit = null;

                while (bestFit is null || sw.ElapsedMilliseconds <= 60_000)
                {
                    // first pass through try 0

                    for (var mod = commands.Count; mod <= byte.MaxValue; mod++)
                    {
                        for (var ushortIndex0 = 0; ushortIndex0 < Vector128<ushort>.Count; ushortIndex0++)
                        {
                            for (var ushortIndex1 = 0; ushortIndex1 < Vector128<ushort>.Count; ushortIndex1++)
                            {
                                uniqueValues.Clear();

                                foreach (var k in commands)
                                {
                                    var ulong0Val = ulong0Vals[k];
                                    var ulong1Val = ulong1Vals[k];

                                    var asVec0 = Vector128.Create((uint)(ulong0Val ^ ulong1Val));
                                    asVec0 = asVec0.WithElement(2, (uint)(ulong0Val ^ ulong1Val ^ bitChurn0));
                                    var asVec1 = Vector128.Create((uint)((ulong0Val ^ ulong1Val) >> 32));
                                    asVec1 = asVec1.WithElement(2, (uint)(((ulong0Val ^ ulong1Val) >> 32) ^ bitChurn1));
                                    var mixedBytes0 = System.Runtime.Intrinsics.X86.Aes.InverseMixColumns(asVec0.AsByte());
                                    var mixedBytes1 = System.Runtime.Intrinsics.X86.Aes.InverseMixColumns(asVec1.AsByte());

                                    var mixedUShorts0 = mixedBytes0.AsUInt16();
                                    var mixedUShorts1 = mixedBytes1.AsUInt16();

                                    var calculatedValue = (byte)((mixedUShorts0.GetElement(ushortIndex0) + mixedUShorts1.GetElement(ushortIndex1)) % (byte)mod);

                                    if (!uniqueValues.Add(calculatedValue))
                                    {
                                        break;
                                    }
                                }

                                if (uniqueValues.Count == commands.Count)
                                {
                                    if (bestFit is null || bestFit.Value.Mod > mod)
                                    {
                                        bestFit = (bitChurn0, bitChurn1, ushortIndex0, ushortIndex1, (byte)mod);
                                    }
                                }
                            }
                        }
                    }

                    Random.Shared.NextBytes(modBuff);
                    bitChurn0 = MemoryMarshal.Read<uint>(modBuff);

                    Random.Shared.NextBytes(modBuff);
                    bitChurn1 = MemoryMarshal.Read<uint>(modBuff);
                }

                return bestFit.Value;
            }

            static (uint BitChurn0, uint BitChurn1, int UShortIndex0, int UShortIndex1, byte Mod) FindBestModULongUInt(Dictionary<RespCommand, ulong> ulongVals, Dictionary<RespCommand, uint> uintVals)
            {
                Span<byte> modBuff = stackalloc byte[4];

                var commands = ulongVals.Keys.Concat(uintVals.Keys).Distinct().ToList();

                var uniqueValues = new HashSet<byte>();

                var sw = Stopwatch.StartNew();

                var bitChurn0 = 0U;
                var bitChurn1 = 0U;

                (uint BitChurn0, uint BitChurn1, int UIntIndex0, int UIntIndex1, byte Mod)? bestFit = null;

                while (bestFit is null || sw.ElapsedMilliseconds <= 60_000)
                {
                    // first pass through try 0

                    for (var mod = commands.Count; mod <= byte.MaxValue; mod++)
                    {
                        for (var ushortIndex0 = 0; ushortIndex0 < Vector128<ushort>.Count; ushortIndex0++)
                        {
                            for (var ushortIndex1 = 0; ushortIndex1 < Vector128<ushort>.Count; ushortIndex1++)
                            {
                                uniqueValues.Clear();

                                foreach (var k in commands)
                                {
                                    var ulongVal = ulongVals[k];
                                    var uintVal = uintVals[k];

                                    var asVec0 = Vector128.Create((uint)ulongVal ^ uintVal);
                                    asVec0 = asVec0.WithElement(2, (uint)(ulongVal ^ uintVal ^ bitChurn0));
                                    var asVec1 = Vector128.Create((uint)((ulongVal >> 32) ^ uintVal));
                                    asVec1 = asVec1.WithElement(2, (uint)((ulongVal >> 32) ^ uintVal ^ bitChurn1));
                                    var mixedBytes0 = System.Runtime.Intrinsics.X86.Aes.InverseMixColumns(asVec0.AsByte());
                                    var mixedBytes1 = System.Runtime.Intrinsics.X86.Aes.InverseMixColumns(asVec1.AsByte());

                                    var mixedUShorts0 = mixedBytes0.AsUInt16();
                                    var mixedUShorts1 = mixedBytes1.AsUInt16();

                                    var calculatedValue = (byte)((mixedUShorts0.GetElement(ushortIndex0) + mixedUShorts1.GetElement(ushortIndex1)) % (byte)mod);

                                    if (!uniqueValues.Add(calculatedValue))
                                    {
                                        break;
                                    }
                                }

                                if (uniqueValues.Count == commands.Count)
                                {
                                    if (bestFit is null || bestFit.Value.Mod > mod)
                                    {
                                        bestFit = (bitChurn0, bitChurn1, ushortIndex0, ushortIndex1, (byte)mod);
                                    }
                                }
                            }
                        }
                    }

                    Random.Shared.NextBytes(modBuff);
                    bitChurn0 = MemoryMarshal.Read<uint>(modBuff);

                    Random.Shared.NextBytes(modBuff);
                    bitChurn1 = MemoryMarshal.Read<uint>(modBuff);
                }

                return bestFit.Value;
            }

            static (uint BitChurn0, uint BitChurn1, int UShortIndex0, int UShortIndex1, byte Mod) FindBestModULong(Dictionary<RespCommand, ulong> vals)
            {
                Span<byte> modBuff = stackalloc byte[4];

                var uniqueValues = new HashSet<byte>();

                var sw = Stopwatch.StartNew();

                var bitChurn0 = 0U;
                var bitChurn1 = 0U;

                (uint BitChurn0, uint BitChurn1, int UIntIndex0, int UIntIndex1, byte Mod)? bestFit = null;

                while (bestFit is null || sw.ElapsedMilliseconds <= 60_000)
                {
                    // first pass through try 0

                    for (var mod = vals.Count; mod <= byte.MaxValue; mod++)
                    {
                        for (var ushortIndex0 = 0; ushortIndex0 < Vector128<ushort>.Count; ushortIndex0++)
                        {
                            for (var ushortIndex1 = 0; ushortIndex1 < Vector128<ushort>.Count; ushortIndex1++)
                            {
                                uniqueValues.Clear();

                                foreach (var v in vals)
                                {
                                    var asVec0 = Vector128.Create((uint)v.Value);
                                    asVec0 = asVec0.WithElement(2, (uint)(v.Value ^ bitChurn0));
                                    var asVec1 = Vector128.Create((uint)(v.Value >> 32));
                                    asVec1 = asVec1.WithElement(2, (uint)((v.Value >> 32) ^ bitChurn1));
                                    var mixedBytes0 = System.Runtime.Intrinsics.X86.Aes.InverseMixColumns(asVec0.AsByte());
                                    var mixedBytes1 = System.Runtime.Intrinsics.X86.Aes.InverseMixColumns(asVec1.AsByte());

                                    var mixedUShorts0 = mixedBytes0.AsUInt16();
                                    var mixedUShorts1 = mixedBytes1.AsUInt16();

                                    var calculatedValue = (byte)((mixedUShorts0.GetElement(ushortIndex0) + mixedUShorts1.GetElement(ushortIndex1)) % (byte)mod);

                                    if (!uniqueValues.Add(calculatedValue))
                                    {
                                        break;
                                    }
                                }

                                if (uniqueValues.Count == vals.Count)
                                {
                                    if (bestFit is null || bestFit.Value.Mod > mod)
                                    {
                                        bestFit = (bitChurn0, bitChurn1, ushortIndex0, ushortIndex1, (byte)mod);
                                    }
                                }
                            }
                        }
                    }

                    Random.Shared.NextBytes(modBuff);
                    bitChurn0 = MemoryMarshal.Read<uint>(modBuff);

                    Random.Shared.NextBytes(modBuff);
                    bitChurn1 = MemoryMarshal.Read<uint>(modBuff);
                }

                return bestFit.Value;
            }

            static (uint BitChurn, int UShortIndex, byte Mod) FindBestModUInt(Dictionary<RespCommand, uint> vals)
            {
                Span<byte> modBuff = stackalloc byte[4];

                var uniqueValues = new HashSet<byte>();

                var sw = Stopwatch.StartNew();

                var bitChurn = 0U;

                (uint BitChurn, int UShortIndex, byte Mod)? bestFit = null;

                while (bestFit is null || sw.ElapsedMilliseconds <= 60_000)
                {
                    // first pass through try 0

                    for (var mod = vals.Count; mod <= byte.MaxValue; mod++)
                    {
                        for (var ushortIndex = 0; ushortIndex < Vector128<ushort>.Count; ushortIndex++)
                        {

                            uniqueValues.Clear();

                            foreach (var v in vals)
                            {
                                var asVec = Vector128.Create(v.Value);
                                asVec = asVec.WithElement(2, v.Value ^ bitChurn);
                                var mixedBytes = System.Runtime.Intrinsics.X86.Aes.InverseMixColumns(asVec.AsByte());
                                var mixedShorts = mixedBytes.AsUInt16();

                                var calculatedValue = (byte)(mixedShorts.GetElement(ushortIndex) % (byte)mod);

                                if (!uniqueValues.Add(calculatedValue))
                                {
                                    break;
                                }
                            }

                            if (uniqueValues.Count == vals.Count)
                            {
                                if (bestFit is null || bestFit.Value.Mod > mod)
                                {
                                    bestFit = (bitChurn, ushortIndex, (byte)mod);
                                }
                            }
                        }
                    }

                    Random.Shared.NextBytes(modBuff);
                    bitChurn = MemoryMarshal.Read<uint>(modBuff);
                }

                return bestFit.Value;
            }

            static (bool MaskCommonBits, bool IncludeULongHighBits, uint BitChurn, byte[] ULongShifts, byte Mod)? FindBestModGeneric(Dictionary<RespCommand, uint>? uintVals, Dictionary<RespCommand, ulong>[] ulongVals)
            {
                var cmds = (uintVals ?? []).Keys.Concat(ulongVals.SelectMany(static x => x.Keys)).Distinct().ToList();
                var minMod = cmds.Count;

                var shifts = new byte[ulongVals.Length];

                Span<byte> uintBuff = stackalloc byte[4];
                uintBuff.Clear();

                var uniqueValues = new HashSet<byte>();

                (bool MaskCommonBits, bool IncludeULongHighBits, uint BitChurn, byte[] ULongShifts, byte Mod)? best = null;

                var sw = Stopwatch.StartNew();

                var maskOpts = new bool[] { false, true };
                var includeHigh = ulongVals.Length > 0 ? new bool[] { false, true } : [false];

                while (sw.ElapsedMilliseconds <= 60_000)
                {
                    for (var mod = minMod; mod <= byte.MaxValue; mod++)
                    {
                        // first pass through, no churn

                        shifts.AsSpan().Clear();

                        foreach (var maskCommonBits in maskOpts)
                        {
                            var bitChurn = BinaryPrimitives.ReadUInt32LittleEndian(uintBuff);

                            foreach (var include in includeHigh)
                            {

                                do
                                {
                                    uniqueValues.Clear();

                                    foreach (var cmd in cmds)
                                    {
                                        var val = 0U;
                                        if (uintVals is not null)
                                        {
                                            val = uintVals[cmd];

                                            if (maskCommonBits)
                                            {
                                                val &= ~(0b0110_0000_0110_0000__0110_0000_0110_0000U);
                                            }
                                        }

                                        for (var i = 0; i < ulongVals.Length; i++)
                                        {
                                            var ulongVal = ulongVals[i][cmd];

                                            if (maskCommonBits)
                                            {
                                                ulongVal &= ~(0b0110_0000_0110_0000__0110_0000_0110_0000__0110_0000_0110_0000__0110_0000_0110_0000UL);
                                            }

                                            ulong processedULong = ulongVal >> shifts[i];

                                            if (include)
                                            {
                                                processedULong ^= (ulongVal >> (32 - shifts[i]));
                                            }

                                            val ^= (uint)processedULong;
                                        }

                                        val ^= bitChurn;

                                        var finalValue = (byte)(val % mod);

                                        if (!uniqueValues.Add(finalValue))
                                        {
                                            break;
                                        }
                                    }

                                    if (uniqueValues.Count == minMod)
                                    {
                                        if (best is null || IsBetter(best.Value, (maskCommonBits, include, bitChurn, shifts, (byte)mod)))
                                        {
                                            best = (maskCommonBits, include, bitChurn, shifts.ToArray(), (byte)mod);
                                        }
                                    }
                                } while (TryAdvanceShift(shifts));
                            }
                        }

                        Random.Shared.NextBytes(uintBuff);
                    }
                }

                return best;

                static bool IsBetter(
                    (bool MaskCommonBits, bool IncludeULongHighBits, uint BitChurn, byte[] ULongShifts, byte Mod) cur,
                    (bool MaskCommonBits, bool IncludeULongHighBits, uint BitChurn, byte[] ULongShifts, byte Mod) cand
                )
                {
                    var curZeros = cur.ULongShifts.Count(static x => x == 0) + (cur.BitChurn == 0 ? 1 : 0);
                    var candZeros = cand.ULongShifts.Count(static x => x == 0) + (cand.BitChurn == 0 ? 1 : 0);

                    if (candZeros > curZeros)
                    {
                        return true;
                    }

                    if (BitOperations.IsPow2(cand.Mod) && !BitOperations.IsPow2(cur.Mod))
                    {
                        return true;
                    }

                    if (!cand.MaskCommonBits && cur.MaskCommonBits)
                    {
                        return true;
                    }

                    if (!cand.IncludeULongHighBits && cur.IncludeULongHighBits)
                    {
                        return true;
                    }

                    if (cand.Mod < cur.Mod)
                    {
                        return true;
                    }

                    return false;
                }

                static bool TryAdvanceShift(byte[] shifts)
                {
                    var ix = 0;

                    while (ix < shifts.Length)
                    {
                        shifts[ix]++;
                        if (shifts[ix] > 32)
                        {
                            shifts[ix] = 0;
                            ix++;
                            continue;
                        }

                        return true;
                    }

                    return false;
                }
            }
        }
    }
}
