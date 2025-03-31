using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace OptimizationExercise.SIMDRespParsing
{
    /// <summary>
    /// The basic idea is to try and parse a whole bunch of commands at once.
    /// 
    /// We exploit SIMD to quickly parse these commands.
    /// </summary>
    public static class RespParser
    {
        /// <summary>
        /// Attempt to parse all commands in <paramref name="commandBuffer"/>.
        /// 
        /// Incomplete commands are ignored (ie. not errors), malformed commands terminate parsing and result in a malformed entry in <paramref name="intoCommands"/>.
        /// 
        /// Valid commands before incomplete or malformed ones are fully parsed and placed in <paramref name="intoCommands"/>.
        /// </summary>
        public static void Parse(
            Span<byte> commandBuffer,
            Span<ParsedRespCommandOrArgument> intoCommands,
            out int intoCommandsSlotsUsed,
            out int bytesConsumed
        )
        {
            Debug.Assert(commandBuffer.Length > 0);
            Debug.Assert(BitConverter.IsLittleEndian);
            Debug.Assert(!intoCommands.IsEmpty);

            var bitmapLength = (commandBuffer.Length / 8) + 1;
            Span<byte> asteriks = stackalloc byte[bitmapLength];
            Span<byte> dollars = stackalloc byte[bitmapLength];
            Span<byte> crs = stackalloc byte[bitmapLength];
            Span<byte> lfs = stackalloc byte[bitmapLength];

            ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);

            // reuse crs to reduce cache pressure
            var crLfs = crs;
            CombineCrLf_SIMD(crs, lfs, crLfs);

            var remainingAsteriks = (ReadOnlySpan<byte>)asteriks;
            var remainingDollars = (ReadOnlySpan<byte>)dollars;
            var remainingCrLfs = (ReadOnlySpan<byte>)crLfs;

            var remainingInto = intoCommands;
            var totalParsed = 0;
            var lastByteConsumed = 0;

            var byteOffsetInCommandBuffer = 0;
            var bitOffsetInFirstBitmapByte = (byte)0;

            while (!remainingInto.IsEmpty && (byteOffsetInCommandBuffer + bitOffsetInFirstBitmapByte) < commandBuffer.Length)
            {
                var canContinueParsing = TryParseSingleCommand(commandBuffer, byteOffsetInCommandBuffer, bitOffsetInFirstBitmapByte, remainingInto, ref remainingAsteriks, ref remainingDollars, ref remainingCrLfs, out var slotsUsed);

                // remember slots consumed, even if we fail, as that's how we indicate malformed
                totalParsed += slotsUsed;
                if (!canContinueParsing)
                {
                    // we encountered a malformed command or a incomplete one, so we're done
                    break;
                }

                lastByteConsumed = remainingInto[slotsUsed - 1].ByteEnd;
                byteOffsetInCommandBuffer = (lastByteConsumed / 8) * 8;
                bitOffsetInFirstBitmapByte = (byte)(lastByteConsumed % 8);

                remainingInto = remainingInto[slotsUsed..];
            }

            intoCommandsSlotsUsed = totalParsed;
            bytesConsumed = lastByteConsumed;
        }

        /// <summary>
        /// Attempt to parse a single command.
        /// 
        /// <paramref name="remainingAsteriks"/>, etc. are all moved relatively forward.
        /// 
        /// <paramref name="byteStart"/> is the byte count into the command stream where <paramref name="remainingAsteriks"/> et al. are.
        /// <paramref name="bitStart"/> is the number of bits into <paramref name="remainingAsteriks"/> we should start scanning at.
        /// 
        /// Returns true if we should continue parsing more commands out of <paramref name="commandBuffer"/>.
        /// </summary>
        public static bool TryParseSingleCommand(
            Span<byte> commandBuffer,
            int byteStart,
            byte bitStart,
            Span<ParsedRespCommandOrArgument> parsed,
            ref ReadOnlySpan<byte> remainingAsteriks,
            ref ReadOnlySpan<byte> remainingDollars,
            ref ReadOnlySpan<byte> remainingCrLfs,
            out int slotsUsed
        )
        {
            Debug.Assert(!remainingAsteriks.IsEmpty);
            Debug.Assert(remainingAsteriks.Length == remainingDollars.Length);
            Debug.Assert(remainingDollars.Length == remainingCrLfs.Length);
            Debug.Assert(bitStart < 8);
            Debug.Assert(parsed.Length >= 1);

            // worked example (remember, little endian)
            // commandBuffer = *1\r\n$12\r\nabcdefghijkl\r\n (len = 23)
            // byteStart = 0
            // bitStart = 1
            // parsed = [-,-]
            // remainingAsteriks = [0000_0001, 0000_0000, 0000_0000] (len = [23 / 8] + 1 = 3)
            // remainingDollars  = [0001_0000, 0000_0000, 0000_0000] (len = 3)
            // remainingCrlfs    = [1000_0100, 0000_0000, 0010_0000] (len = 3)

            // remainingSpaceInCommandBuffer = 23
            var remainingSpaceInCommandBuffer = commandBuffer.Length - (byteStart + bitStart);

            // *1\r\n$1\r\na\r\n
            if (remainingSpaceInCommandBuffer < 11)
            {
                // insufficient data for another command, stop advancing
                slotsUsed = 0;
                return false;
            }

            // hasExpectedAsteriks = 0000_0001 & 0000_0001 = 1
            var hasExpectedAsteriks = (remainingAsteriks[0] & (1 << bitStart)) != 0;
            if (!hasExpectedAsteriks)
            {
                // next command is malformed, stop advancing
                parsed[0] = ParsedRespCommandOrArgument.Malformed;
                slotsUsed = 1;
                return false;
            }

            // arrayEndingCrLf = FindNext24(0, [1000_0100, ...]) = 2
            var arrayEndingCrLf = FindNext24(bitStart, remainingCrLfs);
            if (arrayEndingCrLf == -1)
            {
                // *<11 digits>
                if (remainingSpaceInCommandBuffer >= 12)
                {
                    // if we haven't found a terminator yet, it doesn't matter if one comes in later, we're malformed
                    parsed[0] = ParsedRespCommandOrArgument.Malformed;
                    slotsUsed = 1;
                    return false;
                }

                // more data might make this valid
                slotsUsed = 0;
                return false;
            }

            // arayItemCountBytes = commandBuffer([1..2]) = 1
            var arrayItemCountBytes = commandBuffer[(byteStart + bitStart + 1)..(arrayEndingCrLf + byteStart)];
            if (!TryParsePositiveInt(arrayItemCountBytes, out var arrayItemCount) || arrayItemCount <= 0)
            {
                // array count is invalid, no future parsing will succeed
                parsed[0] = ParsedRespCommandOrArgument.Malformed;
                slotsUsed = 1;
                return false;
            }

            // arrayItemCount = 1
            if (arrayItemCount + 1 > parsed.Length)
            {
                // insufficient space in parsed for this command
                slotsUsed = 0;
                return false;
            }

            var cmdStart = byteStart + bitStart;
            var cmdEnd = byteStart + arrayEndingCrLf;

            if (!TryParseCommand_Hash2(commandBuffer, cmdStart, cmdEnd, out var cmdEnum))
            {
                cmdEnum = RespCommand.Invalid;
            }

            // parsed = [command(1, 0, 4), -] = [*1\r\n, -]
            // command is parsed
            parsed[0] = ParsedRespCommandOrArgument.ForCommand(cmdEnum, arrayItemCount, cmdStart, cmdEnd + 2); // + 2 for the \r\n

            // bitStart = bitStart + 2 + 2 = 4
            // byteStart = 0
            // no changes for remainingXXX
            // remainingAsteriks = [0000_0001, 0000_0000, 0000_0000] (len = [23 / 8] + 1 = 3)
            // remainingDollars  = [0001_0000, 0000_0000, 0000_0000] (len = 3)
            // remainingCrlfs    = [1000_0100, 0000_0000, 0010_0000] (len = 3)
            // advance past the array item length (+2 for the \r\n, -bitStart because that's not included in Find results)
            Advance((arrayEndingCrLf + 2) - bitStart, ref byteStart, ref bitStart, ref remainingAsteriks, ref remainingDollars, ref remainingCrLfs);

            // arrayItemCount = 1
            for (var i = 0; i < arrayItemCount; i++)
            {
                var remainingSpaceForArg = commandBuffer.Length - (byteStart + bitStart);

                // $1\r\na\r\n
                if (remainingSpaceForArg < 7)
                {
                    // insufficient space, need to wait for more data
                    slotsUsed = 0;
                    return false;
                }

                // hasExpecteDollar = 0001_0000 & 0001_0000 = 16
                var hasExpectedDollar = (remainingDollars[0] & (1 << bitStart)) != 0;
                if (!hasExpectedDollar)
                {
                    // argument is malformed, future parsing will fail
                    parsed[i + 1] = ParsedRespCommandOrArgument.Malformed;
                    slotsUsed = i + 2;
                    return false;
                }

                // bulkStringEndingCrLf = FindNext24(4, [1000_0100, ...]) = 7
                var bulkStringEndingCrLf = FindNext24(bitStart, remainingCrLfs);
                if (bulkStringEndingCrLf == -1)
                {
                    // *<11 digits>
                    if (remainingSpaceForArg >= 12)
                    {
                        // if we haven't found a terminator yet, it doesn't matter if one comes in later, we're malformed
                        parsed[i + 1] = ParsedRespCommandOrArgument.Malformed;
                        slotsUsed = i + 2;
                        return false;
                    }

                    // more data might bring in a terminator
                    slotsUsed = 0;
                    return false;
                }

                // argLengthBytes = commandBuffer[5..7] = 12
                var argLengthBytes = commandBuffer[(byteStart + bitStart + 1)..(byteStart + bulkStringEndingCrLf)];
                if (!TryParsePositiveInt(argLengthBytes, out var argLength) || argLength <= 0)
                {
                    // arg length is invalid, no future parsing will succeed
                    parsed[i + 1] = ParsedRespCommandOrArgument.Malformed;
                    slotsUsed = i + 2;
                    return false;
                }

                // argLength = 12

                // Advance(5, ...)
                // bitStart = 4 + 5 = 9 = 1
                // byteStart = 8
                // remainingAsteriks = [0000_0000, 0000_0000] (len = 2)
                // remainingDollars  = [0000_0000, 0000_0000] (len = 2)
                // remainingCrLfs    = [0000_0000, 0010_0000] (len = 2)
                // advance past the bulk string length (+ 2 for the \r\n, - bitStart because it's not included in the Find)
                Advance((bulkStringEndingCrLf + 2) - bitStart, ref byteStart, ref bitStart, ref remainingAsteriks, ref remainingDollars, ref remainingCrLfs);

                // *1rn$12rn
                // argStart = 8 + 1   = (should be) 9
                // argEnd   = 9 + 12 = 21
                // argBytes = abcdefghijkl
                var argStart = byteStart + bitStart;
                var argEnd = argStart + argLength;

                // 23 <= 23
                if (argEnd + 2 > commandBuffer.Length)
                {
                    // not enough space for terminator, wait for more data
                    slotsUsed = 0;
                    return false;
                }

                // argLength + bitStart = 12 + 1 = 13
                // terminatingCrLfExpctedByteIndex = 1
                // terminatingCrLfExpectedBitIndex = 5
                var terminatingCrLfExpctedByteIndex = (argLength + bitStart) / 8;
                var terminatingCrLfExpectedBitIndex = (argLength + bitStart) % 8;

                // remainingCrLfs[1] & (1 << 5) = 0010_0000 & 0010_0000 = 32
                var hasTerminatingCrLf = (remainingCrLfs[terminatingCrLfExpctedByteIndex] & (1 << terminatingCrLfExpectedBitIndex)) != 0;

                if (!hasTerminatingCrLf)
                {
                    // Argument is malformed, terminator not found where expected
                    parsed[i + 1] = ParsedRespCommandOrArgument.Malformed;
                    slotsUsed = i + 2;
                    return false;
                }

                // parsed = [*1\r\n, abcdefghijkl\r\n]
                parsed[i + 1] = ParsedRespCommandOrArgument.ForArgument(argStart, argEnd + 2);

                // Advance(13, ...)
                // bitStart = 1 + 13 = 14 = 6
                // byteStart = 16
                // remainingAsteriks = [0000_0000] (len = 1)
                // remainingDollars  = [0000_0000] (len = 1)
                // remainingCrLfs    = [0010_0000] (len = 1)
                // advance past the bulk string value (+2 for the \r\n, no -bitStart because we don't have a Find call to include it implicitly)
                Advance(argLength + 2, ref byteStart, ref bitStart, ref remainingAsteriks, ref remainingDollars, ref remainingCrLfs);
            }

            // whole command was present, so we can continue onto the next one
            slotsUsed = arrayItemCount + 1;
            return true;
        }

        /// <summary>
        /// Move some number of characters forward in the bitmaps.
        /// 
        /// After the call <paramref name="byteStart"/> is the number of bytes IN THE COMMAND BUFFER that are skipped before the remainingXXX
        /// params start.
        /// 
        /// After the call <paramref name="bitStart"/> is the number of bits IN THE REMAININGXXX PARAMS that should be ignored for future searches.
        /// 
        /// After the call, all the remainingXXX params are advances such index 0 contains at least one relevant bit still.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Advance(
            int charCount,
            ref int byteStart,
            ref byte bitStart,
            ref ReadOnlySpan<byte> remainingAsteriks,
            ref ReadOnlySpan<byte> remainingDollars,
            ref ReadOnlySpan<byte> remaininingCrLfs
        )
        {
            var advanceSpansBy = charCount / 8;
            bitStart += (byte)(charCount % 8);

            advanceSpansBy += bitStart / 8;
            bitStart = (byte)(bitStart % 8);

            byteStart += advanceSpansBy * 8;

            remainingAsteriks = remainingAsteriks[advanceSpansBy..];
            remainingDollars = remainingDollars[advanceSpansBy..];
            remaininingCrLfs = remaininingCrLfs[advanceSpansBy..];
        }

        /// <summary>
        /// Scan in bitmap for next set bit, but looks at most 24 bits ahead.
        /// 
        /// This is fine, because we only ever need to look 10 past our current position,
        /// and <paramref name="bitsToSkip"/> should never exceed 8.
        /// </summary>
        public static int FindNext24(byte bitsToSkip, ReadOnlySpan<byte> bitmap)
        {
            Debug.Assert(bitsToSkip < 8);

            if (bitmap.Length >= sizeof(uint))
            {
                var asUInt = Unsafe.As<byte, uint>(ref MemoryMarshal.GetReference(bitmap));
                var mask = (1U << bitsToSkip) - 1;

                asUInt &= ~mask;

                var count = BitOperations.TrailingZeroCount(asUInt);
                if (count == 32)
                {
                    // no need to fall through, we're done looking
                    return -1;
                }

                return count;
            }

            var skipped = 0;

            if (bitmap.Length >= sizeof(ushort))
            {
                var asUShort = Unsafe.As<byte, ushort>(ref MemoryMarshal.GetReference(bitmap));
                var mask = (ushort)(1U << bitsToSkip) - 1;

                asUShort &= (ushort)~mask;

                var count = BitOperations.TrailingZeroCount(asUShort);
                if (count != 32)
                {
                    // if we found a bit, take it
                    return count;
                }

                // otherwise remember we skipped and move further into bitmap
                skipped += 16;

                bitsToSkip = 0;
                bitmap = bitmap[sizeof(ushort)..];
            }

            if (!bitmap.IsEmpty)
            {
                var asByte = MemoryMarshal.GetReference(bitmap);
                var mask = (byte)((1 << bitsToSkip) - 1);

                asByte &= (byte)~mask;

                var count = BitOperations.TrailingZeroCount(asByte);
                if (count != 32)
                {
                    return skipped + count;
                }
            }

            return -1;
        }

        /// <summary>
        /// A version of <see cref="CombineCrLf_Scalar(ReadOnlySpan{byte}, ReadOnlySpan{byte}, Span{byte})"/> which attempts to use SIMD instructions.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CombineCrLf_SIMD(ReadOnlySpan<byte> crs, ReadOnlySpan<byte> lfs, Span<byte> crLfs)
        {
            Debug.Assert(crs.Length == lfs.Length);
            Debug.Assert(lfs.Length == crLfs.Length);

            // remainingCrs = [abcd_efgh, ..., abcd_efgh]
            var remainingCrs = crs;

            // remainingLfs = [abcd_efgh, ..., abcd_efgh]
            var remainingLfs = lfs;

            // remainingCrLfs = [...]
            var remainingCrLfs = crLfs;

            while (remainingCrs.Length >= Vector512<byte>.Count + 1)
            {
                var curCr = Vector512.LoadUnsafe(ref MemoryMarshal.GetReference(remainingCrs));

                var curLf = Vector512.LoadUnsafe(ref MemoryMarshal.GetReference(remainingLfs));

                // if curLf = [abcd_efgh, ...]
                // curLfElementsShiftedOneBitDown = [0abcd_efg, ...]
                var curLfElementsShiftedOneBitDown = Vector512.ShiftRightLogical(curLf, 1);

                // if curLf = [abcd_efgh, ijkl_mnop, ..., stuv_wxyz] [ABCD_...
                // curLfShiftedDownOneLane = [ijkl_mnop, ..., stuv_wxyz, ABCD_...]
                var curLfShiftedDownOneLane = Vector512.LoadUnsafe(ref Unsafe.Add(ref MemoryMarshal.GetReference(remainingLfs), 1));
                // curLfShiftedDownOneLaneLowBitsHigh = [p000_0000, ..., z000_0000, A000_0000]
                var curLfShiftedDownOneLaneLowBitsHigh = V512ShiftLeft7(curLfShiftedDownOneLane);

                var curLfAllBitsShiftedDown = Vector512.BitwiseOr(curLfElementsShiftedOneBitDown, curLfShiftedDownOneLaneLowBitsHigh);

                var curCrLf = Vector512.BitwiseAnd(curCr, curLfAllBitsShiftedDown);

                Vector512.StoreUnsafe(curCrLf, ref MemoryMarshal.GetReference(remainingCrLfs));

                remainingCrs = remainingCrs[Vector512<byte>.Count..];
                remainingLfs = remainingLfs[Vector512<byte>.Count..];
                remainingCrLfs = remainingCrLfs[Vector512<byte>.Count..];
            }

            if (remainingCrs.Length >= Vector256<byte>.Count + 1)
            {
                var curCr = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(remainingCrs));

                var curLf = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(remainingLfs));

                // if curLf = [abcd_efgh, ...]
                // curLfElementsShiftedOneBitDown = [0abcd_efg, ...]
                var curLfElementsShiftedOneBitDown = Vector256.ShiftRightLogical(curLf, 1);

                // if curLf = [abcd_efgh, ijkl_mnop, ..., stuv_wxyz] [ABCD_...
                // curLfShiftedDownOneLane = [ijkl_mnop, ..., stuv_wxyz, ABCD_...]
                var curLfShiftedDownOneLane = Vector256.LoadUnsafe(ref Unsafe.Add(ref MemoryMarshal.GetReference(remainingLfs), 1));
                // curLfShiftedDownOneLaneLowBitsHigh = [p000_0000, ..., z000_0000, A000_0000]
                var curLfShiftedDownOneLaneLowBitsHigh = V256ShiftLeft7(curLfShiftedDownOneLane);

                var curLfAllBitsShiftedDown = Vector256.BitwiseOr(curLfElementsShiftedOneBitDown, curLfShiftedDownOneLaneLowBitsHigh);

                var curCrLf = Vector256.BitwiseAnd(curCr, curLfAllBitsShiftedDown);

                Vector256.StoreUnsafe(curCrLf, ref MemoryMarshal.GetReference(remainingCrLfs));

                remainingCrs = remainingCrs[Vector256<byte>.Count..];
                remainingLfs = remainingLfs[Vector256<byte>.Count..];
                remainingCrLfs = remainingCrLfs[Vector256<byte>.Count..];
            }

            if (remainingCrs.Length >= Vector128<byte>.Count + 1)
            {
                var curCr = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(remainingCrs));

                var curLf = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(remainingLfs));

                // if curLf = [abcd_efgh, ...]
                // curLfElementsShiftedOneBitDown = [0abcd_efg, ...]
                var curLfElementsShiftedOneBitDown = Vector128.ShiftRightLogical(curLf, 1);

                // if curLf = [abcd_efgh, ijkl_mnop, ..., stuv_wxyz] [ABCD_...
                // curLfShiftedDownOneLane = [ijkl_mnop, ..., stuv_wxyz, ABCD_...]
                var curLfShiftedDownOneLane = Vector128.LoadUnsafe(ref Unsafe.Add(ref MemoryMarshal.GetReference(remainingLfs), 1));
                // curLfShiftedDownOneLaneLowBitsHigh = [p000_0000, ..., z000_0000, A000_0000]
                var curLfShiftedDownOneLaneLowBitsHigh = V128ShiftLeft7(curLfShiftedDownOneLane);

                var curLfAllBitsShiftedDown = Vector128.BitwiseOr(curLfElementsShiftedOneBitDown, curLfShiftedDownOneLaneLowBitsHigh);

                var curCrLf = Vector128.BitwiseAnd(curCr, curLfAllBitsShiftedDown);

                Vector128.StoreUnsafe(curCrLf, ref MemoryMarshal.GetReference(remainingCrLfs));

                remainingCrs = remainingCrs[Vector128<byte>.Count..];
                remainingLfs = remainingLfs[Vector128<byte>.Count..];
                remainingCrLfs = remainingCrLfs[Vector128<byte>.Count..];
            }

            if (!remainingCrs.IsEmpty)
            {
                // the rest we need to defer to scalar code
                CombineCrLf_Scalar(remainingCrs, remainingLfs, remainingCrLfs);
            }

            // Shifting surprisingly can cause decomposition into non-SIMD registers
            // 
            // Since we have a constant, just do it "slow" but in lanes
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static Vector512<byte> V512ShiftLeft7(Vector512<byte> a)
            {
                // shift is equal to *2,
                // so shifting 7 is *2*2*2*2*2*2*2 = 2^7

                // b = a + a = a * 2^1
                var b = Vector512.Add(a, a);
                // c = b + b = (a + a) + (a + a) = a * 2^2
                var c = Vector512.Add(b, b);
                // d = c + c = (b + b) + (b + b) = ((a + a) + (a + a)) + ((a + a) + (a + a)) = a * 2^3
                var d = Vector512.Add(c, c);
                // etc.
                var e = Vector512.Add(d, d);
                var f = Vector512.Add(e, e);
                var g = Vector512.Add(f, f);
                var h = Vector512.Add(g, g);

                return h;
            }

            // Shifting surprisingly can cause decomposition into non-SIMD registers
            // 
            // Since we have a constant, just do it "slow" but in lanes
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static Vector256<byte> V256ShiftLeft7(Vector256<byte> a)
            {
                // shift is equal to *2,
                // so shifting 7 is *2*2*2*2*2*2*2 = 2^7

                // b = a + a = a * 2^1
                var b = Vector256.Add(a, a);
                // c = b + b = (a + a) + (a + a) = a * 2^2
                var c = Vector256.Add(b, b);
                // d = c + c = (b + b) + (b + b) = ((a + a) + (a + a)) + ((a + a) + (a + a)) = a * 2^3
                var d = Vector256.Add(c, c);
                // etc.
                var e = Vector256.Add(d, d);
                var f = Vector256.Add(e, e);
                var g = Vector256.Add(f, f);
                var h = Vector256.Add(g, g);

                return h;
            }

            // Shifting surprisingly can cause decomposition into non-SIMD registers
            // 
            // Since we have a constant, just do it "slow" but in lanes
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static Vector128<byte> V128ShiftLeft7(Vector128<byte> a)
            {
                // shift is equal to *2,
                // so shifting 7 is *2*2*2*2*2*2*2 = 2^7

                // b = a + a = a * 2^1
                var b = Vector128.Add(a, a);
                // c = b + b = (a + a) + (a + a) = a * 2^2
                var c = Vector128.Add(b, b);
                // d = c + c = (b + b) + (b + b) = ((a + a) + (a + a)) + ((a + a) + (a + a)) = a * 2^3
                var d = Vector128.Add(c, c);
                // etc.
                var e = Vector128.Add(d, d);
                var f = Vector128.Add(e, e);
                var g = Vector128.Add(f, f);
                var h = Vector128.Add(g, g);

                return h;
            }
        }

        /// <summary>
        /// Set bits <paramref name="crlfs"/> where the bit in <paramref name="crs"/> is set directly before a set bit in <paramref name="lfs"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CombineCrLf_Scalar(ReadOnlySpan<byte> crs, ReadOnlySpan<byte> lfs, Span<byte> crlfs)
        {
            Debug.Assert(crs.Length == lfs.Length);
            Debug.Assert(lfs.Length == crlfs.Length);

            var remainingCrs = crs;
            var remainingLfs = lfs;

            ref var curCrLf = ref MemoryMarshal.GetReference(crlfs);

            // A complication here is that a CR might be followed by an LF that occurs in the next "chunk" we process.
            // xxx\r_\nxxx is decomposed into
            // 0x0001_0000
            // 0x0000_8000

            while (remainingCrs.Length >= sizeof(ulong))
            {
                var cr = Unsafe.As<byte, ulong>(ref MemoryMarshal.GetReference(remainingCrs));
                var lf = Unsafe.As<byte, ulong>(ref MemoryMarshal.GetReference(remainingLfs));

                var setWhereCrLf = cr & (lf >> 1);

                var hasFinalCr = (cr & 0x8000_0000__0000_0000UL) != 0;
                if (hasFinalCr && remainingLfs.Length > sizeof(ulong))
                {
                    var nextLf = (ulong)(remainingLfs[sizeof(ulong)] & 1) << 63;
                    setWhereCrLf |= nextLf;
                }

                Unsafe.As<byte, ulong>(ref curCrLf) = setWhereCrLf;

                remainingCrs = remainingCrs[sizeof(ulong)..];
                remainingLfs = remainingLfs[sizeof(ulong)..];

                curCrLf = ref Unsafe.Add(ref curCrLf, sizeof(ulong));
            }

            if (remainingCrs.Length >= sizeof(uint))
            {
                var cr = Unsafe.As<byte, uint>(ref MemoryMarshal.GetReference(remainingCrs));
                var lf = Unsafe.As<byte, uint>(ref MemoryMarshal.GetReference(remainingLfs));

                var setWhereCrLf = cr & (lf >> 1);

                var hasFinalCr = (cr & 0x8000_0000U) != 0;
                if (hasFinalCr && remainingLfs.Length > sizeof(uint))
                {
                    var nextLf = (uint)(remainingLfs[sizeof(uint)] & 1) << 31;
                    setWhereCrLf |= nextLf;
                }

                Unsafe.As<byte, uint>(ref curCrLf) = setWhereCrLf;

                remainingCrs = remainingCrs[sizeof(uint)..];
                remainingLfs = remainingLfs[sizeof(uint)..];

                curCrLf = ref Unsafe.Add(ref curCrLf, sizeof(uint));
            }

            if (remainingCrs.Length >= sizeof(ushort))
            {
                var cr = Unsafe.As<byte, ushort>(ref MemoryMarshal.GetReference(remainingCrs));
                var lf = Unsafe.As<byte, ushort>(ref MemoryMarshal.GetReference(remainingLfs));

                var setWhereCrLf = (ushort)(cr & (lf >> 1));

                var hasFinalCr = (cr & 0x8000) != 0;
                if (hasFinalCr && remainingLfs.Length > sizeof(ushort))
                {
                    var nextLf = (ushort)((remainingLfs[sizeof(ushort)] & 1) << 15);
                    setWhereCrLf |= nextLf;
                }

                Unsafe.As<byte, ushort>(ref curCrLf) = setWhereCrLf;

                remainingCrs = remainingCrs[sizeof(ushort)..];
                remainingLfs = remainingLfs[sizeof(ushort)..];

                curCrLf = ref Unsafe.Add(ref curCrLf, sizeof(ushort));
            }

            if (remainingCrs.Length == 1)
            {
                var cr = remainingCrs[0];
                var lf = remainingLfs[0];

                var setWhereCrLf = (byte)(cr & (lf >> 1));

                // We can't look any further ahead, so don't bother

                curCrLf = setWhereCrLf;
            }
        }

        /// <summary>
        /// Quickly scan the whole command buffer, finding all the delimiters that MIGHT matter.
        /// 
        /// Does not consider escaping or anything just yet.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ScanForDelimiters(ReadOnlySpan<byte> commandBuffer, Span<byte> asteriks, Span<byte> dollars, Span<byte> crs, Span<byte> lfs)
        {
            const byte Asteriks = (byte)'*';
            const byte Dollar = (byte)'$';
            const byte CR = (byte)'\r';
            const byte LF = (byte)'\n';

            Debug.Assert(asteriks.Length == dollars.Length);
            Debug.Assert(dollars.Length == crs.Length);
            Debug.Assert(crs.Length == lfs.Length);

            var asterix512 = Vector512.Create(Asteriks);
            var dollar512 = Vector512.Create(Dollar);
            var cr512 = Vector512.Create(CR);
            var nl512 = Vector512.Create(LF);

            var remainingBuffer = commandBuffer;

            ref var remainingAsteriks = ref MemoryMarshal.GetReference(asteriks);
            ref var remainingDollars = ref MemoryMarshal.GetReference(dollars);
            ref var remainingCrs = ref MemoryMarshal.GetReference(crs);
            ref var remainingLfs = ref MemoryMarshal.GetReference(lfs);

            while (remainingBuffer.Length >= Vector512<byte>.Count)
            {
                var chunk512 = Vector512.LoadUnsafe(ref MemoryMarshal.GetReference(remainingBuffer));

                var asterik = Vector512.Equals(asterix512, chunk512);
                var dollar = Vector512.Equals(dollar512, chunk512);
                var cr = Vector512.Equals(cr512, chunk512);
                var lf = Vector512.Equals(nl512, chunk512);

                var asteriksMsbs = Vector512.ExtractMostSignificantBits(asterik);
                var dollarMsbs = Vector512.ExtractMostSignificantBits(dollar);
                var crMsbs = Vector512.ExtractMostSignificantBits(cr);
                var lfMsbs = Vector512.ExtractMostSignificantBits(lf);

                Unsafe.As<byte, ulong>(ref remainingAsteriks) = asteriksMsbs;
                Unsafe.As<byte, ulong>(ref remainingDollars) = dollarMsbs;
                Unsafe.As<byte, ulong>(ref remainingCrs) = crMsbs;
                Unsafe.As<byte, ulong>(ref remainingLfs) = lfMsbs;

                remainingAsteriks = ref Unsafe.Add(ref remainingAsteriks, sizeof(ulong));
                remainingDollars = ref Unsafe.Add(ref remainingDollars, sizeof(ulong));
                remainingCrs = ref Unsafe.Add(ref remainingCrs, sizeof(ulong));
                remainingLfs = ref Unsafe.Add(ref remainingLfs, sizeof(ulong));

                remainingBuffer = remainingBuffer[Vector512<byte>.Count..];
            }

            if (remainingBuffer.Length >= Vector256<byte>.Count)
            {
                var chunk256 = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(remainingBuffer));

                var asteriks256 = asterix512.GetLower();
                var dollar256 = dollar512.GetLower();
                var cr256 = cr512.GetLower();
                var lf256 = nl512.GetLower();

                var asterik = Vector256.Equals(asteriks256, chunk256);
                var dollar = Vector256.Equals(dollar256, chunk256);
                var cr = Vector256.Equals(cr256, chunk256);
                var lf = Vector256.Equals(lf256, chunk256);

                var asteriksMsbs = Vector256.ExtractMostSignificantBits(asterik);
                var dollarMsbs = Vector256.ExtractMostSignificantBits(dollar);
                var crMsbs = Vector256.ExtractMostSignificantBits(cr);
                var lfMsbs = Vector256.ExtractMostSignificantBits(lf);

                Unsafe.As<byte, uint>(ref remainingAsteriks) = asteriksMsbs;
                Unsafe.As<byte, uint>(ref remainingDollars) = dollarMsbs;
                Unsafe.As<byte, uint>(ref remainingCrs) = crMsbs;
                Unsafe.As<byte, uint>(ref remainingLfs) = lfMsbs;

                remainingAsteriks = ref Unsafe.Add(ref remainingAsteriks, sizeof(uint));
                remainingDollars = ref Unsafe.Add(ref remainingDollars, sizeof(uint));
                remainingCrs = ref Unsafe.Add(ref remainingCrs, sizeof(uint));
                remainingLfs = ref Unsafe.Add(ref remainingLfs, sizeof(uint));

                remainingBuffer = remainingBuffer[Vector256<byte>.Count..];
            }

            if (remainingBuffer.Length >= Vector128<byte>.Count)
            {
                var chunk128 = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(remainingBuffer));

                var asteriks128 = asterix512.GetLower().GetLower();
                var dollar128 = dollar512.GetLower().GetLower();
                var cr128 = cr512.GetLower().GetLower();
                var lf128 = nl512.GetLower().GetLower();

                var asterik = Vector128.Equals(asteriks128, chunk128);
                var dollar = Vector128.Equals(dollar128, chunk128);
                var cr = Vector128.Equals(cr128, chunk128);
                var lf = Vector128.Equals(lf128, chunk128);

                var asteriksMsbs = (ushort)Vector128.ExtractMostSignificantBits(asterik);
                var dollarMsbs = (ushort)Vector128.ExtractMostSignificantBits(dollar);
                var crMsbs = (ushort)Vector128.ExtractMostSignificantBits(cr);
                var lfMsbs = (ushort)Vector128.ExtractMostSignificantBits(lf);

                Unsafe.As<byte, ushort>(ref remainingAsteriks) = asteriksMsbs;
                Unsafe.As<byte, ushort>(ref remainingDollars) = dollarMsbs;
                Unsafe.As<byte, ushort>(ref remainingCrs) = crMsbs;
                Unsafe.As<byte, ushort>(ref remainingLfs) = lfMsbs;

                remainingAsteriks = ref Unsafe.Add(ref remainingAsteriks, sizeof(ushort));
                remainingDollars = ref Unsafe.Add(ref remainingDollars, sizeof(ushort));
                remainingCrs = ref Unsafe.Add(ref remainingCrs, sizeof(ushort));
                remainingLfs = ref Unsafe.Add(ref remainingLfs, sizeof(ushort));

                remainingBuffer = remainingBuffer[Vector128<byte>.Count..];
            }

            if (remainingBuffer.Length >= sizeof(ulong))
            {
                const ulong Asteriks64 = ((ulong)Asteriks << (64 - 8)) | ((ulong)Asteriks << (64 - 16)) | ((ulong)Asteriks << (64 - 24)) | ((ulong)Asteriks << (64 - 32)) | ((ulong)Asteriks << (64 - 40)) | ((ulong)Asteriks << (64 - 48)) | ((ulong)Asteriks << (64 - 56)) | ((ulong)Asteriks << (64 - 64));
                const ulong Dollar64 = ((ulong)Dollar << (64 - 8)) | ((ulong)Dollar << (64 - 16)) | ((ulong)Dollar << (64 - 24)) | ((ulong)Dollar << (64 - 32)) | ((ulong)Dollar << (64 - 40)) | ((ulong)Dollar << (64 - 48)) | ((ulong)Dollar << (64 - 56)) | ((ulong)Dollar << (64 - 64));
                const ulong CR64 = ((ulong)CR << (64 - 8)) | ((ulong)CR << (64 - 16)) | ((ulong)CR << (64 - 24)) | ((ulong)CR << (64 - 32)) | ((ulong)CR << (64 - 40)) | ((ulong)CR << (64 - 48)) | ((ulong)CR << (64 - 56)) | ((ulong)CR << (64 - 64));
                const ulong LF64 = ((ulong)LF << (64 - 8)) | ((ulong)LF << (64 - 16)) | ((ulong)LF << (64 - 24)) | ((ulong)LF << (64 - 32)) | ((ulong)LF << (64 - 40)) | ((ulong)LF << (64 - 48)) | ((ulong)LF << (64 - 56)) | ((ulong)LF << (64 - 64));

                var chunk64 = Unsafe.As<byte, ulong>(ref MemoryMarshal.GetReference(remainingBuffer));

                var asteriks64 = chunk64 ^ Asteriks64;
                var dollar64 = chunk64 ^ Dollar64;
                var cr64 = chunk64 ^ CR64;
                var lf64 = chunk64 ^ LF64;

                var asteriksMsbs = SetMSBsWhereZero64_Mult(asteriks64);
                var dollarMsbs = SetMSBsWhereZero64_Mult(dollar64);
                var crMsbs = SetMSBsWhereZero64_Mult(cr64);
                var lfMsbs = SetMSBsWhereZero64_Mult(lf64);

                remainingAsteriks = asteriksMsbs;
                remainingDollars = dollarMsbs;
                remainingCrs = crMsbs;
                remainingLfs = lfMsbs;

                remainingAsteriks = ref Unsafe.Add(ref remainingAsteriks, 1);
                remainingDollars = ref Unsafe.Add(ref remainingDollars, 1);
                remainingCrs = ref Unsafe.Add(ref remainingCrs, 1);
                remainingLfs = ref Unsafe.Add(ref remainingLfs, 1);

                remainingBuffer = remainingBuffer[sizeof(ulong)..];
            }

            // we can't continue for 32, 16, etc. because we need to produce whole bytes
            // and below ulong we aren't getting a whole byte in one "word" size

            var pendingMask = (byte)0b0000_0001;
            var pendingAsteriksMsbs = (byte)0;
            var pendingDollarMsbs = (byte)0;
            var pendingCrMsbs = (byte)0;
            var pendingLfMsbs = (byte)0;

            for (var i = 0; i < remainingBuffer.Length; i++)
            {
                var c = remainingBuffer[i];
                if (c == Asteriks)
                {
                    pendingAsteriksMsbs |= pendingMask;
                }
                else if (c == Dollar)
                {
                    pendingDollarMsbs |= pendingMask;
                }
                else if (c == CR)
                {
                    pendingCrMsbs |= pendingMask;
                }
                else if (c == LF)
                {
                    pendingLfMsbs |= pendingMask;
                }

                var nextMask = (byte)(pendingMask << 1);

                if (nextMask == 0)
                {
                    remainingAsteriks = pendingAsteriksMsbs;
                    remainingDollars = pendingDollarMsbs;
                    remainingCrs = pendingCrMsbs;
                    remainingLfs = pendingLfMsbs;

                    remainingAsteriks = ref Unsafe.Add(ref remainingAsteriks, 1);
                    remainingDollars = ref Unsafe.Add(ref remainingDollars, 1);
                    remainingCrs = ref Unsafe.Add(ref remainingCrs, 1);
                    remainingLfs = ref Unsafe.Add(ref remainingLfs, 1);

                    pendingAsteriksMsbs = 0;
                    pendingDollarMsbs = 0;
                    pendingCrMsbs = 0;
                    pendingLfMsbs = 0;

                    nextMask = 0b0000_0001;
                }

                pendingMask = nextMask;
            }

            if (pendingMask != 0b0000_0001)
            {
                remainingAsteriks = pendingAsteriksMsbs;
                remainingDollars = pendingDollarMsbs;
                remainingCrs = pendingCrMsbs;
                remainingLfs = pendingLfMsbs;
            }
        }

        /// <summary>
        /// Puts a set bit in a byte where there's a corresponding zero byte in val
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte SetMSBsWhereZero64(ulong val)
        {
            // Ye Olde bit twiddling hack: https://graphics.stanford.edu/~seander/bithacks.html#ZeroInWord
            var highBitsSetWhereZero = ((val - 0x0101_0101__0101_0101UL) & ~(val) & 0x8080_8080__8080_8080UL);

            var ret =
                ((highBitsSetWhereZero & 0x8000_0000__0000_0000UL) != 0 ? 0b1000_0000 : 0) |
                ((highBitsSetWhereZero & 0x0080_0000__0000_0000UL) != 0 ? 0b0100_0000 : 0) |
                ((highBitsSetWhereZero & 0x0000_8000__0000_0000UL) != 0 ? 0b0010_0000 : 0) |
                ((highBitsSetWhereZero & 0x0000_0080__0000_0000UL) != 0 ? 0b0001_0000 : 0) |
                ((highBitsSetWhereZero & 0x0000_0000__8000_0000UL) != 0 ? 0b0000_1000 : 0) |
                ((highBitsSetWhereZero & 0x0000_0000__0080_0000UL) != 0 ? 0b0000_0100 : 0) |
                ((highBitsSetWhereZero & 0x0000_0000__0000_8000UL) != 0 ? 0b0000_0010 : 0) |
                ((highBitsSetWhereZero & 0x0000_0000__0000_0080UL) != 0 ? 0b0000_0001 : 0);

            return (byte)ret;
        }

        /// <summary>
        /// Puts a set bit in a byte where there's a corresponding zero byte in val
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte SetMSBsWhereZero64_Mult(ulong val)
        {
            // So bad at deriving these constants, is there a ulong equivalent?

            const uint MagicMult = 2_113_665;
            const byte MagicShift = 32 - 4;

            // Ye Olde bit twiddling hack: https://graphics.stanford.edu/~seander/bithacks.html#ZeroInWord
            var highBitsSetWhereZero = ((val - 0x0101_0101__0101_0101UL) & ~(val) & 0x8080_8080__8080_8080UL);

            var lowUInt = (uint)highBitsSetWhereZero;
            var highUInt = (uint)(highBitsSetWhereZero >> 32);

            var lowContigBits = (lowUInt * MagicMult) >> MagicShift;
            var highContigBits = (highUInt * MagicMult) >> (MagicShift - 4);

            var finalBits = highContigBits | lowContigBits;

            return (byte)finalBits;
        }

        // 00 - 99, but with padding bytes so we can look them up without any multiplication
        private static ReadOnlySpan<byte> TryParsePositiveIntLookup
        => "\x00\x01\x02\x03\x04\x05\x06\x07\x08\x09\x7F\x7F\x7F\x7F\x7F\x7F\x0A\x0B\x0C\x0D\x0E\x0F\x10\x11\x12\x13\x7F\x7F\x7F\x7F\x7F\x7F\x14\x15\x16\x17\x18\x19\x1A\x1B\x1C\x1D\x7F\x7F\x7F\x7F\x7F\x7F\x1E\x1F\x20\x21\x22\x23\x24\x25\x26\x27\x7F\x7F\x7F\x7F\x7F\x7F\x28\x29\x2A\x2B\x2C\x2D\x2E\x2F\x30\x31\x7F\x7F\x7F\x7F\x7F\x7F\x32\x33\x34\x35\x36\x37\x38\x39\x3A\x3B\x7F\x7F\x7F\x7F\x7F\x7F\x3C\x3D\x3E\x3F\x40\x41\x42\x43\x44\x45\x7F\x7F\x7F\x7F\x7F\x7F\x46\x47\x48\x49\x4A\x4B\x4C\x4D\x4E\x4F\x7F\x7F\x7F\x7F\x7F\x7F\x50\x51\x52\x53\x54\x55\x56\x57\x58\x59\x7F\x7F\x7F\x7F\x7F\x7F\x5A\x5B\x5C\x5D\x5E\x5F\x60\x61\x62\x63"u8;

        /// <summary>
        /// Attempt to parse a string into an integer where we know it should be > 0.
        /// </summary>
        public static bool TryParsePositiveInt(ReadOnlySpan<byte> input, out int parsed)
        {
            // bytes that make up a valid integer will be in the range 0x30 - 0x39
            // 0011_0000 = 0
            // ...
            // 0011_1001 = 9

            if (input.Length == 0 || input.Length > 10)
            {
                Unsafe.SkipInit(out parsed);
                return false;
            }
            else if (input.Length == 1)
            {
                parsed = input[0] - '0';
                return parsed is > 0 and < 10;
            }

            var ret = 0L;
            while (input.Length >= 2)
            {
                ret *= 100;

                var twoAsUShort = Unsafe.As<byte, ushort>(ref MemoryMarshal.GetReference(input));

                if ((twoAsUShort & 0b1111_0000__1111_0000) != 0b0011_0000__0011_0000)
                {
                    Unsafe.SkipInit(out parsed);
                    return false;
                }

                var highDigit = twoAsUShort & 0b0000_0000__0000_1111;
                var lowDigit = twoAsUShort & 0b0000_1111__0000_0000;

                var lookup = (highDigit << 4) | (lowDigit >> 8);
                if (lookup >= TryParsePositiveIntLookup.Length)
                {
                    Unsafe.SkipInit(out parsed);
                    return false;
                }

                var twoParts = TryParsePositiveIntLookup[lookup];

                if (twoParts is > 99)
                {
                    Unsafe.SkipInit(out parsed);
                    return false;
                }

                ret += twoParts;

                input = input[2..];
            }

            if (!input.IsEmpty)
            {
                ret *= 10;
                var lastDigit = input[0] - '0';
                if (lastDigit is < 0 or > 9)
                {
                    Unsafe.SkipInit(out parsed);
                    return false;
                }

                ret += lastDigit;
            }

            parsed = (int)ret;
            return parsed > 0;
        }

        /// <summary>
        /// Parse a chunk of a command buffer into a <see cref="RespCommand"/>, or fail.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryParseCommand_Enum(ReadOnlySpan<byte> commandBuffer, int commandStart, int commandEnd, out RespCommand parsed)
        {
            var cmd = commandBuffer[commandStart..commandEnd];

            if (cmd.Length > 64)
            {
                Unsafe.SkipInit(out parsed);
                return false;
            }

            Span<char> asChars = stackalloc char[cmd.Length];
            var len = Encoding.UTF8.GetChars(cmd, asChars);

            return Enum.TryParse(asChars, ignoreCase: true, out parsed) && !char.IsWhiteSpace((char)cmd[0]) && !char.IsWhiteSpace((char)cmd[^1]);
        }

        public static readonly uint ACL = MemoryMarshal.Read<uint>("ACL\r"u8);
        public static readonly uint DEL = MemoryMarshal.Read<uint>("DEL\r"u8);
        public static readonly uint GET = MemoryMarshal.Read<uint>("GET\r"u8);
        public static readonly uint LCS = MemoryMarshal.Read<uint>("LCS\r"u8);
        public static readonly uint SET = MemoryMarshal.Read<uint>("SET\r"u8);
        public static readonly uint TTL = MemoryMarshal.Read<uint>("TTL\r"u8);
        public static readonly uint AUTH = MemoryMarshal.Read<uint>("AUTH"u8);
        public static readonly uint DECR = MemoryMarshal.Read<uint>("DECR"u8);
        public static readonly uint DUMP = MemoryMarshal.Read<uint>("DUMP"u8);
        public static readonly uint ECHO = MemoryMarshal.Read<uint>("ECHO"u8);
        public static readonly uint EVAL = MemoryMarshal.Read<uint>("EVAL"u8);
        public static readonly uint EXEC = MemoryMarshal.Read<uint>("EXEC"u8);
        public static readonly uint HDEL = MemoryMarshal.Read<uint>("HDEL"u8);
        public static readonly uint HGET = MemoryMarshal.Read<uint>("HGET"u8);
        public static readonly uint HLEN = MemoryMarshal.Read<uint>("HLEN"u8);
        public static readonly uint HSET = MemoryMarshal.Read<uint>("HSET"u8);
        public static readonly uint HTTL = MemoryMarshal.Read<uint>("HTTL"u8);
        public static readonly uint INCR = MemoryMarshal.Read<uint>("INCR"u8);
        public static readonly uint INFO = MemoryMarshal.Read<uint>("INFO"u8);
        public static readonly uint KEYS = MemoryMarshal.Read<uint>("KEYS"u8);
        public static readonly uint LLEN = MemoryMarshal.Read<uint>("LLEN"u8);
        public static readonly uint LPOP = MemoryMarshal.Read<uint>("LPOP"u8);
        public static readonly uint LPOS = MemoryMarshal.Read<uint>("LPOS"u8);
        public static readonly uint LREM = MemoryMarshal.Read<uint>("LREM"u8);
        public static readonly uint LSET = MemoryMarshal.Read<uint>("LSET"u8);
        public static readonly uint MGET = MemoryMarshal.Read<uint>("MGET"u8);
        public static readonly uint MSET = MemoryMarshal.Read<uint>("MSET"u8);
        public static readonly uint PING = MemoryMarshal.Read<uint>("PING"u8);
        public static readonly uint PTTL = MemoryMarshal.Read<uint>("PTTL"u8);
        public static readonly uint QUIT = MemoryMarshal.Read<uint>("QUIT"u8);
        public static readonly uint ROLE = MemoryMarshal.Read<uint>("ROLE"u8);
        public static readonly uint RPOP = MemoryMarshal.Read<uint>("RPOP"u8);
        public static readonly uint SADD = MemoryMarshal.Read<uint>("SADD"u8);
        public static readonly uint SAVE = MemoryMarshal.Read<uint>("SAVE"u8);
        public static readonly uint SCAN = MemoryMarshal.Read<uint>("SCAN"u8);
        public static readonly uint SPOP = MemoryMarshal.Read<uint>("SPOP"u8);
        public static readonly uint SREM = MemoryMarshal.Read<uint>("SREM"u8);
        public static readonly uint TIME = MemoryMarshal.Read<uint>("TIME"u8);
        public static readonly uint TYPE = MemoryMarshal.Read<uint>("TYPE"u8);
        public static readonly uint ZADD = MemoryMarshal.Read<uint>("ZADD"u8);
        public static readonly uint ZREM = MemoryMarshal.Read<uint>("ZREM"u8);
        public static readonly uint ZTTL = MemoryMarshal.Read<uint>("ZTTL"u8);
        public static readonly ulong BITOP = MemoryMarshal.Read<ulong>("\nBITOP\r\n"u8);
        public static readonly ulong BLPOP = MemoryMarshal.Read<ulong>("\nBLPOP\r\n"u8);
        public static readonly ulong BRPOP = MemoryMarshal.Read<ulong>("\nBRPOP\r\n"u8);
        public static readonly ulong DEBUG = MemoryMarshal.Read<ulong>("\nDEBUG\r\n"u8);
        public static readonly ulong GETEX = MemoryMarshal.Read<ulong>("\nGETEX\r\n"u8);
        public static readonly ulong HELLO = MemoryMarshal.Read<ulong>("\nHELLO\r\n"u8);
        public static readonly ulong HKEYS = MemoryMarshal.Read<ulong>("\nHKEYS\r\n"u8);
        public static readonly ulong HMGET = MemoryMarshal.Read<ulong>("\nHMGET\r\n"u8);
        public static readonly ulong HMSET = MemoryMarshal.Read<ulong>("\nHMSET\r\n"u8);
        public static readonly ulong HPTTL = MemoryMarshal.Read<ulong>("\nHPTTL\r\n"u8);
        public static readonly ulong HSCAN = MemoryMarshal.Read<ulong>("\nHSCAN\r\n"u8);
        public static readonly ulong HVALS = MemoryMarshal.Read<ulong>("\nHVALS\r\n"u8);
        public static readonly ulong LMOVE = MemoryMarshal.Read<ulong>("\nLMOVE\r\n"u8);
        public static readonly ulong LMPOP = MemoryMarshal.Read<ulong>("\nLMPOP\r\n"u8);
        public static readonly ulong LPUSH = MemoryMarshal.Read<ulong>("\nLPUSH\r\n"u8);
        public static readonly ulong LTRIM = MemoryMarshal.Read<ulong>("\nLTRIM\r\n"u8);
        public static readonly ulong MULTI = MemoryMarshal.Read<ulong>("\nMULTI\r\n"u8);
        public static readonly ulong PFADD = MemoryMarshal.Read<ulong>("\nPFADD\r\n"u8);
        public static readonly ulong RPUSH = MemoryMarshal.Read<ulong>("\nRPUSH\r\n"u8);
        public static readonly ulong SCARD = MemoryMarshal.Read<ulong>("\nSCARD\r\n"u8);
        public static readonly ulong SDIFF = MemoryMarshal.Read<ulong>("\nSDIFF\r\n"u8);
        public static readonly ulong SETEX = MemoryMarshal.Read<ulong>("\nSETEX\r\n"u8);
        public static readonly ulong SMOVE = MemoryMarshal.Read<ulong>("\nSMOVE\r\n"u8);
        public static readonly ulong SSCAN = MemoryMarshal.Read<ulong>("\nSSCAN\r\n"u8);
        public static readonly ulong WATCH = MemoryMarshal.Read<ulong>("\nWATCH\r\n"u8);
        public static readonly ulong ZCARD = MemoryMarshal.Read<ulong>("\nZCARD\r\n"u8);
        public static readonly ulong ZDIFF = MemoryMarshal.Read<ulong>("\nZDIFF\r\n"u8);
        public static readonly ulong ZMPOP = MemoryMarshal.Read<ulong>("\nZMPOP\r\n"u8);
        public static readonly ulong ZPTTL = MemoryMarshal.Read<ulong>("\nZPTTL\r\n"u8);
        public static readonly ulong ZRANK = MemoryMarshal.Read<ulong>("\nZRANK\r\n"u8);
        public static readonly ulong ZSCAN = MemoryMarshal.Read<ulong>("\nZSCAN\r\n"u8);
        public static readonly ulong APPEND = MemoryMarshal.Read<ulong>("APPEND\r\n"u8);
        public static readonly ulong ASKING = MemoryMarshal.Read<ulong>("ASKING\r\n"u8);
        public static readonly ulong BGSAVE = MemoryMarshal.Read<ulong>("BGSAVE\r\n"u8);
        public static readonly ulong BITPOS = MemoryMarshal.Read<ulong>("BITPOS\r\n"u8);
        public static readonly ulong BLMOVE = MemoryMarshal.Read<ulong>("BLMOVE\r\n"u8);
        public static readonly ulong BLMPOP = MemoryMarshal.Read<ulong>("BLMPOP\r\n"u8);
        public static readonly ulong BZMPOP = MemoryMarshal.Read<ulong>("BZMPOP\r\n"u8);
        public static readonly ulong CLIENT = MemoryMarshal.Read<ulong>("CLIENT\r\n"u8);
        public static readonly ulong CONFIG = MemoryMarshal.Read<ulong>("CONFIG\r\n"u8);
        public static readonly ulong COSCAN = MemoryMarshal.Read<ulong>("COSCAN\r\n"u8);
        public static readonly ulong DBSIZE = MemoryMarshal.Read<ulong>("DBSIZE\r\n"u8);
        public static readonly ulong DECRBY = MemoryMarshal.Read<ulong>("DECRBY\r\n"u8);
        public static readonly ulong EXISTS = MemoryMarshal.Read<ulong>("EXISTS\r\n"u8);
        public static readonly ulong EXPIRE = MemoryMarshal.Read<ulong>("EXPIRE\r\n"u8);
        public static readonly ulong GEOADD = MemoryMarshal.Read<ulong>("GEOADD\r\n"u8);
        public static readonly ulong GEOPOS = MemoryMarshal.Read<ulong>("GEOPOS\r\n"u8);
        public static readonly ulong GETBIT = MemoryMarshal.Read<ulong>("GETBIT\r\n"u8);
        public static readonly ulong GETDEL = MemoryMarshal.Read<ulong>("GETDEL\r\n"u8);
        public static readonly ulong GETSET = MemoryMarshal.Read<ulong>("GETSET\r\n"u8);
        public static readonly ulong HSETNX = MemoryMarshal.Read<ulong>("HSETNX\r\n"u8);
        public static readonly ulong INCRBY = MemoryMarshal.Read<ulong>("INCRBY\r\n"u8);
        public static readonly ulong LINDEX = MemoryMarshal.Read<ulong>("LINDEX\r\n"u8);
        public static readonly ulong LPUSHX = MemoryMarshal.Read<ulong>("LPUSHX\r\n"u8);
        public static readonly ulong LRANGE = MemoryMarshal.Read<ulong>("LRANGE\r\n"u8);
        public static readonly ulong MEMORY = MemoryMarshal.Read<ulong>("MEMORY\r\n"u8);
        public static readonly ulong MSETNX = MemoryMarshal.Read<ulong>("MSETNX\r\n"u8);
        public static readonly ulong PSETEX = MemoryMarshal.Read<ulong>("PSETEX\r\n"u8);
        public static readonly ulong PUBSUB = MemoryMarshal.Read<ulong>("PUBSUB\r\n"u8);
        public static readonly ulong RENAME = MemoryMarshal.Read<ulong>("RENAME\r\n"u8);
        public static readonly ulong RPUSHX = MemoryMarshal.Read<ulong>("RPUSHX\r\n"u8);
        public static readonly ulong RUNTXP = MemoryMarshal.Read<ulong>("RUNTXP\r\n"u8);
        public static readonly ulong SCRIPT = MemoryMarshal.Read<ulong>("SCRIPT\r\n"u8);
        public static readonly ulong SELECT = MemoryMarshal.Read<ulong>("SELECT\r\n"u8);
        public static readonly ulong SETBIT = MemoryMarshal.Read<ulong>("SETBIT\r\n"u8);
        public static readonly ulong SINTER = MemoryMarshal.Read<ulong>("SINTER\r\n"u8);
        public static readonly ulong STRLEN = MemoryMarshal.Read<ulong>("STRLEN\r\n"u8);
        public static readonly ulong SUBSTR = MemoryMarshal.Read<ulong>("SUBSTR\r\n"u8);
        public static readonly ulong SUNION = MemoryMarshal.Read<ulong>("SUNION\r\n"u8);
        public static readonly ulong UNLINK = MemoryMarshal.Read<ulong>("UNLINK\r\n"u8);
        public static readonly ulong ZCOUNT = MemoryMarshal.Read<ulong>("ZCOUNT\r\n"u8);
        public static readonly ulong ZINTER = MemoryMarshal.Read<ulong>("ZINTER\r\n"u8);
        public static readonly ulong ZRANGE = MemoryMarshal.Read<ulong>("ZRANGE\r\n"u8);
        public static readonly ulong ZSCORE = MemoryMarshal.Read<ulong>("ZSCORE\r\n"u8);
        public static readonly ulong ZUNION = MemoryMarshal.Read<ulong>("ZUNION\r\n"u8);
        public static readonly ulong CLUSTER = MemoryMarshal.Read<ulong>("CLUSTER\r"u8);
        public static readonly ulong COMMAND = MemoryMarshal.Read<ulong>("COMMAND\r"u8);
        public static readonly ulong DISCARD = MemoryMarshal.Read<ulong>("DISCARD\r"u8);
        public static readonly ulong EVALSHA = MemoryMarshal.Read<ulong>("EVALSHA\r"u8);
        public static readonly ulong FLUSHDB = MemoryMarshal.Read<ulong>("FLUSHDB\r"u8);
        public static readonly ulong FORCEGC = MemoryMarshal.Read<ulong>("FORCEGC\r"u8);
        public static readonly ulong GEODIST = MemoryMarshal.Read<ulong>("GEODIST\r"u8);
        public static readonly ulong GEOHASH = MemoryMarshal.Read<ulong>("GEOHASH\r"u8);
        public static readonly ulong HEXISTS = MemoryMarshal.Read<ulong>("HEXISTS\r"u8);
        public static readonly ulong HEXPIRE = MemoryMarshal.Read<ulong>("HEXPIRE\r"u8);
        public static readonly ulong HGETALL = MemoryMarshal.Read<ulong>("HGETALL\r"u8);
        public static readonly ulong HINCRBY = MemoryMarshal.Read<ulong>("HINCRBY\r"u8);
        public static readonly ulong HSTRLEN = MemoryMarshal.Read<ulong>("HSTRLEN\r"u8);
        public static readonly ulong LATENCY = MemoryMarshal.Read<ulong>("LATENCY\r"u8);
        public static readonly ulong LINSERT = MemoryMarshal.Read<ulong>("LINSERT\r"u8);
        public static readonly ulong MIGRATE = MemoryMarshal.Read<ulong>("MIGRATE\r"u8);
        public static readonly ulong MONITOR = MemoryMarshal.Read<ulong>("MONITOR\r"u8);
        public static readonly ulong PERSIST = MemoryMarshal.Read<ulong>("PERSIST\r"u8);
        public static readonly ulong PEXPIRE = MemoryMarshal.Read<ulong>("PEXPIRE\r"u8);
        public static readonly ulong PFCOUNT = MemoryMarshal.Read<ulong>("PFCOUNT\r"u8);
        public static readonly ulong PFMERGE = MemoryMarshal.Read<ulong>("PFMERGE\r"u8);
        public static readonly ulong PUBLISH = MemoryMarshal.Read<ulong>("PUBLISH\r"u8);
        public static readonly ulong PURGEBP = MemoryMarshal.Read<ulong>("PURGEBP\r"u8);
        public static readonly ulong RESTORE = MemoryMarshal.Read<ulong>("RESTORE\r"u8);
        public static readonly ulong SLOWLOG = MemoryMarshal.Read<ulong>("SLOWLOG\r"u8);
        public static readonly ulong UNWATCH = MemoryMarshal.Read<ulong>("UNWATCH\r"u8);
        public static readonly ulong WATCHMS = MemoryMarshal.Read<ulong>("WATCHMS\r"u8);
        public static readonly ulong WATCHOS = MemoryMarshal.Read<ulong>("WATCHOS\r"u8);
        public static readonly ulong ZEXPIRE = MemoryMarshal.Read<ulong>("ZEXPIRE\r"u8);
        public static readonly ulong ZINCRBY = MemoryMarshal.Read<ulong>("ZINCRBY\r"u8);
        public static readonly ulong ZMSCORE = MemoryMarshal.Read<ulong>("ZMSCORE\r"u8);
        public static readonly ulong ZPOPMAX = MemoryMarshal.Read<ulong>("ZPOPMAX\r"u8);
        public static readonly ulong ZPOPMIN = MemoryMarshal.Read<ulong>("ZPOPMIN\r"u8);
        public static readonly ulong BITCOUNT = MemoryMarshal.Read<ulong>("BITCOUNT"u8);
        public static readonly ulong BITFIELD = MemoryMarshal.Read<ulong>("BITFIELD"u8);
        public static readonly ulong BZPOPMAX = MemoryMarshal.Read<ulong>("BZPOPMAX"u8);
        public static readonly ulong BZPOPMIN = MemoryMarshal.Read<ulong>("BZPOPMIN"u8);
        public static readonly ulong EXPIREAT = MemoryMarshal.Read<ulong>("EXPIREAT"u8);
        public static readonly ulong FAILOVER = MemoryMarshal.Read<ulong>("FAILOVER"u8);
        public static readonly ulong FLUSHALL = MemoryMarshal.Read<ulong>("FLUSHALL"u8);
        public static readonly ulong GETRANGE = MemoryMarshal.Read<ulong>("GETRANGE"u8);
        public static readonly ulong HCOLLECT = MemoryMarshal.Read<ulong>("HCOLLECT"u8);
        public static readonly ulong HPERSIST = MemoryMarshal.Read<ulong>("HPERSIST"u8);
        public static readonly ulong HPEXPIRE = MemoryMarshal.Read<ulong>("HPEXPIRE"u8);
        public static readonly ulong LASTSAVE = MemoryMarshal.Read<ulong>("LASTSAVE"u8);
        public static readonly ulong READONLY = MemoryMarshal.Read<ulong>("READONLY"u8);
        public static readonly ulong RENAMENX = MemoryMarshal.Read<ulong>("RENAMENX"u8);
        public static readonly ulong SETRANGE = MemoryMarshal.Read<ulong>("SETRANGE"u8);
        public static readonly ulong SMEMBERS = MemoryMarshal.Read<ulong>("SMEMBERS"u8);
        public static readonly ulong SPUBLISH = MemoryMarshal.Read<ulong>("SPUBLISH"u8);
        public static readonly ulong ZCOLLECT = MemoryMarshal.Read<ulong>("ZCOLLECT"u8);
        public static readonly ulong ZPERSIST = MemoryMarshal.Read<ulong>("ZPERSIST"u8);
        public static readonly ulong ZPEXPIRE = MemoryMarshal.Read<ulong>("ZPEXPIRE"u8);
        public static readonly ulong ZREVRANK = MemoryMarshal.Read<ulong>("ZREVRANK"u8);
        public static readonly ulong COMMITAO = MemoryMarshal.Read<ulong>("COMMITAO"u8);
        public static readonly uint TAOF = MemoryMarshal.Read<uint>("TAOF"u8);
        public static readonly ulong GEORADIU = MemoryMarshal.Read<ulong>("GEORADIU"u8);
        public static readonly uint DIUS = MemoryMarshal.Read<uint>("DIUS"u8);
        public static readonly ulong GEOSEARC = MemoryMarshal.Read<ulong>("GEOSEARC"u8);
        public static readonly uint ARCH = MemoryMarshal.Read<uint>("ARCH"u8);
        public static readonly ulong HEXPIREA = MemoryMarshal.Read<ulong>("HEXPIREA"u8);
        public static readonly uint REAT = MemoryMarshal.Read<uint>("REAT"u8);
        public static readonly ulong PEXPIREA = MemoryMarshal.Read<ulong>("PEXPIREA"u8);
        public static readonly ulong READWRIT = MemoryMarshal.Read<ulong>("READWRIT"u8);
        public static readonly uint RITE = MemoryMarshal.Read<uint>("RITE"u8);
        public static readonly ulong REPLICAO = MemoryMarshal.Read<ulong>("REPLICAO"u8);
        public static readonly uint CAOF = MemoryMarshal.Read<uint>("CAOF"u8);
        public static readonly ulong RPOPLPUS = MemoryMarshal.Read<ulong>("RPOPLPUS"u8);
        public static readonly uint PUSH = MemoryMarshal.Read<uint>("PUSH"u8);
        public static readonly ulong SISMEMBE = MemoryMarshal.Read<ulong>("SISMEMBE"u8);
        public static readonly uint MBER = MemoryMarshal.Read<uint>("MBER"u8);
        public static readonly ulong SUBSCRIB = MemoryMarshal.Read<ulong>("SUBSCRIB"u8);
        public static readonly uint RIBE = MemoryMarshal.Read<uint>("RIBE"u8);
        public static readonly ulong ZEXPIREA = MemoryMarshal.Read<ulong>("ZEXPIREA"u8);
        public static readonly ulong ZLEXCOUN = MemoryMarshal.Read<ulong>("ZLEXCOUN"u8);
        public static readonly uint OUNT = MemoryMarshal.Read<uint>("OUNT"u8);
        public static readonly ulong ZREVRANG = MemoryMarshal.Read<ulong>("ZREVRANG"u8);
        public static readonly uint ANGE = MemoryMarshal.Read<uint>("ANGE"u8);
        public static readonly ulong BRPOPLPU = MemoryMarshal.Read<ulong>("BRPOPLPU"u8);
        public static readonly ulong EXPIRETI = MemoryMarshal.Read<ulong>("EXPIRETI"u8);
        public static readonly ulong HRANDFIE = MemoryMarshal.Read<ulong>("HRANDFIE"u8);
        public static readonly uint IELD = MemoryMarshal.Read<uint>("IELD"u8);
        public static readonly ulong PSUBSCRI = MemoryMarshal.Read<ulong>("PSUBSCRI"u8);
        public static readonly ulong SDIFFSTO = MemoryMarshal.Read<ulong>("SDIFFSTO"u8);
        public static readonly uint TORE = MemoryMarshal.Read<uint>("TORE"u8);
        public static readonly ulong SETKEEPT = MemoryMarshal.Read<ulong>("SETKEEPT"u8);
        public static readonly ulong SINTERCA = MemoryMarshal.Read<ulong>("SINTERCA"u8);
        public static readonly uint CARD = MemoryMarshal.Read<uint>("CARD"u8);
        public static readonly ulong SMISMEMB = MemoryMarshal.Read<ulong>("SMISMEMB"u8);
        public static readonly ulong SSUBSCRI = MemoryMarshal.Read<ulong>("SSUBSCRI"u8);
        public static readonly ulong ZDIFFSTO = MemoryMarshal.Read<ulong>("ZDIFFSTO"u8);
        public static readonly ulong ZINTERCA = MemoryMarshal.Read<ulong>("ZINTERCA"u8);
        public static readonly uint D_RO = MemoryMarshal.Read<uint>("D_RO"u8);
        public static readonly ulong GETWITHE = MemoryMarshal.Read<ulong>("GETWITHE"u8);
        public static readonly uint ETAG = MemoryMarshal.Read<uint>("ETAG"u8);
        public static readonly ulong HEXPIRET = MemoryMarshal.Read<ulong>("HEXPIRET"u8);
        public static readonly ulong INCRBYFL = MemoryMarshal.Read<ulong>("INCRBYFL"u8);
        public static readonly uint LOAT = MemoryMarshal.Read<uint>("LOAT"u8);
        public static readonly ulong PEXPIRET = MemoryMarshal.Read<ulong>("PEXPIRET"u8);
        public static readonly ulong SECONDAR = MemoryMarshal.Read<ulong>("SECONDAR"u8);
        public static readonly uint RYOF = MemoryMarshal.Read<uint>("RYOF"u8);
        public static readonly ulong SINTERST = MemoryMarshal.Read<ulong>("SINTERST"u8);
        public static readonly ulong SRANDMEM = MemoryMarshal.Read<ulong>("SRANDMEM"u8);
        public static readonly ulong SUNIONST = MemoryMarshal.Read<ulong>("SUNIONST"u8);
        public static readonly ulong UNSUBSCR = MemoryMarshal.Read<ulong>("UNSUBSCR"u8);
        public static readonly ulong ZEXPIRET = MemoryMarshal.Read<ulong>("ZEXPIRET"u8);
        public static readonly ulong ZINTERST = MemoryMarshal.Read<ulong>("ZINTERST"u8);
        public static readonly ulong ZRANDMEM = MemoryMarshal.Read<ulong>("ZRANDMEM"u8);
        public static readonly ulong ZRANGEBY = MemoryMarshal.Read<ulong>("ZRANGEBY"u8);
        public static readonly uint YLEX = MemoryMarshal.Read<uint>("YLEX"u8);
        public static readonly ulong ZRANGEST = MemoryMarshal.Read<ulong>("ZRANGEST"u8);
        public static readonly ulong ZUNIONST = MemoryMarshal.Read<ulong>("ZUNIONST"u8);
        public static readonly uint S_RO = MemoryMarshal.Read<uint>("S_RO"u8);
        public static readonly ulong HINCRBYF = MemoryMarshal.Read<ulong>("HINCRBYF"u8);
        public static readonly ulong MEMORY_U = MemoryMarshal.Read<ulong>("MEMORY_U"u8);
        public static readonly uint SAGE = MemoryMarshal.Read<uint>("SAGE"u8);
        public static readonly ulong PUNSUBSC = MemoryMarshal.Read<ulong>("PUNSUBSC"u8);
        public static readonly uint TLXX = MemoryMarshal.Read<uint>("TLXX"u8);
        public static readonly ulong GETIFNOT = MemoryMarshal.Read<ulong>("GETIFNOT"u8);
        public static readonly ulong NOTMATCH = MemoryMarshal.Read<ulong>("NOTMATCH"u8);
        public static readonly ulong EBYSCORE = MemoryMarshal.Read<ulong>("EBYSCORE"u8);
        public static readonly ulong RCHSTORE = MemoryMarshal.Read<ulong>("RCHSTORE"u8);
        public static readonly ulong ZREMRANG = MemoryMarshal.Read<ulong>("ZREMRANG"u8);
        public static readonly ulong NGEBYLEX = MemoryMarshal.Read<ulong>("NGEBYLEX"u8);
        public static readonly ulong GEBYRANK = MemoryMarshal.Read<ulong>("GEBYRANK"u8);
        public static readonly ulong SBYMEMBE = MemoryMarshal.Read<ulong>("SBYMEMBE"u8);
        public static readonly uint R_RO = MemoryMarshal.Read<uint>("R_RO"u8);

        /// <summary>
        /// Version of <see cref="TryParseCommand_Enum(Span{byte}, int, int, out RespCommand)"/> that uses some dirty reinterpret casts and big switch.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryParseCommand_Switch(Span<byte> cmdBuffer, int commandStart, int commandEnd, out RespCommand parsed)
        {
            ref var cmdStartRef = ref Unsafe.Add(ref MemoryMarshal.GetReference(cmdBuffer), commandStart);
            var firstPass = true;
            var cmdLen = commandEnd - commandStart;

        tryAgain:
            switch (cmdLen)
            {
                case 3:
                    var len3p0 = Unsafe.As<byte, uint>(ref cmdStartRef);

                    //if (len3p0 == ACL)
                    //{
                    //    parsed = RespCommand.ACL;
                    //    return true;
                    //}
                    //else
                    if (len3p0 == DEL)
                    {
                        parsed = RespCommand.DEL;
                        return true;
                    }
                    else if (len3p0 == GET)
                    {
                        parsed = RespCommand.GET;
                        return true;
                    }
                    else if (len3p0 == LCS)
                    {
                        parsed = RespCommand.LCS;
                        return true;
                    }
                    else if (len3p0 == SET)
                    {
                        parsed = RespCommand.SET;
                        return true;
                    }
                    else if (len3p0 == TTL)
                    {
                        parsed = RespCommand.TTL;
                        return true;
                    }
                    break;
                case 4:
                    var len4p0 = Unsafe.As<byte, uint>(ref cmdStartRef);
                    if (len4p0 == AUTH)
                    {
                        parsed = RespCommand.AUTH;
                        return true;
                    }
                    else if (len4p0 == DECR)
                    {
                        parsed = RespCommand.DECR;
                        return true;
                    }
                    else if (len4p0 == DUMP)
                    {
                        parsed = RespCommand.DUMP;
                        return true;
                    }
                    else if (len4p0 == ECHO)
                    {
                        parsed = RespCommand.ECHO;
                        return true;
                    }
                    else if (len4p0 == EVAL)
                    {
                        parsed = RespCommand.EVAL;
                        return true;
                    }
                    else if (len4p0 == EXEC)
                    {
                        parsed = RespCommand.EXEC;
                        return true;
                    }
                    else if (len4p0 == HDEL)
                    {
                        parsed = RespCommand.HDEL;
                        return true;
                    }
                    else if (len4p0 == HGET)
                    {
                        parsed = RespCommand.HGET;
                        return true;
                    }
                    else if (len4p0 == HLEN)
                    {
                        parsed = RespCommand.HLEN;
                        return true;
                    }
                    else if (len4p0 == HSET)
                    {
                        parsed = RespCommand.HSET;
                        return true;
                    }
                    else if (len4p0 == HTTL)
                    {
                        parsed = RespCommand.HTTL;
                        return true;
                    }
                    else if (len4p0 == INCR)
                    {
                        parsed = RespCommand.INCR;
                        return true;
                    }
                    else if (len4p0 == INFO)
                    {
                        parsed = RespCommand.INFO;
                        return true;
                    }
                    else if (len4p0 == KEYS)
                    {
                        parsed = RespCommand.KEYS;
                        return true;
                    }
                    else if (len4p0 == LLEN)
                    {
                        parsed = RespCommand.LLEN;
                        return true;
                    }
                    else if (len4p0 == LPOP)
                    {
                        parsed = RespCommand.LPOP;
                        return true;
                    }
                    else if (len4p0 == LPOS)
                    {
                        parsed = RespCommand.LPOS;
                        return true;
                    }
                    else if (len4p0 == LREM)
                    {
                        parsed = RespCommand.LREM;
                        return true;
                    }
                    else if (len4p0 == LSET)
                    {
                        parsed = RespCommand.LSET;
                        return true;
                    }
                    else if (len4p0 == MGET)
                    {
                        parsed = RespCommand.MGET;
                        return true;
                    }
                    else if (len4p0 == MSET)
                    {
                        parsed = RespCommand.MSET;
                        return true;
                    }
                    else if (len4p0 == PING)
                    {
                        parsed = RespCommand.PING;
                        return true;
                    }
                    else if (len4p0 == PTTL)
                    {
                        parsed = RespCommand.PTTL;
                        return true;
                    }
                    else if (len4p0 == QUIT)
                    {
                        parsed = RespCommand.QUIT;
                        return true;
                    }
                    else if (len4p0 == ROLE)
                    {
                        parsed = RespCommand.ROLE;
                        return true;
                    }
                    else if (len4p0 == RPOP)
                    {
                        parsed = RespCommand.RPOP;
                        return true;
                    }
                    else if (len4p0 == SADD)
                    {
                        parsed = RespCommand.SADD;
                        return true;
                    }
                    else if (len4p0 == SAVE)
                    {
                        parsed = RespCommand.SAVE;
                        return true;
                    }
                    else if (len4p0 == SCAN)
                    {
                        parsed = RespCommand.SCAN;
                        return true;
                    }
                    else if (len4p0 == SPOP)
                    {
                        parsed = RespCommand.SPOP;
                        return true;
                    }
                    else if (len4p0 == SREM)
                    {
                        parsed = RespCommand.SREM;
                        return true;
                    }
                    else if (len4p0 == TIME)
                    {
                        parsed = RespCommand.TIME;
                        return true;
                    }
                    else if (len4p0 == TYPE)
                    {
                        parsed = RespCommand.TYPE;
                        return true;
                    }
                    else if (len4p0 == ZADD)
                    {
                        parsed = RespCommand.ZADD;
                        return true;
                    }
                    else if (len4p0 == ZREM)
                    {
                        parsed = RespCommand.ZREM;
                        return true;
                    }
                    else if (len4p0 == ZTTL)
                    {
                        parsed = RespCommand.ZTTL;
                        return true;
                    }
                    break;
                case 5:
                    var len5p0 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, -1));
                    //if (len5p0 == BITOP)
                    //{
                    //    parsed = RespCommand.BITOP;
                    //    return true;
                    //}
                    //else 
                    if (len5p0 == BLPOP)
                    {
                        parsed = RespCommand.BLPOP;
                        return true;
                    }
                    else if (len5p0 == BRPOP)
                    {
                        parsed = RespCommand.BRPOP;
                        return true;
                    }
                    else if (len5p0 == DEBUG)
                    {
                        parsed = RespCommand.DEBUG;
                        return true;
                    }
                    else if (len5p0 == GETEX)
                    {
                        parsed = RespCommand.GETEX;
                        return true;
                    }
                    else if (len5p0 == HELLO)
                    {
                        parsed = RespCommand.HELLO;
                        return true;
                    }
                    else if (len5p0 == HKEYS)
                    {
                        parsed = RespCommand.HKEYS;
                        return true;
                    }
                    else if (len5p0 == HMGET)
                    {
                        parsed = RespCommand.HMGET;
                        return true;
                    }
                    else if (len5p0 == HMSET)
                    {
                        parsed = RespCommand.HMSET;
                        return true;
                    }
                    else if (len5p0 == HPTTL)
                    {
                        parsed = RespCommand.HPTTL;
                        return true;
                    }
                    else if (len5p0 == HSCAN)
                    {
                        parsed = RespCommand.HSCAN;
                        return true;
                    }
                    else if (len5p0 == HVALS)
                    {
                        parsed = RespCommand.HVALS;
                        return true;
                    }
                    else if (len5p0 == LMOVE)
                    {
                        parsed = RespCommand.LMOVE;
                        return true;
                    }
                    else if (len5p0 == LMPOP)
                    {
                        parsed = RespCommand.LMPOP;
                        return true;
                    }
                    else if (len5p0 == LPUSH)
                    {
                        parsed = RespCommand.LPUSH;
                        return true;
                    }
                    else if (len5p0 == LTRIM)
                    {
                        parsed = RespCommand.LTRIM;
                        return true;
                    }
                    else if (len5p0 == MULTI)
                    {
                        parsed = RespCommand.MULTI;
                        return true;
                    }
                    else if (len5p0 == PFADD)
                    {
                        parsed = RespCommand.PFADD;
                        return true;
                    }
                    else if (len5p0 == RPUSH)
                    {
                        parsed = RespCommand.RPUSH;
                        return true;
                    }
                    else if (len5p0 == SCARD)
                    {
                        parsed = RespCommand.SCARD;
                        return true;
                    }
                    else if (len5p0 == SDIFF)
                    {
                        parsed = RespCommand.SDIFF;
                        return true;
                    }
                    else if (len5p0 == SETEX)
                    {
                        parsed = RespCommand.SETEX;
                        return true;
                    }
                    else if (len5p0 == SMOVE)
                    {
                        parsed = RespCommand.SMOVE;
                        return true;
                    }
                    else if (len5p0 == SSCAN)
                    {
                        parsed = RespCommand.SSCAN;
                        return true;
                    }
                    else if (len5p0 == WATCH)
                    {
                        parsed = RespCommand.WATCH;
                        return true;
                    }
                    else if (len5p0 == ZCARD)
                    {
                        parsed = RespCommand.ZCARD;
                        return true;
                    }
                    else if (len5p0 == ZDIFF)
                    {
                        parsed = RespCommand.ZDIFF;
                        return true;
                    }
                    else if (len5p0 == ZMPOP)
                    {
                        parsed = RespCommand.ZMPOP;
                        return true;
                    }
                    else if (len5p0 == ZPTTL)
                    {
                        parsed = RespCommand.ZPTTL;
                        return true;
                    }
                    else if (len5p0 == ZRANK)
                    {
                        parsed = RespCommand.ZRANK;
                        return true;
                    }
                    else if (len5p0 == ZSCAN)
                    {
                        parsed = RespCommand.ZSCAN;
                        return true;
                    }
                    break;
                case 6:
                    var len6p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    if (len6p0 == APPEND)
                    {
                        parsed = RespCommand.APPEND;
                        return true;
                    }
                    else if (len6p0 == ASKING)
                    {
                        parsed = RespCommand.ASKING;
                        return true;
                    }
                    else if (len6p0 == BGSAVE)
                    {
                        parsed = RespCommand.BGSAVE;
                        return true;
                    }
                    else if (len6p0 == BITPOS)
                    {
                        parsed = RespCommand.BITPOS;
                        return true;
                    }
                    else if (len6p0 == BLMOVE)
                    {
                        parsed = RespCommand.BLMOVE;
                        return true;
                    }
                    else if (len6p0 == BLMPOP)
                    {
                        parsed = RespCommand.BLMPOP;
                        return true;
                    }
                    else if (len6p0 == BZMPOP)
                    {
                        parsed = RespCommand.BZMPOP;
                        return true;
                    }
                    //else if (len6p0 == CLIENT)
                    //{
                    //    parsed = RespCommand.CLIENT;
                    //    return true;
                    //}
                    //else if (len6p0 == CONFIG)
                    //{
                    //    parsed = RespCommand.CONFIG;
                    //    return true;
                    //}
                    //else if (len6p0 == COSCAN)
                    //{
                    //    parsed = RespCommand.COSCAN;
                    //    return true;
                    //}
                    else if (len6p0 == DBSIZE)
                    {
                        parsed = RespCommand.DBSIZE;
                        return true;
                    }
                    else if (len6p0 == DECRBY)
                    {
                        parsed = RespCommand.DECRBY;
                        return true;
                    }
                    else if (len6p0 == EXISTS)
                    {
                        parsed = RespCommand.EXISTS;
                        return true;
                    }
                    else if (len6p0 == EXPIRE)
                    {
                        parsed = RespCommand.EXPIRE;
                        return true;
                    }
                    else if (len6p0 == GEOADD)
                    {
                        parsed = RespCommand.GEOADD;
                        return true;
                    }
                    else if (len6p0 == GEOPOS)
                    {
                        parsed = RespCommand.GEOPOS;
                        return true;
                    }
                    else if (len6p0 == GETBIT)
                    {
                        parsed = RespCommand.GETBIT;
                        return true;
                    }
                    else if (len6p0 == GETDEL)
                    {
                        parsed = RespCommand.GETDEL;
                        return true;
                    }
                    else if (len6p0 == GETSET)
                    {
                        parsed = RespCommand.GETSET;
                        return true;
                    }
                    else if (len6p0 == HSETNX)
                    {
                        parsed = RespCommand.HSETNX;
                        return true;
                    }
                    else if (len6p0 == INCRBY)
                    {
                        parsed = RespCommand.INCRBY;
                        return true;
                    }
                    else if (len6p0 == LINDEX)
                    {
                        parsed = RespCommand.LINDEX;
                        return true;
                    }
                    else if (len6p0 == LPUSHX)
                    {
                        parsed = RespCommand.LPUSHX;
                        return true;
                    }
                    else if (len6p0 == LRANGE)
                    {
                        parsed = RespCommand.LRANGE;
                        return true;
                    }
                    //else if (len6p0 == MEMORY)
                    //{
                    //    parsed = RespCommand.MEMORY;
                    //    return true;
                    //}
                    else if (len6p0 == MSETNX)
                    {
                        parsed = RespCommand.MSETNX;
                        return true;
                    }
                    else if (len6p0 == PSETEX)
                    {
                        parsed = RespCommand.PSETEX;
                        return true;
                    }
                    //else if (len6p0 == PUBSUB)
                    //{
                    //    parsed = RespCommand.PUBSUB;
                    //    return true;
                    //}
                    else if (len6p0 == RENAME)
                    {
                        parsed = RespCommand.RENAME;
                        return true;
                    }
                    else if (len6p0 == RPUSHX)
                    {
                        parsed = RespCommand.RPUSHX;
                        return true;
                    }
                    else if (len6p0 == RUNTXP)
                    {
                        parsed = RespCommand.RUNTXP;
                        return true;
                    }
                    //else if (len6p0 == SCRIPT)
                    //{
                    //    parsed = RespCommand.SCRIPT;
                    //    return true;
                    //}
                    else if (len6p0 == SELECT)
                    {
                        parsed = RespCommand.SELECT;
                        return true;
                    }
                    else if (len6p0 == SETBIT)
                    {
                        parsed = RespCommand.SETBIT;
                        return true;
                    }
                    else if (len6p0 == SINTER)
                    {
                        parsed = RespCommand.SINTER;
                        return true;
                    }
                    else if (len6p0 == STRLEN)
                    {
                        parsed = RespCommand.STRLEN;
                        return true;
                    }
                    else if (len6p0 == SUBSTR)
                    {
                        parsed = RespCommand.SUBSTR;
                        return true;
                    }
                    else if (len6p0 == SUNION)
                    {
                        parsed = RespCommand.SUNION;
                        return true;
                    }
                    else if (len6p0 == UNLINK)
                    {
                        parsed = RespCommand.UNLINK;
                        return true;
                    }
                    else if (len6p0 == ZCOUNT)
                    {
                        parsed = RespCommand.ZCOUNT;
                        return true;
                    }
                    else if (len6p0 == ZINTER)
                    {
                        parsed = RespCommand.ZINTER;
                        return true;
                    }
                    else if (len6p0 == ZRANGE)
                    {
                        parsed = RespCommand.ZRANGE;
                        return true;
                    }
                    else if (len6p0 == ZSCORE)
                    {
                        parsed = RespCommand.ZSCORE;
                        return true;
                    }
                    else if (len6p0 == ZUNION)
                    {
                        parsed = RespCommand.ZUNION;
                        return true;
                    }
                    break;
                case 7:
                    var len7p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    //if (len7p0 == CLUSTER)
                    //{
                    //    parsed = RespCommand.CLUSTER;
                    //    return true;
                    //}
                    //else if (len7p0 == COMMAND)
                    //{
                    //    parsed = RespCommand.COMMAND;
                    //    return true;
                    //}
                    //else 
                    if (len7p0 == DISCARD)
                    {
                        parsed = RespCommand.DISCARD;
                        return true;
                    }
                    else if (len7p0 == EVALSHA)
                    {
                        parsed = RespCommand.EVALSHA;
                        return true;
                    }
                    else if (len7p0 == FLUSHDB)
                    {
                        parsed = RespCommand.FLUSHDB;
                        return true;
                    }
                    else if (len7p0 == FORCEGC)
                    {
                        parsed = RespCommand.FORCEGC;
                        return true;
                    }
                    else if (len7p0 == GEODIST)
                    {
                        parsed = RespCommand.GEODIST;
                        return true;
                    }
                    else if (len7p0 == GEOHASH)
                    {
                        parsed = RespCommand.GEOHASH;
                        return true;
                    }
                    else if (len7p0 == HEXISTS)
                    {
                        parsed = RespCommand.HEXISTS;
                        return true;
                    }
                    else if (len7p0 == HEXPIRE)
                    {
                        parsed = RespCommand.HEXPIRE;
                        return true;
                    }
                    else if (len7p0 == HGETALL)
                    {
                        parsed = RespCommand.HGETALL;
                        return true;
                    }
                    else if (len7p0 == HINCRBY)
                    {
                        parsed = RespCommand.HINCRBY;
                        return true;
                    }
                    else if (len7p0 == HSTRLEN)
                    {
                        parsed = RespCommand.HSTRLEN;
                        return true;
                    }
                    //else if (len7p0 == LATENCY)
                    //{
                    //    parsed = RespCommand.LATENCY;
                    //    return true;
                    //}
                    else if (len7p0 == LINSERT)
                    {
                        parsed = RespCommand.LINSERT;
                        return true;
                    }
                    else if (len7p0 == MIGRATE)
                    {
                        parsed = RespCommand.MIGRATE;
                        return true;
                    }
                    else if (len7p0 == MONITOR)
                    {
                        parsed = RespCommand.MONITOR;
                        return true;
                    }
                    else if (len7p0 == PERSIST)
                    {
                        parsed = RespCommand.PERSIST;
                        return true;
                    }
                    else if (len7p0 == PEXPIRE)
                    {
                        parsed = RespCommand.PEXPIRE;
                        return true;
                    }
                    else if (len7p0 == PFCOUNT)
                    {
                        parsed = RespCommand.PFCOUNT;
                        return true;
                    }
                    else if (len7p0 == PFMERGE)
                    {
                        parsed = RespCommand.PFMERGE;
                        return true;
                    }
                    else if (len7p0 == PUBLISH)
                    {
                        parsed = RespCommand.PUBLISH;
                        return true;
                    }
                    else if (len7p0 == PURGEBP)
                    {
                        parsed = RespCommand.PURGEBP;
                        return true;
                    }
                    else if (len7p0 == RESTORE)
                    {
                        parsed = RespCommand.RESTORE;
                        return true;
                    }
                    //else if (len7p0 == SLOWLOG)
                    //{
                    //    parsed = RespCommand.SLOWLOG;
                    //    return true;
                    //}
                    else if (len7p0 == UNWATCH)
                    {
                        parsed = RespCommand.UNWATCH;
                        return true;
                    }
                    else if (len7p0 == WATCHMS)
                    {
                        parsed = RespCommand.WATCHMS;
                        return true;
                    }
                    else if (len7p0 == WATCHOS)
                    {
                        parsed = RespCommand.WATCHOS;
                        return true;
                    }
                    else if (len7p0 == ZEXPIRE)
                    {
                        parsed = RespCommand.ZEXPIRE;
                        return true;
                    }
                    else if (len7p0 == ZINCRBY)
                    {
                        parsed = RespCommand.ZINCRBY;
                        return true;
                    }
                    else if (len7p0 == ZMSCORE)
                    {
                        parsed = RespCommand.ZMSCORE;
                        return true;
                    }
                    else if (len7p0 == ZPOPMAX)
                    {
                        parsed = RespCommand.ZPOPMAX;
                        return true;
                    }
                    else if (len7p0 == ZPOPMIN)
                    {
                        parsed = RespCommand.ZPOPMIN;
                        return true;
                    }
                    break;
                case 8:
                    var len8p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    if (len8p0 == BITCOUNT)
                    {
                        parsed = RespCommand.BITCOUNT;
                        return true;
                    }
                    else if (len8p0 == BITFIELD)
                    {
                        parsed = RespCommand.BITFIELD;
                        return true;
                    }
                    else if (len8p0 == BZPOPMAX)
                    {
                        parsed = RespCommand.BZPOPMAX;
                        return true;
                    }
                    else if (len8p0 == BZPOPMIN)
                    {
                        parsed = RespCommand.BZPOPMIN;
                        return true;
                    }
                    else if (len8p0 == EXPIREAT)
                    {
                        parsed = RespCommand.EXPIREAT;
                        return true;
                    }
                    else if (len8p0 == FAILOVER)
                    {
                        parsed = RespCommand.FAILOVER;
                        return true;
                    }
                    else if (len8p0 == FLUSHALL)
                    {
                        parsed = RespCommand.FLUSHALL;
                        return true;
                    }
                    else if (len8p0 == GETRANGE)
                    {
                        parsed = RespCommand.GETRANGE;
                        return true;
                    }
                    else if (len8p0 == HCOLLECT)
                    {
                        parsed = RespCommand.HCOLLECT;
                        return true;
                    }
                    else if (len8p0 == HPERSIST)
                    {
                        parsed = RespCommand.HPERSIST;
                        return true;
                    }
                    else if (len8p0 == HPEXPIRE)
                    {
                        parsed = RespCommand.HPEXPIRE;
                        return true;
                    }
                    else if (len8p0 == LASTSAVE)
                    {
                        parsed = RespCommand.LASTSAVE;
                        return true;
                    }
                    else if (len8p0 == READONLY)
                    {
                        parsed = RespCommand.READONLY;
                        return true;
                    }
                    else if (len8p0 == RENAMENX)
                    {
                        parsed = RespCommand.RENAMENX;
                        return true;
                    }
                    else if (len8p0 == SETRANGE)
                    {
                        parsed = RespCommand.SETRANGE;
                        return true;
                    }
                    else if (len8p0 == SMEMBERS)
                    {
                        parsed = RespCommand.SMEMBERS;
                        return true;
                    }
                    else if (len8p0 == SPUBLISH)
                    {
                        parsed = RespCommand.SPUBLISH;
                        return true;
                    }
                    else if (len8p0 == ZCOLLECT)
                    {
                        parsed = RespCommand.ZCOLLECT;
                        return true;
                    }
                    else if (len8p0 == ZPERSIST)
                    {
                        parsed = RespCommand.ZPERSIST;
                        return true;
                    }
                    else if (len8p0 == ZPEXPIRE)
                    {
                        parsed = RespCommand.ZPEXPIRE;
                        return true;
                    }
                    else if (len8p0 == ZREVRANK)
                    {
                        parsed = RespCommand.ZREVRANK;
                        return true;
                    }
                    break;
                case 9:
                    var len9p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len9p1 = Unsafe.As<byte, uint>(ref Unsafe.Add(ref cmdStartRef, 5));
                    if (len9p0 == COMMITAO && len9p1 == TAOF)
                    {
                        parsed = RespCommand.COMMITAOF;
                        return true;
                    }
                    else if (len9p0 == GEORADIU && len9p1 == DIUS)
                    {
                        parsed = RespCommand.GEORADIUS;
                        return true;
                    }
                    else if (len9p0 == GEOSEARC && len9p1 == ARCH)
                    {
                        parsed = RespCommand.GEOSEARCH;
                        return true;
                    }
                    else if (len9p0 == HEXPIREA && len9p1 == REAT)
                    {
                        parsed = RespCommand.HEXPIREAT;
                        return true;
                    }
                    else if (len9p0 == PEXPIREA && len9p1 == REAT)
                    {
                        parsed = RespCommand.PEXPIREAT;
                        return true;
                    }
                    else if (len9p0 == READWRIT && len9p1 == RITE)
                    {
                        parsed = RespCommand.READWRITE;
                        return true;
                    }
                    else if (len9p0 == REPLICAO && len9p1 == CAOF)
                    {
                        parsed = RespCommand.REPLICAOF;
                        return true;
                    }
                    else if (len9p0 == RPOPLPUS && len9p1 == PUSH)
                    {
                        parsed = RespCommand.RPOPLPUSH;
                        return true;
                    }
                    else if (len9p0 == SISMEMBE && len9p1 == MBER)
                    {
                        parsed = RespCommand.SISMEMBER;
                        return true;
                    }
                    else if (len9p0 == SUBSCRIB && len9p1 == RIBE)
                    {
                        parsed = RespCommand.SUBSCRIBE;
                        return true;
                    }
                    else if (len9p0 == ZEXPIREA && len9p1 == REAT)
                    {
                        parsed = RespCommand.ZEXPIREAT;
                        return true;
                    }
                    else if (len9p0 == ZLEXCOUN && len9p1 == OUNT)
                    {
                        parsed = RespCommand.ZLEXCOUNT;
                        return true;
                    }
                    else if (len9p0 == ZREVRANG && len9p1 == ANGE)
                    {
                        parsed = RespCommand.ZREVRANGE;
                        return true;
                    }
                    break;
                case 10:
                    var len10p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len10p1 = Unsafe.As<byte, uint>(ref Unsafe.Add(ref cmdStartRef, 6));
                    if (len10p0 == BRPOPLPU && len10p1 == PUSH)
                    {
                        parsed = RespCommand.BRPOPLPUSH;
                        return true;
                    }
                    else if (len10p0 == EXPIRETI && len10p1 == TIME)
                    {
                        parsed = RespCommand.EXPIRETIME;
                        return true;
                    }
                    else if (len10p0 == HPEXPIRE && len10p1 == REAT)
                    {
                        parsed = RespCommand.HPEXPIREAT;
                        return true;
                    }
                    else if (len10p0 == HRANDFIE && len10p1 == IELD)
                    {
                        parsed = RespCommand.HRANDFIELD;
                        return true;
                    }
                    else if (len10p0 == PSUBSCRI && len10p1 == RIBE)
                    {
                        parsed = RespCommand.PSUBSCRIBE;
                        return true;
                    }
                    else if (len10p0 == SDIFFSTO && len10p1 == TORE)
                    {
                        parsed = RespCommand.SDIFFSTORE;
                        return true;
                    }
                    //else if (len10p0 == SETKEEPT && len10p1 == PTTL)
                    //{
                    //    parsed = RespCommand.SETKEEPTTL;
                    //    return true;
                    //}
                    else if (len10p0 == SINTERCA && len10p1 == CARD)
                    {
                        parsed = RespCommand.SINTERCARD;
                        return true;
                    }
                    else if (len10p0 == SMISMEMB && len10p1 == MBER)
                    {
                        parsed = RespCommand.SMISMEMBER;
                        return true;
                    }
                    else if (len10p0 == SSUBSCRI && len10p1 == RIBE)
                    {
                        parsed = RespCommand.SSUBSCRIBE;
                        return true;
                    }
                    else if (len10p0 == ZDIFFSTO && len10p1 == TORE)
                    {
                        parsed = RespCommand.ZDIFFSTORE;
                        return true;
                    }
                    else if (len10p0 == ZINTERCA && len10p1 == CARD)
                    {
                        parsed = RespCommand.ZINTERCARD;
                        return true;
                    }
                    else if (len10p0 == ZPEXPIRE && len10p1 == REAT)
                    {
                        parsed = RespCommand.ZPEXPIREAT;
                        return true;
                    }
                    break;
                case 11:
                    var len11p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len11p1 = Unsafe.As<byte, uint>(ref Unsafe.Add(ref cmdStartRef, 7));
                    if (len11p0 == BITFIELD && len11p1 == D_RO)
                    {
                        parsed = RespCommand.BITFIELD_RO;
                        return true;
                    }
                    else if (len11p0 == GETWITHE && len11p1 == ETAG)
                    {
                        parsed = RespCommand.GETWITHETAG;
                        return true;
                    }
                    else if (len11p0 == HEXPIRET && len11p1 == TIME)
                    {
                        parsed = RespCommand.HEXPIRETIME;
                        return true;
                    }
                    else if (len11p0 == INCRBYFL && len11p1 == LOAT)
                    {
                        parsed = RespCommand.INCRBYFLOAT;
                        return true;
                    }
                    else if (len11p0 == PEXPIRET && len11p1 == TIME)
                    {
                        parsed = RespCommand.PEXPIRETIME;
                        return true;
                    }
                    else if (len11p0 == SECONDAR && len11p1 == RYOF)
                    {
                        parsed = RespCommand.SECONDARYOF;
                        return true;
                    }
                    else if (len11p0 == SINTERST && len11p1 == TORE)
                    {
                        parsed = RespCommand.SINTERSTORE;
                        return true;
                    }
                    else if (len11p0 == SRANDMEM && len11p1 == MBER)
                    {
                        parsed = RespCommand.SRANDMEMBER;
                        return true;
                    }
                    else if (len11p0 == SUNIONST && len11p1 == TORE)
                    {
                        parsed = RespCommand.SUNIONSTORE;
                        return true;
                    }
                    else if (len11p0 == UNSUBSCR && len11p1 == RIBE)
                    {
                        parsed = RespCommand.UNSUBSCRIBE;
                        return true;
                    }
                    else if (len11p0 == ZEXPIRET && len11p1 == TIME)
                    {
                        parsed = RespCommand.ZEXPIRETIME;
                        return true;
                    }
                    else if (len11p0 == ZINTERST && len11p1 == TORE)
                    {
                        parsed = RespCommand.ZINTERSTORE;
                        return true;
                    }
                    else if (len11p0 == ZRANDMEM && len11p1 == MBER)
                    {
                        parsed = RespCommand.ZRANDMEMBER;
                        return true;
                    }
                    else if (len11p0 == ZRANGEBY && len11p1 == YLEX)
                    {
                        parsed = RespCommand.ZRANGEBYLEX;
                        return true;
                    }
                    else if (len11p0 == ZRANGEST && len11p1 == TORE)
                    {
                        parsed = RespCommand.ZRANGESTORE;
                        return true;
                    }
                    else if (len11p0 == ZUNIONST && len11p1 == TORE)
                    {
                        parsed = RespCommand.ZUNIONSTORE;
                        return true;
                    }
                    break;
                case 12:
                    var len12p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len12p1 = Unsafe.As<byte, uint>(ref Unsafe.Add(ref cmdStartRef, 8));
                    if (len12p0 == GEORADIU && len12p1 == S_RO)
                    {
                        parsed = RespCommand.GEORADIUS_RO;
                        return true;
                    }
                    else if (len12p0 == HINCRBYF && len12p1 == LOAT)
                    {
                        parsed = RespCommand.HINCRBYFLOAT;
                        return true;
                    }
                    else if (len12p0 == HPEXPIRE && len12p1 == TIME)
                    {
                        parsed = RespCommand.HPEXPIRETIME;
                        return true;
                    }
                    //else if (len12p0 == MEMORY_U && len12p1 == SAGE)
                    //{
                    //    parsed = RespCommand.MEMORY_USAGE;
                    //    return true;
                    //}
                    else if (len12p0 == PUNSUBSC && len12p1 == RIBE)
                    {
                        parsed = RespCommand.PUNSUBSCRIBE;
                        return true;
                    }
                    //else if (len12p0 == SETKEEPT && len12p1 == TLXX)
                    //{
                    //    parsed = RespCommand.SETKEEPTTLXX;
                    //    return true;
                    //}
                    else if (len12p0 == ZPEXPIRE && len12p1 == TIME)
                    {
                        parsed = RespCommand.ZPEXPIRETIME;
                        return true;
                    }
                    break;
                case 13:
                    var len13p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len13p1 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, 5));
                    if (len13p0 == GETIFNOT && len13p1 == NOTMATCH)
                    {
                        parsed = RespCommand.GETIFNOTMATCH;
                        return true;
                    }
                    else if (len13p0 == ZRANGEBY && len13p1 == EBYSCORE)
                    {
                        parsed = RespCommand.ZRANGEBYSCORE;
                        return true;
                    }
                    break;
                case 14:
                    var len14p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len14p1 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, 6));
                    if (len14p0 == GEOSEARC && len14p1 == RCHSTORE)
                    {
                        parsed = RespCommand.GEOSEARCHSTORE;
                        return true;
                    }
                    else if (len14p0 == ZREMRANG && len14p1 == NGEBYLEX)
                    {
                        parsed = RespCommand.ZREMRANGEBYLEX;
                        return true;
                    }
                    else if (len14p0 == ZREVRANG && len14p1 == NGEBYLEX)
                    {
                        parsed = RespCommand.ZREVRANGEBYLEX;
                        return true;
                    }
                    break;
                case 15:
                    var len15p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len15p1 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, 7));
                    if (len15p0 == ZREMRANG && len15p1 == GEBYRANK)
                    {
                        parsed = RespCommand.ZREMRANGEBYRANK;
                        return true;
                    }
                    break;
                case 16:
                    var len16p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len16p1 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, 8));
                    if (len16p0 == ZREMRANG && len16p1 == EBYSCORE)
                    {
                        parsed = RespCommand.ZREMRANGEBYSCORE;
                        return true;
                    }
                    else if (len16p0 == ZREVRANG && len16p1 == EBYSCORE)
                    {
                        parsed = RespCommand.ZREVRANGEBYSCORE;
                        return true;
                    }
                    break;
                case 17:
                    var len17p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len17p1 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, 8));
                    var len17p2 = Unsafe.As<byte, uint>(ref Unsafe.Add(ref cmdStartRef, 13));
                    if (len17p0 == GEORADIU && len17p1 == SBYMEMBE && len17p2 == MBER)
                    {
                        parsed = RespCommand.GEORADIUSBYMEMBER;
                        return true;
                    }
                    break;
                case 20:
                    var len20p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len20p1 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, 8));
                    var len20p2 = Unsafe.As<byte, uint>(ref Unsafe.Add(ref cmdStartRef, 16));
                    if (len20p0 == GEORADIU && len20p1 == SBYMEMBE && len20p2 == R_RO)
                    {
                        parsed = RespCommand.GEORADIUSBYMEMBER_RO;
                        return true;
                    }
                    break;
                default:
                    Unsafe.SkipInit(out parsed);
                    return false;
            }
            if (firstPass)
            {
                Ascii.ToUpperInPlace(cmdBuffer[commandStart..commandEnd], out _);
                firstPass = false;
                goto tryAgain;
            }

            Unsafe.SkipInit(out parsed);
            return false;
        }

        /// <summary>
        /// Version of <see cref="TryParseCommand_Enum(Span{byte}, int, int, out RespCommand)"/> that uses intrinsics for fast hashing.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryParseCommand_Hash(Span<byte> cmdBuffer, int commandStart, int commandEnd, out RespCommand parsed)
        {
            ref var cmdStartRef = ref Unsafe.Add(ref MemoryMarshal.GetReference(cmdBuffer), commandStart);
            var firstPass = true;
            var cmdLen = commandEnd - commandStart;

        tryAgain:
            switch (cmdLen)
            {
                case 3:
                    var len3p0 = Unsafe.As<byte, uint>(ref cmdStartRef);
                    var calculatedValue3 = len3p0;
                    calculatedValue3 ^= 265420117U;
                    var len3Ix = (byte)(calculatedValue3 % 32);
                    var len3Value = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash3Lookup), len3Ix);
                    parsed = (RespCommand)len3Value;
                    if ((len3Value >> 32) == len3p0)
                    {
                        return true;
                    }
                    break;
                case 4:
                    var len4p0 = Unsafe.As<byte, uint>(ref cmdStartRef);
                    var calculatedValue4 = len4p0;
                    calculatedValue4 ^= 682004627U;
                    var len4Ix = (byte)(calculatedValue4 % 63);
                    var len4Value = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash4Lookup), len4Ix);
                    parsed = (RespCommand)len4Value;
                    if ((len4Value >> 32) == len4p0)
                    {
                        return true;
                    }
                    break;
                case 5:
                    var len5p0 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, -1));
                    var len5Calc = len5p0;
                    len5Calc ^= 4151764434U;
                    var len5Ix = (byte)((uint)len5Calc % (byte)177);
                    ref var len5ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash5Lookup), len5Ix * 2);

                    parsed = (RespCommand)len5ValueRef;
                    if (Unsafe.Add(ref len5ValueRef, 1) == len5p0)
                    {
                        return true;
                    }
                    break;
                case 6:
                    var len6p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len6Calc = len6p0;
                    len6Calc ^= 3829026875U;
                    var len6Ix = (byte)((uint)len6Calc % (byte)140);
                    ref var len6ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash6Lookup), len6Ix * 2);

                    parsed = (RespCommand)len6ValueRef;
                    if (Unsafe.Add(ref len6ValueRef, 1) == len6p0)
                    {
                        return true;
                    }
                    break;
                case 7:
                    var len7p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len7Calc = len7p0;
                    len7Calc ^= (len7p0 >> (32 - 0));
                    len7Calc ^= 3263854412U;
                    var len7Ix = (byte)((uint)len7Calc % (byte)127);
                    ref var len7ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash7Lookup), len7Ix * 2);

                    parsed = (RespCommand)len7ValueRef;
                    if (Unsafe.Add(ref len7ValueRef, 1) == len7p0)
                    {
                        return true;
                    }
                    break;
                case 8:
                    var len8p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len8Calc = len8p0;
                    len8Calc ^= (len8p0 >> (32 - 0));
                    len8Calc ^= 1216371613U;
                    var len8Ix = (byte)((uint)len8Calc % (byte)27);
                    ref var len8ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash8Lookup), len8Ix * 2);

                    parsed = (RespCommand)len8ValueRef;
                    if (Unsafe.Add(ref len8ValueRef, 1) == len8p0)
                    {
                        return true;
                    }
                    break;
                case 9:
                    var len9p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len9p1 = Unsafe.As<byte, uint>(ref Unsafe.Add(ref cmdStartRef, 5));

                    var len9Val = len9p1;
                    len9Val ^= (uint)(len9p0 >> 0);
                    len9Val ^= 3558180048U;
                    var len9Ix = (byte)(len9Val % 131);
                    ref var len9ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash9Lookup), len9Ix * 2);

                    var len9Val0 = len9ValueRef;
                    var len9Val1 = Unsafe.Add(ref len9ValueRef, 1);

                    parsed = (RespCommand)len9Val0;
                    if (len9Val1 == len9p0 && (uint)(len9Val0 >> 32) == len9p1)
                    {
                        return true;
                    }
                    break;
                case 10:
                    var len10p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len10p1 = Unsafe.As<byte, uint>(ref Unsafe.Add(ref cmdStartRef, 6));

                    var len10Val = len10p1;
                    len10Val ^= (uint)(len10p0 >> 0);
                    len10Val ^= 3690845978U;
                    var len10Ix = (byte)(len10Val % 131);
                    ref var len10ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash10Lookup), len10Ix * 2);

                    var len10Val0 = len10ValueRef;
                    var len10Val1 = Unsafe.Add(ref len10ValueRef, 1);

                    parsed = (RespCommand)len10Val0;
                    if (len10Val1 == len10p0 && (uint)(len10Val0 >> 32) == len10p1)
                    {
                        return true;
                    }
                    break;
                case 11:
                    var len11p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len11p1 = Unsafe.As<byte, uint>(ref Unsafe.Add(ref cmdStartRef, 7));

                    var len11Val = len11p1;
                    len11Val ^= (uint)(len11p0 >> 0);
                    len11Val ^= 929541917U;
                    var len11Ix = (byte)(len11Val % 60);
                    ref var len11ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash11Lookup), len11Ix * 2);

                    var len11Val0 = len11ValueRef;
                    var len11Val1 = Unsafe.Add(ref len11ValueRef, 1);

                    parsed = (RespCommand)len11Val0;
                    if (len11Val1 == len11p0 && (uint)(len11Val0 >> 32) == len11p1)
                    {
                        return true;
                    }
                    break;
                case 12:
                    var len12p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len12p1 = Unsafe.As<byte, uint>(ref Unsafe.Add(ref cmdStartRef, 8));

                    var len12Val = len12p1;
                    len12Val ^= (uint)(len12p0 >> 0);
                    len12Val ^= 2663684139U;
                    var len12Ix = (byte)(len12Val % 32);
                    ref var len12ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash12Lookup), len12Ix * 2);

                    var len12Val0 = len12ValueRef;
                    var len12Val1 = Unsafe.Add(ref len12ValueRef, 1);

                    parsed = (RespCommand)len12Val0;
                    if (len12Val1 == len12p0 && (uint)(len12Val0 >> 32) == len12p1)
                    {
                        return true;
                    }
                    break;
                case 13:
                    var len13p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len13p1 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, 5));
                    var len13Val = (uint)(len13p0 >> 0);
                    len13Val ^= (uint)(len13p1 >> 0);
                    len13Val ^= 2327279063U;
                    var len13Ix = (byte)(len13Val % 4);
                    ref var len13ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash13Lookup), len13Ix * 3);

                    var len13Val0 = len13ValueRef;
                    var len13Val1 = Unsafe.Add(ref len13ValueRef, 1);
                    var len13Val2 = Unsafe.Add(ref len13ValueRef, 2);

                    parsed = (RespCommand)len13Val0;
                    if (len13Val1 == len13p0 && len13Val2 == len13p1)
                    {
                        return true;
                    }
                    break;
                case 14:
                    var len14p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len14p1 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, 6));
                    var len14Val = (uint)(len14p0 >> 0);
                    len14Val ^= (uint)(len14p1 >> 0);
                    len14Val ^= 100014858U;
                    var len14Ix = (byte)(len14Val % 129);
                    ref var len14ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash14Lookup), len14Ix * 3);

                    var len14Val0 = len14ValueRef;
                    var len14Val1 = Unsafe.Add(ref len14ValueRef, 1);
                    var len14Val2 = Unsafe.Add(ref len14ValueRef, 2);

                    parsed = (RespCommand)len14Val0;
                    if (len14Val1 == len14p0 && len14Val2 == len14p1)
                    {
                        return true;
                    }
                    break;
                case 15:
                    parsed = RespCommand.ZREMRANGEBYRANK;
                    var len15p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len15p1 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, 7));
                    if (len15p0 == ZREMRANG && len15p1 == GEBYRANK)
                    {
                        return true;
                    }
                    break;
                case 16:
                    var len16p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len16p1 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, 8));
                    var len16Val = (uint)(len16p0 >> 24);
                    len16Val ^= (uint)(len16p1 >> 0);
                    var len16Ix = (byte)(len16Val % 2);
                    ref var len16ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash16Lookup), len16Ix * 3);

                    var len16Val0 = len16ValueRef;
                    var len16Val1 = Unsafe.Add(ref len16ValueRef, 1);
                    var len16Val2 = Unsafe.Add(ref len16ValueRef, 2);

                    parsed = (RespCommand)len16Val0;
                    if (len16Val1 == len16p0 && len16Val2 == len16p1)
                    {
                        return true;
                    }
                    break;
                case 17:
                    parsed = RespCommand.GEORADIUSBYMEMBER;
                    var len17p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len17p1 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, 8));
                    var len17p2 = Unsafe.As<byte, uint>(ref Unsafe.Add(ref cmdStartRef, 13));
                    if (len17p0 == GEORADIU && len17p1 == SBYMEMBE && len17p2 == MBER)
                    {
                        return true;
                    }
                    break;
                case 20:
                    parsed = RespCommand.GEORADIUSBYMEMBER_RO;
                    var len20p0 = Unsafe.As<byte, ulong>(ref cmdStartRef);
                    var len20p1 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, 8));
                    var len20p2 = Unsafe.As<byte, uint>(ref Unsafe.Add(ref cmdStartRef, 16));
                    if (len20p0 == GEORADIU && len20p1 == SBYMEMBE && len20p2 == R_RO)
                    {
                        return true;
                    }
                    break;
                default:
                    Unsafe.SkipInit(out parsed);
                    return false;
            }

            if (firstPass)
            {
                Ascii.ToUpperInPlace(cmdBuffer[commandStart..commandEnd], out _);
                firstPass = false;
                goto tryAgain;
            }

            Unsafe.SkipInit(out parsed);
            return false;
        }

        /// <summary>
        /// Version of <see cref="TryParseCommand_Hash(Span{byte}, int, int, out RespCommand)"/> but simplified.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryParseCommand_Hash2(Span<byte> cmdBuffer, int commandStart, int commandEnd, out RespCommand parsed)
        {
            const uint LOWER_TO_UPPER_UINT = ~0x2020_2020U;
            const ulong LOWER_TO_UPPER_ULONG = ~0x2020_2020__2020_2020UL;

            ref var cmdStartRef = ref Unsafe.Add(ref MemoryMarshal.GetReference(cmdBuffer), commandStart);
            var cmdLen = commandEnd - commandStart;

            switch (cmdLen)
            {
                case 3:
                    var len3p0 = Unsafe.As<byte, uint>(ref cmdStartRef) & LOWER_TO_UPPER_UINT;
                    var calculatedValue3 = len3p0;
                    calculatedValue3 ^= 265420117U;
                    var len3Ix = (byte)(calculatedValue3 % 32);
                    var len3Value = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash3Lookup), len3Ix);
                    parsed = (RespCommand)len3Value;
                    return ((len3Value >> 32) == len3p0);
                case 4:
                    var len4p0 = Unsafe.As<byte, uint>(ref cmdStartRef) & LOWER_TO_UPPER_UINT;
                    var calculatedValue4 = len4p0;
                    calculatedValue4 ^= 682004627U;
                    var len4Ix = (byte)(calculatedValue4 % 63);
                    var len4Value = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash4Lookup), len4Ix);
                    parsed = (RespCommand)len4Value;
                    return ((len4Value >> 32) == len4p0);
                case 5:
                    var len5p0 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, -1)) & LOWER_TO_UPPER_ULONG;
                    var len5Calc = len5p0;
                    len5Calc ^= 4151764434U;
                    var len5Ix = (byte)((uint)len5Calc % (byte)177);
                    ref var len5ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash5Lookup), len5Ix * 2);

                    parsed = (RespCommand)len5ValueRef;
                    return (Unsafe.Add(ref len5ValueRef, 1) == len5p0);
                case 6:
                    var len6p0 = Unsafe.As<byte, ulong>(ref cmdStartRef) & LOWER_TO_UPPER_ULONG;
                    var len6Calc = len6p0;
                    len6Calc ^= 3829026875U;
                    var len6Ix = (byte)((uint)len6Calc % (byte)140);
                    ref var len6ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash6Lookup), len6Ix * 2);

                    parsed = (RespCommand)len6ValueRef;
                    return (Unsafe.Add(ref len6ValueRef, 1) == len6p0);
                case 7:
                    var len7p0 = Unsafe.As<byte, ulong>(ref cmdStartRef) & LOWER_TO_UPPER_ULONG;
                    var len7Calc = len7p0;
                    len7Calc ^= (len7p0 >> (32 - 0));
                    len7Calc ^= 3263854412U;
                    var len7Ix = (byte)((uint)len7Calc % (byte)127);
                    ref var len7ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash7Lookup), len7Ix * 2);

                    parsed = (RespCommand)len7ValueRef;
                    return (Unsafe.Add(ref len7ValueRef, 1) == len7p0);
                case 8:
                    var len8p0 = Unsafe.As<byte, ulong>(ref cmdStartRef) & LOWER_TO_UPPER_ULONG;
                    var len8Calc = len8p0;
                    len8Calc ^= (len8p0 >> (32 - 0));
                    len8Calc ^= 1216371613U;
                    var len8Ix = (byte)((uint)len8Calc % (byte)27);
                    ref var len8ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash8Lookup), len8Ix * 2);

                    parsed = (RespCommand)len8ValueRef;
                    return (Unsafe.Add(ref len8ValueRef, 1) == len8p0);
                case 9:
                    var len9p0 = Unsafe.As<byte, ulong>(ref cmdStartRef) & LOWER_TO_UPPER_ULONG;
                    var len9p1 = Unsafe.As<byte, uint>(ref Unsafe.Add(ref cmdStartRef, 5)) & LOWER_TO_UPPER_UINT;

                    var len9Val = len9p1;
                    len9Val ^= (uint)(len9p0 >> 0);
                    len9Val ^= 3558180048U;
                    var len9Ix = (byte)(len9Val % 131);
                    ref var len9ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash9Lookup), len9Ix * 2);

                    var len9Val0 = len9ValueRef;
                    var len9Val1 = Unsafe.Add(ref len9ValueRef, 1);

                    parsed = (RespCommand)len9Val0;
                    return (len9Val1 == len9p0 & (uint)(len9Val0 >> 32) == len9p1);
                case 10:
                    var len10p0 = Unsafe.As<byte, ulong>(ref cmdStartRef) & LOWER_TO_UPPER_ULONG;
                    var len10p1 = Unsafe.As<byte, uint>(ref Unsafe.Add(ref cmdStartRef, 6)) & LOWER_TO_UPPER_UINT;

                    var len10Val = len10p1;
                    len10Val ^= (uint)(len10p0 >> 0);
                    len10Val ^= 3690845978U;
                    var len10Ix = (byte)(len10Val % 131);
                    ref var len10ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash10Lookup), len10Ix * 2);

                    var len10Val0 = len10ValueRef;
                    var len10Val1 = Unsafe.Add(ref len10ValueRef, 1);

                    parsed = (RespCommand)len10Val0;
                    return (len10Val1 == len10p0 & (uint)(len10Val0 >> 32) == len10p1);
                case 11:
                    var len11p0 = Unsafe.As<byte, ulong>(ref cmdStartRef) & LOWER_TO_UPPER_ULONG;
                    var len11p1 = Unsafe.As<byte, uint>(ref Unsafe.Add(ref cmdStartRef, 7)) & LOWER_TO_UPPER_UINT;

                    var len11Val = len11p1;
                    len11Val ^= (uint)(len11p0 >> 0);
                    len11Val ^= 929541917U;
                    var len11Ix = (byte)(len11Val % 60);
                    ref var len11ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash11Lookup), len11Ix * 2);

                    var len11Val0 = len11ValueRef;
                    var len11Val1 = Unsafe.Add(ref len11ValueRef, 1);

                    parsed = (RespCommand)len11Val0;
                    return (len11Val1 == len11p0 && (uint)(len11Val0 >> 32) == len11p1);
                case 12:
                    var len12p0 = Unsafe.As<byte, ulong>(ref cmdStartRef) & LOWER_TO_UPPER_ULONG;
                    var len12p1 = Unsafe.As<byte, uint>(ref Unsafe.Add(ref cmdStartRef, 8)) & LOWER_TO_UPPER_UINT;

                    var len12Val = len12p1;
                    len12Val ^= (uint)(len12p0 >> 0);
                    len12Val ^= 2663684139U;
                    var len12Ix = (byte)(len12Val % 32);
                    ref var len12ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash12Lookup), len12Ix * 2);

                    var len12Val0 = len12ValueRef;
                    var len12Val1 = Unsafe.Add(ref len12ValueRef, 1);

                    parsed = (RespCommand)len12Val0;
                    return (len12Val1 == len12p0 & (uint)(len12Val0 >> 32) == len12p1);
                case 13:
                    var len13p0 = Unsafe.As<byte, ulong>(ref cmdStartRef) & LOWER_TO_UPPER_ULONG;
                    var len13p1 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, 5)) & LOWER_TO_UPPER_ULONG;
                    var len13Val = (uint)(len13p0 >> 0);
                    len13Val ^= (uint)(len13p1 >> 0);
                    len13Val ^= 2327279063U;
                    var len13Ix = (byte)(len13Val % 4);
                    ref var len13ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash13Lookup), len13Ix * 3);

                    var len13Val0 = len13ValueRef;
                    var len13Val1 = Unsafe.Add(ref len13ValueRef, 1);
                    var len13Val2 = Unsafe.Add(ref len13ValueRef, 2);

                    parsed = (RespCommand)len13Val0;
                    return (len13Val1 == len13p0 & len13Val2 == len13p1);
                case 14:
                    var len14p0 = Unsafe.As<byte, ulong>(ref cmdStartRef) & LOWER_TO_UPPER_ULONG;
                    var len14p1 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, 6)) & LOWER_TO_UPPER_ULONG;
                    var len14Val = (uint)(len14p0 >> 0);
                    len14Val ^= (uint)(len14p1 >> 0);
                    len14Val ^= 100014858U;
                    var len14Ix = (byte)(len14Val % 129);
                    ref var len14ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash14Lookup), len14Ix * 3);

                    var len14Val0 = len14ValueRef;
                    var len14Val1 = Unsafe.Add(ref len14ValueRef, 1);
                    var len14Val2 = Unsafe.Add(ref len14ValueRef, 2);

                    parsed = (RespCommand)len14Val0;
                    return (len14Val1 == len14p0 & len14Val2 == len14p1);
                case 15:
                    parsed = RespCommand.ZREMRANGEBYRANK;
                    var len15p0 = Unsafe.As<byte, ulong>(ref cmdStartRef) & LOWER_TO_UPPER_ULONG;
                    var len15p1 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, 7)) & LOWER_TO_UPPER_ULONG;
                    return (len15p0 == ZREMRANG && len15p1 == GEBYRANK);
                case 16:
                    var len16p0 = Unsafe.As<byte, ulong>(ref cmdStartRef) & LOWER_TO_UPPER_ULONG;
                    var len16p1 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, 8)) & LOWER_TO_UPPER_ULONG;
                    var len16Val = (uint)(len16p0 >> 24);
                    len16Val ^= (uint)(len16p1 >> 0);
                    var len16Ix = (byte)(len16Val % 2);
                    ref var len16ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Hash16Lookup), len16Ix * 3);

                    var len16Val0 = len16ValueRef;
                    var len16Val1 = Unsafe.Add(ref len16ValueRef, 1);
                    var len16Val2 = Unsafe.Add(ref len16ValueRef, 2);

                    parsed = (RespCommand)len16Val0;
                    return (len16Val1 == len16p0 & len16Val2 == len16p1);
                case 17:
                    parsed = RespCommand.GEORADIUSBYMEMBER;
                    var len17p0 = Unsafe.As<byte, ulong>(ref cmdStartRef) & LOWER_TO_UPPER_ULONG;
                    var len17p1 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, 8)) & LOWER_TO_UPPER_ULONG;
                    var len17p2 = Unsafe.As<byte, uint>(ref Unsafe.Add(ref cmdStartRef, 13)) & LOWER_TO_UPPER_UINT;
                    return (len17p0 == GEORADIU & len17p1 == SBYMEMBE & len17p2 == MBER);
                case 20:
                    parsed = RespCommand.GEORADIUSBYMEMBER_RO;
                    var len20p0 = Unsafe.As<byte, ulong>(ref cmdStartRef) & LOWER_TO_UPPER_ULONG;
                    var len20p1 = Unsafe.As<byte, ulong>(ref Unsafe.Add(ref cmdStartRef, 8)) & LOWER_TO_UPPER_ULONG;
                    var len20p2 = Unsafe.As<byte, uint>(ref Unsafe.Add(ref cmdStartRef, 16)) & LOWER_TO_UPPER_UINT;
                    return (len20p0 == GEORADIU & len20p1 == SBYMEMBE & len20p2 == R_RO);
                default:
                    Unsafe.SkipInit(out parsed);
                    return false;
            }
        }

        public static readonly ulong[] Hash3Lookup = [0, ((ulong)TTL << 32) | (ulong)RespCommand.TTL, 0, 0, 0, 0, ((ulong)SET << 32) | (ulong)RespCommand.SET, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)DEL << 32) | (ulong)RespCommand.DEL, ((ulong)GET << 32) | (ulong)RespCommand.GET, 0, 0 | (ulong)0, 0, 0, 0, 0, ((ulong)LCS << 32) | (ulong)RespCommand.LCS, 0, 0, 0, 0, 0, 0, 0,];
        public static readonly ulong[] Hash4Lookup = [((ulong)SAVE << 32) | (ulong)RespCommand.SAVE, 0, ((ulong)DECR << 32) | (ulong)RespCommand.DECR, 0, 0, ((ulong)INFO << 32) | (ulong)RespCommand.INFO, 0, ((ulong)LREM << 32) | (ulong)RespCommand.LREM, ((ulong)TYPE << 32) | (ulong)RespCommand.TYPE, 0, 0, ((ulong)HDEL << 32) | (ulong)RespCommand.HDEL, 0, 0, ((ulong)EVAL << 32) | (ulong)RespCommand.EVAL, 0, ((ulong)QUIT << 32) | (ulong)RespCommand.QUIT, 0, 0, 0, ((ulong)SPOP << 32) | (ulong)RespCommand.SPOP, ((ulong)RPOP << 32) | (ulong)RespCommand.RPOP, ((ulong)TIME << 32) | (ulong)RespCommand.TIME, 0, ((ulong)ROLE << 32) | (ulong)RespCommand.ROLE, 0, 0, ((ulong)SADD << 32) | (ulong)RespCommand.SADD, ((ulong)EXEC << 32) | (ulong)RespCommand.EXEC, 0, ((ulong)HSET << 32) | (ulong)RespCommand.HSET, 0, ((ulong)DUMP << 32) | (ulong)RespCommand.DUMP, ((ulong)MSET << 32) | (ulong)RespCommand.MSET, ((ulong)LSET << 32) | (ulong)RespCommand.LSET, ((ulong)PING << 32) | (ulong)RespCommand.PING, ((ulong)ZADD << 32) | (ulong)RespCommand.ZADD, ((ulong)PTTL << 32) | (ulong)RespCommand.PTTL, 0, ((ulong)SREM << 32) | (ulong)RespCommand.SREM, 0, ((ulong)INCR << 32) | (ulong)RespCommand.INCR, ((ulong)KEYS << 32) | (ulong)RespCommand.KEYS, ((ulong)ZTTL << 32) | (ulong)RespCommand.ZTTL, 0, ((ulong)HLEN << 32) | (ulong)RespCommand.HLEN, ((ulong)SCAN << 32) | (ulong)RespCommand.SCAN, ((ulong)HGET << 32) | (ulong)RespCommand.HGET, ((ulong)ZREM << 32) | (ulong)RespCommand.ZREM, ((ulong)LLEN << 32) | (ulong)RespCommand.LLEN, ((ulong)MGET << 32) | (ulong)RespCommand.MGET, ((ulong)LPOP << 32) | (ulong)RespCommand.LPOP, ((ulong)AUTH << 32) | (ulong)RespCommand.AUTH, 0, ((ulong)LPOS << 32) | (ulong)RespCommand.LPOS, ((ulong)ECHO << 32) | (ulong)RespCommand.ECHO, 0, 0, 0, 0, 0, ((ulong)HTTL << 32) | (ulong)RespCommand.HTTL, 0, 0,];
        public static readonly ulong[] Hash5Lookup = [(ulong)RespCommand.HELLO, HELLO, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.LTRIM, LTRIM, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.PFADD, PFADD, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.HMGET, HMGET, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.SETEX, SETEX, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.GETEX, GETEX, (ulong)RespCommand.SDIFF, SDIFF, 0, 0, 0, 0, (ulong)RespCommand.ZDIFF, ZDIFF, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.HVALS, HVALS, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.DEBUG, DEBUG, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.ZRANK, ZRANK, 0, 0, (ulong)RespCommand.ZMPOP, ZMPOP, 0, 0, (ulong)RespCommand.SMOVE, SMOVE, 0, 0, 0, 0, (ulong)RespCommand.BLPOP, BLPOP, (ulong)RespCommand.WATCH, WATCH, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.LPUSH, LPUSH, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.MULTI, MULTI, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.HSCAN, HSCAN, 0, 0, 0, 0, (ulong)RespCommand.SSCAN, SSCAN, 0, 0, (ulong)RespCommand.LMPOP, LMPOP, (ulong)RespCommand.ZSCAN, ZSCAN, 0, 0, 0, 0, (ulong)0, 0, (ulong)RespCommand.LMOVE, LMOVE, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.BRPOP, BRPOP, (ulong)RespCommand.RPUSH, RPUSH, 0, 0, (ulong)RespCommand.HKEYS, HKEYS, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.HMSET, HMSET, 0, 0, (ulong)RespCommand.HPTTL, HPTTL, (ulong)RespCommand.SCARD, SCARD, 0, 0, 0, 0, (ulong)RespCommand.ZCARD, ZCARD, 0, 0, (ulong)RespCommand.ZPTTL, ZPTTL, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,];
        public static readonly ulong[] Hash6Lookup = [(ulong)RespCommand.GETDEL, GETDEL, 0, 0, 0, 0, (ulong)RespCommand.PSETEX, PSETEX, (ulong)RespCommand.SELECT, SELECT, (ulong)RespCommand.BZMPOP, BZMPOP, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.HSETNX, HSETNX, (ulong)RespCommand.GEOADD, GEOADD, 0, 0, (ulong)RespCommand.MSETNX, MSETNX, 0, 0, 0, 0, (ulong)RespCommand.ZRANGE, ZRANGE, (ulong)RespCommand.EXPIRE, EXPIRE, 0, 0, (ulong)RespCommand.STRLEN, STRLEN, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.LRANGE, LRANGE, 0, 0, 0, 0, (ulong)RespCommand.INCRBY, INCRBY, (ulong)RespCommand.DBSIZE, DBSIZE, 0, 0, (ulong)RespCommand.ZCOUNT, ZCOUNT, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.BGSAVE, BGSAVE, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.APPEND, APPEND, 0, 0, (ulong)RespCommand.SETBIT, SETBIT, (ulong)RespCommand.RUNTXP, RUNTXP, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.BITPOS, BITPOS, 0, 0, 0, 0, (ulong)0, 0, 0, 0, 0, 0, 0, 0, (ulong)0, 0, (ulong)RespCommand.RPUSHX, RPUSHX, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.GETBIT, GETBIT, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.ZUNION, ZUNION, 0, 0, (ulong)RespCommand.LPUSHX, LPUSHX, (ulong)0, 0, 0, 0, (ulong)RespCommand.UNLINK, UNLINK, 0, 0, (ulong)RespCommand.SUNION, SUNION, (ulong)RespCommand.BLMOVE, BLMOVE, 0, 0, 0, 0, (ulong)RespCommand.SUBSTR, SUBSTR, 0, 0, 0, 0, (ulong)0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.EXISTS, EXISTS, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.ASKING, ASKING, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)0, 0, 0, 0, 0, 0, (ulong)RespCommand.RENAME, RENAME, 0, 0, 0, 0, (ulong)0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.GETSET, GETSET, (ulong)RespCommand.ZINTER, ZINTER, 0, 0, (ulong)RespCommand.DECRBY, DECRBY, (ulong)RespCommand.GEOPOS, GEOPOS, (ulong)RespCommand.BLMPOP, BLMPOP, 0, 0, (ulong)RespCommand.LINDEX, LINDEX, (ulong)RespCommand.SINTER, SINTER, (ulong)RespCommand.ZSCORE, ZSCORE, 0, 0, 0, 0, 0, 0,];
        public static readonly ulong[] Hash7Lookup = [0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.ZEXPIRE, ZEXPIRE, (ulong)RespCommand.DISCARD, DISCARD, 0, 0, (ulong)RespCommand.GEOHASH, GEOHASH, (ulong)RespCommand.PURGEBP, PURGEBP, (ulong)RespCommand.ZMSCORE, ZMSCORE, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)0, 0, 0, 0, (ulong)RespCommand.UNWATCH, UNWATCH, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.ZINCRBY, ZINCRBY, (ulong)RespCommand.PFMERGE, PFMERGE, 0, 0, 0, 0, (ulong)RespCommand.ZPOPMAX, ZPOPMAX, (ulong)RespCommand.FLUSHDB, FLUSHDB, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.FORCEGC, FORCEGC, 0, 0, 0, 0, (ulong)RespCommand.MIGRATE, MIGRATE, 0, 0, 0, 0, (ulong)RespCommand.GEODIST, GEODIST, 0, 0, (ulong)RespCommand.HGETALL, HGETALL, (ulong)RespCommand.HINCRBY, HINCRBY, (ulong)0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.PERSIST, PERSIST, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.HSTRLEN, HSTRLEN, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.PFCOUNT, PFCOUNT, 0, 0, 0, 0, (ulong)RespCommand.LINSERT, LINSERT, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.MONITOR, MONITOR, 0, 0, 0, 0, (ulong)RespCommand.PUBLISH, PUBLISH, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)0, 0, (ulong)RespCommand.HEXISTS, HEXISTS, 0, 0, 0, 0, (ulong)RespCommand.WATCHOS, WATCHOS, (ulong)RespCommand.RESTORE, RESTORE, 0, 0, (ulong)RespCommand.ZPOPMIN, ZPOPMIN, (ulong)RespCommand.WATCHMS, WATCHMS, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.HEXPIRE, HEXPIRE, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.PEXPIRE, PEXPIRE, (ulong)RespCommand.EVALSHA, EVALSHA, (ulong)0, 0, 0, 0, 0, 0, 0, 0, 0, 0,];
        public static readonly ulong[] Hash8Lookup = [(ulong)RespCommand.ZPEXPIRE, ZPEXPIRE, (ulong)RespCommand.ZPERSIST, ZPERSIST, (ulong)RespCommand.LASTSAVE, LASTSAVE, (ulong)RespCommand.SETRANGE, SETRANGE, (ulong)RespCommand.SMEMBERS, SMEMBERS, 0, 0, (ulong)RespCommand.FAILOVER, FAILOVER, (ulong)RespCommand.BITFIELD, BITFIELD, (ulong)RespCommand.READONLY, READONLY, (ulong)RespCommand.HPEXPIRE, HPEXPIRE, (ulong)RespCommand.HCOLLECT, HCOLLECT, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.HPERSIST, HPERSIST, (ulong)RespCommand.GETRANGE, GETRANGE, (ulong)RespCommand.BZPOPMIN, BZPOPMIN, (ulong)RespCommand.BITCOUNT, BITCOUNT, (ulong)RespCommand.BZPOPMAX, BZPOPMAX, (ulong)RespCommand.ZREVRANK, ZREVRANK, (ulong)RespCommand.FLUSHALL, FLUSHALL, (ulong)RespCommand.SPUBLISH, SPUBLISH, (ulong)RespCommand.RENAMENX, RENAMENX, (ulong)RespCommand.ZCOLLECT, ZCOLLECT, (ulong)RespCommand.EXPIREAT, EXPIREAT, 0, 0, 0, 0, 0, 0,];
        public static readonly ulong[] Hash9Lookup = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)MBER << 32) | (ulong)RespCommand.SISMEMBER, SISMEMBE, 0, 0, 0, 0, ((ulong)RITE << 32) | (ulong)RespCommand.READWRITE, READWRIT, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)DIUS << 32) | (ulong)RespCommand.GEORADIUS, GEORADIU, 0, 0, ((ulong)CAOF << 32) | (ulong)RespCommand.REPLICAOF, REPLICAO, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)OUNT << 32) | (ulong)RespCommand.ZLEXCOUNT, ZLEXCOUN, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)ARCH << 32) | (ulong)RespCommand.GEOSEARCH, GEOSEARC, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)RIBE << 32) | (ulong)RespCommand.SUBSCRIBE, SUBSCRIB, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)ANGE << 32) | (ulong)RespCommand.ZREVRANGE, ZREVRANG, 0, 0, ((ulong)PUSH << 32) | (ulong)RespCommand.RPOPLPUSH, RPOPLPUS, 0, 0, 0, 0, ((ulong)TAOF << 32) | (ulong)RespCommand.COMMITAOF, COMMITAO, ((ulong)REAT << 32) | (ulong)RespCommand.HEXPIREAT, HEXPIREA, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)REAT << 32) | (ulong)RespCommand.PEXPIREAT, PEXPIREA, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)REAT << 32) | (ulong)RespCommand.ZEXPIREAT, ZEXPIREA, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,];
        public static readonly ulong[] Hash10Lookup = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)REAT << 32) | (ulong)RespCommand.HPEXPIREAT, HPEXPIRE, 0, 0, 0, 0, 0, 0, ((ulong)TIME << 32) | (ulong)RespCommand.EXPIRETIME, EXPIRETI, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)TORE << 32) | (ulong)RespCommand.ZDIFFSTORE, ZDIFFSTO, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)REAT << 32) | (ulong)RespCommand.ZPEXPIREAT, ZPEXPIRE, 0, 0, 0, 0, ((ulong)TORE << 32) | (ulong)RespCommand.SDIFFSTORE, SDIFFSTO, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)RIBE << 32) | (ulong)RespCommand.PSUBSCRIBE, PSUBSCRI, 0, 0, 0, 0, ((ulong)RIBE << 32) | (ulong)RespCommand.SSUBSCRIBE, SSUBSCRI, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)PUSH << 32) | (ulong)RespCommand.BRPOPLPUSH, BRPOPLPU, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)IELD << 32) | (ulong)RespCommand.HRANDFIELD, HRANDFIE, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 | (ulong)0, 0, 0, 0, ((ulong)MBER << 32) | (ulong)RespCommand.SMISMEMBER, SMISMEMB, ((ulong)CARD << 32) | (ulong)RespCommand.ZINTERCARD, ZINTERCA, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)CARD << 32) | (ulong)RespCommand.SINTERCARD, SINTERCA, 0, 0, 0, 0, 0, 0,];
        public static readonly ulong[] Hash11Lookup = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)RIBE << 32) | (ulong)RespCommand.UNSUBSCRIBE, UNSUBSCR, ((ulong)TORE << 32) | (ulong)RespCommand.ZUNIONSTORE, ZUNIONST, 0, 0, 0, 0, 0, 0, ((ulong)D_RO << 32) | (ulong)RespCommand.BITFIELD_RO, BITFIELD, 0, 0, 0, 0, ((ulong)TORE << 32) | (ulong)RespCommand.SUNIONSTORE, SUNIONST, ((ulong)TIME << 32) | (ulong)RespCommand.ZEXPIRETIME, ZEXPIRET, ((ulong)RYOF << 32) | (ulong)RespCommand.SECONDARYOF, SECONDAR, 0, 0, 0, 0, ((ulong)TORE << 32) | (ulong)RespCommand.ZINTERSTORE, ZINTERST, 0, 0, ((ulong)TIME << 32) | (ulong)RespCommand.PEXPIRETIME, PEXPIRET, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)TORE << 32) | (ulong)RespCommand.SINTERSTORE, SINTERST, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)TORE << 32) | (ulong)RespCommand.ZRANGESTORE, ZRANGEST, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)LOAT << 32) | (ulong)RespCommand.INCRBYFLOAT, INCRBYFL, 0, 0, 0, 0, ((ulong)MBER << 32) | (ulong)RespCommand.SRANDMEMBER, SRANDMEM, 0, 0, 0, 0, ((ulong)YLEX << 32) | (ulong)RespCommand.ZRANGEBYLEX, ZRANGEBY, 0, 0, 0, 0, ((ulong)TIME << 32) | (ulong)RespCommand.HEXPIRETIME, HEXPIRET, ((ulong)MBER << 32) | (ulong)RespCommand.ZRANDMEMBER, ZRANDMEM, ((ulong)ETAG << 32) | (ulong)RespCommand.GETWITHETAG, GETWITHE, 0, 0,];
        public static readonly ulong[] Hash12Lookup = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)TIME << 32) | (ulong)RespCommand.ZPEXPIRETIME, ZPEXPIRE, 0, 0, 0, 0, 0, 0, ((ulong)RIBE << 32) | (ulong)RespCommand.PUNSUBSCRIBE, PUNSUBSC, 0, 0, 0, 0, 0 | (ulong)0, 0, 0, 0, 0, 0, ((ulong)LOAT << 32) | (ulong)RespCommand.HINCRBYFLOAT, HINCRBYF, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)SAGE << 32) | (ulong)0, 0, 0, 0, ((ulong)TIME << 32) | (ulong)RespCommand.HPEXPIRETIME, HPEXPIRE, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, ((ulong)S_RO << 32) | (ulong)RespCommand.GEORADIUS_RO, GEORADIU, 0, 0,];
        public static readonly ulong[] Hash13Lookup = [(ulong)RespCommand.ZRANGEBYSCORE, ZRANGEBY, EBYSCORE, 0, 0, 0, (ulong)RespCommand.GETIFNOTMATCH, GETIFNOT, NOTMATCH, 0, 0, 0, 0, 0, 0,];
        public static readonly ulong[] Hash14Lookup = [0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.ZREMRANGEBYLEX, ZREMRANG, NGEBYLEX, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.ZREVRANGEBYLEX, ZREVRANG, NGEBYLEX, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (ulong)RespCommand.GEOSEARCHSTORE, GEOSEARC, RCHSTORE, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,];
        public static readonly ulong[] Hash16Lookup = [(ulong)RespCommand.ZREMRANGEBYSCORE, ZREMRANG, EBYSCORE, (ulong)RespCommand.ZREVRANGEBYSCORE, ZREVRANG, EBYSCORE, 0, 0, 0,];
    }

    [StructLayout(LayoutKind.Explicit, Size = sizeof(int) * 4)]
    public readonly struct ParsedRespCommandOrArgument
    {
        public static readonly ParsedRespCommandOrArgument Malformed = new(0, 0, 0, 0);

        internal const int ArgumentCountIx = 0;
        internal const int CommandIx = 1;
        internal const int ByteStartIx = 2;
        internal const int ByteEndIx = 3;

        public bool IsMalformed
        => ByteStart == 0 && ByteEnd == 0;

        public bool IsCommand
        => ArgumentCount > 0;

        public bool IsArgument
        => ArgumentCount == 0;

        [FieldOffset(ArgumentCountIx * sizeof(int))]
        private readonly int argumentCount;

        [FieldOffset(CommandIx * sizeof(int))]
        private readonly RespCommand command;

        [FieldOffset(ByteStartIx * sizeof(int))]
        private readonly int byteStart;

        [FieldOffset(ByteEndIx * sizeof(int))]
        private readonly int byteEnd;

        /// <summary>
        /// For commands the number of arguments in the command.
        /// 
        /// This includes the command NAME, which is itself an argument.
        /// 
        /// For arguments, undefined.
        /// </summary>
        public int ArgumentCount
        => argumentCount;

        /// <summary>
        /// For commands, the parsed command name.
        /// 
        /// For arugments, undefined.
        /// </summary>
        public RespCommand Command
        => command;

        /// <summary>
        /// For commands, the byte of the *.
        /// 
        /// For arguments, the byte of the START of the string.
        /// So if the argument is <16 bytes of whatever>$123\r\nabcd...\r\n
        /// This is 22.
        /// </summary>
        public int ByteStart
        => byteStart;

        /// <summary>
        /// For commands and arguments, the byte after the trailing \r\n.
        /// </summary>
        public int ByteEnd
        => byteEnd;

        private ParsedRespCommandOrArgument(RespCommand cmd, int args, int start, int stop)
        {
            command = cmd;
            argumentCount = args;
            byteStart = start;
            byteEnd = stop;
        }

        public static ParsedRespCommandOrArgument ForCommand(RespCommand cmd, int count, int start, int stop)
        => new(cmd, count, start, stop);

        public static ParsedRespCommandOrArgument ForArgument(int start, int stop)
        => new(RespCommand.None, 0, start, stop);

        /// <inheritdoc/>
        public override string ToString()
        {
            if (IsMalformed)
            {
                return "!!!";
            }
            else if (IsArgument)
            {
                return $"Argument [{ByteStart}..{ByteEnd}]";
            }
            else if (IsCommand)
            {
                return $"Command [{ByteStart}..{ByteEnd}] = {Command}";
            }
            else
            {
                throw new InvalidOperationException();
            }
        }
    }
}