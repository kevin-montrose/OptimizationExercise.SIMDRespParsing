using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace OptimizationExercise.SIMDRespParsing
{
    /// <summary>
    /// <see cref="RespParser"> but with the training wheels off.
    /// </summary>
    public static class RespParserV2
    {
        /// <summary>
        /// Attempt to parse all commands in <paramref name="commandBuffer"/>.
        /// 
        /// Incomplete commands are ignored (ie. not errors), malformed commands terminate parsing and result in a malformed entry in <paramref name="intoCommands"/>.
        /// 
        /// Valid commands before incomplete or malformed ones are fully parsed and placed in <paramref name="intoCommands"/>.
        /// </summary>
        public static unsafe void Parse(
            Span<byte> commandBufferSpan,
            Span<ParsedRespCommandOrArgument> intoCommandsSpan,
            out int intoCommandsSlotsUsed,
            out int bytesConsumed
        )
        {
            Debug.Assert(commandBufferSpan.Length > 0);
            Debug.Assert(BitConverter.IsLittleEndian);
            Debug.Assert(!intoCommandsSpan.IsEmpty);

            fixed (byte* commandBuffer = commandBufferSpan)
            fixed (ParsedRespCommandOrArgument* intoCommands = intoCommandsSpan)
            {
                var commandBufferLength = commandBufferSpan.Length;

                var bitmapLength = (commandBufferLength / 8) + 1;
                byte* asteriks = stackalloc byte[bitmapLength];
                byte* dollars = stackalloc byte[bitmapLength];
                byte* crs = stackalloc byte[bitmapLength];
                byte* lfs = stackalloc byte[bitmapLength];

                ScanForDelimitersPointers(commandBufferLength, commandBuffer, asteriks, dollars, crs, lfs);

                // reuse crs to reduce cache pressure
                var crLfs = crs;
                CombineCrLfPointers_SIMD(bitmapLength, crs, lfs, crLfs);

                var remainingAsteriks = asteriks;
                var remainingDollars = dollars;
                var remainingCrLfs = crLfs;

                var remainingIntoLength = intoCommandsSpan.Length;
                var remainingInto = intoCommands;
                var totalParsed = 0;
                var lastByteConsumed = 0;

                var byteOffsetInCommandBuffer = 0;
                var bitOffsetInFirstBitmapByte = (byte)0;

                while (remainingIntoLength > 0 && (byteOffsetInCommandBuffer + bitOffsetInFirstBitmapByte) < commandBufferLength)
                {
                    var canContinueParsing =
                        TryParseSingleCommandPointers(
                            commandBufferLength,
                            commandBuffer,
                            byteOffsetInCommandBuffer,
                            bitOffsetInFirstBitmapByte,
                            remainingIntoLength,
                            remainingInto,
                            bitmapLength,
                            ref remainingAsteriks,
                            ref remainingDollars,
                            ref remainingCrLfs,
                            out var slotsUsed
                    );

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

                    remainingInto = remainingInto + slotsUsed;
                    remainingIntoLength -= slotsUsed;
                }

                intoCommandsSlotsUsed = totalParsed;
                bytesConsumed = lastByteConsumed;
            }
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
        public static unsafe bool TryParseSingleCommandPointers(
            int commandBufferLength,
            byte* commandBuffer,
            int byteStart,
            byte bitStart,
            int parsedLength,
            ParsedRespCommandOrArgument* parsed,
            int bitmapLength,
            ref byte* remainingAsteriks,
            ref byte* remainingDollars,
            ref byte* remainingCrLfs,
            out int slotsUsed
        )
        {
            Debug.Assert(bitStart < 8);

            var remainingSpaceInCommandBuffer = commandBufferLength - (byteStart + bitStart);

            if (remainingSpaceInCommandBuffer < 11)
            {
                slotsUsed = 0;
                return false;
            }

            var hasExpectedAsteriks = (remainingAsteriks[0] & (1 << bitStart)) != 0;
            if (!hasExpectedAsteriks)
            {
                *parsed = ParsedRespCommandOrArgument.Malformed;
                slotsUsed = 1;
                return false;
            }

            var arrayEndingCrLf = FindNext24Pointer(bitStart, bitmapLength, remainingCrLfs);
            if (arrayEndingCrLf == -1)
            {
                if (remainingSpaceInCommandBuffer >= 12)
                {
                    *parsed = ParsedRespCommandOrArgument.Malformed;
                    slotsUsed = 1;
                    return false;
                }

                slotsUsed = 0;
                return false;
            }

            if (!TryParsePositiveIntPointers((arrayEndingCrLf + byteStart) - (byteStart + bitStart + 1), commandBuffer + (byteStart + bitStart + 1), out var arrayItemCount) || arrayItemCount <= 0)
            {
                *parsed = ParsedRespCommandOrArgument.Malformed;
                slotsUsed = 1;
                return false;
            }

            if (arrayItemCount + 1 > parsedLength)
            {
                slotsUsed = 0;
                return false;
            }

            var cmdStart = byteStart + bitStart;
            var cmdEnd = byteStart + arrayEndingCrLf;

            if (!TryParseCommandPointers_Hash2(commandBuffer, cmdStart, cmdEnd, out var cmdEnum))
            {
                cmdEnum = RespCommand.Invalid;
            }

            *parsed = ParsedRespCommandOrArgument.ForCommand(cmdEnum, arrayItemCount, cmdStart, cmdEnd + 2);

            AdvancePointers((arrayEndingCrLf + 2) - bitStart, ref byteStart, ref bitStart, ref bitmapLength, ref remainingAsteriks, ref remainingDollars, ref remainingCrLfs);

            for (var i = 0; i < arrayItemCount; i++)
            {
                var remainingSpaceForArg = commandBufferLength - (byteStart + bitStart);

                if (remainingSpaceForArg < 7)
                {
                    slotsUsed = 0;
                    return false;
                }

                var hasExpectedDollar = (remainingDollars[0] & (1 << bitStart)) != 0;
                if (!hasExpectedDollar)
                {
                    parsed[i + 1] = ParsedRespCommandOrArgument.Malformed;
                    slotsUsed = i + 2;
                    return false;
                }

                var bulkStringEndingCrLf = FindNext24Pointer(bitStart, bitmapLength, remainingCrLfs);
                if (bulkStringEndingCrLf == -1)
                {
                    if (remainingSpaceForArg >= 12)
                    {
                        parsed[i + 1] = ParsedRespCommandOrArgument.Malformed;
                        slotsUsed = i + 2;
                        return false;
                    }

                    slotsUsed = 0;
                    return false;
                }

                if (!TryParsePositiveIntPointers((byteStart + bulkStringEndingCrLf) - (byteStart + bitStart + 1), commandBuffer + (byteStart + bitStart + 1), out var argLength) || argLength <= 0)
                {
                    parsed[i + 1] = ParsedRespCommandOrArgument.Malformed;
                    slotsUsed = i + 2;
                    return false;
                }

                AdvancePointers((bulkStringEndingCrLf + 2) - bitStart, ref byteStart, ref bitStart, ref bitmapLength, ref remainingAsteriks, ref remainingDollars, ref remainingCrLfs);

                var argStart = byteStart + bitStart;
                var argEnd = argStart + argLength;

                if (argEnd + 2 > commandBufferLength)
                {
                    slotsUsed = 0;
                    return false;
                }

                var terminatingCrLfExpctedByteIndex = (argLength + bitStart) / 8;
                var terminatingCrLfExpectedBitIndex = (argLength + bitStart) % 8;

                var hasTerminatingCrLf = (remainingCrLfs[terminatingCrLfExpctedByteIndex] & (1 << terminatingCrLfExpectedBitIndex)) != 0;

                if (!hasTerminatingCrLf)
                {
                    parsed[i + 1] = ParsedRespCommandOrArgument.Malformed;
                    slotsUsed = i + 2;
                    return false;
                }

                parsed[i + 1] = ParsedRespCommandOrArgument.ForArgument(argStart, argEnd + 2);

                AdvancePointers(argLength + 2, ref byteStart, ref bitStart, ref bitmapLength, ref remainingAsteriks, ref remainingDollars, ref remainingCrLfs);
            }

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
        public static unsafe void AdvancePointers(
            int charCount,
            ref int byteStart,
            ref byte bitStart,
            ref int bitmapLength,
            ref byte* remainingAsteriks,
            ref byte* remainingDollars,
            ref byte* remaininingCrLfs
        )
        {
            var advanceSpansBy = charCount / 8;
            bitStart += (byte)(charCount % 8);

            advanceSpansBy += bitStart / 8;
            bitStart = (byte)(bitStart % 8);

            byteStart += advanceSpansBy * 8;

            remainingAsteriks += advanceSpansBy;
            remainingDollars += advanceSpansBy;
            remaininingCrLfs += advanceSpansBy;

            bitmapLength -= advanceSpansBy;
        }

        /// <summary>
        /// Scan in bitmap for next set bit, but looks at most 24 bits ahead.
        /// 
        /// This is fine, because we only ever need to look 10 past our current position,
        /// and <paramref name="bitsToSkip"/> should never exceed 8.
        /// </summary>
        public static unsafe int FindNext24Pointer(byte bitsToSkip, int bitmapLength, byte* bitmap)
        {
            Debug.Assert(bitsToSkip < 8);

            if (bitmapLength >= sizeof(uint))
            {
                var asUInt = *((uint*)bitmap);
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

            if (bitmapLength >= sizeof(ushort))
            {
                var asUShort = *((ushort*)bitmap);
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

                bitmap += sizeof(ushort);

                bitmapLength -= sizeof(ushort);
            }

            if (bitmapLength != 0)
            {
                var asByte = *bitmap;
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
        /// A version of <see cref="CombineCrLfPointers_Scalar"/> which attempts to use SIMD instructions.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void CombineCrLfPointers_SIMD(int length, byte* crs, byte* lfs, byte* crLfs)
        {
            while (length >= Vector512<byte>.Count + 1)
            {
                var curCr = Vector512.Load(crs);

                var curLf = Vector512.Load(lfs);

                var curLfElementsShiftedOneBitDown = Vector512.ShiftRightLogical(curLf, 1);

                var curLfShiftedDownOneLane = Vector512.Load(lfs + 1);
                var curLfShiftedDownOneLaneLowBitsHigh = V512ShiftLeft7(curLfShiftedDownOneLane);

                var curLfAllBitsShiftedDown = Vector512.BitwiseOr(curLfElementsShiftedOneBitDown, curLfShiftedDownOneLaneLowBitsHigh);

                var curCrLf = Vector512.BitwiseAnd(curCr, curLfAllBitsShiftedDown);

                Vector512.Store(curCrLf, crLfs);

                crs += Vector512<byte>.Count;
                lfs += Vector512<byte>.Count;
                crLfs += Vector512<byte>.Count;

                length -= Vector512<byte>.Count;
            }

            if (length >= Vector256<byte>.Count + 1)
            {
                var curCr = Vector256.Load(crs);

                var curLf = Vector256.Load(lfs);

                var curLfElementsShiftedOneBitDown = Vector256.ShiftRightLogical(curLf, 1);

                var curLfShiftedDownOneLane = Vector256.Load(lfs + 1);
                var curLfShiftedDownOneLaneLowBitsHigh = V256ShiftLeft7(curLfShiftedDownOneLane);

                var curLfAllBitsShiftedDown = Vector256.BitwiseOr(curLfElementsShiftedOneBitDown, curLfShiftedDownOneLaneLowBitsHigh);

                var curCrLf = Vector256.BitwiseAnd(curCr, curLfAllBitsShiftedDown);

                Vector256.Store(curCrLf, crLfs);

                crs += Vector256<byte>.Count;
                lfs += Vector256<byte>.Count;
                crLfs += Vector256<byte>.Count;

                length -= Vector256<byte>.Count;
            }

            if (length >= Vector128<byte>.Count + 1)
            {
                var curCr = Vector128.Load(crs);

                var curLf = Vector128.Load(lfs);

                var curLfElementsShiftedOneBitDown = Vector128.ShiftRightLogical(curLf, 1);

                var curLfShiftedDownOneLane = Vector128.Load(lfs + 1);
                var curLfShiftedDownOneLaneLowBitsHigh = V128ShiftLeft7(curLfShiftedDownOneLane);

                var curLfAllBitsShiftedDown = Vector128.BitwiseOr(curLfElementsShiftedOneBitDown, curLfShiftedDownOneLaneLowBitsHigh);

                var curCrLf = Vector128.BitwiseAnd(curCr, curLfAllBitsShiftedDown);

                Vector128.Store(curCrLf, crLfs);

                crs += Vector128<byte>.Count;
                lfs += Vector128<byte>.Count;
                crLfs += Vector128<byte>.Count;

                length -= Vector128<byte>.Count;
            }

            if (length != 0)
            {
                // the rest we need to defer to scalar code
                CombineCrLfPointers_Scalar(length, crs, lfs, crLfs);
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
        public static unsafe void CombineCrLfPointers_Scalar(int length, byte* crs, byte* lfs, byte* crlfs)
        {
            while (length >= sizeof(ulong))
            {
                var cr = *((ulong*)crs);
                var lf = *((ulong*)lfs);

                var setWhereCrLf = cr & (lf >> 1);

                var hasFinalCr = (cr & 0x8000_0000__0000_0000UL) != 0;
                if (hasFinalCr && length > sizeof(ulong))
                {
                    var nextLf = (ulong)(lfs[sizeof(ulong)] & 1) << 63;
                    setWhereCrLf |= nextLf;
                }

                *((ulong*)crlfs) = setWhereCrLf;

                crs += sizeof(ulong);
                lfs += sizeof(ulong);
                crlfs += sizeof(ulong);

                length -= sizeof(ulong);
            }

            if (length >= sizeof(uint))
            {
                var cr = *((uint*)crs);
                var lf = *((uint*)lfs);

                var setWhereCrLf = cr & (lf >> 1);

                var hasFinalCr = (cr & 0x8000_0000U) != 0;
                if (hasFinalCr && length > sizeof(uint))
                {
                    var nextLf = (uint)(lfs[sizeof(uint)] & 1) << 31;
                    setWhereCrLf |= nextLf;
                }

                *((uint*)crlfs) = setWhereCrLf;

                crs += sizeof(uint);
                lfs += sizeof(uint);
                crlfs += sizeof(uint);

                length -= sizeof(uint);
            }

            if (length >= sizeof(ushort))
            {
                var cr = *((ushort*)crs);
                var lf = *((ushort*)lfs);

                var setWhereCrLf = (ushort)(cr & (lf >> 1));

                var hasFinalCr = (cr & 0x8000) != 0;
                if (hasFinalCr && length > sizeof(ushort))
                {
                    var nextLf = (ushort)((lfs[sizeof(ushort)] & 1) << 15);
                    setWhereCrLf |= nextLf;
                }

                crs += sizeof(ushort);
                lfs += sizeof(ushort);
                crlfs += sizeof(ushort);

                length -= sizeof(ushort);
            }

            if (length != 0)
            {
                var cr = crs[0];
                var lf = lfs[0];

                var setWhereCrLf = (byte)(cr & (lf >> 1));

                // We can't look any further ahead, so don't bother

                *crlfs = setWhereCrLf;
            }
        }

        /// <summary>
        /// Quickly scan the whole command buffer, finding all the delimiters that MIGHT matter.
        /// 
        /// Does not consider escaping or anything just yet.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void ScanForDelimitersPointers(int commandBufferLength, byte* commandBuffer, byte* asteriks, byte* dollars, byte* crs, byte* lfs)
        {
            const byte Asteriks = (byte)'*';
            const byte Dollar = (byte)'$';
            const byte CR = (byte)'\r';
            const byte LF = (byte)'\n';

            var asterix512 = Vector512.Create(Asteriks);
            var dollar512 = Vector512.Create(Dollar);
            var cr512 = Vector512.Create(CR);
            var nl512 = Vector512.Create(LF);

            while (commandBufferLength >= Vector512<byte>.Count)
            {
                var chunk512 = Vector512.Load(commandBuffer);

                var asterik = Vector512.Equals(asterix512, chunk512);
                var dollar = Vector512.Equals(dollar512, chunk512);
                var cr = Vector512.Equals(cr512, chunk512);
                var lf = Vector512.Equals(nl512, chunk512);

                var asteriksMsbs = Vector512.ExtractMostSignificantBits(asterik);
                var dollarMsbs = Vector512.ExtractMostSignificantBits(dollar);
                var crMsbs = Vector512.ExtractMostSignificantBits(cr);
                var lfMsbs = Vector512.ExtractMostSignificantBits(lf);

                *((ulong*)asteriks) = asteriksMsbs;
                *((ulong*)dollars) = dollarMsbs;
                *((ulong*)crs) = crMsbs;
                *((ulong*)lfs) = lfMsbs;

                asteriks += sizeof(ulong);
                dollars += sizeof(ulong);
                crs += sizeof(ulong);
                lfs += sizeof(ulong);

                commandBuffer += Vector512<byte>.Count;
                commandBufferLength -= Vector512<byte>.Count;
            }

            if (commandBufferLength >= Vector256<byte>.Count)
            {
                var chunk256 = Vector256.Load(commandBuffer);

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

                *((uint*)asteriks) = asteriksMsbs;
                *((uint*)dollars) = dollarMsbs;
                *((uint*)crs) = crMsbs;
                *((uint*)lfs) = lfMsbs;

                asteriks += sizeof(uint);
                dollars += sizeof(uint);
                crs += sizeof(uint);
                lfs += sizeof(uint);

                commandBuffer += Vector256<byte>.Count;
                commandBufferLength -= Vector256<byte>.Count;
            }

            if (commandBufferLength >= Vector128<byte>.Count)
            {
                var chunk128 = Vector128.Load(commandBuffer);

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

                *((ushort*)asteriks) = asteriksMsbs;
                *((ushort*)dollars) = dollarMsbs;
                *((ushort*)crs) = crMsbs;
                *((ushort*)lfs) = lfMsbs;

                asteriks += sizeof(ushort);
                dollars += sizeof(ushort);
                crs += sizeof(ushort);
                lfs += sizeof(ushort);

                commandBuffer += Vector128<byte>.Count;
                commandBufferLength -= Vector128<byte>.Count;
            }

            if (commandBufferLength >= sizeof(ulong))
            {
                const ulong Asteriks64 = ((ulong)Asteriks << (64 - 8)) | ((ulong)Asteriks << (64 - 16)) | ((ulong)Asteriks << (64 - 24)) | ((ulong)Asteriks << (64 - 32)) | ((ulong)Asteriks << (64 - 40)) | ((ulong)Asteriks << (64 - 48)) | ((ulong)Asteriks << (64 - 56)) | ((ulong)Asteriks << (64 - 64));
                const ulong Dollar64 = ((ulong)Dollar << (64 - 8)) | ((ulong)Dollar << (64 - 16)) | ((ulong)Dollar << (64 - 24)) | ((ulong)Dollar << (64 - 32)) | ((ulong)Dollar << (64 - 40)) | ((ulong)Dollar << (64 - 48)) | ((ulong)Dollar << (64 - 56)) | ((ulong)Dollar << (64 - 64));
                const ulong CR64 = ((ulong)CR << (64 - 8)) | ((ulong)CR << (64 - 16)) | ((ulong)CR << (64 - 24)) | ((ulong)CR << (64 - 32)) | ((ulong)CR << (64 - 40)) | ((ulong)CR << (64 - 48)) | ((ulong)CR << (64 - 56)) | ((ulong)CR << (64 - 64));
                const ulong LF64 = ((ulong)LF << (64 - 8)) | ((ulong)LF << (64 - 16)) | ((ulong)LF << (64 - 24)) | ((ulong)LF << (64 - 32)) | ((ulong)LF << (64 - 40)) | ((ulong)LF << (64 - 48)) | ((ulong)LF << (64 - 56)) | ((ulong)LF << (64 - 64));

                var chunk64 = *((ulong*)commandBuffer);

                var asteriks64 = chunk64 ^ Asteriks64;
                var dollar64 = chunk64 ^ Dollar64;
                var cr64 = chunk64 ^ CR64;
                var lf64 = chunk64 ^ LF64;

                var asteriksMsbs = SetMSBsWhereZero64_Mult(asteriks64);
                var dollarMsbs = SetMSBsWhereZero64_Mult(dollar64);
                var crMsbs = SetMSBsWhereZero64_Mult(cr64);
                var lfMsbs = SetMSBsWhereZero64_Mult(lf64);

                *asteriks = asteriksMsbs;
                *dollars = dollarMsbs;
                *crs = crMsbs;
                *lfs = lfMsbs;

                asteriks++;
                dollars++;
                crs++;
                lfs++;

                commandBuffer += sizeof(ulong);
                commandBufferLength -= sizeof(ulong);
            }

            // we can't continue for 32, 16, etc. because we need to produce whole bytes
            // and below ulong we aren't getting a whole byte in one "word" size

            var pendingMask = (byte)0b0000_0001;
            var pendingAsteriksMsbs = (byte)0;
            var pendingDollarMsbs = (byte)0;
            var pendingCrMsbs = (byte)0;
            var pendingLfMsbs = (byte)0;

            for (var i = 0; i < commandBufferLength; i++)
            {
                var c = commandBuffer[i];
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
                    *asteriks = pendingAsteriksMsbs;
                    *dollars = pendingDollarMsbs;
                    *crs = pendingCrMsbs;
                    *lfs = pendingLfMsbs;

                    asteriks++;
                    dollars++;
                    crs++;
                    lfs++;

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
                *asteriks = pendingAsteriksMsbs;
                *dollars = pendingDollarMsbs;
                *crs = pendingCrMsbs;
                *lfs = pendingLfMsbs;
            }
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
        public static unsafe bool TryParsePositiveIntPointers(int length, byte* input, out int parsed)
        {
            // bytes that make up a valid integer will be in the range 0x30 - 0x39
            // 0011_0000 = 0
            // ...
            // 0011_1001 = 9

            if (length == 0 || length > 10)
            {
                Unsafe.SkipInit(out parsed);
                return false;
            }
            else if (length == 1)
            {
                parsed = *input - '0';
                return parsed is > 0 and < 10;
            }

            var ret = 0L;
            while (length >= 2)
            {
                ret *= 100;

                var twoAsUShort = *((ushort*)input);

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

                input += sizeof(ushort);
                length -= sizeof(ushort);
            }

            if (length != 0)
            {
                ret *= 10;
                var lastDigit = *input - '0';
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
        /// Version of <see cref="RespParser.TryParseCommand_Hash(Span{byte}, int, int, out RespCommand)"/> but simplified.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool TryParseCommandPointers_Hash2(byte* cmdBuffer, int commandStart, int commandEnd, out RespCommand parsed)
        {
            const uint LOWER_TO_UPPER_UINT = ~0x2020_2020U;
            const ulong LOWER_TO_UPPER_ULONG = ~0x2020_2020__2020_2020UL;

            var cmdStartPtr = cmdBuffer + commandStart;
            var cmdLen = commandEnd - commandStart;

            switch (cmdLen)
            {
                case 3:
                    var len3p0 = *((uint*)cmdStartPtr) & LOWER_TO_UPPER_UINT;
                    var calculatedValue3 = len3p0;
                    calculatedValue3 ^= 265420117U;
                    var len3Ix = (byte)(calculatedValue3 % 32);
                    var len3Value = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(RespParser.Hash3Lookup), len3Ix);
                    parsed = (RespCommand)len3Value;
                    return ((len3Value >> 32) == len3p0);
                case 4:
                    var len4p0 = *((uint*)cmdStartPtr) & LOWER_TO_UPPER_UINT;
                    var calculatedValue4 = len4p0;
                    calculatedValue4 ^= 682004627U;
                    var len4Ix = (byte)(calculatedValue4 % 63);
                    var len4Value = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(RespParser.Hash4Lookup), len4Ix);
                    parsed = (RespCommand)len4Value;
                    return ((len4Value >> 32) == len4p0);
                case 5:
                    var len5p0 = *((ulong*)(cmdStartPtr - 1)) & LOWER_TO_UPPER_ULONG;
                    var len5Calc = len5p0;
                    len5Calc ^= 4151764434U;
                    var len5Ix = (byte)((uint)len5Calc % (byte)177);
                    ref var len5ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(RespParser.Hash5Lookup), len5Ix * 2);

                    parsed = (RespCommand)len5ValueRef;
                    return (Unsafe.Add(ref len5ValueRef, 1) == len5p0);
                case 6:
                    var len6p0 = *((ulong*)cmdStartPtr) & LOWER_TO_UPPER_ULONG;
                    var len6Calc = len6p0;
                    len6Calc ^= 3829026875U;
                    var len6Ix = (byte)((uint)len6Calc % (byte)140);
                    ref var len6ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(RespParser.Hash6Lookup), len6Ix * 2);

                    parsed = (RespCommand)len6ValueRef;
                    return (Unsafe.Add(ref len6ValueRef, 1) == len6p0);
                case 7:
                    var len7p0 = *((ulong*)cmdStartPtr) & LOWER_TO_UPPER_ULONG;
                    var len7Calc = len7p0;
                    len7Calc ^= (len7p0 >> (32 - 0));
                    len7Calc ^= 3263854412U;
                    var len7Ix = (byte)((uint)len7Calc % (byte)127);
                    ref var len7ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(RespParser.Hash7Lookup), len7Ix * 2);

                    parsed = (RespCommand)len7ValueRef;
                    return (Unsafe.Add(ref len7ValueRef, 1) == len7p0);
                case 8:
                    var len8p0 = *((ulong*)cmdStartPtr) & LOWER_TO_UPPER_ULONG;
                    var len8Calc = len8p0;
                    len8Calc ^= (len8p0 >> (32 - 0));
                    len8Calc ^= 1216371613U;
                    var len8Ix = (byte)((uint)len8Calc % (byte)27);
                    ref var len8ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(RespParser.Hash8Lookup), len8Ix * 2);

                    parsed = (RespCommand)len8ValueRef;
                    return (Unsafe.Add(ref len8ValueRef, 1) == len8p0);
                case 9:
                    var len9p0 = *((ulong*)cmdStartPtr) & LOWER_TO_UPPER_ULONG;
                    var len9p1 = *((uint*)(cmdStartPtr + 5)) & LOWER_TO_UPPER_UINT;

                    var len9Val = len9p1;
                    len9Val ^= (uint)(len9p0 >> 0);
                    len9Val ^= 3558180048U;
                    var len9Ix = (byte)(len9Val % 131);
                    ref var len9ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(RespParser.Hash9Lookup), len9Ix * 2);

                    var len9Val0 = len9ValueRef;
                    var len9Val1 = Unsafe.Add(ref len9ValueRef, 1);

                    parsed = (RespCommand)len9Val0;
                    return (len9Val1 == len9p0 & (uint)(len9Val0 >> 32) == len9p1);
                case 10:
                    var len10p0 = *((ulong*)cmdStartPtr) & LOWER_TO_UPPER_ULONG;
                    var len10p1 = *((uint*)(cmdStartPtr + 6)) & LOWER_TO_UPPER_UINT;

                    var len10Val = len10p1;
                    len10Val ^= (uint)(len10p0 >> 0);
                    len10Val ^= 3690845978U;
                    var len10Ix = (byte)(len10Val % 131);
                    ref var len10ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(RespParser.Hash10Lookup), len10Ix * 2);

                    var len10Val0 = len10ValueRef;
                    var len10Val1 = Unsafe.Add(ref len10ValueRef, 1);

                    parsed = (RespCommand)len10Val0;
                    return (len10Val1 == len10p0 & (uint)(len10Val0 >> 32) == len10p1);
                case 11:
                    var len11p0 = *((ulong*)cmdStartPtr) & LOWER_TO_UPPER_ULONG;
                    var len11p1 = *((uint*)(cmdStartPtr + 7)) & LOWER_TO_UPPER_UINT;

                    var len11Val = len11p1;
                    len11Val ^= (uint)(len11p0 >> 0);
                    len11Val ^= 929541917U;
                    var len11Ix = (byte)(len11Val % 60);
                    ref var len11ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(RespParser.Hash11Lookup), len11Ix * 2);

                    var len11Val0 = len11ValueRef;
                    var len11Val1 = Unsafe.Add(ref len11ValueRef, 1);

                    parsed = (RespCommand)len11Val0;
                    return (len11Val1 == len11p0 && (uint)(len11Val0 >> 32) == len11p1);
                case 12:
                    var len12p0 = *((ulong*)cmdStartPtr) & LOWER_TO_UPPER_ULONG;
                    var len12p1 = *((uint*)(cmdStartPtr + 8)) & LOWER_TO_UPPER_UINT;

                    var len12Val = len12p1;
                    len12Val ^= (uint)(len12p0 >> 0);
                    len12Val ^= 2663684139U;
                    var len12Ix = (byte)(len12Val % 32);
                    ref var len12ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(RespParser.Hash12Lookup), len12Ix * 2);

                    var len12Val0 = len12ValueRef;
                    var len12Val1 = Unsafe.Add(ref len12ValueRef, 1);

                    parsed = (RespCommand)len12Val0;
                    return (len12Val1 == len12p0 & (uint)(len12Val0 >> 32) == len12p1);
                case 13:
                    var len13p0 = *((ulong*)cmdStartPtr) & LOWER_TO_UPPER_ULONG;
                    var len13p1 = *((ulong*)(cmdStartPtr + 5)) & LOWER_TO_UPPER_ULONG;
                    var len13Val = (uint)(len13p0 >> 0);
                    len13Val ^= (uint)(len13p1 >> 0);
                    len13Val ^= 2327279063U;
                    var len13Ix = (byte)(len13Val % 4);
                    ref var len13ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(RespParser.Hash13Lookup), len13Ix * 3);

                    var len13Val0 = len13ValueRef;
                    var len13Val1 = Unsafe.Add(ref len13ValueRef, 1);
                    var len13Val2 = Unsafe.Add(ref len13ValueRef, 2);

                    parsed = (RespCommand)len13Val0;
                    return (len13Val1 == len13p0 & len13Val2 == len13p1);
                case 14:
                    var len14p0 = *((ulong*)cmdStartPtr) & LOWER_TO_UPPER_ULONG;
                    var len14p1 = *((ulong*)(cmdStartPtr + 6)) & LOWER_TO_UPPER_ULONG;
                    var len14Val = (uint)(len14p0 >> 0);
                    len14Val ^= (uint)(len14p1 >> 0);
                    len14Val ^= 100014858U;
                    var len14Ix = (byte)(len14Val % 129);
                    ref var len14ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(RespParser.Hash14Lookup), len14Ix * 3);

                    var len14Val0 = len14ValueRef;
                    var len14Val1 = Unsafe.Add(ref len14ValueRef, 1);
                    var len14Val2 = Unsafe.Add(ref len14ValueRef, 2);

                    parsed = (RespCommand)len14Val0;
                    return (len14Val1 == len14p0 & len14Val2 == len14p1);
                case 15:
                    parsed = RespCommand.ZREMRANGEBYRANK;
                    var len15p0 = *((ulong*)cmdStartPtr) & LOWER_TO_UPPER_ULONG;
                    var len15p1 = *((ulong*)(cmdStartPtr + 7)) & LOWER_TO_UPPER_ULONG;
                    return (len15p0 == RespParser.ZREMRANG && len15p1 == RespParser.GEBYRANK);
                case 16:
                    var len16p0 = *((ulong*)cmdStartPtr) & LOWER_TO_UPPER_ULONG;
                    var len16p1 = *((ulong*)(cmdStartPtr + 8)) & LOWER_TO_UPPER_ULONG;
                    var len16Val = (uint)(len16p0 >> 24);
                    len16Val ^= (uint)(len16p1 >> 0);
                    var len16Ix = (byte)(len16Val % 2);
                    ref var len16ValueRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(RespParser.Hash16Lookup), len16Ix * 3);

                    var len16Val0 = len16ValueRef;
                    var len16Val1 = Unsafe.Add(ref len16ValueRef, 1);
                    var len16Val2 = Unsafe.Add(ref len16ValueRef, 2);

                    parsed = (RespCommand)len16Val0;
                    return (len16Val1 == len16p0 & len16Val2 == len16p1);
                case 17:
                    parsed = RespCommand.GEORADIUSBYMEMBER;
                    var len17p0 = *((ulong*)cmdStartPtr) & LOWER_TO_UPPER_ULONG;
                    var len17p1 = *((ulong*)(cmdStartPtr + 8)) & LOWER_TO_UPPER_ULONG;
                    var len17p2 = *((uint*)(cmdStartPtr + 13)) & LOWER_TO_UPPER_UINT;
                    return (len17p0 == RespParser.GEORADIU & len17p1 == RespParser.SBYMEMBE & len17p2 == RespParser.MBER);
                case 20:
                    parsed = RespCommand.GEORADIUSBYMEMBER_RO;
                    var len20p0 = *((ulong*)cmdStartPtr) & LOWER_TO_UPPER_ULONG;
                    var len20p1 = *((ulong*)(cmdStartPtr + 8)) & LOWER_TO_UPPER_ULONG;
                    var len20p2 = *((uint*)(cmdStartPtr + 16)) & LOWER_TO_UPPER_UINT;
                    return (len20p0 == RespParser.GEORADIU & len20p1 == RespParser.SBYMEMBE & len20p2 == RespParser.R_RO);
                default:
                    Unsafe.SkipInit(out parsed);
                    return false;
            }
        }
    }
}