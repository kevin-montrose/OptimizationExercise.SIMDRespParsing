using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace OptimizationExercise.SIMDRespParsing
{
    public static class RespParserV3
    {
        public readonly ref struct RefWrapper<T>
            where T : unmanaged
        {
            private readonly ref T wrapped;

            public RefWrapper(ref T toWrap)
            {
                wrapped = ref toWrap;
            }

            public readonly ref T Unwrap()
            => ref wrapped;
        }

        public readonly ref struct UpdatedPairs
        {
            private readonly ref byte currentCommandBufferRef;
            private readonly ref int writeNextResultIntoRef;

            public UpdatedPairs(ref byte commandBufferRef, ref int nextResultIntoRef)
            {
                currentCommandBufferRef = ref commandBufferRef;
                writeNextResultIntoRef = ref nextResultIntoRef;
            }

            public readonly void Deconstruct(out RefWrapper<byte> currentCommandBufferRef, out RefWrapper<int> writeNextResultIntoRef)
            {
                currentCommandBufferRef = new(ref this.currentCommandBufferRef);
                writeNextResultIntoRef = new(ref this.writeNextResultIntoRef);
            }
        }

        public const uint FALSE = 0U;
        public const uint TRUE = ~FALSE;

        private const byte ArrayStart = (byte)'*';
        private const byte BulkStringStart = (byte)'$';
        private const byte ZERO = (byte)'0';
        private const byte NINE = (byte)'9';

        private const ushort CRLF = (('\r' << 0) | ('\n' << 8)); // Little-endian, so LF is the high byte

        public static void Parse(
            int commandBufferAllocatedSize,
            Span<byte> commandBufferTotalSpan,
            int commandBufferFilledBytes,
            int bitmapScratchBufferAllocatedSize,
            Span<byte> bitmapScratchBuffer,
            Span<ParsedRespCommandOrArgument> intoCommandsSpan,
            out int intoCommandsSlotsUsed,
            out int bytesConsumed
        )
        {
            Debug.Assert(commandBufferFilledBytes > 0, "Illegal to call if no data has been received");
            Debug.Assert(!intoCommandsSpan.IsEmpty, "Illegal to call if no space for any results");

            Debug.Assert(BitConverter.IsLittleEndian, "Hard assumptions about byte ordering are in here");

            Debug.Assert((commandBufferTotalSpan.Length % Vector512<byte>.Count) == 0, "We assume we can over-read a bit, so buffer size must be multiple of 64");
            Debug.Assert(bitmapScratchBuffer.Length >= commandBufferTotalSpan.Length / 8, "Must be perfectly sized for command buffer");

            ref var commandBuffer = ref MemoryMarshal.GetReference(commandBufferTotalSpan);
            ref var commandBufferEnd = ref Unsafe.Add(ref commandBuffer, commandBufferAllocatedSize);
            ref var digitsBitmap = ref MemoryMarshal.GetReference(bitmapScratchBuffer);
            ref var digitsBitmapEnd = ref Unsafe.Add(ref digitsBitmap, bitmapScratchBufferAllocatedSize);

            //ScanForSigils(ref commandBufferEnd, ref commandBuffer, ref digitsBitmapEnd, ref digitsBitmap, commandBufferFilledBytes);
            {
                Debug.Assert(commandBufferFilledBytes > 0, "Do not call with empty buffer");

                var zeros = Vector512.Create(ZERO);
                var nines = Vector512.Create(NINE);

                //ref var curCmd = ref commandBuffer;
                ref var cmdEnd = ref Unsafe.Add(ref commandBuffer, commandBufferFilledBytes);

                ref var curBitmap = ref digitsBitmap;

                // we always go at least one round, so elide a comparison
                do
                {
                    // read in chunk of characters, going past the new stuff (or into padding) is handled fine later
                    Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref commandBuffer, Vector512<byte>.Count), ref commandBufferEnd), "About to read past end of allocated command buffer");
                    var data = Vector512.LoadUnsafe(ref commandBuffer);

                    // determine where the [0-9] characters are
                    var d0 = Vector512.GreaterThanOrEqual(data, zeros);
                    var d1 = Vector512.LessThanOrEqual(data, nines);

                    var digits = Vector512.BitwiseAnd(d0, d1);
                    var digitsPacked = Vector512.ExtractMostSignificantBits(digits);

                    // write digits bitmap, which may go into padding which is fine
                    Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref curBitmap, sizeof(ulong)), ref digitsBitmapEnd), "About to write past end of allocated digit bitmap");
                    Unsafe.As<byte, ulong>(ref curBitmap) = digitsPacked;

                    // advance!
                    commandBuffer = ref Unsafe.Add(ref commandBuffer, Vector512<byte>.Count);
                    curBitmap = ref Unsafe.Add(ref curBitmap, sizeof(ulong));
                } while (Unsafe.IsAddressLessThan(ref commandBuffer, ref cmdEnd));
            }

            commandBuffer = ref MemoryMarshal.GetReference(commandBufferTotalSpan);

            ref var intoCommandsStartRef = ref MemoryMarshal.GetReference(intoCommandsSpan);
            var intoSize = intoCommandsSpan.Length;
            //TakeMultipleCommands(commandBufferAllocatedSize, ref commandBuffer, commandBufferFilledBytes, bitmapScratchBufferAllocatedSize, ref digitsBitmap, intoSize, ref intoCommandsStartRef, out intoCommandsSlotsUsed, out bytesConsumed);
            {
                Debug.Assert(commandBufferAllocatedSize > 0, "Must have some data to parse");
                Debug.Assert(intoSize > 0, "Into cannot be empty, and must have enough space for at least one full entry");

                // parsing RESP with (almost) no branches
                //
                // idea is we can be in a couple states
                //  1. everything is fine
                //  2. we've run out of data
                //     * this can happen if either commandBuffer is exhausted, or into is exhausted
                //  3. we've encountered an error
                //
                // running out of data implies being in an error state, BUT
                // we shouldn't write out any errors

                // inErrorState == TRUE for 2 or 3
                var inErrorState = FALSE;

                // ranOutOfData == TRUE only for 2
                var ranOutOfData = FALSE;

                // directly manipulating these as ints is easier
                ref var intoAsInts = ref Unsafe.As<ParsedRespCommandOrArgument, int>(ref intoCommandsStartRef);
                ref var intoAsIntsAllocatedEnd = ref Unsafe.Add(ref intoAsInts, intoSize * 4);

                // advances as each element is taken
                scoped ref var currentCommandRef = ref commandBuffer;

                // advances by 4 for each ParsedRespCommandOrArgument written
                scoped ref var currentIntoRef = ref intoAsInts;

                //ref var commandBufferAllocatedEnd = ref Unsafe.Add(ref commandBuffer, commandBufferAllocatedSize);
                //ref var digitsBitmapAllocatedEnd = ref Unsafe.Add(ref digitsBitmap, bitmapScratchBufferAllocatedSize);

                // always take at least one
                do
                {
                    //var (newCmd, newInto) = UnrollReadCommand_ManualInline(ref commandBufferAllocatedEnd, ref commandBuffer, commandBufferUsedBytes, ref digitBitmapAllocatedEnd, ref digitsBitmap, ref intoAsIntsAllocatedEnd, ref currentCommandRef, ref currentIntoRef, ref inErrorState, ref ranOutOfData);
                    {
                        const int MinimumCommandSize = 1 + 1 + 2;   // *0\r\n
                        const uint CommonArrayLengthSuffix = 0x240A_0D31;  // $\n\r1
                        const uint CommonCommandLengthPrefix = 0x0A0D_3024; // \n\r0$

                        Debug.Assert(commandBufferFilledBytes > 0, "Command buffer should have data in it");

                        // for this, we attempt to read the a command, BUT if we're already in an error state
                        // we throw the result away with some ANDS
                        //
                        // if we encounter an error, we enter the error state and we write out a malformed
                        // if we run out of data, we enter an error state that but do not write out a malformed
                        //
                        // if we're in an error state by the end, we rollback currentCommandBufferIx and writeNextResultInto
                        // to their initial states

                        var rollbackCommandRefCount = 0;
                        var rollbackWriteIntoRefCount = 0;

                        var oldErrorState = inErrorState;

                        // first check that there's sufficient space for a command
                        var availableBytes = commandBufferFilledBytes - (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
                        ranOutOfData |= (uint)((availableBytes - MinimumCommandSize) >> 31);    // availableBytes < MinimumCommandSize
                        inErrorState |= ranOutOfData;

                        // check that we can even write a malformed, if needed
                        ranOutOfData |= (uint)((~(-(int)Unsafe.ByteOffset(ref currentIntoRef, ref intoAsIntsAllocatedEnd))) >> 31);  // currentIntoRef == intoAsIntsAllocatedEnd
                        inErrorState |= ranOutOfData;

                        // check for *
                        Debug.Assert(Unsafe.IsAddressLessThan(ref currentCommandRef, ref commandBufferEnd), "About to read past end of allocated command buffer");
                        inErrorState |= (uint)((-(currentCommandRef ^ ArrayStart)) >> 31);   // currentCommandRef != ArrayStart

                        // advance past *
                        currentCommandRef = ref Unsafe.Add(ref currentCommandRef, 1);
                        rollbackCommandRefCount++;

                        // will be [0-9]\r\n$ MOST of the time, so eat a branch here
                        Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref currentCommandRef, sizeof(uint)), ref commandBufferEnd), "About to read past end of allocated command buffer");
                        var arrayLengthProbe = Unsafe.As<byte, uint>(ref currentCommandRef);
                        arrayLengthProbe -= CommonArrayLengthSuffix;

                        int arrayLength;
                        if (arrayLengthProbe < 8)
                        {
                            // array length of 0 is illegal, so we -1 in the probe
                            // so that we don't need to check length in the common case
                            //
                            // but that means we need to +1 to get the real length later
                            arrayLength = (int)(arrayLengthProbe + 1);

                            // skip 1 digit, \r, and \n
                            currentCommandRef = ref Unsafe.Add(ref currentCommandRef, 3);
                            rollbackCommandRefCount += 3;
                        }
                        else
                        {
                            // get the number of parts in the array

                            //currentCommandRef = ref TakePositiveNumberBranchless_ManualInline(ref commandBufferAllocatedEnd, ref commandBuffer, commandBufferFilledBytes, ref digitsBitmapAllocatedEnd, ref digitsBitmap, ref currentCommandRef, ref rollbackCommandRefCount, ref inErrorState, ref ranOutOfData, out arrayLength);
                            {
                                Debug.Assert(commandBufferFilledBytes > 0, "Cannot call with empty buffer");

                                var commandBufferIx = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
                                var startByteIndex = commandBufferIx / 8;
                                var startBitIndex = commandBufferIx % 8;

                                // we expect a run of digits followed by a \r\n, so digitBitmap should be 0b0...xxx1
                                // a run of 11+ is invalid, considering a shift of up to 8 this will always fit in a uint32
                                Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsBitmap, startByteIndex + sizeof(uint)), ref digitsBitmapEnd), "About to read past end of digits bitmap");

                                var digitsBitmapValue = Unsafe.As<byte, uint>(ref Unsafe.Add(ref digitsBitmap, startByteIndex)) >> startBitIndex;
                                var digitCount = (uint)BitOperations.TrailingZeroCount(~digitsBitmapValue);

                                // we expect no more than 10 digits (int.MaxValue is 2,147,483,647), and at least 1 digit
                                inErrorState |= ((10 - digitCount) >> 31);  // digitCount >= 11

                                // check for \r\n
                                ref var expectedCrLfRef = ref Unsafe.Add(ref currentCommandRef, digitCount);
                                var expectedCrPosition = (int)Unsafe.ByteOffset(ref commandBuffer, ref expectedCrLfRef);
                                ranOutOfData |= (uint)((commandBufferFilledBytes - (expectedCrPosition + 2)) >> 31);    // expectedCrPosition + 2 > commandBufferFilledBytes
                                inErrorState |= ranOutOfData;

                                Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref expectedCrLfRef, sizeof(ushort)), ref commandBufferEnd), "About to read past end of allocated command buffer");
                                inErrorState |= (uint)((0 - (Unsafe.As<byte, ushort>(ref expectedCrLfRef) ^ CRLF)) >> 31);    // expectedCrLfRef != CRLF;

                                // if we're in an error state, we'll take an empty buffer
                                // but we'll get a good one if we're not
                                ref var digitsStartRef = ref currentCommandRef;
                                digitsStartRef = ref Unsafe.Subtract(ref digitsStartRef, (int)(inErrorState & rollbackCommandRefCount));
                                ref var digitsEndRef = ref expectedCrLfRef;
                                digitsEndRef = ref Unsafe.Subtract(ref digitsEndRef, (int)(inErrorState & (rollbackCommandRefCount + digitCount)));

                                // actually do the parsing
                                // parsed = UnconditionalParsePositiveInt(ref commandBufferAllocatedEnd, ref digitsStartRef, ref digitsEndRef);
                                {
                                    Debug.Assert(Unsafe.IsAddressGreaterThan(ref digitsEndRef, ref digitsStartRef) || Unsafe.AreSame(ref digitsStartRef, ref digitsEndRef), "Incoherent digit end and start");
                                    Debug.Assert(Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef) is >= 0 and < 11, "Should have validated correct size");
                                    Debug.Assert(!MemoryMarshal.CreateSpan(ref digitsStartRef, (int)Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef)).ContainsAnyExceptInRange((byte)'0', (byte)'9'), "Should have validated digits only");

                                    var length = (int)Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef);

                                    var multLookupIx = length * 12;
                                    ref var multStart = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(UnconditionalParsePositiveIntMultiples), multLookupIx);

                                    // many numbers are going to fit in 4 digits
                                    // 0\r\n* = len = 1 = 0x**_\n_\r_00
                                    // 01\r\n = len = 2 = 0x\n_\r_11_00
                                    // 012\r  = len = 3 = 0x\r_22_11_00
                                    // 0123   = len = 4 = 0x33_22_11_00
                                    if (length <= 4)
                                    {
                                        // load all the digits (and some extra junk, maybe)
                                        Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsStartRef, sizeof(uint)), ref commandBufferEnd), "About to read past end of command buffer");
                                        var fastPathDigits = Unsafe.As<byte, uint>(ref digitsStartRef);

                                        // reverse so we can pad more easily
                                        // 0\r\n* = len = 1 = 0x**_\n_\r_00 => 0x00_\r_\n_**
                                        // 01\r\n = len = 2 = 0x\n_\r_11_00 => 0x00_11_\r_\n
                                        // 012\r  = len = 3 = 0x\r_22_11_00 => 0x00_11_22_\r
                                        // 0123   = len = 4 = 0x33_22_11_00 => 0x00_11_22_33
                                        fastPathDigits = BinaryPrimitives.ReverseEndianness(fastPathDigits); // this is an intrinsic, so should be a single op

                                        // shift right to pad with zeros
                                        // 0\r\n* = len = 1 = 0x**_\n_\r_00 => 0x00_\r_\n_** => 0x00_00_00_00 ; shift right 24 (32 - (8 * len))
                                        // 01\r\n = len = 2 = 0x\n_\r_11_00 => 0x00_11_\r_\n => 0x00_00_00_11 ; shift right 16 (32 - (8 * len))
                                        // 012\r  = len = 3 = 0x\r_22_11_00 => 0x00_11_22_\r => 0x00_00_11_22 ; shift right  8 (32 - (8 * len))
                                        // 0123   = len = 4 = 0x33_22_11_00 => 0x00_11_22_33 => 0x00_11_22_33 ; shift right  0 (32 - (8 * len))
                                        fastPathDigits = fastPathDigits >> (32 - (8 * length));

                                        // fastPathDigits = 0x.4_.3__.2_.1
                                        // onesAndHundreds = 0x00_03__00_01
                                        var onesAndHundreds = fastPathDigits & ~0xFF_30_FF_30U;
                                        // tensAndThousands = 0x04_00__02_00
                                        var tensAndThousands = fastPathDigits & ~0x30_FF_30_FFU;

                                        // mixedOnce = 0x(4 * 10 + 3)__(2 * 10 + 1) = 0d043__21
                                        var mixedOnce = (tensAndThousands * 10U) + (onesAndHundreds << 8);

                                        // topPair = 0d43__00
                                        ulong topPair = mixedOnce & 0xFF_FF_00_00U;
                                        // lowPair = 0d00__21
                                        ulong lowPair = mixedOnce & 0x00_00_FF_FFU;

                                        // mixedTwice = 0d(43 * 100 + 21)_00
                                        var mixedTwice = (topPair * 100U) + (lowPair << 16);

                                        var result = (int)(mixedTwice >> 24);

                                        // leading zero check, force result to -1 if below expected value
                                        result |= ((-(length ^ 1)) >> 31) & ((result - (int)multStart) >> 31);   // length != 1 & multStart > result;

                                        arrayLength = result;
                                    }
                                    else
                                    {
                                        var maskLookupIx = length * 16;

                                        ref var maskStart = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(UnconditionalParsePositiveIntLookup), maskLookupIx);

                                        var mask = Vector128.LoadUnsafe(ref maskStart);

                                        // load 16 bytes of data, which will have \r\n and some trailing garbage
                                        Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsStartRef, Vector128<byte>.Count), ref commandBufferEnd), "About to read past end of allocated command buffer");
                                        var data = Vector128.LoadUnsafe(ref digitsStartRef);

                                        var toUseBytes = Vector128.BitwiseAnd(data, mask);                        // bytes 0-9 are (potentially) valid

                                        // expand so we can multiply cleanly
                                        var (d0To7, d8To15) = Vector128.Widen(toUseBytes);
                                        var (d0To3, d4To7) = Vector128.Widen(d0To7);
                                        var (d8To11, _) = Vector128.Widen(d8To15);

                                        // load per digit multiples
                                        var scale0To3 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 0));
                                        var scale4To7 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 4));
                                        var scale8To11 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 8));

                                        // scale each digit by it's place value
                                        // 
                                        // at maximum length, m0To3[0] might overflow
                                        var m0To3 = Vector128.Multiply(d0To3, scale0To3);
                                        var m4To7 = Vector128.Multiply(d4To7, scale4To7);
                                        var m8To11 = Vector128.Multiply(d8To11, scale8To11);

                                        var maybeMultOverflow = Vector128.LessThan(m0To3, scale0To3);

                                        // calculate the vertical sum
                                        // 
                                        // vertical sum cannot overflow (assuming m0To3 didn't overflow)
                                        var verticalSum = Vector128.Add(Vector128.Add(m0To3, m4To7), m8To11);

                                        // calculate the horizontal sum
                                        //
                                        // horizontal sum can overflow, even if m0To3 didn't overflow
                                        var horizontalSum = Vector128.Dot(verticalSum, Vector128<uint>.One);

                                        var maybeHorizontalOverflow = Vector128.GreaterThan(verticalSum, Vector128.CreateScalar(horizontalSum));

                                        var maybeOverflow = Vector128.BitwiseOr(maybeMultOverflow, maybeHorizontalOverflow);

                                        var res = horizontalSum;

                                        // check for overflow
                                        //
                                        // note that overflow can only happen when length == 10
                                        // only the first digit can overflow on it's own (if it's > 2)
                                        // everything else has to overflow when summed
                                        // 
                                        // we know length is <= 10, we can exploit this in the == check
                                        var allOnesIfOverflow = (uint)(((9 - length) >> 31) & ((0L - maybeOverflow.GetElement(0)) >> 63));  // maybeOverflow.GetElement(0) != 0 & length == 10

                                        // check for leading zeros
                                        var allOnesIfLeadingZeros = (uint)(((-(length ^ 1)) >> 31) & (((int)res - (int)multStart) >> 31)); // length != 1 & multStart > (int)res

                                        // turn into -1 if things are invalid
                                        res |= allOnesIfOverflow | allOnesIfLeadingZeros;

                                        arrayLength = (int)res;
                                    }
                                }
                                inErrorState |= (uint)(arrayLength >> 31);    // value < 0

                                // update to point one past the \r\n if we're not in an error state
                                // otherwise leave it unmodified
                                currentCommandRef = ref Unsafe.Add(ref currentCommandRef, (int)((digitCount + 2) & ~inErrorState));
                                rollbackCommandRefCount += (int)((digitCount + 2) & ~inErrorState);
                            }

                            inErrorState |= (uint)((arrayLength - 1) >> 31);    // arrayLength < 1
                        }

                        var remainingArrayItems = arrayLength;

                        // first string will be a command, so we can do a smarter length check MOST of the time
                        // worth it to eat a branch then
                        Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref currentCommandRef, sizeof(uint)), ref commandBufferEnd), "About to read past end of command buffer");
                        var cmdLengthProbe = Unsafe.As<byte, uint>(ref currentCommandRef);
                        cmdLengthProbe -= CommonCommandLengthPrefix;
                        cmdLengthProbe = BitOperations.RotateRight(cmdLengthProbe, 8);
                        if (cmdLengthProbe < 10)
                        {
                            // handle the common case where command length is < 10

                            ranOutOfData |= (uint)~(((int)-Unsafe.ByteOffset(ref currentIntoRef, ref intoAsIntsAllocatedEnd)) >> 31);                // currentIntoRef == intoAsIntsAllocatedEnd

                            var byteAfterCurrentCommandRef = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
                            ranOutOfData |= (uint)(((commandBufferFilledBytes - byteAfterCurrentCommandRef) - 4) >> 31);    // (commandBufferFilledBytes - byteAfterCurrentCommandRef) < 4  (considering $\d\r\n)
                            inErrorState |= ranOutOfData;

                            // if we're out of data, we also need an extra bit of rollback to prevent 
                            var outOfDataRollback = (int)ranOutOfData & 4;

                            // get a ref which is rolled back to the original value IF we're in an errorState
                            ref var commandIntoRef = ref Unsafe.Subtract(ref currentIntoRef, (rollbackWriteIntoRefCount & (int)inErrorState) + outOfDataRollback);
                            var commonCommandStart = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef) + 4;
                            var commonCommandEnd = (int)(commonCommandStart + cmdLengthProbe + 2);

                            ranOutOfData |= (uint)((commandBufferFilledBytes - commonCommandEnd) >> 31);    // commonCommandEnd > commandBufferFilledBytes
                            inErrorState |= ranOutOfData;

                            // check for \r\n
                            var trailingCrLfOffset = ~inErrorState & (4 + cmdLengthProbe);
                            ref var trailingCrLfRef = ref Unsafe.Add(ref currentCommandRef, trailingCrLfOffset);
                            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref trailingCrLfRef, sizeof(ushort)), ref commandBufferEnd), "About to read past end of command buffer");
                            var trailingCrLf = Unsafe.As<byte, ushort>(ref trailingCrLfRef);
                            inErrorState |= (uint)((-(trailingCrLf ^ CRLF)) >> 31);  // trailingCrLf != CRLF

                            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref commandIntoRef, ParsedRespCommandOrArgument.ByteEndIx), ref intoAsIntsAllocatedEnd), "About to read/write past end of intoAsInts");
                            ref var commonCmdAndArgCountRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref commandIntoRef, ParsedRespCommandOrArgument.ArgumentCountIx));
                            ref var commonByteStartAndEndRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref commandIntoRef, ParsedRespCommandOrArgument.ByteStartIx));

                            // clear argCount & cmd if not in error state
                            commonCmdAndArgCountRef &= (int)inErrorState;

                            // leave unmodified if in error state, or set to commonCommandStart & commonCommandEnd
                            var packedStartAndEnd = (long)(((uint)commonCommandStart) | (((ulong)(uint)commonCommandEnd) << 32));
                            commonByteStartAndEndRef = (commonByteStartAndEndRef & (long)(int)inErrorState) | (packedStartAndEnd & ~(long)(int)inErrorState);

                            // advance writeNextResultInto if not in error state
                            rollbackWriteIntoRefCount += (int)(4 & ~inErrorState);
                            currentIntoRef = ref Unsafe.Add(ref currentIntoRef, (int)(4 & ~inErrorState));
                            remainingArrayItems--;

                            // update currentCommandBufferIx if not in error state
                            currentCommandRef = ref Unsafe.Add(ref currentCommandRef, (int)(~inErrorState & (4 + cmdLengthProbe + 2)));
                            rollbackCommandRefCount += (int)(~inErrorState & (4 + cmdLengthProbe + 2));
                        }

                        // handle remaining strings (which may not include the command string)
                        do
                        {
                            //var (updateCommandRef, updateWriteNextRef) = UnrollTakeCommandBulkString_ManualInline(ref commandBufferAllocatedEnd, ref commandBuffer, commandBufferFilledBytes, ref digitsBitmapAllocatedEnd, ref digitsBitmap, ref intoAsIntsAllocatedEnd, ref currentCommandRef, ref currentIntoRef, ref remainingArrayItems, ref rollbackCommandRefCount, ref rollbackWriteIntoRefCount, ref inErrorState, ref ranOutOfData);
                            {
                                const int MinimumBulkStringSize = 1 + 1 + 2 + 2; // $0\r\n\r\n

                                Debug.Assert(Unsafe.ByteOffset(ref commandBuffer, ref commandBufferEnd) > 0, "Command buffer cannot be of 0 size");

                                // if we've read everything, we need to just do nothing (including updating inErrorState and ranOutOfData)
                                // but still run through everything unconditionally
                                var readAllItems = (uint)~((-remainingArrayItems) >> 31);  // remainingItemsInArray == 0
                                var oldInErrorState = inErrorState;
                                var oldRanOutOfData = ranOutOfData;

                                // we need to prevent action now, so act like we're in an error state
                                inErrorState |= readAllItems;

                                // check that there's enough data to even fit a string
                                var availableBytesSub = commandBufferFilledBytes - (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
                                ranOutOfData |= (uint)((availableBytesSub - MinimumBulkStringSize) >> 31); // availableBytes < MinimumBulkStringSize
                                inErrorState |= ranOutOfData;

                                // check for $
                                ref var bulkStringStartExpectedRef = ref currentCommandRef;

                                Debug.Assert(Unsafe.IsAddressLessThan(ref bulkStringStartExpectedRef, ref commandBufferEnd), "About to read past end of allocated command buffer");
                                inErrorState |= (uint)((-(bulkStringStartExpectedRef ^ BulkStringStart)) >> 31); // (ref commandBuffer + bulkStringStartExpectedIx) != BulkStringStart

                                // move past $
                                currentCommandRef = ref Unsafe.Add(ref currentCommandRef, 1);
                                rollbackCommandRefCount++;

                                // parse the length
                                //currentCommandRef = ref TakePositiveNumberBranchless_ManualInline(ref commandBufferAllocatedEnd, ref commandBuffer, commandBufferFilledBytes, ref digitsBitmapAllocatedEnd, ref digitsBitmap, ref currentCommandRef, ref rollbackCommandRefCount, ref inErrorState, ref ranOutOfData, out var bulkStringLength);
                                int bulkStringLength;
                                {
                                    Debug.Assert(commandBufferFilledBytes > 0, "Cannot call with empty buffer");

                                    var commandBufferIx = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
                                    var startByteIndex = commandBufferIx / 8;
                                    var startBitIndex = commandBufferIx % 8;

                                    // we expect a run of digits followed by a \r\n, so digitBitmap should be 0b0...xxx1
                                    // a run of 11+ is invalid, considering a shift of up to 8 this will always fit in a uint32
                                    Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsBitmap, startByteIndex + sizeof(uint)), ref digitsBitmapEnd), "About to read past end of digits bitmap");

                                    var digitsBitmapValue = Unsafe.As<byte, uint>(ref Unsafe.Add(ref digitsBitmap, startByteIndex)) >> startBitIndex;
                                    var digitCount = (uint)BitOperations.TrailingZeroCount(~digitsBitmapValue);

                                    // we expect no more than 10 digits (int.MaxValue is 2,147,483,647), and at least 1 digit
                                    inErrorState |= ((10 - digitCount) >> 31);  // digitCount >= 11

                                    // check for \r\n
                                    ref var expectedCrLfRef = ref Unsafe.Add(ref currentCommandRef, digitCount);
                                    var expectedCrPosition_inline = (int)Unsafe.ByteOffset(ref commandBuffer, ref expectedCrLfRef);
                                    ranOutOfData |= (uint)((commandBufferFilledBytes - (expectedCrPosition_inline + 2)) >> 31);    // expectedCrPosition + 2 > commandBufferFilledBytes
                                    inErrorState |= ranOutOfData;

                                    Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref expectedCrLfRef, sizeof(ushort)), ref commandBufferEnd), "About to read past end of allocated command buffer");
                                    inErrorState |= (uint)((-(Unsafe.As<byte, ushort>(ref expectedCrLfRef) ^ CRLF)) >> 31);    // expectedCrLfRef != CRLF;

                                    // if we're in an error state, we'll take an empty buffer
                                    // but we'll get a good one if we're not
                                    ref var digitsStartRef = ref currentCommandRef;
                                    digitsStartRef = ref Unsafe.Subtract(ref digitsStartRef, (int)(inErrorState & rollbackCommandRefCount));
                                    ref var digitsEndRef = ref expectedCrLfRef;
                                    digitsEndRef = ref Unsafe.Subtract(ref digitsEndRef, (int)(inErrorState & (rollbackCommandRefCount + digitCount)));

                                    // actually do the parsing
                                    // parsed = UnconditionalParsePositiveInt(ref commandBufferAllocatedEnd, ref digitsStartRef, ref digitsEndRef);
                                    {
                                        Debug.Assert(Unsafe.IsAddressGreaterThan(ref digitsEndRef, ref digitsStartRef) || Unsafe.AreSame(ref digitsStartRef, ref digitsEndRef), "Incoherent digit end and start");
                                        Debug.Assert(Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef) is >= 0 and < 11, "Should have validated correct size");
                                        Debug.Assert(!MemoryMarshal.CreateSpan(ref digitsStartRef, (int)Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef)).ContainsAnyExceptInRange((byte)'0', (byte)'9'), "Should have validated digits only");

                                        var length = (int)Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef);

                                        var multLookupIx = length * 12;
                                        ref var multStart = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(UnconditionalParsePositiveIntMultiples), multLookupIx);

                                        // many numbers are going to fit in 4 digits
                                        // 0\r\n* = len = 1 = 0x**_\n_\r_00
                                        // 01\r\n = len = 2 = 0x\n_\r_11_00
                                        // 012\r  = len = 3 = 0x\r_22_11_00
                                        // 0123   = len = 4 = 0x33_22_11_00
                                        if (length <= 4)
                                        {
                                            // load all the digits (and some extra junk, maybe)
                                            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsStartRef, sizeof(uint)), ref commandBufferEnd), "About to read past end of command buffer");
                                            var fastPathDigits = Unsafe.As<byte, uint>(ref digitsStartRef);

                                            // reverse so we can pad more easily
                                            // 0\r\n* = len = 1 = 0x**_\n_\r_00 => 0x00_\r_\n_**
                                            // 01\r\n = len = 2 = 0x\n_\r_11_00 => 0x00_11_\r_\n
                                            // 012\r  = len = 3 = 0x\r_22_11_00 => 0x00_11_22_\r
                                            // 0123   = len = 4 = 0x33_22_11_00 => 0x00_11_22_33
                                            fastPathDigits = BinaryPrimitives.ReverseEndianness(fastPathDigits); // this is an intrinsic, so should be a single op

                                            // shift right to pad with zeros
                                            // 0\r\n* = len = 1 = 0x**_\n_\r_00 => 0x00_\r_\n_** => 0x00_00_00_00 ; shift right 24 (32 - (8 * len))
                                            // 01\r\n = len = 2 = 0x\n_\r_11_00 => 0x00_11_\r_\n => 0x00_00_00_11 ; shift right 16 (32 - (8 * len))
                                            // 012\r  = len = 3 = 0x\r_22_11_00 => 0x00_11_22_\r => 0x00_00_11_22 ; shift right  8 (32 - (8 * len))
                                            // 0123   = len = 4 = 0x33_22_11_00 => 0x00_11_22_33 => 0x00_11_22_33 ; shift right  0 (32 - (8 * len))
                                            fastPathDigits = fastPathDigits >> (32 - (8 * length));

                                            // fastPathDigits = 0x.4_.3__.2_.1
                                            // onesAndHundreds = 0x00_03__00_01
                                            var onesAndHundreds = fastPathDigits & ~0xFF_30_FF_30U;
                                            // tensAndThousands = 0x04_00__02_00
                                            var tensAndThousands = fastPathDigits & ~0x30_FF_30_FFU;

                                            // mixedOnce = 0x(4 * 10 + 3)__(2 * 10 + 1) = 0d043__21
                                            var mixedOnce = (tensAndThousands * 10U) + (onesAndHundreds << 8);

                                            // topPair = 0d43__00
                                            ulong topPair = mixedOnce & 0xFF_FF_00_00U;
                                            // lowPair = 0d00__21
                                            ulong lowPair = mixedOnce & 0x00_00_FF_FFU;

                                            // mixedTwice = 0d(43 * 100 + 21)_00
                                            var mixedTwice = (topPair * 100U) + (lowPair << 16);

                                            var result = (int)(mixedTwice >> 24);

                                            // leading zero check, force result to -1 if below expected value
                                            result |= ((-(length ^ 1)) >> 31) & ((result - (int)multStart) >> 31);   // length != 1 & multStart > result;

                                            bulkStringLength = result;
                                        }
                                        else
                                        {
                                            var maskLookupIx = length * 16;

                                            ref var maskStart = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(UnconditionalParsePositiveIntLookup), maskLookupIx);

                                            var mask = Vector128.LoadUnsafe(ref maskStart);

                                            // load 16 bytes of data, which will have \r\n and some trailing garbage
                                            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsStartRef, Vector128<byte>.Count), ref commandBufferEnd), "About to read past end of allocated command buffer");
                                            var data = Vector128.LoadUnsafe(ref digitsStartRef);

                                            var toUseBytes = Vector128.BitwiseAnd(data, mask);                        // bytes 0-9 are (potentially) valid

                                            // expand so we can multiply cleanly
                                            var (d0To7, d8To15) = Vector128.Widen(toUseBytes);
                                            var (d0To3, d4To7) = Vector128.Widen(d0To7);
                                            var (d8To11, _) = Vector128.Widen(d8To15);

                                            // load per digit multiples
                                            var scale0To3 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 0));
                                            var scale4To7 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 4));
                                            var scale8To11 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 8));

                                            // scale each digit by it's place value
                                            // 
                                            // at maximum length, m0To3[0] might overflow
                                            var m0To3 = Vector128.Multiply(d0To3, scale0To3);
                                            var m4To7 = Vector128.Multiply(d4To7, scale4To7);
                                            var m8To11 = Vector128.Multiply(d8To11, scale8To11);

                                            var maybeMultOverflow = Vector128.LessThan(m0To3, scale0To3);

                                            // calculate the vertical sum
                                            // 
                                            // vertical sum cannot overflow (assuming m0To3 didn't overflow)
                                            var verticalSum = Vector128.Add(Vector128.Add(m0To3, m4To7), m8To11);

                                            // calculate the horizontal sum
                                            //
                                            // horizontal sum can overflow, even if m0To3 didn't overflow
                                            var horizontalSum = Vector128.Dot(verticalSum, Vector128<uint>.One);

                                            var maybeHorizontalOverflow = Vector128.GreaterThan(verticalSum, Vector128.CreateScalar(horizontalSum));

                                            var maybeOverflow = Vector128.BitwiseOr(maybeMultOverflow, maybeHorizontalOverflow);

                                            var res = horizontalSum;

                                            // check for overflow
                                            //
                                            // note that overflow can only happen when length == 10
                                            // only the first digit can overflow on it's own (if it's > 2)
                                            // everything else has to overflow when summed
                                            // 
                                            // we know length is <= 10, we can exploit this in the == check
                                            var allOnesIfOverflow = (uint)(((9 - length) >> 31) & ((0L - maybeOverflow.GetElement(0)) >> 63));  // maybeOverflow.GetElement(0) != 0 & length == 10

                                            // check for leading zeros
                                            var allOnesIfLeadingZeros = (uint)(((-(length ^ 1)) >> 31) & (((int)res - (int)multStart) >> 31)); // length != 1 & multStart > (int)res

                                            // turn into -1 if things are invalid
                                            res |= allOnesIfOverflow | allOnesIfLeadingZeros;

                                            bulkStringLength = (int)res;
                                        }
                                    }
                                    inErrorState |= (uint)(bulkStringLength >> 31);    // value < 0

                                    // update to point one past the \r\n if we're not in an error state
                                    // otherwise leave it unmodified
                                    currentCommandRef = ref Unsafe.Add(ref currentCommandRef, (int)((digitCount + 2) & ~inErrorState));
                                    rollbackCommandRefCount += (int)((digitCount + 2) & ~inErrorState);
                                }

                                var bulkStringStart = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
                                var bulkStringEnd = bulkStringStart + bulkStringLength;

                                // check for \r\n
                                var expectedCrPosition = bulkStringEnd;
                                ranOutOfData |= (uint)((commandBufferFilledBytes - (expectedCrPosition + 2)) >> 31);    // expectedCrPosition + 2 > commandBufferFilledBytes
                                inErrorState |= ranOutOfData;

                                expectedCrPosition &= (int)~inErrorState;
                                Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref commandBuffer, expectedCrPosition + sizeof(ushort)), ref commandBufferEnd), "About to read past allocated command buffer");
                                inErrorState |= (uint)((-(Unsafe.As<byte, ushort>(ref Unsafe.Add(ref commandBuffer, expectedCrPosition)) ^ CRLF)) >> 31);    // (ref commandBuffer + expectedCrPosition) != CRLF

                                // move past the \r\n
                                currentCommandRef = ref Unsafe.Add(ref currentCommandRef, bulkStringLength + 2);
                                rollbackCommandRefCount += bulkStringLength + 2;

                                // now we're ready to write out results

                                // do we even have space to write something?
                                ranOutOfData |= (uint)~(((int)-Unsafe.ByteOffset(ref currentIntoRef, ref intoAsIntsAllocatedEnd)) >> 31);    // writeNextResultInto == intoAsIntsSize
                                inErrorState |= ranOutOfData;

                                // write the results out
                                //var effectiveInto = (int)(~inErrorState & writeNextResultInto);
                                ref var effectiveInto = ref Unsafe.Subtract(ref currentIntoRef, inErrorState & 4);
                                Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref effectiveInto, ParsedRespCommandOrArgument.ByteEndIx), ref intoAsIntsAllocatedEnd), "About to read/write past end of intoAsInts");
                                //ref var intoAsIntsEffectiveIntoRef = ref Unsafe.Add(ref intoAsInts, effectiveInto);
                                ref var argCountAndCommandRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref effectiveInto, ParsedRespCommandOrArgument.ArgumentCountIx));
                                ref var byteStartAndEndRefSub = ref Unsafe.As<int, long>(ref Unsafe.Add(ref effectiveInto, ParsedRespCommandOrArgument.ByteStartIx));

                                // clear argCount and command UNLESS we're in an error state
                                argCountAndCommandRef &= (int)inErrorState;

                                // set byteStart and byteEnd if we're not in an error state, otherwise leave them unmodified
                                var packedByteStartAndEnd = (long)(((uint)bulkStringStart) | (((ulong)(uint)(expectedCrPosition + 2)) << 32));
                                byteStartAndEndRefSub = (byteStartAndEndRefSub & (int)inErrorState) | (packedByteStartAndEnd & ~(int)inErrorState);

                                // now update state

                                // advance write target if we succeeded
                                currentIntoRef = ref Unsafe.Add(ref currentIntoRef, (int)(~inErrorState & 4));
                                rollbackWriteIntoRefCount += (int)(~inErrorState & 4);

                                // note that we rollback currentCommandBufferIx if there's any error
                                //
                                // if we've read past the end of the command, we will _for sure_ be in error state
                                //currentCommandBufferIx = (int)((inErrorState & oldCurrentCommandBufferIx) | (~inErrorState & currentCommandBufferIx));
                                currentCommandRef = ref Unsafe.Subtract(ref currentCommandRef, (int)(inErrorState & (uint)(bulkStringLength + 2 + 1)));
                                rollbackCommandRefCount -= (int)(inErrorState & (uint)(bulkStringLength + 2 + 1));

                                // update the number of items read from the array, if appropriate
                                var shouldUpdateRemainingItemCount = ~readAllItems & ~inErrorState;
                                remainingArrayItems -= (int)(shouldUpdateRemainingItemCount & 1);

                                // but we rollback errors and out of data if we fully consumed the array
                                inErrorState = ((readAllItems & oldInErrorState) | (~readAllItems & inErrorState));
                                ranOutOfData = ((readAllItems & oldRanOutOfData) | (~readAllItems & ranOutOfData));

                                // pun away scoped, we know this is safe
                                //return new(ref Unsafe.AsRef(ref currentCommandRef), ref Unsafe.AsRef(ref currentIntoRef));
                            }

                            //currentCommandRef = ref updateCommandRef.Unwrap();
                            //currentIntoRef = ref updateWriteNextRef.Unwrap();
                        } while ((remainingArrayItems & ~inErrorState) != 0);

                        // here we need an adjustment to avoid read/writing past end of currentIntoRef
                        var effectiveIntoReverse = (int)(ranOutOfData & 4);

                        // we've now parsed everything EXCEPT the command enum
                        // and we haven't written the argument count either
                        //
                        // we might be in an error state, or we might be out of data
                        //
                        // if we're out of data, we just need to take nothing (rolling everything back)
                        ref var effectInto = ref Unsafe.Subtract(ref currentIntoRef, rollbackWriteIntoRefCount + effectiveIntoReverse);
                        Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ByteEndIx), ref intoAsIntsAllocatedEnd), "About to read/write past the end of intoAsInts");
                        var cmdStart = (int)(Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ByteStartIx) & ~inErrorState);
                        var cmdEnd = (int)(Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ByteEndIx) & ~inErrorState);

                        // place ourselves in an error state if we could not parse the command
                        //var cmd = UnconditionalParseRespCommandImpl.Parse(ref commandBufferAllocatedEnd, ref commandBuffer, cmdStart, (cmdEnd - cmdStart) - 2);
                        RespCommand cmd;
                        {
                            var commandLength = (cmdEnd - cmdStart) - 2;
                            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref commandBuffer, cmdStart + Vector256<byte>.Count), ref commandBufferEnd), "About to read past end of allocated command buffer");
                            var commandVector = Vector256.LoadUnsafe(ref Unsafe.Add(ref commandBuffer, cmdStart));

                            var effectiveLength = commandLength & 31;
                            var maskIx = effectiveLength * 64;
                            var xorIx = maskIx + 32;

                            ref var lengthMaskAndHashXorsRef = ref MemoryMarshal.GetArrayDataReference(UnconditionalParseRespCommandImpl.LengthMaskAndHashXors);
                            Debug.Assert(xorIx + sizeof(uint) <= UnconditionalParseRespCommandImpl.LengthMaskAndHashXors.Length, "About to read past end of LengthMaskAndHashXors");
                            var maskVector = Vector256.LoadUnsafe(ref Unsafe.Add(ref lengthMaskAndHashXorsRef, maskIx));
                            var xorVector = Vector256.Create(Unsafe.As<byte, uint>(ref Unsafe.Add(ref lengthMaskAndHashXorsRef, xorIx)));

                            var truncatedCommandVector = Vector256.BitwiseAnd(commandVector, Vector256.GreaterThan(maskVector, Vector256<byte>.Zero));
                            var upperCommandVector = Vector256.BitwiseAnd(truncatedCommandVector, maskVector);

                            var invalid = Vector256.GreaterThanAny(truncatedCommandVector, Vector256.Create((byte)'z'));
                            var allZerosIfInvalid = Unsafe.As<bool, byte>(ref invalid) - 1;

                            var xored = Vector256.Xor(upperCommandVector, Vector256.As<uint, byte>(xorVector));
                            var mixed = Vector256.Dot(Vector256.As<byte, uint>(xored), Vector256<uint>.One);
                            var moded = mixed % 71;

                            var dataStartIx = 8192 * effectiveLength;
                            dataStartIx += (int)(moded * 64);

                            ref var commandAndExpectedValuesRef = ref MemoryMarshal.GetArrayDataReference(UnconditionalParseRespCommandImpl.CommandAndExpectedValues);
                            Debug.Assert(dataStartIx + sizeof(uint) + Vector256<byte>.Count <= UnconditionalParseRespCommandImpl.CommandAndExpectedValues.Length, "About to read past end of CommandAndExpectedValues");
                            var cmdSub = Unsafe.As<byte, int>(ref Unsafe.Add(ref commandAndExpectedValuesRef, dataStartIx));
                            var expectedCommandVector = Vector256.LoadUnsafe(ref Unsafe.Add(ref commandAndExpectedValuesRef, dataStartIx + 4));

                            var matches = Vector256.EqualsAll(upperCommandVector, expectedCommandVector);
                            var allOnesIfMatches = (-Unsafe.As<bool, byte>(ref matches)) >> 31;

                            cmd = (RespCommand)(cmdSub & allOnesIfMatches & allZerosIfInvalid);
                        }
                        inErrorState |= (uint)~(-(int)cmd >> 31);   // cmd == 0

                        // we need to update the first entry, to either have command and argument values,
                        Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ByteEndIx), ref intoAsIntsAllocatedEnd), "About to read/write past the end of intoAsInts");
                        ref var argCountAndCmdRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ArgumentCountIx));
                        ref var byteStartAndEndRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ByteStartIx));

                        // update command and arg count if not in error state, otherwise leave them unchanged
                        var packedArgCountAndCmd = (long)((uint)arrayLength | (((ulong)(uint)cmd) << 32));
                        argCountAndCmdRef = (argCountAndCmdRef & (long)(int)inErrorState) | (packedArgCountAndCmd & ~(long)(int)inErrorState);

                        // we need to zero these out if we're in error state, but NOT out of data
                        // this will produce a Malformed entry
                        var writeMalformed = inErrorState & ~ranOutOfData;
                        byteStartAndEndRef &= ~(int)writeMalformed;

                        // update the pointer for storage

                        // if we ENTERED the error state, we wrote a Malformed out
                        // so we need to advance writeNextResultInto by 4
                        // if we ran out of data, we roll writeNextResultInto all the way back
                        // 
                        // so the logic is writeNextResultInto - inErrorState
                        var rollbackNextResultIntoBy = (int)(inErrorState & ~oldErrorState) & (rollbackWriteIntoRefCount - 4);
                        rollbackNextResultIntoBy = ((int)ranOutOfData & rollbackWriteIntoRefCount) | ((int)~ranOutOfData & rollbackNextResultIntoBy);
                        currentIntoRef = ref Unsafe.Subtract(ref currentIntoRef, rollbackNextResultIntoBy);

                        // roll current pointer back if we are in an error state
                        currentCommandRef = ref Unsafe.Subtract(ref currentCommandRef, (int)(inErrorState & (uint)rollbackCommandRefCount));
                    }
                    //currentCommandRef = ref newCmd.Unwrap();
                    //currentIntoRef = ref newInto.Unwrap();
                } while (inErrorState == 0);

                intoCommandsSlotsUsed = (int)(Unsafe.ByteOffset(ref intoAsInts, ref currentIntoRef) / (sizeof(int) * 4));
                bytesConsumed = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
            }
        }

        /// <summary>
        /// Determine size of buffers and bitmaps to allocate based on a minimum desired size of a network receive buffer.
        /// 
        /// Note that allocation sizes and useable sizes are not the same, as we need the ability
        /// to overread in certain cases.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CalculateByteBufferSizes(int minSize, out int allocateBufferSize, out int useableBufferSize, out int bitmapSize)
        {
            // actual receive buffer needs to be walkable in an exact number of Vector512 chunks
            var numVectors = minSize / Vector512<byte>.Count + ((minSize % Vector512<byte>.Count) != 0 ? 1 : 0);

            useableBufferSize = numVectors * Vector512<byte>.Count;

            // we over allocate, since we can read extra bytes during number parsing (16 bytes) and command parsing (32 bytes), just take an extra 33
            allocateBufferSize = useableBufferSize + Vector256<byte>.Count + 1;

            // we also over allocate here, we'll read 4 bytes past the end, so just take an extra 5
            bitmapSize = useableBufferSize / 8 + sizeof(uint) + 1;

            // why > than the actual limit?  So the Debug.Asserts are a little shorter when we use Unsafe.IsAddressLessThan
        }

        /// <summary>
        /// Parse all the commands in <paramref name="commandBuffer"/> storing the result in <paramref name="into"/> and setting <paramref name="intoCommandsSlotsUsed"/> to the number
        /// of slots used.
        /// 
        /// If an error occurs, the last entry indicated by <paramref name="intoCommandsSlotsUsed"/> will have a malformed <see cref="ParsedRespCommandOrArgument"/> in it.
        /// The number of bytes for fully and successfully parsed commands is stored in <paramref name="bytesConsumed"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void TakeMultipleCommands(
            int commandBufferAllocatedSize,
            ref byte commandBuffer,
            int commandBufferFilledBytes,
            int digitsAllocatedSize,
            ref byte digitsBitmap,
            int intoSize,
            ref ParsedRespCommandOrArgument into,
            out int intoCommandsSlotsUsed,
            out int bytesConsumed
        )
        {
            Debug.Assert(commandBufferAllocatedSize > 0, "Must have some data to parse");
            Debug.Assert(intoSize > 0, "Into cannot be empty, and must have enough space for at least one full entry");

            // parsing RESP with (almost) no branches
            //
            // idea is we can be in a couple states
            //  1. everything is fine
            //  2. we've run out of data
            //     * this can happen if either commandBuffer is exhausted, or into is exhausted
            //  3. we've encountered an error
            //
            // running out of data implies being in an error state, BUT
            // we shouldn't write out any errors

            // inErrorState == TRUE for 2 or 3
            var inErrorState = FALSE;

            // ranOutOfData == TRUE only for 2
            var ranOutOfData = FALSE;

            // directly manipulating these as ints is easier
            ref var intoAsInts = ref Unsafe.As<ParsedRespCommandOrArgument, int>(ref into);
            ref var intoAsIntsAllocatedEnd = ref Unsafe.Add(ref intoAsInts, intoSize * 4);

            // advances as each element is taken
            scoped ref var currentCommandRef = ref commandBuffer;

            // advances by 4 for each ParsedRespCommandOrArgument written
            scoped ref var currentIntoRef = ref intoAsInts;

            ref var commandBufferAllocatedEnd = ref Unsafe.Add(ref commandBuffer, commandBufferAllocatedSize);
            ref var digitsBitmapAllocatedEnd = ref Unsafe.Add(ref digitsBitmap, digitsAllocatedSize);

            // always take at least one
            do
            {
                //var (newCmd, newInto) = UnrollReadCommand_ManualInline(ref commandBufferAllocatedEnd, ref commandBuffer, commandBufferUsedBytes, ref digitBitmapAllocatedEnd, ref digitsBitmap, ref intoAsIntsAllocatedEnd, ref currentCommandRef, ref currentIntoRef, ref inErrorState, ref ranOutOfData);
                {
                    const int MinimumCommandSize = 1 + 1 + 2;   // *0\r\n
                    const uint CommonArrayLengthSuffix = 0x240A_0D31;  // $\n\r1
                    const uint CommonCommandLengthPrefix = 0x0A0D_3024; // \n\r0$

                    Debug.Assert(commandBufferFilledBytes > 0, "Command buffer should have data in it");

                    // for this, we attempt to read the a command, BUT if we're already in an error state
                    // we throw the result away with some ANDS
                    //
                    // if we encounter an error, we enter the error state and we write out a malformed
                    // if we run out of data, we enter an error state that but do not write out a malformed
                    //
                    // if we're in an error state by the end, we rollback currentCommandBufferIx and writeNextResultInto
                    // to their initial states

                    var rollbackCommandRefCount = 0;
                    var rollbackWriteIntoRefCount = 0;

                    var oldErrorState = inErrorState;

                    // first check that there's sufficient space for a command
                    var availableBytes = commandBufferFilledBytes - (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
                    ranOutOfData |= (uint)((availableBytes - MinimumCommandSize) >> 31);    // availableBytes < MinimumCommandSize
                    inErrorState |= ranOutOfData;

                    // check that we can even write a malformed, if needed
                    ranOutOfData |= (uint)((~(-(int)Unsafe.ByteOffset(ref currentIntoRef, ref intoAsIntsAllocatedEnd))) >> 31);  // currentIntoRef == intoAsIntsAllocatedEnd
                    inErrorState |= ranOutOfData;

                    // check for *
                    Debug.Assert(Unsafe.IsAddressLessThan(ref currentCommandRef, ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
                    inErrorState |= (uint)((0 - (currentCommandRef ^ ArrayStart)) >> 31);   // currentCommandRef != ArrayStart

                    // advance past *
                    currentCommandRef = ref Unsafe.Add(ref currentCommandRef, 1);
                    rollbackCommandRefCount++;

                    // will be [0-9]\r\n$ MOST of the time, so eat a branch here
                    Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref currentCommandRef, sizeof(uint)), ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
                    var arrayLengthProbe = Unsafe.As<byte, uint>(ref currentCommandRef);
                    arrayLengthProbe -= CommonArrayLengthSuffix;

                    int arrayLength;
                    if (arrayLengthProbe < 8)
                    {
                        // array length of 0 is illegal, so we -1 in the probe
                        // so that we don't need to check length in the common case
                        //
                        // but that means we need to +1 to get the real length later
                        arrayLength = (int)(arrayLengthProbe + 1);

                        // skip 1 digit, \r, and \n
                        currentCommandRef = ref Unsafe.Add(ref currentCommandRef, 3);
                        rollbackCommandRefCount += 3;
                    }
                    else
                    {
                        // get the number of parts in the array

                        //currentCommandRef = ref TakePositiveNumberBranchless_ManualInline(ref commandBufferAllocatedEnd, ref commandBuffer, commandBufferFilledBytes, ref digitsBitmapAllocatedEnd, ref digitsBitmap, ref currentCommandRef, ref rollbackCommandRefCount, ref inErrorState, ref ranOutOfData, out arrayLength);
                        {
                            Debug.Assert(commandBufferFilledBytes > 0, "Cannot call with empty buffer");

                            var commandBufferIx = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
                            var startByteIndex = commandBufferIx / 8;
                            var startBitIndex = commandBufferIx % 8;

                            // we expect a run of digits followed by a \r\n, so digitBitmap should be 0b0...xxx1
                            // a run of 11+ is invalid, considering a shift of up to 8 this will always fit in a uint32
                            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsBitmap, startByteIndex + sizeof(uint)), ref digitsBitmapAllocatedEnd), "About to read past end of digits bitmap");

                            var digitsBitmapValue = Unsafe.As<byte, uint>(ref Unsafe.Add(ref digitsBitmap, startByteIndex)) >> startBitIndex;
                            var digitCount = (uint)BitOperations.TrailingZeroCount(~digitsBitmapValue);

                            // we expect no more than 10 digits (int.MaxValue is 2,147,483,647), and at least 1 digit
                            inErrorState |= ((10 - digitCount) >> 31);  // digitCount >= 11

                            // check for \r\n
                            ref var expectedCrLfRef = ref Unsafe.Add(ref currentCommandRef, digitCount);
                            var expectedCrPosition = (int)Unsafe.ByteOffset(ref commandBuffer, ref expectedCrLfRef);
                            ranOutOfData |= (uint)((commandBufferFilledBytes - (expectedCrPosition + 2)) >> 31);    // expectedCrPosition + 2 > commandBufferFilledBytes
                            inErrorState |= ranOutOfData;

                            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref expectedCrLfRef, sizeof(ushort)), ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
                            inErrorState |= (uint)((0 - (Unsafe.As<byte, ushort>(ref expectedCrLfRef) ^ CRLF)) >> 31);    // expectedCrLfRef != CRLF;

                            // if we're in an error state, we'll take an empty buffer
                            // but we'll get a good one if we're not
                            ref var digitsStartRef = ref currentCommandRef;
                            digitsStartRef = ref Unsafe.Subtract(ref digitsStartRef, (int)(inErrorState & rollbackCommandRefCount));
                            ref var digitsEndRef = ref expectedCrLfRef;
                            digitsEndRef = ref Unsafe.Subtract(ref digitsEndRef, (int)(inErrorState & (rollbackCommandRefCount + digitCount)));

                            // actually do the parsing
                            // parsed = UnconditionalParsePositiveInt(ref commandBufferAllocatedEnd, ref digitsStartRef, ref digitsEndRef);
                            {
                                Debug.Assert(Unsafe.IsAddressGreaterThan(ref digitsEndRef, ref digitsStartRef) || Unsafe.AreSame(ref digitsStartRef, ref digitsEndRef), "Incoherent digit end and start");
                                Debug.Assert(Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef) is >= 0 and < 11, "Should have validated correct size");
                                Debug.Assert(!MemoryMarshal.CreateSpan(ref digitsStartRef, (int)Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef)).ContainsAnyExceptInRange((byte)'0', (byte)'9'), "Should have validated digits only");

                                var length = (int)Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef);

                                var multLookupIx = length * 12;
                                ref var multStart = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(UnconditionalParsePositiveIntMultiples), multLookupIx);

                                // many numbers are going to fit in 4 digits
                                // 0\r\n* = len = 1 = 0x**_\n_\r_00
                                // 01\r\n = len = 2 = 0x\n_\r_11_00
                                // 012\r  = len = 3 = 0x\r_22_11_00
                                // 0123   = len = 4 = 0x33_22_11_00
                                if (length <= 4)
                                {
                                    // load all the digits (and some extra junk, maybe)
                                    Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsStartRef, sizeof(uint)), ref commandBufferAllocatedEnd), "About to read past end of command buffer");
                                    var fastPathDigits = Unsafe.As<byte, uint>(ref digitsStartRef);

                                    // reverse so we can pad more easily
                                    // 0\r\n* = len = 1 = 0x**_\n_\r_00 => 0x00_\r_\n_**
                                    // 01\r\n = len = 2 = 0x\n_\r_11_00 => 0x00_11_\r_\n
                                    // 012\r  = len = 3 = 0x\r_22_11_00 => 0x00_11_22_\r
                                    // 0123   = len = 4 = 0x33_22_11_00 => 0x00_11_22_33
                                    fastPathDigits = BinaryPrimitives.ReverseEndianness(fastPathDigits); // this is an intrinsic, so should be a single op

                                    // shift right to pad with zeros
                                    // 0\r\n* = len = 1 = 0x**_\n_\r_00 => 0x00_\r_\n_** => 0x00_00_00_00 ; shift right 24 (32 - (8 * len))
                                    // 01\r\n = len = 2 = 0x\n_\r_11_00 => 0x00_11_\r_\n => 0x00_00_00_11 ; shift right 16 (32 - (8 * len))
                                    // 012\r  = len = 3 = 0x\r_22_11_00 => 0x00_11_22_\r => 0x00_00_11_22 ; shift right  8 (32 - (8 * len))
                                    // 0123   = len = 4 = 0x33_22_11_00 => 0x00_11_22_33 => 0x00_11_22_33 ; shift right  0 (32 - (8 * len))
                                    fastPathDigits = fastPathDigits >> (32 - (8 * length));

                                    // fastPathDigits = 0x.4_.3__.2_.1
                                    // onesAndHundreds = 0x00_03__00_01
                                    var onesAndHundreds = fastPathDigits & ~0xFF_30_FF_30U;
                                    // tensAndThousands = 0x04_00__02_00
                                    var tensAndThousands = fastPathDigits & ~0x30_FF_30_FFU;

                                    // mixedOnce = 0x(4 * 10 + 3)__(2 * 10 + 1) = 0d043__21
                                    var mixedOnce = (tensAndThousands * 10U) + (onesAndHundreds << 8);

                                    // topPair = 0d43__00
                                    ulong topPair = mixedOnce & 0xFF_FF_00_00U;
                                    // lowPair = 0d00__21
                                    ulong lowPair = mixedOnce & 0x00_00_FF_FFU;

                                    // mixedTwice = 0d(43 * 100 + 21)_00
                                    var mixedTwice = (topPair * 100U) + (lowPair << 16);

                                    var result = (int)(mixedTwice >> 24);

                                    // leading zero check, force result to -1 if below expected value
                                    result |= ((0 - (length ^ 1)) >> 31) & ((result - (int)multStart) >> 31);   // length != 1 & multStart > result;

                                    arrayLength = result;
                                }
                                else
                                {
                                    var maskLookupIx = length * 16;

                                    ref var maskStart = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(UnconditionalParsePositiveIntLookup), maskLookupIx);

                                    var mask = Vector128.LoadUnsafe(ref maskStart);

                                    // load 16 bytes of data, which will have \r\n and some trailing garbage
                                    Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsStartRef, Vector128<byte>.Count), ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
                                    var data = Vector128.LoadUnsafe(ref digitsStartRef);

                                    var toUseBytes = Vector128.BitwiseAnd(data, mask);                        // bytes 0-9 are (potentially) valid

                                    // expand so we can multiply cleanly
                                    var (d0To7, d8To15) = Vector128.Widen(toUseBytes);
                                    var (d0To3, d4To7) = Vector128.Widen(d0To7);
                                    var (d8To11, _) = Vector128.Widen(d8To15);

                                    // load per digit multiples
                                    var scale0To3 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 0));
                                    var scale4To7 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 4));
                                    var scale8To11 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 8));

                                    // scale each digit by it's place value
                                    // 
                                    // at maximum length, m0To3[0] might overflow
                                    var m0To3 = Vector128.Multiply(d0To3, scale0To3);
                                    var m4To7 = Vector128.Multiply(d4To7, scale4To7);
                                    var m8To11 = Vector128.Multiply(d8To11, scale8To11);

                                    var maybeMultOverflow = Vector128.LessThan(m0To3, scale0To3);

                                    // calculate the vertical sum
                                    // 
                                    // vertical sum cannot overflow (assuming m0To3 didn't overflow)
                                    var verticalSum = Vector128.Add(Vector128.Add(m0To3, m4To7), m8To11);

                                    // calculate the horizontal sum
                                    //
                                    // horizontal sum can overflow, even if m0To3 didn't overflow
                                    var horizontalSum = Vector128.Dot(verticalSum, Vector128<uint>.One);

                                    var maybeHorizontalOverflow = Vector128.GreaterThan(verticalSum, Vector128.CreateScalar(horizontalSum));

                                    var maybeOverflow = Vector128.BitwiseOr(maybeMultOverflow, maybeHorizontalOverflow);

                                    var res = horizontalSum;

                                    // check for overflow
                                    //
                                    // note that overflow can only happen when length == 10
                                    // only the first digit can overflow on it's own (if it's > 2)
                                    // everything else has to overflow when summed
                                    // 
                                    // we know length is <= 10, we can exploit this in the == check
                                    var allOnesIfOverflow = (uint)(((9 - length) >> 31) & ((0L - maybeOverflow.GetElement(0)) >> 63));  // maybeOverflow.GetElement(0) != 0 & length == 10

                                    // check for leading zeros
                                    var allOnesIfLeadingZeros = (uint)(((0 - (length ^ 1)) >> 31) & (((int)res - (int)multStart) >> 31)); // length != 1 & multStart > (int)res

                                    // turn into -1 if things are invalid
                                    res |= allOnesIfOverflow | allOnesIfLeadingZeros;

                                    arrayLength = (int)res;
                                }
                            }
                            inErrorState |= (uint)(arrayLength >> 31);    // value < 0

                            // update to point one past the \r\n if we're not in an error state
                            // otherwise leave it unmodified
                            currentCommandRef = ref Unsafe.Add(ref currentCommandRef, (int)((digitCount + 2) & ~inErrorState));
                            rollbackCommandRefCount += (int)((digitCount + 2) & ~inErrorState);
                        }

                        inErrorState |= (uint)((arrayLength - 1) >> 31);    // arrayLength < 1
                    }

                    var remainingArrayItems = arrayLength;

                    // first string will be a command, so we can do a smarter length check MOST of the time
                    // worth it to eat a branch then
                    Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref currentCommandRef, sizeof(uint)), ref commandBufferAllocatedEnd), "About to read past end of command buffer");
                    var cmdLengthProbe = Unsafe.As<byte, uint>(ref currentCommandRef);
                    cmdLengthProbe -= CommonCommandLengthPrefix;
                    cmdLengthProbe = BitOperations.RotateRight(cmdLengthProbe, 8);
                    if (cmdLengthProbe < 10)
                    {
                        // handle the common case where command length is < 10

                        ranOutOfData |= (uint)~(((int)-Unsafe.ByteOffset(ref currentIntoRef, ref intoAsIntsAllocatedEnd)) >> 31);                // currentIntoRef == intoAsIntsAllocatedEnd

                        var byteAfterCurrentCommandRef = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
                        ranOutOfData |= (uint)(((commandBufferFilledBytes - byteAfterCurrentCommandRef) - 4) >> 31);    // (commandBufferFilledBytes - byteAfterCurrentCommandRef) < 4  (considering $\d\r\n)
                        inErrorState |= ranOutOfData;

                        // if we're out of data, we also need an extra bit of rollback to prevent 
                        var outOfDataRollback = (int)ranOutOfData & 4;

                        // get a ref which is rolled back to the original value IF we're in an errorState
                        ref var commandIntoRef = ref Unsafe.Subtract(ref currentIntoRef, (rollbackWriteIntoRefCount & (int)inErrorState) + outOfDataRollback);
                        var commonCommandStart = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef) + 4;
                        var commonCommandEnd = (int)(commonCommandStart + cmdLengthProbe + 2);

                        ranOutOfData |= (uint)((commandBufferFilledBytes - commonCommandEnd) >> 31);    // commonCommandEnd > commandBufferFilledBytes
                        inErrorState |= ranOutOfData;

                        // check for \r\n
                        var trailingCrLfOffset = ~inErrorState & (4 + cmdLengthProbe);
                        ref var trailingCrLfRef = ref Unsafe.Add(ref currentCommandRef, trailingCrLfOffset);
                        Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref trailingCrLfRef, sizeof(ushort)), ref commandBufferAllocatedEnd), "About to read past end of command buffer");
                        var trailingCrLf = Unsafe.As<byte, ushort>(ref trailingCrLfRef);
                        inErrorState |= (uint)((0 - (trailingCrLf ^ CRLF)) >> 31);  // trailingCrLf != CRLF

                        Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref commandIntoRef, ParsedRespCommandOrArgument.ByteEndIx), ref intoAsIntsAllocatedEnd), "About to read/write past end of intoAsInts");
                        ref var commonCmdAndArgCountRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref commandIntoRef, ParsedRespCommandOrArgument.ArgumentCountIx));
                        ref var commonByteStartAndEndRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref commandIntoRef, ParsedRespCommandOrArgument.ByteStartIx));

                        // clear argCount & cmd if not in error state
                        commonCmdAndArgCountRef &= (long)(int)inErrorState;

                        // leave unmodified if in error state, or set to commonCommandStart & commonCommandEnd
                        var packedStartAndEnd = (long)(((ulong)(uint)commonCommandStart) | (((ulong)(uint)commonCommandEnd) << 32));
                        commonByteStartAndEndRef = (commonByteStartAndEndRef & (long)(int)inErrorState) | (packedStartAndEnd & ~(long)(int)inErrorState);

                        // advance writeNextResultInto if not in error state
                        rollbackWriteIntoRefCount += (int)(4 & ~inErrorState);
                        currentIntoRef = ref Unsafe.Add(ref currentIntoRef, (int)(4 & ~inErrorState));
                        remainingArrayItems--;

                        // update currentCommandBufferIx if not in error state
                        currentCommandRef = ref Unsafe.Add(ref currentCommandRef, (int)(~inErrorState & (4 + cmdLengthProbe + 2)));
                        rollbackCommandRefCount += (int)(~inErrorState & (4 + cmdLengthProbe + 2));
                    }

                    // handle remaining strings (which may not include the command string)
                    do
                    {
                        //var (updateCommandRef, updateWriteNextRef) = UnrollTakeCommandBulkString(ref commandBufferAllocatedEnd, ref commandBuffer, commandBufferFilledBytes, ref digitsBitmapAllocatedEnd, ref digitsBitmap, ref intoAsIntsAllocatedEnd, ref currentCommandRef, ref currentIntoRef, ref remainingArrayItems, ref rollbackCommandRefCount, ref rollbackWriteIntoRefCount, ref inErrorState, ref ranOutOfData);
                        //var (updateCommandRef, updateWriteNextRef) = UnrollTakeCommandBulkString_ManualInline(ref commandBufferAllocatedEnd, ref commandBuffer, commandBufferFilledBytes, ref digitsBitmapAllocatedEnd, ref digitsBitmap, ref intoAsIntsAllocatedEnd, ref currentCommandRef, ref currentIntoRef, ref remainingArrayItems, ref rollbackCommandRefCount, ref rollbackWriteIntoRefCount, ref inErrorState, ref ranOutOfData);
                        {
                            const int MinimumBulkStringSize = 1 + 1 + 2 + 2; // $0\r\n\r\n

                            Debug.Assert(Unsafe.ByteOffset(ref commandBuffer, ref commandBufferAllocatedEnd) > 0, "Command buffer cannot be of 0 size");

                            // if we've read everything, we need to just do nothing (including updating inErrorState and ranOutOfData)
                            // but still run through everything unconditionally
                            var readAllItems = (uint)~((0 - remainingArrayItems) >> 31);  // remainingItemsInArray == 0
                            var oldInErrorState = inErrorState;
                            var oldRanOutOfData = ranOutOfData;

                            // we need to prevent action now, so act like we're in an error state
                            inErrorState |= readAllItems;

                            // check that there's enough data to even fit a string
                            var availableBytesSub = commandBufferFilledBytes - (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
                            ranOutOfData |= (uint)((availableBytesSub - MinimumBulkStringSize) >> 31); // availableBytes < MinimumBulkStringSize
                            inErrorState |= ranOutOfData;

                            // check for $
                            ref var bulkStringStartExpectedRef = ref currentCommandRef;

                            Debug.Assert(Unsafe.IsAddressLessThan(ref bulkStringStartExpectedRef, ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
                            inErrorState |= (uint)((-(bulkStringStartExpectedRef ^ BulkStringStart)) >> 31); // (ref commandBuffer + bulkStringStartExpectedIx) != BulkStringStart

                            // move past $
                            currentCommandRef = ref Unsafe.Add(ref currentCommandRef, 1);
                            rollbackCommandRefCount++;

                            // parse the length
                            //currentCommandRef = ref TakePositiveNumberBranchless_ManualInline(ref commandBufferAllocatedEnd, ref commandBuffer, commandBufferFilledBytes, ref digitsBitmapAllocatedEnd, ref digitsBitmap, ref currentCommandRef, ref rollbackCommandRefCount, ref inErrorState, ref ranOutOfData, out var bulkStringLength);
                            int bulkStringLength;
                            {
                                Debug.Assert(commandBufferFilledBytes > 0, "Cannot call with empty buffer");

                                var commandBufferIx = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
                                var startByteIndex = commandBufferIx / 8;
                                var startBitIndex = commandBufferIx % 8;

                                // we expect a run of digits followed by a \r\n, so digitBitmap should be 0b0...xxx1
                                // a run of 11+ is invalid, considering a shift of up to 8 this will always fit in a uint32
                                Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsBitmap, startByteIndex + sizeof(uint)), ref digitsBitmapAllocatedEnd), "About to read past end of digits bitmap");

                                var digitsBitmapValue = Unsafe.As<byte, uint>(ref Unsafe.Add(ref digitsBitmap, startByteIndex)) >> startBitIndex;
                                var digitCount = (uint)BitOperations.TrailingZeroCount(~digitsBitmapValue);

                                // we expect no more than 10 digits (int.MaxValue is 2,147,483,647), and at least 1 digit
                                inErrorState |= ((10 - digitCount) >> 31);  // digitCount >= 11

                                // check for \r\n
                                ref var expectedCrLfRef = ref Unsafe.Add(ref currentCommandRef, digitCount);
                                var expectedCrPosition_inline = (int)Unsafe.ByteOffset(ref commandBuffer, ref expectedCrLfRef);
                                ranOutOfData |= (uint)((commandBufferFilledBytes - (expectedCrPosition_inline + 2)) >> 31);    // expectedCrPosition + 2 > commandBufferFilledBytes
                                inErrorState |= ranOutOfData;

                                Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref expectedCrLfRef, sizeof(ushort)), ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
                                inErrorState |= (uint)((0 - (Unsafe.As<byte, ushort>(ref expectedCrLfRef) ^ CRLF)) >> 31);    // expectedCrLfRef != CRLF;

                                // if we're in an error state, we'll take an empty buffer
                                // but we'll get a good one if we're not
                                ref var digitsStartRef = ref currentCommandRef;
                                digitsStartRef = ref Unsafe.Subtract(ref digitsStartRef, (int)(inErrorState & rollbackCommandRefCount));
                                ref var digitsEndRef = ref expectedCrLfRef;
                                digitsEndRef = ref Unsafe.Subtract(ref digitsEndRef, (int)(inErrorState & (rollbackCommandRefCount + digitCount)));

                                // actually do the parsing
                                // parsed = UnconditionalParsePositiveInt(ref commandBufferAllocatedEnd, ref digitsStartRef, ref digitsEndRef);
                                {
                                    Debug.Assert(Unsafe.IsAddressGreaterThan(ref digitsEndRef, ref digitsStartRef) || Unsafe.AreSame(ref digitsStartRef, ref digitsEndRef), "Incoherent digit end and start");
                                    Debug.Assert(Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef) is >= 0 and < 11, "Should have validated correct size");
                                    Debug.Assert(!MemoryMarshal.CreateSpan(ref digitsStartRef, (int)Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef)).ContainsAnyExceptInRange((byte)'0', (byte)'9'), "Should have validated digits only");

                                    var length = (int)Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef);

                                    var multLookupIx = length * 12;
                                    ref var multStart = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(UnconditionalParsePositiveIntMultiples), multLookupIx);

                                    // many numbers are going to fit in 4 digits
                                    // 0\r\n* = len = 1 = 0x**_\n_\r_00
                                    // 01\r\n = len = 2 = 0x\n_\r_11_00
                                    // 012\r  = len = 3 = 0x\r_22_11_00
                                    // 0123   = len = 4 = 0x33_22_11_00
                                    if (length <= 4)
                                    {
                                        // load all the digits (and some extra junk, maybe)
                                        Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsStartRef, sizeof(uint)), ref commandBufferAllocatedEnd), "About to read past end of command buffer");
                                        var fastPathDigits = Unsafe.As<byte, uint>(ref digitsStartRef);

                                        // reverse so we can pad more easily
                                        // 0\r\n* = len = 1 = 0x**_\n_\r_00 => 0x00_\r_\n_**
                                        // 01\r\n = len = 2 = 0x\n_\r_11_00 => 0x00_11_\r_\n
                                        // 012\r  = len = 3 = 0x\r_22_11_00 => 0x00_11_22_\r
                                        // 0123   = len = 4 = 0x33_22_11_00 => 0x00_11_22_33
                                        fastPathDigits = BinaryPrimitives.ReverseEndianness(fastPathDigits); // this is an intrinsic, so should be a single op

                                        // shift right to pad with zeros
                                        // 0\r\n* = len = 1 = 0x**_\n_\r_00 => 0x00_\r_\n_** => 0x00_00_00_00 ; shift right 24 (32 - (8 * len))
                                        // 01\r\n = len = 2 = 0x\n_\r_11_00 => 0x00_11_\r_\n => 0x00_00_00_11 ; shift right 16 (32 - (8 * len))
                                        // 012\r  = len = 3 = 0x\r_22_11_00 => 0x00_11_22_\r => 0x00_00_11_22 ; shift right  8 (32 - (8 * len))
                                        // 0123   = len = 4 = 0x33_22_11_00 => 0x00_11_22_33 => 0x00_11_22_33 ; shift right  0 (32 - (8 * len))
                                        fastPathDigits = fastPathDigits >> (32 - (8 * length));

                                        // fastPathDigits = 0x.4_.3__.2_.1
                                        // onesAndHundreds = 0x00_03__00_01
                                        var onesAndHundreds = fastPathDigits & ~0xFF_30_FF_30U;
                                        // tensAndThousands = 0x04_00__02_00
                                        var tensAndThousands = fastPathDigits & ~0x30_FF_30_FFU;

                                        // mixedOnce = 0x(4 * 10 + 3)__(2 * 10 + 1) = 0d043__21
                                        var mixedOnce = (tensAndThousands * 10U) + (onesAndHundreds << 8);

                                        // topPair = 0d43__00
                                        ulong topPair = mixedOnce & 0xFF_FF_00_00U;
                                        // lowPair = 0d00__21
                                        ulong lowPair = mixedOnce & 0x00_00_FF_FFU;

                                        // mixedTwice = 0d(43 * 100 + 21)_00
                                        var mixedTwice = (topPair * 100U) + (lowPair << 16);

                                        var result = (int)(mixedTwice >> 24);

                                        // leading zero check, force result to -1 if below expected value
                                        result |= ((0 - (length ^ 1)) >> 31) & ((result - (int)multStart) >> 31);   // length != 1 & multStart > result;

                                        bulkStringLength = result;
                                    }
                                    else
                                    {

                                        var maskLookupIx = length * 16;

                                        ref var maskStart = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(UnconditionalParsePositiveIntLookup), maskLookupIx);

                                        var mask = Vector128.LoadUnsafe(ref maskStart);

                                        // load 16 bytes of data, which will have \r\n and some trailing garbage
                                        Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsStartRef, Vector128<byte>.Count), ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
                                        var data = Vector128.LoadUnsafe(ref digitsStartRef);

                                        var toUseBytes = Vector128.BitwiseAnd(data, mask);                        // bytes 0-9 are (potentially) valid

                                        // expand so we can multiply cleanly
                                        var (d0To7, d8To15) = Vector128.Widen(toUseBytes);
                                        var (d0To3, d4To7) = Vector128.Widen(d0To7);
                                        var (d8To11, _) = Vector128.Widen(d8To15);

                                        // load per digit multiples
                                        var scale0To3 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 0));
                                        var scale4To7 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 4));
                                        var scale8To11 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 8));

                                        // scale each digit by it's place value
                                        // 
                                        // at maximum length, m0To3[0] might overflow
                                        var m0To3 = Vector128.Multiply(d0To3, scale0To3);
                                        var m4To7 = Vector128.Multiply(d4To7, scale4To7);
                                        var m8To11 = Vector128.Multiply(d8To11, scale8To11);

                                        var maybeMultOverflow = Vector128.LessThan(m0To3, scale0To3);

                                        // calculate the vertical sum
                                        // 
                                        // vertical sum cannot overflow (assuming m0To3 didn't overflow)
                                        var verticalSum = Vector128.Add(Vector128.Add(m0To3, m4To7), m8To11);

                                        // calculate the horizontal sum
                                        //
                                        // horizontal sum can overflow, even if m0To3 didn't overflow
                                        var horizontalSum = Vector128.Dot(verticalSum, Vector128<uint>.One);

                                        var maybeHorizontalOverflow = Vector128.GreaterThan(verticalSum, Vector128.CreateScalar(horizontalSum));

                                        var maybeOverflow = Vector128.BitwiseOr(maybeMultOverflow, maybeHorizontalOverflow);

                                        var res = horizontalSum;

                                        // check for overflow
                                        //
                                        // note that overflow can only happen when length == 10
                                        // only the first digit can overflow on it's own (if it's > 2)
                                        // everything else has to overflow when summed
                                        // 
                                        // we know length is <= 10, we can exploit this in the == check
                                        var allOnesIfOverflow = (uint)(((9 - length) >> 31) & ((0L - maybeOverflow.GetElement(0)) >> 63));  // maybeOverflow.GetElement(0) != 0 & length == 10

                                        // check for leading zeros
                                        var allOnesIfLeadingZeros = (uint)(((0 - (length ^ 1)) >> 31) & (((int)res - (int)multStart) >> 31)); // length != 1 & multStart > (int)res

                                        // turn into -1 if things are invalid
                                        res |= allOnesIfOverflow | allOnesIfLeadingZeros;

                                        bulkStringLength = (int)res;
                                    }
                                }
                                inErrorState |= (uint)(bulkStringLength >> 31);    // value < 0

                                // update to point one past the \r\n if we're not in an error state
                                // otherwise leave it unmodified
                                currentCommandRef = ref Unsafe.Add(ref currentCommandRef, (int)((digitCount + 2) & ~inErrorState));
                                rollbackCommandRefCount += (int)((digitCount + 2) & ~inErrorState);
                            }

                            var bulkStringStart = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
                            var bulkStringEnd = bulkStringStart + bulkStringLength;

                            // check for \r\n
                            var expectedCrPosition = bulkStringEnd;
                            ranOutOfData |= (uint)((commandBufferFilledBytes - (expectedCrPosition + 2)) >> 31);    // expectedCrPosition + 2 > commandBufferFilledBytes
                            inErrorState |= ranOutOfData;

                            expectedCrPosition &= (int)~inErrorState;
                            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref commandBuffer, expectedCrPosition + sizeof(ushort)), ref commandBufferAllocatedEnd), "About to read past allocated command buffer");
                            inErrorState |= (uint)((0 - (Unsafe.As<byte, ushort>(ref Unsafe.Add(ref commandBuffer, expectedCrPosition)) ^ CRLF)) >> 31);    // (ref commandBuffer + expectedCrPosition) != CRLF

                            // move past the \r\n
                            currentCommandRef = ref Unsafe.Add(ref currentCommandRef, bulkStringLength + 2);
                            rollbackCommandRefCount += bulkStringLength + 2;

                            // now we're ready to write out results

                            // do we even have space to write something?
                            ranOutOfData |= (uint)~(((int)-Unsafe.ByteOffset(ref currentIntoRef, ref intoAsIntsAllocatedEnd)) >> 31);    // writeNextResultInto == intoAsIntsSize
                            inErrorState |= ranOutOfData;

                            // write the results out
                            //var effectiveInto = (int)(~inErrorState & writeNextResultInto);
                            ref var effectiveInto = ref Unsafe.Subtract(ref currentIntoRef, inErrorState & 4);
                            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref effectiveInto, ParsedRespCommandOrArgument.ByteEndIx), ref intoAsIntsAllocatedEnd), "About to read/write past end of intoAsInts");
                            //ref var intoAsIntsEffectiveIntoRef = ref Unsafe.Add(ref intoAsInts, effectiveInto);
                            ref var argCountAndCommandRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref effectiveInto, ParsedRespCommandOrArgument.ArgumentCountIx));
                            ref var byteStartAndEndRefSub = ref Unsafe.As<int, long>(ref Unsafe.Add(ref effectiveInto, ParsedRespCommandOrArgument.ByteStartIx));

                            // clear argCount and command UNLESS we're in an error state
                            argCountAndCommandRef &= (long)(int)inErrorState;

                            // set byteStart and byteEnd if we're not in an error state, otherwise leave them unmodified
                            var packedByteStartAndEnd = (long)(((ulong)(uint)bulkStringStart) | (((ulong)(uint)(expectedCrPosition + 2)) << 32));
                            byteStartAndEndRefSub = (byteStartAndEndRefSub & (long)(int)inErrorState) | (packedByteStartAndEnd & ~(long)(int)inErrorState);

                            // now update state

                            // advance write target if we succeeded
                            currentIntoRef = ref Unsafe.Add(ref currentIntoRef, (int)(~inErrorState & 4));
                            rollbackWriteIntoRefCount += (int)(~inErrorState & 4);

                            // note that we rollback currentCommandBufferIx if there's any error
                            //
                            // if we've read past the end of the command, we will _for sure_ be in error state
                            //currentCommandBufferIx = (int)((inErrorState & oldCurrentCommandBufferIx) | (~inErrorState & currentCommandBufferIx));
                            currentCommandRef = ref Unsafe.Subtract(ref currentCommandRef, (int)(inErrorState & (uint)(bulkStringLength + 2 + 1)));
                            rollbackCommandRefCount -= (int)(inErrorState & (uint)(bulkStringLength + 2 + 1));

                            // update the number of items read from the array, if appropriate
                            var shouldUpdateRemainingItemCount = ~readAllItems & ~inErrorState;
                            remainingArrayItems -= (int)(shouldUpdateRemainingItemCount & 1);

                            // but we rollback errors and out of data if we fully consumed the array
                            inErrorState = ((readAllItems & oldInErrorState) | (~readAllItems & inErrorState));
                            ranOutOfData = ((readAllItems & oldRanOutOfData) | (~readAllItems & ranOutOfData));

                            // pun away scoped, we know this is safe
                            //return new(ref Unsafe.AsRef(ref currentCommandRef), ref Unsafe.AsRef(ref currentIntoRef));
                        }

                        //currentCommandRef = ref updateCommandRef.Unwrap();
                        //currentIntoRef = ref updateWriteNextRef.Unwrap();
                    } while ((remainingArrayItems & ~inErrorState) != 0);

                    // here we need an adjustment to avoid read/writing past end of currentIntoRef
                    var effectiveIntoReverse = (int)(ranOutOfData & 4);

                    // we've now parsed everything EXCEPT the command enum
                    // and we haven't written the argument count either
                    //
                    // we might be in an error state, or we might be out of data
                    //
                    // if we're out of data, we just need to take nothing (rolling everything back)
                    ref var effectInto = ref Unsafe.Subtract(ref currentIntoRef, rollbackWriteIntoRefCount + effectiveIntoReverse);
                    Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ByteEndIx), ref intoAsIntsAllocatedEnd), "About to read/write past the end of intoAsInts");
                    var cmdStart = (int)(Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ByteStartIx) & ~inErrorState);
                    var cmdEnd = (int)(Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ByteEndIx) & ~inErrorState);

                    // place ourselves in an error state if we could not parse the command
                    //var cmd = UnconditionalParseRespCommandImpl.Parse(ref commandBufferAllocatedEnd, ref commandBuffer, cmdStart, (cmdEnd - cmdStart) - 2);
                    RespCommand cmd;
                    {
                        var commandLength = (cmdEnd - cmdStart) - 2;
                        Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref commandBuffer, cmdStart + Vector256<byte>.Count), ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
                        var commandVector = Vector256.LoadUnsafe(ref Unsafe.Add(ref commandBuffer, cmdStart));

                        var effectiveLength = commandLength & 31;
                        var maskIx = effectiveLength * 64;
                        var xorIx = maskIx + 32;

                        ref var lengthMaskAndHashXorsRef = ref MemoryMarshal.GetArrayDataReference(UnconditionalParseRespCommandImpl.LengthMaskAndHashXors);
                        Debug.Assert(xorIx + sizeof(uint) <= UnconditionalParseRespCommandImpl.LengthMaskAndHashXors.Length, "About to read past end of LengthMaskAndHashXors");
                        var maskVector = Vector256.LoadUnsafe(ref Unsafe.Add(ref lengthMaskAndHashXorsRef, maskIx));
                        var xorVector = Vector256.Create(Unsafe.As<byte, uint>(ref Unsafe.Add(ref lengthMaskAndHashXorsRef, xorIx)));

                        var truncatedCommandVector = Vector256.BitwiseAnd(commandVector, Vector256.GreaterThan(maskVector, Vector256<byte>.Zero));
                        var upperCommandVector = Vector256.BitwiseAnd(truncatedCommandVector, maskVector);

                        var invalid = Vector256.GreaterThanAny(truncatedCommandVector, Vector256.Create((byte)'z'));
                        var allZerosIfInvalid = Unsafe.As<bool, byte>(ref invalid) - 1;

                        var xored = Vector256.Xor(upperCommandVector, Vector256.As<uint, byte>(xorVector));
                        var mixed = Vector256.Dot(Vector256.As<byte, uint>(xored), Vector256<uint>.One);
                        var moded = mixed % 71;

                        var dataStartIx = 8192 * effectiveLength;
                        dataStartIx += (int)(moded * 64);

                        ref var commandAndExpectedValuesRef = ref MemoryMarshal.GetArrayDataReference(UnconditionalParseRespCommandImpl.CommandAndExpectedValues);
                        Debug.Assert(dataStartIx + sizeof(uint) + Vector256<byte>.Count <= UnconditionalParseRespCommandImpl.CommandAndExpectedValues.Length, "About to read past end of CommandAndExpectedValues");
                        var cmdSub = Unsafe.As<byte, int>(ref Unsafe.Add(ref commandAndExpectedValuesRef, dataStartIx));
                        var expectedCommandVector = Vector256.LoadUnsafe(ref Unsafe.Add(ref commandAndExpectedValuesRef, dataStartIx + 4));

                        var matches = Vector256.EqualsAll(upperCommandVector, expectedCommandVector);
                        var allOnesIfMatches = -Unsafe.As<bool, byte>(ref matches);

                        cmd = (RespCommand)(cmdSub & allOnesIfMatches & allZerosIfInvalid);
                    }
                    inErrorState |= (uint)~(-(int)cmd >> 31);   // cmd == 0

                    // we need to update the first entry, to either have command and argument values,
                    Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ByteEndIx), ref intoAsIntsAllocatedEnd), "About to read/write past the end of intoAsInts");
                    ref var argCountAndCmdRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ArgumentCountIx));
                    ref var byteStartAndEndRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ByteStartIx));

                    // update command and arg count if not in error state, otherwise leave them unchanged
                    var packedArgCountAndCmd = (long)((uint)arrayLength | (((ulong)(uint)cmd) << 32));
                    argCountAndCmdRef = (argCountAndCmdRef & (long)(int)inErrorState) | (packedArgCountAndCmd & ~(long)(int)inErrorState);

                    // we need to zero these out if we're in error state, but NOT out of data
                    // this will produce a Malformed entry
                    var writeMalformed = inErrorState & ~ranOutOfData;
                    byteStartAndEndRef &= ~(long)(int)writeMalformed;

                    // update the pointer for storage

                    // if we ENTERED the error state, we wrote a Malformed out
                    // so we need to advance writeNextResultInto by 4
                    // if we ran out of data, we roll writeNextResultInto all the way back
                    // 
                    // so the logic is writeNextResultInto - inErrorState
                    var rollbackNextResultIntoBy = (int)(inErrorState & ~oldErrorState) & (rollbackWriteIntoRefCount - 4);
                    rollbackNextResultIntoBy = ((int)ranOutOfData & rollbackWriteIntoRefCount) | ((int)~ranOutOfData & rollbackNextResultIntoBy);
                    currentIntoRef = ref Unsafe.Subtract(ref currentIntoRef, rollbackNextResultIntoBy);

                    // roll current pointer back if we are in an error state
                    currentCommandRef = ref Unsafe.Subtract(ref currentCommandRef, (int)(inErrorState & (uint)rollbackCommandRefCount));
                }
                //currentCommandRef = ref newCmd.Unwrap();
                //currentIntoRef = ref newInto.Unwrap();
            } while (inErrorState == 0);

            intoCommandsSlotsUsed = (int)(Unsafe.ByteOffset(ref intoAsInts, ref currentIntoRef) / (sizeof(int) * 4));
            bytesConsumed = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
        }

        /// <summary>
        /// Reads a whole command of the form *\d+\r\n($\d+\r\ndata\r\n)+ and writes the results into <paramref name="intoAsInts"/> as a set of quads.
        /// 
        /// Updates <paramref name="currentCommandBufferIx"/> and <paramref name="writeNextResultInto"/> on success.
        /// 
        /// If a parsing error occurs, sets <paramref name="inErrorState"/>.
        /// If more data could fix that error, sets <paramref name="ranOutOfData"/>.
        /// 
        /// If <paramref name="inErrorState"/> is set when this method returns, <paramref name="currentCommandBufferIx"/>  and <paramref name="writeNextResultInto"/>
        /// are unchanged.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UpdatedPairs UnrollReadCommand(
            ref byte commandBufferAllocatedEnd,
            ref byte commandBuffer,
            int commandBufferFilledBytes,
            ref byte digitsBitmapAllocatedEnd,
            ref byte digitsBitmap,
            ref int intoAsIntsAllocatedEnd,
            scoped ref byte currentCommandRef,
            scoped ref int currentIntoRef,
            ref uint inErrorState,
            ref uint ranOutOfData
        )
        {
            const int MinimumCommandSize = 1 + 1 + 2;   // *0\r\n
            const uint CommonArrayLengthSuffix = 0x240A_0D31;  // $\n\r1
            const uint CommonCommandLengthPrefix = 0x0A0D_3024; // \n\r0$

            Debug.Assert(commandBufferFilledBytes > 0, "Command buffer should have data in it");

            // for this, we attempt to read the a command, BUT if we're already in an error state
            // we throw the result away with some ANDS
            //
            // if we encounter an error, we enter the error state and we write out a malformed
            // if we run out of data, we enter an error state that but do not write out a malformed
            //
            // if we're in an error state by the end, we rollback currentCommandBufferIx and writeNextResultInto
            // to their initial states

            var rollbackCommandRefCount = 0;
            var rollbackWriteIntoRefCount = 0;

            var oldErrorState = inErrorState;

            // first check that there's sufficient space for a command
            var availableBytes = commandBufferFilledBytes - (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
            ranOutOfData |= (uint)((availableBytes - MinimumCommandSize) >> 31);    // availableBytes < MinimumCommandSize
            inErrorState |= ranOutOfData;

            // check that we can even write a malformed, if needed
            ranOutOfData |= (uint)((~(-(int)Unsafe.ByteOffset(ref currentIntoRef, ref intoAsIntsAllocatedEnd))) >> 31);  // currentIntoRef == intoAsIntsAllocatedEnd
            inErrorState |= ranOutOfData;

            // check for *
            Debug.Assert(Unsafe.IsAddressLessThan(ref currentCommandRef, ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
            inErrorState |= (uint)((0 - (currentCommandRef ^ ArrayStart)) >> 31);   // currentCommandRef != ArrayStart

            // advance past *
            currentCommandRef = ref Unsafe.Add(ref currentCommandRef, 1);
            rollbackCommandRefCount++;

            // will be [0-9]\r\n$ MOST of the time, so eat a branch here
            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref currentCommandRef, sizeof(uint)), ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
            var arrayLengthProbe = Unsafe.As<byte, uint>(ref currentCommandRef);
            arrayLengthProbe -= CommonArrayLengthSuffix;

            int arrayLength;
            if (arrayLengthProbe < 8)
            {
                // array length of 0 is illegal, so we -1 in the probe
                // so that we don't need to check length in the common case
                //
                // but that means we need to +1 to get the real length later
                arrayLength = (int)(arrayLengthProbe + 1);

                // skip 1 digit, \r, and \n
                currentCommandRef = ref Unsafe.Add(ref currentCommandRef, 3);
                rollbackCommandRefCount += 3;
            }
            else
            {
                // get the number of parts in the array

                currentCommandRef = ref TakePositiveNumberBranchless_ManualInline(ref commandBufferAllocatedEnd, ref commandBuffer, commandBufferFilledBytes, ref digitsBitmapAllocatedEnd, ref digitsBitmap, ref currentCommandRef, ref rollbackCommandRefCount, ref inErrorState, ref ranOutOfData, out arrayLength);
                inErrorState |= (uint)((arrayLength - 1) >> 31);    // arrayLength < 1
            }

            var remainingArrayItems = arrayLength;

            // first string will be a command, so we can do a smarter length check MOST of the time
            // worth it to eat a branch then
            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref currentCommandRef, sizeof(uint)), ref commandBufferAllocatedEnd), "About to read past end of command buffer");
            var cmdLengthProbe = Unsafe.As<byte, uint>(ref currentCommandRef);
            cmdLengthProbe -= CommonCommandLengthPrefix;
            cmdLengthProbe = BitOperations.RotateRight(cmdLengthProbe, 8);
            if (cmdLengthProbe < 10)
            {
                // handle the common case where command length is < 10

                ranOutOfData |= (uint)~(((int)-Unsafe.ByteOffset(ref currentIntoRef, ref intoAsIntsAllocatedEnd)) >> 31);                // currentIntoRef == intoAsIntsAllocatedEnd

                var byteAfterCurrentCommandRef = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
                ranOutOfData |= (uint)(((commandBufferFilledBytes - byteAfterCurrentCommandRef) - 4) >> 31);    // (commandBufferFilledBytes - byteAfterCurrentCommandRef) < 4  (considering $\d\r\n)
                inErrorState |= ranOutOfData;

                // if we're out of data, we also need an extra bit of rollback to prevent 
                var outOfDataRollback = (int)ranOutOfData & 4;

                // get a ref which is rolled back to the original value IF we're in an errorState
                ref var commandIntoRef = ref Unsafe.Subtract(ref currentIntoRef, (rollbackWriteIntoRefCount & (int)inErrorState) + outOfDataRollback);
                var commonCommandStart = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef) + 4;
                var commonCommandEnd = (int)(commonCommandStart + cmdLengthProbe + 2);

                ranOutOfData |= (uint)((commandBufferFilledBytes - commonCommandEnd) >> 31);    // commonCommandEnd > commandBufferFilledBytes
                inErrorState |= ranOutOfData;

                // check for \r\n
                var trailingCrLfOffset = ~inErrorState & (4 + cmdLengthProbe);
                ref var trailingCrLfRef = ref Unsafe.Add(ref currentCommandRef, trailingCrLfOffset);
                Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref trailingCrLfRef, sizeof(ushort)), ref commandBufferAllocatedEnd), "About to read past end of command buffer");
                var trailingCrLf = Unsafe.As<byte, ushort>(ref trailingCrLfRef);
                inErrorState |= (uint)((0 - (trailingCrLf ^ CRLF)) >> 31);  // trailingCrLf != CRLF

                Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref commandIntoRef, ParsedRespCommandOrArgument.ByteEndIx), ref intoAsIntsAllocatedEnd), "About to read/write past end of intoAsInts");
                ref var commonCmdAndArgCountRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref commandIntoRef, ParsedRespCommandOrArgument.ArgumentCountIx));
                ref var commonByteStartAndEndRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref commandIntoRef, ParsedRespCommandOrArgument.ByteStartIx));

                // clear argCount & cmd if not in error state
                commonCmdAndArgCountRef &= (long)(int)inErrorState;

                // leave unmodified if in error state, or set to commonCommandStart & commonCommandEnd
                var packedStartAndEnd = (long)(((ulong)(uint)commonCommandStart) | (((ulong)(uint)commonCommandEnd) << 32));
                commonByteStartAndEndRef = (commonByteStartAndEndRef & (long)(int)inErrorState) | (packedStartAndEnd & ~(long)(int)inErrorState);

                // advance writeNextResultInto if not in error state
                rollbackWriteIntoRefCount += (int)(4 & ~inErrorState);
                currentIntoRef = ref Unsafe.Add(ref currentIntoRef, (int)(4 & ~inErrorState));
                remainingArrayItems--;

                // update currentCommandBufferIx if not in error state
                currentCommandRef = ref Unsafe.Add(ref currentCommandRef, (int)(~inErrorState & (4 + cmdLengthProbe + 2)));
                rollbackCommandRefCount += (int)(~inErrorState & (4 + cmdLengthProbe + 2));
            }

            // handle remaining strings (which may not include the command string)
            do
            {
                //var (updateCommandRef, updateWriteNextRef) = UnrollTakeCommandBulkString(ref commandBufferAllocatedEnd, ref commandBuffer, commandBufferFilledBytes, ref digitsBitmapAllocatedEnd, ref digitsBitmap, ref intoAsIntsAllocatedEnd, ref currentCommandRef, ref currentIntoRef, ref remainingArrayItems, ref rollbackCommandRefCount, ref rollbackWriteIntoRefCount, ref inErrorState, ref ranOutOfData);
                var (updateCommandRef, updateWriteNextRef) = UnrollTakeCommandBulkString_ManualInline(ref commandBufferAllocatedEnd, ref commandBuffer, commandBufferFilledBytes, ref digitsBitmapAllocatedEnd, ref digitsBitmap, ref intoAsIntsAllocatedEnd, ref currentCommandRef, ref currentIntoRef, ref remainingArrayItems, ref rollbackCommandRefCount, ref rollbackWriteIntoRefCount, ref inErrorState, ref ranOutOfData);

                currentCommandRef = ref updateCommandRef.Unwrap();
                currentIntoRef = ref updateWriteNextRef.Unwrap();
            } while ((remainingArrayItems & ~inErrorState) != 0);

            // here we need an adjustment to avoid read/writing past end of currentIntoRef
            var effectiveIntoReverse = (int)(ranOutOfData & 4);

            // we've now parsed everything EXCEPT the command enum
            // and we haven't written the argument count either
            //
            // we might be in an error state, or we might be out of data
            //
            // if we're out of data, we just need to take nothing (rolling everything back)
            ref var effectInto = ref Unsafe.Subtract(ref currentIntoRef, rollbackWriteIntoRefCount + effectiveIntoReverse);
            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ByteEndIx), ref intoAsIntsAllocatedEnd), "About to read/write past the end of intoAsInts");
            var cmdStart = (int)(Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ByteStartIx) & ~inErrorState);
            var cmdEnd = (int)(Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ByteEndIx) & ~inErrorState);

            // place ourselves in an error state if we could not parse the command
            var cmd = UnconditionalParseRespCommandImpl.Parse(ref commandBufferAllocatedEnd, ref commandBuffer, cmdStart, (cmdEnd - cmdStart) - 2);
            inErrorState |= (uint)~(-(int)cmd >> 31);   // cmd == 0

            // we need to update the first entry, to either have command and argument values,
            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ByteEndIx), ref intoAsIntsAllocatedEnd), "About to read/write past the end of intoAsInts");
            ref var argCountAndCmdRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ArgumentCountIx));
            ref var byteStartAndEndRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ByteStartIx));

            // update command and arg count if not in error state, otherwise leave them unchanged
            var packedArgCountAndCmd = (long)((uint)arrayLength | (((ulong)(uint)cmd) << 32));
            argCountAndCmdRef = (argCountAndCmdRef & (long)(int)inErrorState) | (packedArgCountAndCmd & ~(long)(int)inErrorState);

            // we need to zero these out if we're in error state, but NOT out of data
            // this will produce a Malformed entry
            var writeMalformed = inErrorState & ~ranOutOfData;
            byteStartAndEndRef &= ~(long)(int)writeMalformed;

            // update the pointer for storage

            // if we ENTERED the error state, we wrote a Malformed out
            // so we need to advance writeNextResultInto by 4
            // if we ran out of data, we roll writeNextResultInto all the way back
            // 
            // so the logic is writeNextResultInto - inErrorState
            var rollbackNextResultIntoBy = (int)(inErrorState & ~oldErrorState) & (rollbackWriteIntoRefCount - 4);
            rollbackNextResultIntoBy = ((int)ranOutOfData & rollbackWriteIntoRefCount) | ((int)~ranOutOfData & rollbackNextResultIntoBy);
            currentIntoRef = ref Unsafe.Subtract(ref currentIntoRef, rollbackNextResultIntoBy);

            // roll current pointer back if we are in an error state
            currentCommandRef = ref Unsafe.Subtract(ref currentCommandRef, (int)(inErrorState & (uint)rollbackCommandRefCount));

            // manual lifetime management, this is fine
            return new(ref Unsafe.AsRef(ref currentCommandRef), ref Unsafe.AsRef(ref currentIntoRef));
        }

        /// <summary>
        /// <see cref="UnrollReadCommand(ref byte, ref byte, int, ref byte, ref byte, ref int, ref byte, ref int, ref uint, ref uint)"/>, but manually inlined.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UpdatedPairs UnrollReadCommand_ManualInline(
            ref byte commandBufferAllocatedEnd,
            ref byte commandBuffer,
            int commandBufferFilledBytes,
            ref byte digitsBitmapAllocatedEnd,
            ref byte digitsBitmap,
            ref int intoAsIntsAllocatedEnd,
            scoped ref byte currentCommandRef,
            scoped ref int currentIntoRef,
            ref uint inErrorState,
            ref uint ranOutOfData
        )
        {
            const int MinimumCommandSize = 1 + 1 + 2;   // *0\r\n
            const uint CommonArrayLengthSuffix = 0x240A_0D31;  // $\n\r1
            const uint CommonCommandLengthPrefix = 0x0A0D_3024; // \n\r0$

            Debug.Assert(commandBufferFilledBytes > 0, "Command buffer should have data in it");

            // for this, we attempt to read the a command, BUT if we're already in an error state
            // we throw the result away with some ANDS
            //
            // if we encounter an error, we enter the error state and we write out a malformed
            // if we run out of data, we enter an error state that but do not write out a malformed
            //
            // if we're in an error state by the end, we rollback currentCommandBufferIx and writeNextResultInto
            // to their initial states

            var rollbackCommandRefCount = 0;
            var rollbackWriteIntoRefCount = 0;

            var oldErrorState = inErrorState;

            // first check that there's sufficient space for a command
            var availableBytes = commandBufferFilledBytes - (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
            ranOutOfData |= (uint)((availableBytes - MinimumCommandSize) >> 31);    // availableBytes < MinimumCommandSize
            inErrorState |= ranOutOfData;

            // check that we can even write a malformed, if needed
            ranOutOfData |= (uint)((~(-(int)Unsafe.ByteOffset(ref currentIntoRef, ref intoAsIntsAllocatedEnd))) >> 31);  // currentIntoRef == intoAsIntsAllocatedEnd
            inErrorState |= ranOutOfData;

            // check for *
            Debug.Assert(Unsafe.IsAddressLessThan(ref currentCommandRef, ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
            inErrorState |= (uint)((0 - (currentCommandRef ^ ArrayStart)) >> 31);   // currentCommandRef != ArrayStart

            // advance past *
            currentCommandRef = ref Unsafe.Add(ref currentCommandRef, 1);
            rollbackCommandRefCount++;

            // will be [0-9]\r\n$ MOST of the time, so eat a branch here
            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref currentCommandRef, sizeof(uint)), ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
            var arrayLengthProbe = Unsafe.As<byte, uint>(ref currentCommandRef);
            arrayLengthProbe -= CommonArrayLengthSuffix;

            int arrayLength;
            if (arrayLengthProbe < 8)
            {
                // array length of 0 is illegal, so we -1 in the probe
                // so that we don't need to check length in the common case
                //
                // but that means we need to +1 to get the real length later
                arrayLength = (int)(arrayLengthProbe + 1);

                // skip 1 digit, \r, and \n
                currentCommandRef = ref Unsafe.Add(ref currentCommandRef, 3);
                rollbackCommandRefCount += 3;
            }
            else
            {
                // get the number of parts in the array

                //currentCommandRef = ref TakePositiveNumberBranchless_ManualInline(ref commandBufferAllocatedEnd, ref commandBuffer, commandBufferFilledBytes, ref digitsBitmapAllocatedEnd, ref digitsBitmap, ref currentCommandRef, ref rollbackCommandRefCount, ref inErrorState, ref ranOutOfData, out arrayLength);
                {
                    Debug.Assert(commandBufferFilledBytes > 0, "Cannot call with empty buffer");

                    var commandBufferIx = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
                    var startByteIndex = commandBufferIx / 8;
                    var startBitIndex = commandBufferIx % 8;

                    // we expect a run of digits followed by a \r\n, so digitBitmap should be 0b0...xxx1
                    // a run of 11+ is invalid, considering a shift of up to 8 this will always fit in a uint32
                    Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsBitmap, startByteIndex + sizeof(uint)), ref digitsBitmapAllocatedEnd), "About to read past end of digits bitmap");

                    var digitsBitmapValue = Unsafe.As<byte, uint>(ref Unsafe.Add(ref digitsBitmap, startByteIndex)) >> startBitIndex;
                    var digitCount = (uint)BitOperations.TrailingZeroCount(~digitsBitmapValue);

                    // we expect no more than 10 digits (int.MaxValue is 2,147,483,647), and at least 1 digit
                    inErrorState |= ((10 - digitCount) >> 31);  // digitCount >= 11

                    // check for \r\n
                    ref var expectedCrLfRef = ref Unsafe.Add(ref currentCommandRef, digitCount);
                    var expectedCrPosition = (int)Unsafe.ByteOffset(ref commandBuffer, ref expectedCrLfRef);
                    ranOutOfData |= (uint)((commandBufferFilledBytes - (expectedCrPosition + 2)) >> 31);    // expectedCrPosition + 2 > commandBufferFilledBytes
                    inErrorState |= ranOutOfData;

                    Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref expectedCrLfRef, sizeof(ushort)), ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
                    inErrorState |= (uint)((0 - (Unsafe.As<byte, ushort>(ref expectedCrLfRef) ^ CRLF)) >> 31);    // expectedCrLfRef != CRLF;

                    // if we're in an error state, we'll take an empty buffer
                    // but we'll get a good one if we're not
                    ref var digitsStartRef = ref currentCommandRef;
                    digitsStartRef = ref Unsafe.Subtract(ref digitsStartRef, (int)(inErrorState & rollbackCommandRefCount));
                    ref var digitsEndRef = ref expectedCrLfRef;
                    digitsEndRef = ref Unsafe.Subtract(ref digitsEndRef, (int)(inErrorState & (rollbackCommandRefCount + digitCount)));

                    // actually do the parsing
                    // parsed = UnconditionalParsePositiveInt(ref commandBufferAllocatedEnd, ref digitsStartRef, ref digitsEndRef);
                    {
                        Debug.Assert(Unsafe.IsAddressGreaterThan(ref digitsEndRef, ref digitsStartRef) || Unsafe.AreSame(ref digitsStartRef, ref digitsEndRef), "Incoherent digit end and start");
                        Debug.Assert(Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef) is >= 0 and < 11, "Should have validated correct size");
                        Debug.Assert(!MemoryMarshal.CreateSpan(ref digitsStartRef, (int)Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef)).ContainsAnyExceptInRange((byte)'0', (byte)'9'), "Should have validated digits only");

                        var length = (int)Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef);

                        var multLookupIx = length * 12;
                        ref var multStart = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(UnconditionalParsePositiveIntMultiples), multLookupIx);

                        // many numbers are going to fit in 4 digits
                        // 0\r\n* = len = 1 = 0x**_\n_\r_00
                        // 01\r\n = len = 2 = 0x\n_\r_11_00
                        // 012\r  = len = 3 = 0x\r_22_11_00
                        // 0123   = len = 4 = 0x33_22_11_00
                        if (length <= 4)
                        {
                            // load all the digits (and some extra junk, maybe)
                            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsStartRef, sizeof(uint)), ref commandBufferAllocatedEnd), "About to read past end of command buffer");
                            var fastPathDigits = Unsafe.As<byte, uint>(ref digitsStartRef);

                            // reverse so we can pad more easily
                            // 0\r\n* = len = 1 = 0x**_\n_\r_00 => 0x00_\r_\n_**
                            // 01\r\n = len = 2 = 0x\n_\r_11_00 => 0x00_11_\r_\n
                            // 012\r  = len = 3 = 0x\r_22_11_00 => 0x00_11_22_\r
                            // 0123   = len = 4 = 0x33_22_11_00 => 0x00_11_22_33
                            fastPathDigits = BinaryPrimitives.ReverseEndianness(fastPathDigits); // this is an intrinsic, so should be a single op

                            // shift right to pad with zeros
                            // 0\r\n* = len = 1 = 0x**_\n_\r_00 => 0x00_\r_\n_** => 0x00_00_00_00 ; shift right 24 (32 - (8 * len))
                            // 01\r\n = len = 2 = 0x\n_\r_11_00 => 0x00_11_\r_\n => 0x00_00_00_11 ; shift right 16 (32 - (8 * len))
                            // 012\r  = len = 3 = 0x\r_22_11_00 => 0x00_11_22_\r => 0x00_00_11_22 ; shift right  8 (32 - (8 * len))
                            // 0123   = len = 4 = 0x33_22_11_00 => 0x00_11_22_33 => 0x00_11_22_33 ; shift right  0 (32 - (8 * len))
                            fastPathDigits = fastPathDigits >> (32 - (8 * length));

                            // fastPathDigits = 0x.4_.3__.2_.1
                            // onesAndHundreds = 0x00_03__00_01
                            var onesAndHundreds = fastPathDigits & ~0xFF_30_FF_30U;
                            // tensAndThousands = 0x04_00__02_00
                            var tensAndThousands = fastPathDigits & ~0x30_FF_30_FFU;

                            // mixedOnce = 0x(4 * 10 + 3)__(2 * 10 + 1) = 0d043__21
                            var mixedOnce = (tensAndThousands * 10U) + (onesAndHundreds << 8);

                            // topPair = 0d43__00
                            ulong topPair = mixedOnce & 0xFF_FF_00_00U;
                            // lowPair = 0d00__21
                            ulong lowPair = mixedOnce & 0x00_00_FF_FFU;

                            // mixedTwice = 0d(43 * 100 + 21)_00
                            var mixedTwice = (topPair * 100U) + (lowPair << 16);

                            var result = (int)(mixedTwice >> 24);

                            // leading zero check, force result to -1 if below expected value
                            result |= ((0 - (length ^ 1)) >> 31) & ((result - (int)multStart) >> 31);   // length != 1 & multStart > result;

                            arrayLength = result;
                        }
                        else
                        {
                            var maskLookupIx = length * 16;

                            ref var maskStart = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(UnconditionalParsePositiveIntLookup), maskLookupIx);

                            var mask = Vector128.LoadUnsafe(ref maskStart);

                            // load 16 bytes of data, which will have \r\n and some trailing garbage
                            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsStartRef, Vector128<byte>.Count), ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
                            var data = Vector128.LoadUnsafe(ref digitsStartRef);

                            var toUseBytes = Vector128.BitwiseAnd(data, mask);                        // bytes 0-9 are (potentially) valid

                            // expand so we can multiply cleanly
                            var (d0To7, d8To15) = Vector128.Widen(toUseBytes);
                            var (d0To3, d4To7) = Vector128.Widen(d0To7);
                            var (d8To11, _) = Vector128.Widen(d8To15);

                            // load per digit multiples
                            var scale0To3 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 0));
                            var scale4To7 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 4));
                            var scale8To11 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 8));

                            // scale each digit by it's place value
                            // 
                            // at maximum length, m0To3[0] might overflow
                            var m0To3 = Vector128.Multiply(d0To3, scale0To3);
                            var m4To7 = Vector128.Multiply(d4To7, scale4To7);
                            var m8To11 = Vector128.Multiply(d8To11, scale8To11);

                            var maybeMultOverflow = Vector128.LessThan(m0To3, scale0To3);

                            // calculate the vertical sum
                            // 
                            // vertical sum cannot overflow (assuming m0To3 didn't overflow)
                            var verticalSum = Vector128.Add(Vector128.Add(m0To3, m4To7), m8To11);

                            // calculate the horizontal sum
                            //
                            // horizontal sum can overflow, even if m0To3 didn't overflow
                            var horizontalSum = Vector128.Dot(verticalSum, Vector128<uint>.One);

                            var maybeHorizontalOverflow = Vector128.GreaterThan(verticalSum, Vector128.CreateScalar(horizontalSum));

                            var maybeOverflow = Vector128.BitwiseOr(maybeMultOverflow, maybeHorizontalOverflow);

                            var res = horizontalSum;

                            // check for overflow
                            //
                            // note that overflow can only happen when length == 10
                            // only the first digit can overflow on it's own (if it's > 2)
                            // everything else has to overflow when summed
                            // 
                            // we know length is <= 10, we can exploit this in the == check
                            var allOnesIfOverflow = (uint)(((9 - length) >> 31) & ((0L - maybeOverflow.GetElement(0)) >> 63));  // maybeOverflow.GetElement(0) != 0 & length == 10

                            // check for leading zeros
                            var allOnesIfLeadingZeros = (uint)(((0 - (length ^ 1)) >> 31) & (((int)res - (int)multStart) >> 31)); // length != 1 & multStart > (int)res

                            // turn into -1 if things are invalid
                            res |= allOnesIfOverflow | allOnesIfLeadingZeros;

                            arrayLength = (int)res;
                        }
                    }
                    inErrorState |= (uint)(arrayLength >> 31);    // value < 0

                    // update to point one past the \r\n if we're not in an error state
                    // otherwise leave it unmodified
                    currentCommandRef = ref Unsafe.Add(ref currentCommandRef, (int)((digitCount + 2) & ~inErrorState));
                    rollbackCommandRefCount += (int)((digitCount + 2) & ~inErrorState);
                }

                inErrorState |= (uint)((arrayLength - 1) >> 31);    // arrayLength < 1
            }

            var remainingArrayItems = arrayLength;

            // first string will be a command, so we can do a smarter length check MOST of the time
            // worth it to eat a branch then
            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref currentCommandRef, sizeof(uint)), ref commandBufferAllocatedEnd), "About to read past end of command buffer");
            var cmdLengthProbe = Unsafe.As<byte, uint>(ref currentCommandRef);
            cmdLengthProbe -= CommonCommandLengthPrefix;
            cmdLengthProbe = BitOperations.RotateRight(cmdLengthProbe, 8);
            if (cmdLengthProbe < 10)
            {
                // handle the common case where command length is < 10

                ranOutOfData |= (uint)~(((int)-Unsafe.ByteOffset(ref currentIntoRef, ref intoAsIntsAllocatedEnd)) >> 31);                // currentIntoRef == intoAsIntsAllocatedEnd

                var byteAfterCurrentCommandRef = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
                ranOutOfData |= (uint)(((commandBufferFilledBytes - byteAfterCurrentCommandRef) - 4) >> 31);    // (commandBufferFilledBytes - byteAfterCurrentCommandRef) < 4  (considering $\d\r\n)
                inErrorState |= ranOutOfData;

                // if we're out of data, we also need an extra bit of rollback to prevent 
                var outOfDataRollback = (int)ranOutOfData & 4;

                // get a ref which is rolled back to the original value IF we're in an errorState
                ref var commandIntoRef = ref Unsafe.Subtract(ref currentIntoRef, (rollbackWriteIntoRefCount & (int)inErrorState) + outOfDataRollback);
                var commonCommandStart = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef) + 4;
                var commonCommandEnd = (int)(commonCommandStart + cmdLengthProbe + 2);

                ranOutOfData |= (uint)((commandBufferFilledBytes - commonCommandEnd) >> 31);    // commonCommandEnd > commandBufferFilledBytes
                inErrorState |= ranOutOfData;

                // check for \r\n
                var trailingCrLfOffset = ~inErrorState & (4 + cmdLengthProbe);
                ref var trailingCrLfRef = ref Unsafe.Add(ref currentCommandRef, trailingCrLfOffset);
                Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref trailingCrLfRef, sizeof(ushort)), ref commandBufferAllocatedEnd), "About to read past end of command buffer");
                var trailingCrLf = Unsafe.As<byte, ushort>(ref trailingCrLfRef);
                inErrorState |= (uint)((0 - (trailingCrLf ^ CRLF)) >> 31);  // trailingCrLf != CRLF

                Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref commandIntoRef, ParsedRespCommandOrArgument.ByteEndIx), ref intoAsIntsAllocatedEnd), "About to read/write past end of intoAsInts");
                ref var commonCmdAndArgCountRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref commandIntoRef, ParsedRespCommandOrArgument.ArgumentCountIx));
                ref var commonByteStartAndEndRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref commandIntoRef, ParsedRespCommandOrArgument.ByteStartIx));

                // clear argCount & cmd if not in error state
                commonCmdAndArgCountRef &= (long)(int)inErrorState;

                // leave unmodified if in error state, or set to commonCommandStart & commonCommandEnd
                var packedStartAndEnd = (long)(((ulong)(uint)commonCommandStart) | (((ulong)(uint)commonCommandEnd) << 32));
                commonByteStartAndEndRef = (commonByteStartAndEndRef & (long)(int)inErrorState) | (packedStartAndEnd & ~(long)(int)inErrorState);

                // advance writeNextResultInto if not in error state
                rollbackWriteIntoRefCount += (int)(4 & ~inErrorState);
                currentIntoRef = ref Unsafe.Add(ref currentIntoRef, (int)(4 & ~inErrorState));
                remainingArrayItems--;

                // update currentCommandBufferIx if not in error state
                currentCommandRef = ref Unsafe.Add(ref currentCommandRef, (int)(~inErrorState & (4 + cmdLengthProbe + 2)));
                rollbackCommandRefCount += (int)(~inErrorState & (4 + cmdLengthProbe + 2));
            }

            // handle remaining strings (which may not include the command string)
            do
            {
                //var (updateCommandRef, updateWriteNextRef) = UnrollTakeCommandBulkString(ref commandBufferAllocatedEnd, ref commandBuffer, commandBufferFilledBytes, ref digitsBitmapAllocatedEnd, ref digitsBitmap, ref intoAsIntsAllocatedEnd, ref currentCommandRef, ref currentIntoRef, ref remainingArrayItems, ref rollbackCommandRefCount, ref rollbackWriteIntoRefCount, ref inErrorState, ref ranOutOfData);
                //var (updateCommandRef, updateWriteNextRef) = UnrollTakeCommandBulkString_ManualInline(ref commandBufferAllocatedEnd, ref commandBuffer, commandBufferFilledBytes, ref digitsBitmapAllocatedEnd, ref digitsBitmap, ref intoAsIntsAllocatedEnd, ref currentCommandRef, ref currentIntoRef, ref remainingArrayItems, ref rollbackCommandRefCount, ref rollbackWriteIntoRefCount, ref inErrorState, ref ranOutOfData);
                {
                    const int MinimumBulkStringSize = 1 + 1 + 2 + 2; // $0\r\n\r\n

                    Debug.Assert(Unsafe.ByteOffset(ref commandBuffer, ref commandBufferAllocatedEnd) > 0, "Command buffer cannot be of 0 size");

                    // if we've read everything, we need to just do nothing (including updating inErrorState and ranOutOfData)
                    // but still run through everything unconditionally
                    var readAllItems = (uint)~((0 - remainingArrayItems) >> 31);  // remainingItemsInArray == 0
                    var oldInErrorState = inErrorState;
                    var oldRanOutOfData = ranOutOfData;

                    // we need to prevent action now, so act like we're in an error state
                    inErrorState |= readAllItems;

                    // check that there's enough data to even fit a string
                    var availableBytesSub = commandBufferFilledBytes - (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
                    ranOutOfData |= (uint)((availableBytesSub - MinimumBulkStringSize) >> 31); // availableBytes < MinimumBulkStringSize
                    inErrorState |= ranOutOfData;

                    // check for $
                    ref var bulkStringStartExpectedRef = ref currentCommandRef;

                    Debug.Assert(Unsafe.IsAddressLessThan(ref bulkStringStartExpectedRef, ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
                    inErrorState |= (uint)((-(bulkStringStartExpectedRef ^ BulkStringStart)) >> 31); // (ref commandBuffer + bulkStringStartExpectedIx) != BulkStringStart

                    // move past $
                    currentCommandRef = ref Unsafe.Add(ref currentCommandRef, 1);
                    rollbackCommandRefCount++;

                    // parse the length
                    //currentCommandRef = ref TakePositiveNumberBranchless_ManualInline(ref commandBufferAllocatedEnd, ref commandBuffer, commandBufferFilledBytes, ref digitsBitmapAllocatedEnd, ref digitsBitmap, ref currentCommandRef, ref rollbackCommandRefCount, ref inErrorState, ref ranOutOfData, out var bulkStringLength);
                    int bulkStringLength;
                    {
                        Debug.Assert(commandBufferFilledBytes > 0, "Cannot call with empty buffer");

                        var commandBufferIx = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
                        var startByteIndex = commandBufferIx / 8;
                        var startBitIndex = commandBufferIx % 8;

                        // we expect a run of digits followed by a \r\n, so digitBitmap should be 0b0...xxx1
                        // a run of 11+ is invalid, considering a shift of up to 8 this will always fit in a uint32
                        Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsBitmap, startByteIndex + sizeof(uint)), ref digitsBitmapAllocatedEnd), "About to read past end of digits bitmap");

                        var digitsBitmapValue = Unsafe.As<byte, uint>(ref Unsafe.Add(ref digitsBitmap, startByteIndex)) >> startBitIndex;
                        var digitCount = (uint)BitOperations.TrailingZeroCount(~digitsBitmapValue);

                        // we expect no more than 10 digits (int.MaxValue is 2,147,483,647), and at least 1 digit
                        inErrorState |= ((10 - digitCount) >> 31);  // digitCount >= 11

                        // check for \r\n
                        ref var expectedCrLfRef = ref Unsafe.Add(ref currentCommandRef, digitCount);
                        var expectedCrPosition_inline = (int)Unsafe.ByteOffset(ref commandBuffer, ref expectedCrLfRef);
                        ranOutOfData |= (uint)((commandBufferFilledBytes - (expectedCrPosition_inline + 2)) >> 31);    // expectedCrPosition + 2 > commandBufferFilledBytes
                        inErrorState |= ranOutOfData;

                        Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref expectedCrLfRef, sizeof(ushort)), ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
                        inErrorState |= (uint)((0 - (Unsafe.As<byte, ushort>(ref expectedCrLfRef) ^ CRLF)) >> 31);    // expectedCrLfRef != CRLF;

                        // if we're in an error state, we'll take an empty buffer
                        // but we'll get a good one if we're not
                        ref var digitsStartRef = ref currentCommandRef;
                        digitsStartRef = ref Unsafe.Subtract(ref digitsStartRef, (int)(inErrorState & rollbackCommandRefCount));
                        ref var digitsEndRef = ref expectedCrLfRef;
                        digitsEndRef = ref Unsafe.Subtract(ref digitsEndRef, (int)(inErrorState & (rollbackCommandRefCount + digitCount)));

                        // actually do the parsing
                        // parsed = UnconditionalParsePositiveInt(ref commandBufferAllocatedEnd, ref digitsStartRef, ref digitsEndRef);
                        {
                            Debug.Assert(Unsafe.IsAddressGreaterThan(ref digitsEndRef, ref digitsStartRef) || Unsafe.AreSame(ref digitsStartRef, ref digitsEndRef), "Incoherent digit end and start");
                            Debug.Assert(Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef) is >= 0 and < 11, "Should have validated correct size");
                            Debug.Assert(!MemoryMarshal.CreateSpan(ref digitsStartRef, (int)Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef)).ContainsAnyExceptInRange((byte)'0', (byte)'9'), "Should have validated digits only");

                            var length = (int)Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef);

                            var multLookupIx = length * 12;
                            ref var multStart = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(UnconditionalParsePositiveIntMultiples), multLookupIx);

                            // many numbers are going to fit in 4 digits
                            // 0\r\n* = len = 1 = 0x**_\n_\r_00
                            // 01\r\n = len = 2 = 0x\n_\r_11_00
                            // 012\r  = len = 3 = 0x\r_22_11_00
                            // 0123   = len = 4 = 0x33_22_11_00
                            if (length <= 4)
                            {
                                // load all the digits (and some extra junk, maybe)
                                Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsStartRef, sizeof(uint)), ref commandBufferAllocatedEnd), "About to read past end of command buffer");
                                var fastPathDigits = Unsafe.As<byte, uint>(ref digitsStartRef);

                                // reverse so we can pad more easily
                                // 0\r\n* = len = 1 = 0x**_\n_\r_00 => 0x00_\r_\n_**
                                // 01\r\n = len = 2 = 0x\n_\r_11_00 => 0x00_11_\r_\n
                                // 012\r  = len = 3 = 0x\r_22_11_00 => 0x00_11_22_\r
                                // 0123   = len = 4 = 0x33_22_11_00 => 0x00_11_22_33
                                fastPathDigits = BinaryPrimitives.ReverseEndianness(fastPathDigits); // this is an intrinsic, so should be a single op

                                // shift right to pad with zeros
                                // 0\r\n* = len = 1 = 0x**_\n_\r_00 => 0x00_\r_\n_** => 0x00_00_00_00 ; shift right 24 (32 - (8 * len))
                                // 01\r\n = len = 2 = 0x\n_\r_11_00 => 0x00_11_\r_\n => 0x00_00_00_11 ; shift right 16 (32 - (8 * len))
                                // 012\r  = len = 3 = 0x\r_22_11_00 => 0x00_11_22_\r => 0x00_00_11_22 ; shift right  8 (32 - (8 * len))
                                // 0123   = len = 4 = 0x33_22_11_00 => 0x00_11_22_33 => 0x00_11_22_33 ; shift right  0 (32 - (8 * len))
                                fastPathDigits = fastPathDigits >> (32 - (8 * length));

                                // fastPathDigits = 0x.4_.3__.2_.1
                                // onesAndHundreds = 0x00_03__00_01
                                var onesAndHundreds = fastPathDigits & ~0xFF_30_FF_30U;
                                // tensAndThousands = 0x04_00__02_00
                                var tensAndThousands = fastPathDigits & ~0x30_FF_30_FFU;

                                // mixedOnce = 0x(4 * 10 + 3)__(2 * 10 + 1) = 0d043__21
                                var mixedOnce = (tensAndThousands * 10U) + (onesAndHundreds << 8);

                                // topPair = 0d43__00
                                ulong topPair = mixedOnce & 0xFF_FF_00_00U;
                                // lowPair = 0d00__21
                                ulong lowPair = mixedOnce & 0x00_00_FF_FFU;

                                // mixedTwice = 0d(43 * 100 + 21)_00
                                var mixedTwice = (topPair * 100U) + (lowPair << 16);

                                var result = (int)(mixedTwice >> 24);

                                // leading zero check, force result to -1 if below expected value
                                result |= ((0 - (length ^ 1)) >> 31) & ((result - (int)multStart) >> 31);   // length != 1 & multStart > result;

                                bulkStringLength = result;
                            }
                            else
                            {

                                var maskLookupIx = length * 16;

                                ref var maskStart = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(UnconditionalParsePositiveIntLookup), maskLookupIx);

                                var mask = Vector128.LoadUnsafe(ref maskStart);

                                // load 16 bytes of data, which will have \r\n and some trailing garbage
                                Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsStartRef, Vector128<byte>.Count), ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
                                var data = Vector128.LoadUnsafe(ref digitsStartRef);

                                var toUseBytes = Vector128.BitwiseAnd(data, mask);                        // bytes 0-9 are (potentially) valid

                                // expand so we can multiply cleanly
                                var (d0To7, d8To15) = Vector128.Widen(toUseBytes);
                                var (d0To3, d4To7) = Vector128.Widen(d0To7);
                                var (d8To11, _) = Vector128.Widen(d8To15);

                                // load per digit multiples
                                var scale0To3 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 0));
                                var scale4To7 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 4));
                                var scale8To11 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 8));

                                // scale each digit by it's place value
                                // 
                                // at maximum length, m0To3[0] might overflow
                                var m0To3 = Vector128.Multiply(d0To3, scale0To3);
                                var m4To7 = Vector128.Multiply(d4To7, scale4To7);
                                var m8To11 = Vector128.Multiply(d8To11, scale8To11);

                                var maybeMultOverflow = Vector128.LessThan(m0To3, scale0To3);

                                // calculate the vertical sum
                                // 
                                // vertical sum cannot overflow (assuming m0To3 didn't overflow)
                                var verticalSum = Vector128.Add(Vector128.Add(m0To3, m4To7), m8To11);

                                // calculate the horizontal sum
                                //
                                // horizontal sum can overflow, even if m0To3 didn't overflow
                                var horizontalSum = Vector128.Dot(verticalSum, Vector128<uint>.One);

                                var maybeHorizontalOverflow = Vector128.GreaterThan(verticalSum, Vector128.CreateScalar(horizontalSum));

                                var maybeOverflow = Vector128.BitwiseOr(maybeMultOverflow, maybeHorizontalOverflow);

                                var res = horizontalSum;

                                // check for overflow
                                //
                                // note that overflow can only happen when length == 10
                                // only the first digit can overflow on it's own (if it's > 2)
                                // everything else has to overflow when summed
                                // 
                                // we know length is <= 10, we can exploit this in the == check
                                var allOnesIfOverflow = (uint)(((9 - length) >> 31) & ((0L - maybeOverflow.GetElement(0)) >> 63));  // maybeOverflow.GetElement(0) != 0 & length == 10

                                // check for leading zeros
                                var allOnesIfLeadingZeros = (uint)(((0 - (length ^ 1)) >> 31) & (((int)res - (int)multStart) >> 31)); // length != 1 & multStart > (int)res

                                // turn into -1 if things are invalid
                                res |= allOnesIfOverflow | allOnesIfLeadingZeros;

                                bulkStringLength = (int)res;
                            }
                        }
                        inErrorState |= (uint)(bulkStringLength >> 31);    // value < 0

                        // update to point one past the \r\n if we're not in an error state
                        // otherwise leave it unmodified
                        currentCommandRef = ref Unsafe.Add(ref currentCommandRef, (int)((digitCount + 2) & ~inErrorState));
                        rollbackCommandRefCount += (int)((digitCount + 2) & ~inErrorState);
                    }

                    var bulkStringStart = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
                    var bulkStringEnd = bulkStringStart + bulkStringLength;

                    // check for \r\n
                    var expectedCrPosition = bulkStringEnd;
                    ranOutOfData |= (uint)((commandBufferFilledBytes - (expectedCrPosition + 2)) >> 31);    // expectedCrPosition + 2 > commandBufferFilledBytes
                    inErrorState |= ranOutOfData;

                    expectedCrPosition &= (int)~inErrorState;
                    Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref commandBuffer, expectedCrPosition + sizeof(ushort)), ref commandBufferAllocatedEnd), "About to read past allocated command buffer");
                    inErrorState |= (uint)((0 - (Unsafe.As<byte, ushort>(ref Unsafe.Add(ref commandBuffer, expectedCrPosition)) ^ CRLF)) >> 31);    // (ref commandBuffer + expectedCrPosition) != CRLF

                    // move past the \r\n
                    currentCommandRef = ref Unsafe.Add(ref currentCommandRef, bulkStringLength + 2);
                    rollbackCommandRefCount += bulkStringLength + 2;

                    // now we're ready to write out results

                    // do we even have space to write something?
                    ranOutOfData |= (uint)~(((int)-Unsafe.ByteOffset(ref currentIntoRef, ref intoAsIntsAllocatedEnd)) >> 31);    // writeNextResultInto == intoAsIntsSize
                    inErrorState |= ranOutOfData;

                    // write the results out
                    //var effectiveInto = (int)(~inErrorState & writeNextResultInto);
                    ref var effectiveInto = ref Unsafe.Subtract(ref currentIntoRef, inErrorState & 4);
                    Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref effectiveInto, ParsedRespCommandOrArgument.ByteEndIx), ref intoAsIntsAllocatedEnd), "About to read/write past end of intoAsInts");
                    //ref var intoAsIntsEffectiveIntoRef = ref Unsafe.Add(ref intoAsInts, effectiveInto);
                    ref var argCountAndCommandRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref effectiveInto, ParsedRespCommandOrArgument.ArgumentCountIx));
                    ref var byteStartAndEndRefSub = ref Unsafe.As<int, long>(ref Unsafe.Add(ref effectiveInto, ParsedRespCommandOrArgument.ByteStartIx));

                    // clear argCount and command UNLESS we're in an error state
                    argCountAndCommandRef &= (long)(int)inErrorState;

                    // set byteStart and byteEnd if we're not in an error state, otherwise leave them unmodified
                    var packedByteStartAndEnd = (long)(((ulong)(uint)bulkStringStart) | (((ulong)(uint)(expectedCrPosition + 2)) << 32));
                    byteStartAndEndRefSub = (byteStartAndEndRefSub & (long)(int)inErrorState) | (packedByteStartAndEnd & ~(long)(int)inErrorState);

                    // now update state

                    // advance write target if we succeeded
                    currentIntoRef = ref Unsafe.Add(ref currentIntoRef, (int)(~inErrorState & 4));
                    rollbackWriteIntoRefCount += (int)(~inErrorState & 4);

                    // note that we rollback currentCommandBufferIx if there's any error
                    //
                    // if we've read past the end of the command, we will _for sure_ be in error state
                    //currentCommandBufferIx = (int)((inErrorState & oldCurrentCommandBufferIx) | (~inErrorState & currentCommandBufferIx));
                    currentCommandRef = ref Unsafe.Subtract(ref currentCommandRef, (int)(inErrorState & (uint)(bulkStringLength + 2 + 1)));
                    rollbackCommandRefCount -= (int)(inErrorState & (uint)(bulkStringLength + 2 + 1));

                    // update the number of items read from the array, if appropriate
                    var shouldUpdateRemainingItemCount = ~readAllItems & ~inErrorState;
                    remainingArrayItems -= (int)(shouldUpdateRemainingItemCount & 1);

                    // but we rollback errors and out of data if we fully consumed the array
                    inErrorState = ((readAllItems & oldInErrorState) | (~readAllItems & inErrorState));
                    ranOutOfData = ((readAllItems & oldRanOutOfData) | (~readAllItems & ranOutOfData));

                    // pun away scoped, we know this is safe
                    //return new(ref Unsafe.AsRef(ref currentCommandRef), ref Unsafe.AsRef(ref currentIntoRef));
                }

                //currentCommandRef = ref updateCommandRef.Unwrap();
                //currentIntoRef = ref updateWriteNextRef.Unwrap();
            } while ((remainingArrayItems & ~inErrorState) != 0);

            // here we need an adjustment to avoid read/writing past end of currentIntoRef
            var effectiveIntoReverse = (int)(ranOutOfData & 4);

            // we've now parsed everything EXCEPT the command enum
            // and we haven't written the argument count either
            //
            // we might be in an error state, or we might be out of data
            //
            // if we're out of data, we just need to take nothing (rolling everything back)
            ref var effectInto = ref Unsafe.Subtract(ref currentIntoRef, rollbackWriteIntoRefCount + effectiveIntoReverse);
            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ByteEndIx), ref intoAsIntsAllocatedEnd), "About to read/write past the end of intoAsInts");
            var cmdStart = (int)(Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ByteStartIx) & ~inErrorState);
            var cmdEnd = (int)(Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ByteEndIx) & ~inErrorState);

            // place ourselves in an error state if we could not parse the command
            //var cmd = UnconditionalParseRespCommandImpl.Parse(ref commandBufferAllocatedEnd, ref commandBuffer, cmdStart, (cmdEnd - cmdStart) - 2);
            RespCommand cmd;
            {
                var commandLength = (cmdEnd - cmdStart) - 2;
                Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref commandBuffer, cmdStart + Vector256<byte>.Count), ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
                var commandVector = Vector256.LoadUnsafe(ref Unsafe.Add(ref commandBuffer, cmdStart));

                var effectiveLength = commandLength & 31;
                var maskIx = effectiveLength * 64;
                var xorIx = maskIx + 32;

                ref var lengthMaskAndHashXorsRef = ref MemoryMarshal.GetArrayDataReference(UnconditionalParseRespCommandImpl.LengthMaskAndHashXors);
                Debug.Assert(xorIx + sizeof(uint) <= UnconditionalParseRespCommandImpl.LengthMaskAndHashXors.Length, "About to read past end of LengthMaskAndHashXors");
                var maskVector = Vector256.LoadUnsafe(ref Unsafe.Add(ref lengthMaskAndHashXorsRef, maskIx));
                var xorVector = Vector256.Create(Unsafe.As<byte, uint>(ref Unsafe.Add(ref lengthMaskAndHashXorsRef, xorIx)));

                var truncatedCommandVector = Vector256.BitwiseAnd(commandVector, Vector256.GreaterThan(maskVector, Vector256<byte>.Zero));
                var upperCommandVector = Vector256.BitwiseAnd(truncatedCommandVector, maskVector);

                var invalid = Vector256.GreaterThanAny(truncatedCommandVector, Vector256.Create((byte)'z'));
                var allZerosIfInvalid = Unsafe.As<bool, byte>(ref invalid) - 1;

                var xored = Vector256.Xor(upperCommandVector, Vector256.As<uint, byte>(xorVector));
                var mixed = Vector256.Dot(Vector256.As<byte, uint>(xored), Vector256<uint>.One);
                var moded = mixed % 71;

                var dataStartIx = 8192 * effectiveLength;
                dataStartIx += (int)(moded * 64);

                ref var commandAndExpectedValuesRef = ref MemoryMarshal.GetArrayDataReference(UnconditionalParseRespCommandImpl.CommandAndExpectedValues);
                Debug.Assert(dataStartIx + sizeof(uint) + Vector256<byte>.Count <= UnconditionalParseRespCommandImpl.CommandAndExpectedValues.Length, "About to read past end of CommandAndExpectedValues");
                var cmdSub = Unsafe.As<byte, int>(ref Unsafe.Add(ref commandAndExpectedValuesRef, dataStartIx));
                var expectedCommandVector = Vector256.LoadUnsafe(ref Unsafe.Add(ref commandAndExpectedValuesRef, dataStartIx + 4));

                var matches = Vector256.EqualsAll(upperCommandVector, expectedCommandVector);
                var allOnesIfMatches = -Unsafe.As<bool, byte>(ref matches);

                cmd = (RespCommand)(cmdSub & allOnesIfMatches & allZerosIfInvalid);
            }
            inErrorState |= (uint)~(-(int)cmd >> 31);   // cmd == 0

            // we need to update the first entry, to either have command and argument values,
            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ByteEndIx), ref intoAsIntsAllocatedEnd), "About to read/write past the end of intoAsInts");
            ref var argCountAndCmdRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ArgumentCountIx));
            ref var byteStartAndEndRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref effectInto, ParsedRespCommandOrArgument.ByteStartIx));

            // update command and arg count if not in error state, otherwise leave them unchanged
            var packedArgCountAndCmd = (long)((uint)arrayLength | (((ulong)(uint)cmd) << 32));
            argCountAndCmdRef = (argCountAndCmdRef & (long)(int)inErrorState) | (packedArgCountAndCmd & ~(long)(int)inErrorState);

            // we need to zero these out if we're in error state, but NOT out of data
            // this will produce a Malformed entry
            var writeMalformed = inErrorState & ~ranOutOfData;
            byteStartAndEndRef &= ~(long)(int)writeMalformed;

            // update the pointer for storage

            // if we ENTERED the error state, we wrote a Malformed out
            // so we need to advance writeNextResultInto by 4
            // if we ran out of data, we roll writeNextResultInto all the way back
            // 
            // so the logic is writeNextResultInto - inErrorState
            var rollbackNextResultIntoBy = (int)(inErrorState & ~oldErrorState) & (rollbackWriteIntoRefCount - 4);
            rollbackNextResultIntoBy = ((int)ranOutOfData & rollbackWriteIntoRefCount) | ((int)~ranOutOfData & rollbackNextResultIntoBy);
            currentIntoRef = ref Unsafe.Subtract(ref currentIntoRef, rollbackNextResultIntoBy);

            // roll current pointer back if we are in an error state
            currentCommandRef = ref Unsafe.Subtract(ref currentCommandRef, (int)(inErrorState & (uint)rollbackCommandRefCount));

            // manual lifetime management, this is fine
            return new(ref Unsafe.AsRef(ref currentCommandRef), ref Unsafe.AsRef(ref currentIntoRef));
        }

        /// <summary>
        /// Take a single bulk string (of the form $0123+\r\ndata\r\n) and store it in <paramref name="intoAsInts"/> as a quad.
        /// Updates <paramref name="currentCommandBufferIx"/>, increments <paramref name="writeNextResultInto"/>, and decrements <paramref name="remainingItemsInArray"/> on success.
        /// 
        /// If parsing fails <paramref name="inErrorState"/> is set.
        /// If the failure could be fixed by more data, <paramref name="ranOutOfData"/> is set.
        /// 
        /// If <paramref name="inErrorState"/> is set when this method returns, no data will have been written to <paramref name="intoAsInts"/> and
        /// <paramref name="remainingItemsInArray"/>, <paramref name="currentCommandBufferIx"/>, and <paramref name="writeNextResultInto"/> will be unchanged.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UpdatedPairs UnrollTakeCommandBulkString(
            ref byte commandBufferAllocatedEnd,
            ref byte commandBuffer,
            int commandBufferFilledBytes,
            ref byte digitsBitmapAllocatedEnd,
            ref byte digitsBitmap,
            ref int intoAsIntsAllocatedEnd,
            scoped ref byte currentCommandRef,
            scoped ref int currentIntoRef,
            ref int remainingItemsInArray,
            ref int rollbackCommandRefCount,
            ref int rollbackWriteIntoRefCount,
            ref uint inErrorState,
            ref uint ranOutOfData
        )
        {
            const int MinimumBulkStringSize = 1 + 1 + 2 + 2; // $0\r\n\r\n

            Debug.Assert(Unsafe.ByteOffset(ref commandBuffer, ref commandBufferAllocatedEnd) > 0, "Command buffer cannot be of 0 size");

            // if we've read everything, we need to just do nothing (including updating inErrorState and ranOutOfData)
            // but still run through everything unconditionally
            var readAllItems = (uint)~((0 - remainingItemsInArray) >> 31);  // remainingItemsInArray == 0
            var oldInErrorState = inErrorState;
            var oldRanOutOfData = ranOutOfData;

            // we need to prevent action now, so act like we're in an error state
            inErrorState |= readAllItems;

            // check that there's enough data to even fit a string
            var availableBytes = commandBufferFilledBytes - (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
            ranOutOfData |= (uint)((availableBytes - MinimumBulkStringSize) >> 31); // availableBytes < MinimumBulkStringSize
            inErrorState |= ranOutOfData;

            // check for $
            ref var bulkStringStartExpectedRef = ref currentCommandRef;

            Debug.Assert(Unsafe.IsAddressLessThan(ref bulkStringStartExpectedRef, ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
            inErrorState |= (uint)((-(bulkStringStartExpectedRef ^ BulkStringStart)) >> 31); // (ref commandBuffer + bulkStringStartExpectedIx) != BulkStringStart

            // move past $
            currentCommandRef = ref Unsafe.Add(ref currentCommandRef, 1);
            rollbackCommandRefCount++;

            // parse the length
            currentCommandRef = ref TakePositiveNumberBranchless_ManualInline(ref commandBufferAllocatedEnd, ref commandBuffer, commandBufferFilledBytes, ref digitsBitmapAllocatedEnd, ref digitsBitmap, ref currentCommandRef, ref rollbackCommandRefCount, ref inErrorState, ref ranOutOfData, out var bulkStringLength);

            var bulkStringStart = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
            var bulkStringEnd = bulkStringStart + bulkStringLength;

            // check for \r\n
            var expectedCrPosition = bulkStringEnd;
            ranOutOfData |= (uint)((commandBufferFilledBytes - (expectedCrPosition + 2)) >> 31);    // expectedCrPosition + 2 > commandBufferFilledBytes
            inErrorState |= ranOutOfData;

            expectedCrPosition &= (int)~inErrorState;
            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref commandBuffer, expectedCrPosition + sizeof(ushort)), ref commandBufferAllocatedEnd), "About to read past allocated command buffer");
            inErrorState |= (uint)((0 - (Unsafe.As<byte, ushort>(ref Unsafe.Add(ref commandBuffer, expectedCrPosition)) ^ CRLF)) >> 31);    // (ref commandBuffer + expectedCrPosition) != CRLF

            // move past the \r\n
            currentCommandRef = ref Unsafe.Add(ref currentCommandRef, bulkStringLength + 2);
            rollbackCommandRefCount += bulkStringLength + 2;

            // now we're ready to write out results

            // do we even have space to write something?
            ranOutOfData |= (uint)~(((int)-Unsafe.ByteOffset(ref currentIntoRef, ref intoAsIntsAllocatedEnd)) >> 31);    // writeNextResultInto == intoAsIntsSize
            inErrorState |= ranOutOfData;

            // write the results out
            //var effectiveInto = (int)(~inErrorState & writeNextResultInto);
            ref var effectiveInto = ref Unsafe.Subtract(ref currentIntoRef, inErrorState & 4);
            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref effectiveInto, ParsedRespCommandOrArgument.ByteEndIx), ref intoAsIntsAllocatedEnd), "About to read/write past end of intoAsInts");
            //ref var intoAsIntsEffectiveIntoRef = ref Unsafe.Add(ref intoAsInts, effectiveInto);
            ref var argCountAndCommandRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref effectiveInto, ParsedRespCommandOrArgument.ArgumentCountIx));
            ref var byteStartAndEndRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref effectiveInto, ParsedRespCommandOrArgument.ByteStartIx));

            // clear argCount and command UNLESS we're in an error state
            argCountAndCommandRef &= (long)(int)inErrorState;

            // set byteStart and byteEnd if we're not in an error state, otherwise leave them unmodified
            var packedByteStartAndEnd = (long)(((ulong)(uint)bulkStringStart) | (((ulong)(uint)(expectedCrPosition + 2)) << 32));
            byteStartAndEndRef = (byteStartAndEndRef & (long)(int)inErrorState) | (packedByteStartAndEnd & ~(long)(int)inErrorState);

            // now update state

            // advance write target if we succeeded
            currentIntoRef = ref Unsafe.Add(ref currentIntoRef, (int)(~inErrorState & 4));
            rollbackWriteIntoRefCount += (int)(~inErrorState & 4);

            // note that we rollback currentCommandBufferIx if there's any error
            //
            // if we've read past the end of the command, we will _for sure_ be in error state
            //currentCommandBufferIx = (int)((inErrorState & oldCurrentCommandBufferIx) | (~inErrorState & currentCommandBufferIx));
            currentCommandRef = ref Unsafe.Subtract(ref currentCommandRef, (int)(inErrorState & (uint)(bulkStringLength + 2 + 1)));
            rollbackCommandRefCount -= (int)(inErrorState & (uint)(bulkStringLength + 2 + 1));

            // update the number of items read from the array, if appropriate
            var shouldUpdateRemainingItemCount = ~readAllItems & ~inErrorState;
            remainingItemsInArray -= (int)(shouldUpdateRemainingItemCount & 1);

            // but we rollback errors and out of data if we fully consumed the array
            inErrorState = ((readAllItems & oldInErrorState) | (~readAllItems & inErrorState));
            ranOutOfData = ((readAllItems & oldRanOutOfData) | (~readAllItems & ranOutOfData));

            // pun away scoped, we know this is safe
            return new(ref Unsafe.AsRef(ref currentCommandRef), ref Unsafe.AsRef(ref currentIntoRef));
        }

        /// <summary>
        /// <see cref="UnrollTakeCommandBulkString(ref byte, ref byte, int, ref byte, ref byte, ref int, ref byte, ref int, ref int, ref int, ref int, ref uint, ref uint)"/>, but manually inlined.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UpdatedPairs UnrollTakeCommandBulkString_ManualInline(
            ref byte commandBufferAllocatedEnd,
            ref byte commandBuffer,
            int commandBufferFilledBytes,
            ref byte digitsBitmapAllocatedEnd,
            ref byte digitsBitmap,
            ref int intoAsIntsAllocatedEnd,
            scoped ref byte currentCommandRef,
            scoped ref int currentIntoRef,
            ref int remainingItemsInArray,
            ref int rollbackCommandRefCount,
            ref int rollbackWriteIntoRefCount,
            ref uint inErrorState,
            ref uint ranOutOfData
        )
        {
            const int MinimumBulkStringSize = 1 + 1 + 2 + 2; // $0\r\n\r\n

            Debug.Assert(Unsafe.ByteOffset(ref commandBuffer, ref commandBufferAllocatedEnd) > 0, "Command buffer cannot be of 0 size");

            // if we've read everything, we need to just do nothing (including updating inErrorState and ranOutOfData)
            // but still run through everything unconditionally
            var readAllItems = (uint)~((0 - remainingItemsInArray) >> 31);  // remainingItemsInArray == 0
            var oldInErrorState = inErrorState;
            var oldRanOutOfData = ranOutOfData;

            // we need to prevent action now, so act like we're in an error state
            inErrorState |= readAllItems;

            // check that there's enough data to even fit a string
            var availableBytes = commandBufferFilledBytes - (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
            ranOutOfData |= (uint)((availableBytes - MinimumBulkStringSize) >> 31); // availableBytes < MinimumBulkStringSize
            inErrorState |= ranOutOfData;

            // check for $
            ref var bulkStringStartExpectedRef = ref currentCommandRef;

            Debug.Assert(Unsafe.IsAddressLessThan(ref bulkStringStartExpectedRef, ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
            inErrorState |= (uint)((-(bulkStringStartExpectedRef ^ BulkStringStart)) >> 31); // (ref commandBuffer + bulkStringStartExpectedIx) != BulkStringStart

            // move past $
            currentCommandRef = ref Unsafe.Add(ref currentCommandRef, 1);
            rollbackCommandRefCount++;

            // parse the length
            //currentCommandRef = ref TakePositiveNumberBranchless_ManualInline(ref commandBufferAllocatedEnd, ref commandBuffer, commandBufferFilledBytes, ref digitsBitmapAllocatedEnd, ref digitsBitmap, ref currentCommandRef, ref rollbackCommandRefCount, ref inErrorState, ref ranOutOfData, out var bulkStringLength);
            int bulkStringLength;
            {
                Debug.Assert(commandBufferFilledBytes > 0, "Cannot call with empty buffer");

                var commandBufferIx = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
                var startByteIndex = commandBufferIx / 8;
                var startBitIndex = commandBufferIx % 8;

                // we expect a run of digits followed by a \r\n, so digitBitmap should be 0b0...xxx1
                // a run of 11+ is invalid, considering a shift of up to 8 this will always fit in a uint32
                Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsBitmap, startByteIndex + sizeof(uint)), ref digitsBitmapAllocatedEnd), "About to read past end of digits bitmap");

                var digitsBitmapValue = Unsafe.As<byte, uint>(ref Unsafe.Add(ref digitsBitmap, startByteIndex)) >> startBitIndex;
                var digitCount = (uint)BitOperations.TrailingZeroCount(~digitsBitmapValue);

                // we expect no more than 10 digits (int.MaxValue is 2,147,483,647), and at least 1 digit
                inErrorState |= ((10 - digitCount) >> 31);  // digitCount >= 11

                // check for \r\n
                ref var expectedCrLfRef = ref Unsafe.Add(ref currentCommandRef, digitCount);
                var expectedCrPosition_inline = (int)Unsafe.ByteOffset(ref commandBuffer, ref expectedCrLfRef);
                ranOutOfData |= (uint)((commandBufferFilledBytes - (expectedCrPosition_inline + 2)) >> 31);    // expectedCrPosition + 2 > commandBufferFilledBytes
                inErrorState |= ranOutOfData;

                Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref expectedCrLfRef, sizeof(ushort)), ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
                inErrorState |= (uint)((0 - (Unsafe.As<byte, ushort>(ref expectedCrLfRef) ^ CRLF)) >> 31);    // expectedCrLfRef != CRLF;

                // if we're in an error state, we'll take an empty buffer
                // but we'll get a good one if we're not
                ref var digitsStartRef = ref currentCommandRef;
                digitsStartRef = ref Unsafe.Subtract(ref digitsStartRef, (int)(inErrorState & rollbackCommandRefCount));
                ref var digitsEndRef = ref expectedCrLfRef;
                digitsEndRef = ref Unsafe.Subtract(ref digitsEndRef, (int)(inErrorState & (rollbackCommandRefCount + digitCount)));

                // actually do the parsing
                // parsed = UnconditionalParsePositiveInt(ref commandBufferAllocatedEnd, ref digitsStartRef, ref digitsEndRef);
                {
                    Debug.Assert(Unsafe.IsAddressGreaterThan(ref digitsEndRef, ref digitsStartRef) || Unsafe.AreSame(ref digitsStartRef, ref digitsEndRef), "Incoherent digit end and start");
                    Debug.Assert(Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef) is >= 0 and < 11, "Should have validated correct size");
                    Debug.Assert(!MemoryMarshal.CreateSpan(ref digitsStartRef, (int)Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef)).ContainsAnyExceptInRange((byte)'0', (byte)'9'), "Should have validated digits only");

                    var length = (int)Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef);

                    var multLookupIx = length * 12;
                    ref var multStart = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(UnconditionalParsePositiveIntMultiples), multLookupIx);

                    // many numbers are going to fit in 4 digits
                    // 0\r\n* = len = 1 = 0x**_\n_\r_00
                    // 01\r\n = len = 2 = 0x\n_\r_11_00
                    // 012\r  = len = 3 = 0x\r_22_11_00
                    // 0123   = len = 4 = 0x33_22_11_00
                    if (length <= 4)
                    {
                        // load all the digits (and some extra junk, maybe)
                        Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsStartRef, sizeof(uint)), ref commandBufferAllocatedEnd), "About to read past end of command buffer");
                        var fastPathDigits = Unsafe.As<byte, uint>(ref digitsStartRef);

                        // reverse so we can pad more easily
                        // 0\r\n* = len = 1 = 0x**_\n_\r_00 => 0x00_\r_\n_**
                        // 01\r\n = len = 2 = 0x\n_\r_11_00 => 0x00_11_\r_\n
                        // 012\r  = len = 3 = 0x\r_22_11_00 => 0x00_11_22_\r
                        // 0123   = len = 4 = 0x33_22_11_00 => 0x00_11_22_33
                        fastPathDigits = BinaryPrimitives.ReverseEndianness(fastPathDigits); // this is an intrinsic, so should be a single op

                        // shift right to pad with zeros
                        // 0\r\n* = len = 1 = 0x**_\n_\r_00 => 0x00_\r_\n_** => 0x00_00_00_00 ; shift right 24 (32 - (8 * len))
                        // 01\r\n = len = 2 = 0x\n_\r_11_00 => 0x00_11_\r_\n => 0x00_00_00_11 ; shift right 16 (32 - (8 * len))
                        // 012\r  = len = 3 = 0x\r_22_11_00 => 0x00_11_22_\r => 0x00_00_11_22 ; shift right  8 (32 - (8 * len))
                        // 0123   = len = 4 = 0x33_22_11_00 => 0x00_11_22_33 => 0x00_11_22_33 ; shift right  0 (32 - (8 * len))
                        fastPathDigits = fastPathDigits >> (32 - (8 * length));

                        // fastPathDigits = 0x.4_.3__.2_.1
                        // onesAndHundreds = 0x00_03__00_01
                        var onesAndHundreds = fastPathDigits & ~0xFF_30_FF_30U;
                        // tensAndThousands = 0x04_00__02_00
                        var tensAndThousands = fastPathDigits & ~0x30_FF_30_FFU;

                        // mixedOnce = 0x(4 * 10 + 3)__(2 * 10 + 1) = 0d043__21
                        var mixedOnce = (tensAndThousands * 10U) + (onesAndHundreds << 8);

                        // topPair = 0d43__00
                        ulong topPair = mixedOnce & 0xFF_FF_00_00U;
                        // lowPair = 0d00__21
                        ulong lowPair = mixedOnce & 0x00_00_FF_FFU;

                        // mixedTwice = 0d(43 * 100 + 21)_00
                        var mixedTwice = (topPair * 100U) + (lowPair << 16);

                        var result = (int)(mixedTwice >> 24);

                        // leading zero check, force result to -1 if below expected value
                        result |= ((0 - (length ^ 1)) >> 31) & ((result - (int)multStart) >> 31);   // length != 1 & multStart > result;

                        bulkStringLength = result;
                    }
                    else
                    {

                        var maskLookupIx = length * 16;

                        ref var maskStart = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(UnconditionalParsePositiveIntLookup), maskLookupIx);

                        var mask = Vector128.LoadUnsafe(ref maskStart);

                        // load 16 bytes of data, which will have \r\n and some trailing garbage
                        Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsStartRef, Vector128<byte>.Count), ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
                        var data = Vector128.LoadUnsafe(ref digitsStartRef);

                        var toUseBytes = Vector128.BitwiseAnd(data, mask);                        // bytes 0-9 are (potentially) valid

                        // expand so we can multiply cleanly
                        var (d0To7, d8To15) = Vector128.Widen(toUseBytes);
                        var (d0To3, d4To7) = Vector128.Widen(d0To7);
                        var (d8To11, _) = Vector128.Widen(d8To15);

                        // load per digit multiples
                        var scale0To3 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 0));
                        var scale4To7 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 4));
                        var scale8To11 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 8));

                        // scale each digit by it's place value
                        // 
                        // at maximum length, m0To3[0] might overflow
                        var m0To3 = Vector128.Multiply(d0To3, scale0To3);
                        var m4To7 = Vector128.Multiply(d4To7, scale4To7);
                        var m8To11 = Vector128.Multiply(d8To11, scale8To11);

                        var maybeMultOverflow = Vector128.LessThan(m0To3, scale0To3);

                        // calculate the vertical sum
                        // 
                        // vertical sum cannot overflow (assuming m0To3 didn't overflow)
                        var verticalSum = Vector128.Add(Vector128.Add(m0To3, m4To7), m8To11);

                        // calculate the horizontal sum
                        //
                        // horizontal sum can overflow, even if m0To3 didn't overflow
                        var horizontalSum = Vector128.Dot(verticalSum, Vector128<uint>.One);

                        var maybeHorizontalOverflow = Vector128.GreaterThan(verticalSum, Vector128.CreateScalar(horizontalSum));

                        var maybeOverflow = Vector128.BitwiseOr(maybeMultOverflow, maybeHorizontalOverflow);

                        var res = horizontalSum;

                        // check for overflow
                        //
                        // note that overflow can only happen when length == 10
                        // only the first digit can overflow on it's own (if it's > 2)
                        // everything else has to overflow when summed
                        // 
                        // we know length is <= 10, we can exploit this in the == check
                        var allOnesIfOverflow = (uint)(((9 - length) >> 31) & ((0L - maybeOverflow.GetElement(0)) >> 63));  // maybeOverflow.GetElement(0) != 0 & length == 10

                        // check for leading zeros
                        var allOnesIfLeadingZeros = (uint)(((0 - (length ^ 1)) >> 31) & (((int)res - (int)multStart) >> 31)); // length != 1 & multStart > (int)res

                        // turn into -1 if things are invalid
                        res |= allOnesIfOverflow | allOnesIfLeadingZeros;

                        bulkStringLength = (int)res;
                    }
                }
                inErrorState |= (uint)(bulkStringLength >> 31);    // value < 0

                // update to point one past the \r\n if we're not in an error state
                // otherwise leave it unmodified
                currentCommandRef = ref Unsafe.Add(ref currentCommandRef, (int)((digitCount + 2) & ~inErrorState));
                rollbackCommandRefCount += (int)((digitCount + 2) & ~inErrorState);
            }

            var bulkStringStart = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
            var bulkStringEnd = bulkStringStart + bulkStringLength;

            // check for \r\n
            var expectedCrPosition = bulkStringEnd;
            ranOutOfData |= (uint)((commandBufferFilledBytes - (expectedCrPosition + 2)) >> 31);    // expectedCrPosition + 2 > commandBufferFilledBytes
            inErrorState |= ranOutOfData;

            expectedCrPosition &= (int)~inErrorState;
            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref commandBuffer, expectedCrPosition + sizeof(ushort)), ref commandBufferAllocatedEnd), "About to read past allocated command buffer");
            inErrorState |= (uint)((0 - (Unsafe.As<byte, ushort>(ref Unsafe.Add(ref commandBuffer, expectedCrPosition)) ^ CRLF)) >> 31);    // (ref commandBuffer + expectedCrPosition) != CRLF

            // move past the \r\n
            currentCommandRef = ref Unsafe.Add(ref currentCommandRef, bulkStringLength + 2);
            rollbackCommandRefCount += bulkStringLength + 2;

            // now we're ready to write out results

            // do we even have space to write something?
            ranOutOfData |= (uint)~(((int)-Unsafe.ByteOffset(ref currentIntoRef, ref intoAsIntsAllocatedEnd)) >> 31);    // writeNextResultInto == intoAsIntsSize
            inErrorState |= ranOutOfData;

            // write the results out
            //var effectiveInto = (int)(~inErrorState & writeNextResultInto);
            ref var effectiveInto = ref Unsafe.Subtract(ref currentIntoRef, inErrorState & 4);
            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref effectiveInto, ParsedRespCommandOrArgument.ByteEndIx), ref intoAsIntsAllocatedEnd), "About to read/write past end of intoAsInts");
            //ref var intoAsIntsEffectiveIntoRef = ref Unsafe.Add(ref intoAsInts, effectiveInto);
            ref var argCountAndCommandRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref effectiveInto, ParsedRespCommandOrArgument.ArgumentCountIx));
            ref var byteStartAndEndRef = ref Unsafe.As<int, long>(ref Unsafe.Add(ref effectiveInto, ParsedRespCommandOrArgument.ByteStartIx));

            // clear argCount and command UNLESS we're in an error state
            argCountAndCommandRef &= (long)(int)inErrorState;

            // set byteStart and byteEnd if we're not in an error state, otherwise leave them unmodified
            var packedByteStartAndEnd = (long)(((ulong)(uint)bulkStringStart) | (((ulong)(uint)(expectedCrPosition + 2)) << 32));
            byteStartAndEndRef = (byteStartAndEndRef & (long)(int)inErrorState) | (packedByteStartAndEnd & ~(long)(int)inErrorState);

            // now update state

            // advance write target if we succeeded
            currentIntoRef = ref Unsafe.Add(ref currentIntoRef, (int)(~inErrorState & 4));
            rollbackWriteIntoRefCount += (int)(~inErrorState & 4);

            // note that we rollback currentCommandBufferIx if there's any error
            //
            // if we've read past the end of the command, we will _for sure_ be in error state
            //currentCommandBufferIx = (int)((inErrorState & oldCurrentCommandBufferIx) | (~inErrorState & currentCommandBufferIx));
            currentCommandRef = ref Unsafe.Subtract(ref currentCommandRef, (int)(inErrorState & (uint)(bulkStringLength + 2 + 1)));
            rollbackCommandRefCount -= (int)(inErrorState & (uint)(bulkStringLength + 2 + 1));

            // update the number of items read from the array, if appropriate
            var shouldUpdateRemainingItemCount = ~readAllItems & ~inErrorState;
            remainingItemsInArray -= (int)(shouldUpdateRemainingItemCount & 1);

            // but we rollback errors and out of data if we fully consumed the array
            inErrorState = ((readAllItems & oldInErrorState) | (~readAllItems & inErrorState));
            ranOutOfData = ((readAllItems & oldRanOutOfData) | (~readAllItems & ranOutOfData));

            // pun away scoped, we know this is safe
            return new(ref Unsafe.AsRef(ref currentCommandRef), ref Unsafe.AsRef(ref currentIntoRef));
        }

        /// <summary>
        /// Take and parse a number starting at <paramref name="currentCommandBufferIx"/>.
        /// 
        /// Expects the number to be followed by \r\n.
        /// 
        /// If the number is too big (> 10 digits), missing, or not followed by \r\n then <paramref name="inErrorState"/> is set.
        /// If the \r or \n is missing and that position corresponds to the end of data, both <paramref name="inErrorState"/> and <paramref name="ranOutOfData"/> are set.
        /// 
        /// If <paramref name="inErrorState"/> is set when this method returns, <paramref name="currentCommandBufferIx"/> will not have changed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref byte TakePositiveNumberBranchless(
            ref byte commandBufferAllocatedEnd,
            ref byte commandBuffer,
            int commandBufferFilledBytes,
            ref byte digitsBitmapAllocatedEnd,
            ref byte digitsBitmap,
            ref byte currentCommandRef,
            ref int rollbackCommandRefCount,
            ref uint inErrorState,
            ref uint ranOutOfData,
            out int parsed
        )
        {
            Debug.Assert(commandBufferFilledBytes > 0, "Cannot call with empty buffer");

            var commandBufferIx = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
            var startByteIndex = commandBufferIx / 8;
            var startBitIndex = commandBufferIx % 8;

            // we expect a run of digits followed by a \r\n, so digitBitmap should be 0b0...xxx1
            // a run of 11+ is invalid, considering a shift of up to 8 this will always fit in a uint32
            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsBitmap, startByteIndex + sizeof(uint)), ref digitsBitmapAllocatedEnd), "About to read past end of digits bitmap");

            var digitsBitmapValue = Unsafe.As<byte, uint>(ref Unsafe.Add(ref digitsBitmap, startByteIndex)) >> startBitIndex;
            var digitCount = (uint)BitOperations.TrailingZeroCount(~digitsBitmapValue);

            // we expect no more than 10 digits (int.MaxValue is 2,147,483,647), and at least 1 digit
            inErrorState |= ((10 - digitCount) >> 31);  // digitCount >= 11

            // check for \r\n
            ref var expectedCrLfRef = ref Unsafe.Add(ref currentCommandRef, digitCount);
            var expectedCrPosition = (int)Unsafe.ByteOffset(ref commandBuffer, ref expectedCrLfRef);
            ranOutOfData |= (uint)((commandBufferFilledBytes - (expectedCrPosition + 2)) >> 31);    // expectedCrPosition + 2 > commandBufferFilledBytes
            inErrorState |= ranOutOfData;

            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref expectedCrLfRef, sizeof(ushort)), ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
            inErrorState |= (uint)((0 - (Unsafe.As<byte, ushort>(ref expectedCrLfRef) ^ CRLF)) >> 31);    // expectedCrLfRef != CRLF;

            // if we're in an error state, we'll take an empty buffer
            // but we'll get a good one if we're not
            ref var digitsStartRef = ref currentCommandRef;
            digitsStartRef = ref Unsafe.Subtract(ref digitsStartRef, (int)(inErrorState & rollbackCommandRefCount));
            ref var digitsEndRef = ref expectedCrLfRef;
            digitsEndRef = ref Unsafe.Subtract(ref digitsEndRef, (int)(inErrorState & (rollbackCommandRefCount + digitCount)));

            // actually do the parsing
            parsed = UnconditionalParsePositiveInt(ref commandBufferAllocatedEnd, ref digitsStartRef, ref digitsEndRef);
            inErrorState |= (uint)(parsed >> 31);    // value < 0

            // update to point one past the \r\n if we're not in an error state
            // otherwise leave it unmodified
            currentCommandRef = ref Unsafe.Add(ref currentCommandRef, (int)((digitCount + 2) & ~inErrorState));
            rollbackCommandRefCount += (int)((digitCount + 2) & ~inErrorState);

            return ref currentCommandRef;
        }

        /// <summary>
        /// <see cref="TakePositiveNumberBranchless(ref byte, ref byte, int, ref byte, ref byte, ref byte, ref int, ref uint, ref uint, out int)"/>, but callees inlined.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref byte TakePositiveNumberBranchless_ManualInline(
            ref byte commandBufferAllocatedEnd,
            ref byte commandBuffer,
            int commandBufferFilledBytes,
            ref byte digitsBitmapAllocatedEnd,
            ref byte digitsBitmap,
            ref byte currentCommandRef,
            ref int rollbackCommandRefCount,
            ref uint inErrorState,
            ref uint ranOutOfData,
            out int parsed
        )
        {
            Debug.Assert(commandBufferFilledBytes > 0, "Cannot call with empty buffer");

            var commandBufferIx = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
            var startByteIndex = commandBufferIx / 8;
            var startBitIndex = commandBufferIx % 8;

            // we expect a run of digits followed by a \r\n, so digitBitmap should be 0b0...xxx1
            // a run of 11+ is invalid, considering a shift of up to 8 this will always fit in a uint32
            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsBitmap, startByteIndex + sizeof(uint)), ref digitsBitmapAllocatedEnd), "About to read past end of digits bitmap");

            var digitsBitmapValue = Unsafe.As<byte, uint>(ref Unsafe.Add(ref digitsBitmap, startByteIndex)) >> startBitIndex;
            var digitCount = (uint)BitOperations.TrailingZeroCount(~digitsBitmapValue);

            // we expect no more than 10 digits (int.MaxValue is 2,147,483,647), and at least 1 digit
            inErrorState |= ((10 - digitCount) >> 31);  // digitCount >= 11

            // check for \r\n
            ref var expectedCrLfRef = ref Unsafe.Add(ref currentCommandRef, digitCount);
            var expectedCrPosition = (int)Unsafe.ByteOffset(ref commandBuffer, ref expectedCrLfRef);
            ranOutOfData |= (uint)((commandBufferFilledBytes - (expectedCrPosition + 2)) >> 31);    // expectedCrPosition + 2 > commandBufferFilledBytes
            inErrorState |= ranOutOfData;

            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref expectedCrLfRef, sizeof(ushort)), ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
            inErrorState |= (uint)((0 - (Unsafe.As<byte, ushort>(ref expectedCrLfRef) ^ CRLF)) >> 31);    // expectedCrLfRef != CRLF;

            // if we're in an error state, we'll take an empty buffer
            // but we'll get a good one if we're not
            ref var digitsStartRef = ref currentCommandRef;
            digitsStartRef = ref Unsafe.Subtract(ref digitsStartRef, (int)(inErrorState & rollbackCommandRefCount));
            ref var digitsEndRef = ref expectedCrLfRef;
            digitsEndRef = ref Unsafe.Subtract(ref digitsEndRef, (int)(inErrorState & (rollbackCommandRefCount + digitCount)));

            // actually do the parsing
            // parsed = UnconditionalParsePositiveInt(ref commandBufferAllocatedEnd, ref digitsStartRef, ref digitsEndRef);
            {
                Debug.Assert(Unsafe.IsAddressGreaterThan(ref digitsEndRef, ref digitsStartRef) || Unsafe.AreSame(ref digitsStartRef, ref digitsEndRef), "Incoherent digit end and start");
                Debug.Assert(Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef) is >= 0 and < 11, "Should have validated correct size");
                Debug.Assert(!MemoryMarshal.CreateSpan(ref digitsStartRef, (int)Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef)).ContainsAnyExceptInRange((byte)'0', (byte)'9'), "Should have validated digits only");

                var length = (int)Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef);

                var multLookupIx = length * 12;
                ref var multStart = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(UnconditionalParsePositiveIntMultiples), multLookupIx);

                // many numbers are going to fit in 4 digits
                // 0\r\n* = len = 1 = 0x**_\n_\r_00
                // 01\r\n = len = 2 = 0x\n_\r_11_00
                // 012\r  = len = 3 = 0x\r_22_11_00
                // 0123   = len = 4 = 0x33_22_11_00
                if (length <= 4)
                {
                    // load all the digits (and some extra junk, maybe)
                    Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsStartRef, sizeof(uint)), ref commandBufferAllocatedEnd), "About to read past end of command buffer");
                    var fastPathDigits = Unsafe.As<byte, uint>(ref digitsStartRef);

                    // reverse so we can pad more easily
                    // 0\r\n* = len = 1 = 0x**_\n_\r_00 => 0x00_\r_\n_**
                    // 01\r\n = len = 2 = 0x\n_\r_11_00 => 0x00_11_\r_\n
                    // 012\r  = len = 3 = 0x\r_22_11_00 => 0x00_11_22_\r
                    // 0123   = len = 4 = 0x33_22_11_00 => 0x00_11_22_33
                    fastPathDigits = BinaryPrimitives.ReverseEndianness(fastPathDigits); // this is an intrinsic, so should be a single op

                    // shift right to pad with zeros
                    // 0\r\n* = len = 1 = 0x**_\n_\r_00 => 0x00_\r_\n_** => 0x00_00_00_00 ; shift right 24 (32 - (8 * len))
                    // 01\r\n = len = 2 = 0x\n_\r_11_00 => 0x00_11_\r_\n => 0x00_00_00_11 ; shift right 16 (32 - (8 * len))
                    // 012\r  = len = 3 = 0x\r_22_11_00 => 0x00_11_22_\r => 0x00_00_11_22 ; shift right  8 (32 - (8 * len))
                    // 0123   = len = 4 = 0x33_22_11_00 => 0x00_11_22_33 => 0x00_11_22_33 ; shift right  0 (32 - (8 * len))
                    fastPathDigits = fastPathDigits >> (32 - (8 * length));

                    // fastPathDigits = 0x.4_.3__.2_.1
                    // onesAndHundreds = 0x00_03__00_01
                    var onesAndHundreds = fastPathDigits & ~0xFF_30_FF_30U;
                    // tensAndThousands = 0x04_00__02_00
                    var tensAndThousands = fastPathDigits & ~0x30_FF_30_FFU;

                    // mixedOnce = 0x(4 * 10 + 3)__(2 * 10 + 1) = 0d043__21
                    var mixedOnce = (tensAndThousands * 10U) + (onesAndHundreds << 8);

                    // topPair = 0d43__00
                    ulong topPair = mixedOnce & 0xFF_FF_00_00U;
                    // lowPair = 0d00__21
                    ulong lowPair = mixedOnce & 0x00_00_FF_FFU;

                    // mixedTwice = 0d(43 * 100 + 21)_00
                    var mixedTwice = (topPair * 100U) + (lowPair << 16);

                    var result = (int)(mixedTwice >> 24);

                    // leading zero check, force result to -1 if below expected value
                    result |= ((0 - (length ^ 1)) >> 31) & ((result - (int)multStart) >> 31);   // length != 1 & multStart > result;

                    parsed = result;
                }
                else
                {

                    var maskLookupIx = length * 16;

                    ref var maskStart = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(UnconditionalParsePositiveIntLookup), maskLookupIx);

                    var mask = Vector128.LoadUnsafe(ref maskStart);

                    // load 16 bytes of data, which will have \r\n and some trailing garbage
                    Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsStartRef, Vector128<byte>.Count), ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
                    var data = Vector128.LoadUnsafe(ref digitsStartRef);

                    var toUseBytes = Vector128.BitwiseAnd(data, mask);                        // bytes 0-9 are (potentially) valid

                    // expand so we can multiply cleanly
                    var (d0To7, d8To15) = Vector128.Widen(toUseBytes);
                    var (d0To3, d4To7) = Vector128.Widen(d0To7);
                    var (d8To11, _) = Vector128.Widen(d8To15);

                    // load per digit multiples
                    var scale0To3 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 0));
                    var scale4To7 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 4));
                    var scale8To11 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 8));

                    // scale each digit by it's place value
                    // 
                    // at maximum length, m0To3[0] might overflow
                    var m0To3 = Vector128.Multiply(d0To3, scale0To3);
                    var m4To7 = Vector128.Multiply(d4To7, scale4To7);
                    var m8To11 = Vector128.Multiply(d8To11, scale8To11);

                    var maybeMultOverflow = Vector128.LessThan(m0To3, scale0To3);

                    // calculate the vertical sum
                    // 
                    // vertical sum cannot overflow (assuming m0To3 didn't overflow)
                    var verticalSum = Vector128.Add(Vector128.Add(m0To3, m4To7), m8To11);

                    // calculate the horizontal sum
                    //
                    // horizontal sum can overflow, even if m0To3 didn't overflow
                    var horizontalSum = Vector128.Dot(verticalSum, Vector128<uint>.One);

                    var maybeHorizontalOverflow = Vector128.GreaterThan(verticalSum, Vector128.CreateScalar(horizontalSum));

                    var maybeOverflow = Vector128.BitwiseOr(maybeMultOverflow, maybeHorizontalOverflow);

                    var res = horizontalSum;

                    // check for overflow
                    //
                    // note that overflow can only happen when length == 10
                    // only the first digit can overflow on it's own (if it's > 2)
                    // everything else has to overflow when summed
                    // 
                    // we know length is <= 10, we can exploit this in the == check
                    var allOnesIfOverflow = (uint)(((9 - length) >> 31) & ((0L - maybeOverflow.GetElement(0)) >> 63));  // maybeOverflow.GetElement(0) != 0 & length == 10

                    // check for leading zeros
                    var allOnesIfLeadingZeros = (uint)(((0 - (length ^ 1)) >> 31) & (((int)res - (int)multStart) >> 31)); // length != 1 & multStart > (int)res

                    // turn into -1 if things are invalid
                    res |= allOnesIfOverflow | allOnesIfLeadingZeros;

                    parsed = (int)res;
                }
            }
            inErrorState |= (uint)(parsed >> 31);    // value < 0

            // update to point one past the \r\n if we're not in an error state
            // otherwise leave it unmodified
            currentCommandRef = ref Unsafe.Add(ref currentCommandRef, (int)((digitCount + 2) & ~inErrorState));
            rollbackCommandRefCount += (int)((digitCount + 2) & ~inErrorState);

            return ref currentCommandRef;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool MaskBoolToBool(uint value)
        {
            Debug.Assert(value is TRUE or FALSE, "Unexpected mask boolean!");

            return value == TRUE;
        }

        private static readonly byte[] UnconditionalParsePositiveIntLookup = [
            0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, // len = 0 (malformed)
            0xF, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, // len = 1 
            0xF, 0xF, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, // len = 2 
            0xF, 0xF, 0xF, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, // len = 3 
            0xF, 0xF, 0xF, 0xF, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, // len = 4 
            0xF, 0xF, 0xF, 0xF, 0xF, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, // len = 5 
            0xF, 0xF, 0xF, 0xF, 0xF, 0xF, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, // len = 6 
            0xF, 0xF, 0xF, 0xF, 0xF, 0xF, 0xF, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, // len = 7
            0xF, 0xF, 0xF, 0xF, 0xF, 0xF, 0xF, 0xF, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, // len = 8 
            0xF, 0xF, 0xF, 0xF, 0xF, 0xF, 0xF, 0xF, 0xF, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, // len = 9 
            0xF, 0xF, 0xF, 0xF, 0xF, 0xF, 0xF, 0xF, 0xF, 0xF, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, // len = 10
        ];

        private static readonly uint[] UnconditionalParsePositiveIntMultiples =
            [
                // len = 0 (malformed)
                100_000, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 0,

                // len = 1
                1, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 0,

                // len = 2
                10, 1, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 0,

                // len = 3
                100, 10, 1, 0,
                0, 0, 0, 0,
                0, 0, 0, 0,

                // len = 4
                1_000, 100, 10, 1,
                0, 0, 0, 0,
                0, 0, 0, 0,

                // len = 5
                10_000, 1_000, 100, 10,
                1, 0, 0, 0,
                0, 0, 0, 0,

                // len = 6
                100_000, 10_000, 1_000, 100,
                10, 1, 0, 0,
                0, 0, 0, 0,

                // len = 7
                1_000_000, 100_000, 10_000, 1_000,
                100, 10, 1, 0,
                0, 0, 0, 0,

                // len = 8
                10_000_000, 1_000_000, 100_000, 10_000,
                1_000, 100, 10, 1,
                0, 0, 0, 0,

                // len = 9
                100_000_000, 10_000_000, 1_000_000, 100_000,
                10_000, 1_000, 100, 10,
                1, 0, 0, 0,

                // len = 10
                1_000_000_000, 100_000_000, 10_000_000, 1_000_000,
                100_000, 10_000, 1_000, 100,
                10, 1, 0, 0,
            ];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int UnconditionalParsePositiveInt(
            ref byte commandBufferAllocatedEnd,
            ref byte digitsStartRef,
            ref byte digitsEndRef
        )
        {
            // we know length <= 10, and we've padded UnconditionalParsePositiveIntLookup

            Debug.Assert(Unsafe.IsAddressGreaterThan(ref digitsEndRef, ref digitsStartRef) || Unsafe.AreSame(ref digitsStartRef, ref digitsEndRef), "Incoherent digit end and start");
            Debug.Assert(Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef) is >= 0 and < 11, "Should have validated correct size");
            Debug.Assert(!MemoryMarshal.CreateSpan(ref digitsStartRef, (int)Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef)).ContainsAnyExceptInRange((byte)'0', (byte)'9'), "Should have validated digits only");

            var length = (int)Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef);

            var multLookupIx = length * 12;
            ref var multStart = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(UnconditionalParsePositiveIntMultiples), multLookupIx);

            // many numbers are going to fit in 4 digits
            // 0\r\n* = len = 1 = 0x**_\n_\r_00
            // 01\r\n = len = 2 = 0x\n_\r_11_00
            // 012\r  = len = 3 = 0x\r_22_11_00
            // 0123   = len = 4 = 0x33_22_11_00
            if (length <= 4)
            {
                // load all the digits (and some extra junk, maybe)
                Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsStartRef, sizeof(uint)), ref commandBufferAllocatedEnd), "About to read past end of command buffer");
                var fastPathDigits = Unsafe.As<byte, uint>(ref digitsStartRef);

                // reverse so we can pad more easily
                // 0\r\n* = len = 1 = 0x**_\n_\r_00 => 0x00_\r_\n_**
                // 01\r\n = len = 2 = 0x\n_\r_11_00 => 0x00_11_\r_\n
                // 012\r  = len = 3 = 0x\r_22_11_00 => 0x00_11_22_\r
                // 0123   = len = 4 = 0x33_22_11_00 => 0x00_11_22_33
                fastPathDigits = BinaryPrimitives.ReverseEndianness(fastPathDigits); // this is an intrinsic, so should be a single op

                // shift right to pad with zeros
                // 0\r\n* = len = 1 = 0x**_\n_\r_00 => 0x00_\r_\n_** => 0x00_00_00_00 ; shift right 24 (32 - (8 * len))
                // 01\r\n = len = 2 = 0x\n_\r_11_00 => 0x00_11_\r_\n => 0x00_00_00_11 ; shift right 16 (32 - (8 * len))
                // 012\r  = len = 3 = 0x\r_22_11_00 => 0x00_11_22_\r => 0x00_00_11_22 ; shift right  8 (32 - (8 * len))
                // 0123   = len = 4 = 0x33_22_11_00 => 0x00_11_22_33 => 0x00_11_22_33 ; shift right  0 (32 - (8 * len))
                fastPathDigits = fastPathDigits >> (32 - (8 * length));

                // fastPathDigits = 0x.4_.3__.2_.1
                // onesAndHundreds = 0x00_03__00_01
                var onesAndHundreds = fastPathDigits & ~0xFF_30_FF_30U;
                // tensAndThousands = 0x04_00__02_00
                var tensAndThousands = fastPathDigits & ~0x30_FF_30_FFU;

                // mixedOnce = 0x(4 * 10 + 3)__(2 * 10 + 1) = 0d043__21
                var mixedOnce = (tensAndThousands * 10U) + (onesAndHundreds << 8);

                // topPair = 0d43__00
                ulong topPair = mixedOnce & 0xFF_FF_00_00U;
                // lowPair = 0d00__21
                ulong lowPair = mixedOnce & 0x00_00_FF_FFU;

                // mixedTwice = 0d(43 * 100 + 21)_00
                var mixedTwice = (topPair * 100U) + (lowPair << 16);

                var result = (int)(mixedTwice >> 24);

                // leading zero check, force result to -1 if below expected value
                result |= ((0 - (length ^ 1)) >> 31) & ((result - (int)multStart) >> 31);   // length != 1 & multStart > result;

                return result;
            }

            var maskLookupIx = length * 16;

            ref var maskStart = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(UnconditionalParsePositiveIntLookup), maskLookupIx);

            var mask = Vector128.LoadUnsafe(ref maskStart);

            // load 16 bytes of data, which will have \r\n and some trailing garbage
            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsStartRef, Vector128<byte>.Count), ref commandBufferAllocatedEnd), "About to read past end of allocated command buffer");
            var data = Vector128.LoadUnsafe(ref digitsStartRef);

            var toUseBytes = Vector128.BitwiseAnd(data, mask);                        // bytes 0-9 are (potentially) valid

            // expand so we can multiply cleanly
            var (d0To7, d8To15) = Vector128.Widen(toUseBytes);
            var (d0To3, d4To7) = Vector128.Widen(d0To7);
            var (d8To11, _) = Vector128.Widen(d8To15);

            // load per digit multiples
            var scale0To3 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 0));
            var scale4To7 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 4));
            var scale8To11 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 8));

            // scale each digit by it's place value
            // 
            // at maximum length, m0To3[0] might overflow
            var m0To3 = Vector128.Multiply(d0To3, scale0To3);
            var m4To7 = Vector128.Multiply(d4To7, scale4To7);
            var m8To11 = Vector128.Multiply(d8To11, scale8To11);

            var maybeMultOverflow = Vector128.LessThan(m0To3, scale0To3);

            // calculate the vertical sum
            // 
            // vertical sum cannot overflow (assuming m0To3 didn't overflow)
            var verticalSum = Vector128.Add(Vector128.Add(m0To3, m4To7), m8To11);

            // calculate the horizontal sum
            //
            // horizontal sum can overflow, even if m0To3 didn't overflow
            var horizontalSum = Vector128.Dot(verticalSum, Vector128<uint>.One);

            var maybeHorizontalOverflow = Vector128.GreaterThan(verticalSum, Vector128.CreateScalar(horizontalSum));

            var maybeOverflow = Vector128.BitwiseOr(maybeMultOverflow, maybeHorizontalOverflow);

            var res = horizontalSum;

            // check for overflow
            //
            // note that overflow can only happen when length == 10
            // only the first digit can overflow on it's own (if it's > 2)
            // everything else has to overflow when summed
            // 
            // we know length is <= 10, we can exploit this in the == check
            var allOnesIfOverflow = (uint)(((9 - length) >> 31) & ((0L - maybeOverflow.GetElement(0)) >> 63));  // maybeOverflow.GetElement(0) != 0 & length == 10

            // check for leading zeros
            var allOnesIfLeadingZeros = (uint)(((0 - (length ^ 1)) >> 31) & (((int)res - (int)multStart) >> 31)); // length != 1 & multStart > (int)res

            // turn into -1 if things are invalid
            res |= allOnesIfOverflow | allOnesIfLeadingZeros;

            return (int)res;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ScanForSigils(
            ref byte commandBufferEnd,
            ref byte commandBuffer,
            ref byte digitsBitmapEnd,
            ref byte digitsBitmap,
            int commandBufferFilledBytes
        )
        {
            Debug.Assert(commandBufferFilledBytes > 0, "Do not call with empty buffer");

            var zeros = Vector512.Create(ZERO);
            var nines = Vector512.Create(NINE);

            ref var curCmd = ref commandBuffer;
            ref var cmdEnd = ref Unsafe.Add(ref commandBuffer, commandBufferFilledBytes);

            ref var curBitmap = ref digitsBitmap;

            // we always go at least one round, so elide a comparison
            do
            {
                // read in chunk of characters, going past the new stuff (or into padding) is handled fine later
                Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref curCmd, Vector512<byte>.Count), ref commandBufferEnd), "About to read past end of allocated command buffer");
                var data = Vector512.LoadUnsafe(ref curCmd);

                // determine where the [0-9] characters are
                var d0 = Vector512.GreaterThanOrEqual(data, zeros);
                var d1 = Vector512.LessThanOrEqual(data, nines);

                var digits = Vector512.BitwiseAnd(d0, d1);
                var digitsPacked = Vector512.ExtractMostSignificantBits(digits);

                // write digits bitmap, which may go into padding which is fine
                Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref curBitmap, sizeof(ulong)), ref digitsBitmapEnd), "About to write past end of allocated digit bitmap");
                Unsafe.As<byte, ulong>(ref curBitmap) = digitsPacked;

                // advance!
                curCmd = ref Unsafe.Add(ref curCmd, Vector512<byte>.Count);
                curBitmap = ref Unsafe.Add(ref curBitmap, sizeof(ulong));
            } while (Unsafe.IsAddressLessThan(ref curCmd, ref cmdEnd));
        }
    }
}
