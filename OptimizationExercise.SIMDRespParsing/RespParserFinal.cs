using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace OptimizationExercise.SIMDRespParsing
{
    /// <summary>
    /// Version of <see cref="RespParserV3"/> but fit for publication and discussion.
    /// </summary>
    public static class RespParserFinal
    {
        private const uint FALSE = 0U;
        private const uint TRUE = ~FALSE;

        private const byte ArrayStart = (byte)'*';
        private const byte BulkStringStart = (byte)'$';
        private const byte ZERO = (byte)'0';
        private const byte NINE = (byte)'9';

        private const ushort CRLF = (('\r' << 0) | ('\n' << 8)); // Little-endian, so LF is the high byte


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

            // Scans commandBufferTotalSpan[..commandBufferFilledBytes] for digits
            // Stores bitmap of digits into bitmapScratchBuffer
            // Potentially reads past the end of commandBufferTotalSpan by 64 bytes, extra padding in commandBufferAllocatedSize makes this safe
            {
                Debug.Assert(commandBufferFilledBytes > 0, "Do not call with empty buffer");

                var zeros = Vector512.Create(ZERO);
                var nines = Vector512.Create(NINE);

                ref var cmdEnd = ref Unsafe.Add(ref commandBuffer, commandBufferFilledBytes);

                ref var curBitmap = ref digitsBitmap;

                // We always go at least one round, so elide a comparison
                do
                {
                    Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref commandBuffer, Vector512<byte>.Count), ref commandBufferEnd), "About to read past end of allocated command buffer");
                    var data = Vector512.LoadUnsafe(ref commandBuffer);

                    var d0 = Vector512.GreaterThanOrEqual(data, zeros);
                    var d1 = Vector512.LessThanOrEqual(data, nines);

                    var digits = Vector512.BitwiseAnd(d0, d1);
                    var digitsPacked = Vector512.ExtractMostSignificantBits(digits);

                    Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref curBitmap, sizeof(ulong)), ref digitsBitmapEnd), "About to write past end of allocated digit bitmap");
                    Unsafe.As<byte, ulong>(ref curBitmap) = digitsPacked;

                    commandBuffer = ref Unsafe.Add(ref commandBuffer, Vector512<byte>.Count);
                    curBitmap = ref Unsafe.Add(ref curBitmap, sizeof(ulong));
                } while (Unsafe.IsAddressLessThan(ref commandBuffer, ref cmdEnd));
            }

            commandBuffer = ref MemoryMarshal.GetReference(commandBufferTotalSpan);

            ref var intoCommandsStartRef = ref MemoryMarshal.GetReference(intoCommandsSpan);
            var intoSize = intoCommandsSpan.Length;
            // Parse actual requests
            // Results are store in intoCommandsSpan
            {
                Debug.Assert(commandBufferAllocatedSize > 0, "Must have some data to parse");
                Debug.Assert(intoSize > 0, "Into cannot be empty, and must have enough space for at least one full entry");

                // Track if we're "done" (ie in an error state)
                // and if we are "done" if that happened because we ran out of data (in either the command buffer or intoCommandsSpan)
                //
                // Running out of data implies the command stream is not malformed
                var inErrorState = FALSE;
                var ranOutOfData = FALSE;

                ref var intoAsInts = ref Unsafe.As<ParsedRespCommandOrArgument, int>(ref intoCommandsStartRef);
                ref var intoAsIntsAllocatedEnd = ref Unsafe.Add(ref intoAsInts, intoSize * 4);

                scoped ref var currentCommandRef = ref commandBuffer;

                scoped ref var currentIntoRef = ref intoAsInts;

                // Loop for each request (command + arguments)
                do
                {
                    {
                        const int MinimumCommandSize = 1 + 1 + 2;
                        const uint CommonArrayLengthSuffix = 0x240A_0D31;  // $\n\r1
                        const uint CommonCommandLengthPrefix = 0x0A0D_3024; // \n\r0$

                        Debug.Assert(commandBufferFilledBytes > 0, "Command buffer should have data in it");

                        // Track how many bytes we need to rollback the command buffer ref
                        var rollbackCommandRefCount = 0;
                        // Track how many ints we need to rollback the into ref
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

                        // Ultra fast check for array length
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
                            // Slower check for arrya length
                            {
                                Debug.Assert(commandBufferFilledBytes > 0, "Cannot call with empty buffer");

                                var commandBufferIx = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
                                var startByteIndex = commandBufferIx / 8;
                                var startBitIndex = commandBufferIx % 8;

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

                                ref var digitsStartRef = ref currentCommandRef;
                                digitsStartRef = ref Unsafe.Subtract(ref digitsStartRef, (int)(inErrorState & rollbackCommandRefCount));
                                ref var digitsEndRef = ref expectedCrLfRef;
                                digitsEndRef = ref Unsafe.Subtract(ref digitsEndRef, (int)(inErrorState & (rollbackCommandRefCount + digitCount)));

                                // Having found the bounds of the number, parse it
                                {
                                    Debug.Assert(Unsafe.IsAddressGreaterThan(ref digitsEndRef, ref digitsStartRef) || Unsafe.AreSame(ref digitsStartRef, ref digitsEndRef), "Incoherent digit end and start");
                                    Debug.Assert(Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef) is >= 0 and < 11, "Should have validated correct size");
                                    Debug.Assert(!MemoryMarshal.CreateSpan(ref digitsStartRef, (int)Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef)).ContainsAnyExceptInRange((byte)'0', (byte)'9'), "Should have validated digits only");

                                    var length = (int)Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef);

                                    var multLookupIx = length * 12;
                                    ref var multStart = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(UnconditionalParsePositiveIntMultiples), multLookupIx);

                                    // Fast-ish approach for numbers <= 9,999
                                    if (length <= 4)
                                    {
                                        // load all the digits (and some extra junk, maybe)
                                        Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsStartRef, sizeof(uint)), ref commandBufferEnd), "About to read past end of command buffer");
                                        var fastPathDigits = Unsafe.As<byte, uint>(ref digitsStartRef);

                                        // reverse so we can pad more easily
                                        fastPathDigits = BinaryPrimitives.ReverseEndianness(fastPathDigits); // this is an intrinsic, so should be a single op

                                        // shift right to pad with zeros
                                        fastPathDigits = fastPathDigits >> (32 - (8 * length));

                                        var onesAndHundreds = fastPathDigits & ~0xFF_30_FF_30U;
                                        var tensAndThousands = fastPathDigits & ~0x30_FF_30_FFU;

                                        var mixedOnce = (tensAndThousands * 10U) + (onesAndHundreds << 8);

                                        ulong topPair = mixedOnce & 0xFF_FF_00_00U;
                                        ulong lowPair = mixedOnce & 0x00_00_FF_FFU;

                                        var mixedTwice = (topPair * 100U) + (lowPair << 16);

                                        var result = (int)(mixedTwice >> 24);

                                        // leading zero check, force result to -1 if below expected value
                                        result |= ((-(length ^ 1)) >> 31) & ((result - (int)multStart) >> 31);

                                        arrayLength = result;
                                    }
                                    else
                                    {
                                        // Slow path for numbers >= 10,000, uses SIMD and lookup tables to remain fast and branchless

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
                                currentCommandRef = ref Unsafe.Add(ref currentCommandRef, (int)((digitCount + 2) & ~inErrorState));
                                rollbackCommandRefCount += (int)((digitCount + 2) & ~inErrorState);
                            }

                            inErrorState |= (uint)((arrayLength - 1) >> 31);    // arrayLength < 1
                        }

                        var remainingArrayItems = arrayLength;

                        // first string will be a command, so we can do a smarter length check MOST of the time
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

                        // Loop for handling remaining strings
                        // 
                        // This always executes at least once, but remainingArrayItems == 0 will prevent
                        // it from taking effect for the (rare) commands that take no arguments
                        do
                        {
                            // Read one string
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

                                // Parse the length again, this is a duplicate of the array length logic w/o the ultra fast path for lengths <=9
                                //
                                // Most values are not going to be that small, unlike most arrays
                                int bulkStringLength;
                                {
                                    Debug.Assert(commandBufferFilledBytes > 0, "Cannot call with empty buffer");

                                    var commandBufferIx = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
                                    var startByteIndex = commandBufferIx / 8;
                                    var startBitIndex = commandBufferIx % 8;

                                    Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsBitmap, startByteIndex + sizeof(uint)), ref digitsBitmapEnd), "About to read past end of digits bitmap");

                                    var digitsBitmapValue = Unsafe.As<byte, uint>(ref Unsafe.Add(ref digitsBitmap, startByteIndex)) >> startBitIndex;
                                    var digitCount = (uint)BitOperations.TrailingZeroCount(~digitsBitmapValue);

                                    inErrorState |= ((10 - digitCount) >> 31);  // digitCount >= 11

                                    ref var expectedCrLfRef = ref Unsafe.Add(ref currentCommandRef, digitCount);
                                    var expectedCrPosition_inline = (int)Unsafe.ByteOffset(ref commandBuffer, ref expectedCrLfRef);
                                    ranOutOfData |= (uint)((commandBufferFilledBytes - (expectedCrPosition_inline + 2)) >> 31);    // expectedCrPosition + 2 > commandBufferFilledBytes
                                    inErrorState |= ranOutOfData;

                                    Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref expectedCrLfRef, sizeof(ushort)), ref commandBufferEnd), "About to read past end of allocated command buffer");
                                    inErrorState |= (uint)((-(Unsafe.As<byte, ushort>(ref expectedCrLfRef) ^ CRLF)) >> 31);    // expectedCrLfRef != CRLF;

                                    ref var digitsStartRef = ref currentCommandRef;
                                    digitsStartRef = ref Unsafe.Subtract(ref digitsStartRef, (int)(inErrorState & rollbackCommandRefCount));
                                    ref var digitsEndRef = ref expectedCrLfRef;
                                    digitsEndRef = ref Unsafe.Subtract(ref digitsEndRef, (int)(inErrorState & (rollbackCommandRefCount + digitCount)));

                                    // actually do the parsing
                                    {
                                        Debug.Assert(Unsafe.IsAddressGreaterThan(ref digitsEndRef, ref digitsStartRef) || Unsafe.AreSame(ref digitsStartRef, ref digitsEndRef), "Incoherent digit end and start");
                                        Debug.Assert(Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef) is >= 0 and < 11, "Should have validated correct size");
                                        Debug.Assert(!MemoryMarshal.CreateSpan(ref digitsStartRef, (int)Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef)).ContainsAnyExceptInRange((byte)'0', (byte)'9'), "Should have validated digits only");

                                        var length = (int)Unsafe.ByteOffset(ref digitsStartRef, ref digitsEndRef);

                                        var multLookupIx = length * 12;
                                        ref var multStart = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(UnconditionalParsePositiveIntMultiples), multLookupIx);

                                        if (length <= 4)
                                        {
                                            // load all the digits (and some extra junk, maybe)
                                            Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref digitsStartRef, sizeof(uint)), ref commandBufferEnd), "About to read past end of command buffer");
                                            var fastPathDigits = Unsafe.As<byte, uint>(ref digitsStartRef);

                                            // reverse so we can pad more easily
                                            fastPathDigits = BinaryPrimitives.ReverseEndianness(fastPathDigits); // this is an intrinsic, so should be a single op

                                            // shift right to pad with zeros
                                            fastPathDigits = fastPathDigits >> (32 - (8 * length));

                                            var onesAndHundreds = fastPathDigits & ~0xFF_30_FF_30U;
                                            var tensAndThousands = fastPathDigits & ~0x30_FF_30_FFU;

                                            var mixedOnce = (tensAndThousands * 10U) + (onesAndHundreds << 8);

                                            ulong topPair = mixedOnce & 0xFF_FF_00_00U;
                                            ulong lowPair = mixedOnce & 0x00_00_FF_FFU;

                                            var mixedTwice = (topPair * 100U) + (lowPair << 16);

                                            var result = (int)(mixedTwice >> 24);

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

                                            var (d0To7, d8To15) = Vector128.Widen(toUseBytes);
                                            var (d0To3, d4To7) = Vector128.Widen(d0To7);
                                            var (d8To11, _) = Vector128.Widen(d8To15);

                                            var scale0To3 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 0));
                                            var scale4To7 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 4));
                                            var scale8To11 = Vector128.LoadUnsafe(ref Unsafe.Add(ref multStart, 8));

                                            var m0To3 = Vector128.Multiply(d0To3, scale0To3);
                                            var m4To7 = Vector128.Multiply(d4To7, scale4To7);
                                            var m8To11 = Vector128.Multiply(d8To11, scale8To11);

                                            var maybeMultOverflow = Vector128.LessThan(m0To3, scale0To3);

                                            var verticalSum = Vector128.Add(Vector128.Add(m0To3, m4To7), m8To11);

                                            var horizontalSum = Vector128.Dot(verticalSum, Vector128<uint>.One);

                                            var maybeHorizontalOverflow = Vector128.GreaterThan(verticalSum, Vector128.CreateScalar(horizontalSum));

                                            var maybeOverflow = Vector128.BitwiseOr(maybeMultOverflow, maybeHorizontalOverflow);

                                            var res = horizontalSum;

                                            var allOnesIfOverflow = (uint)(((9 - length) >> 31) & ((0L - maybeOverflow.GetElement(0)) >> 63));  // maybeOverflow.GetElement(0) != 0 & length == 10

                                            var allOnesIfLeadingZeros = (uint)(((-(length ^ 1)) >> 31) & (((int)res - (int)multStart) >> 31)); // length != 1 & multStart > (int)res

                                            res |= allOnesIfOverflow | allOnesIfLeadingZeros;

                                            bulkStringLength = (int)res;
                                        }
                                    }
                                    inErrorState |= (uint)(bulkStringLength >> 31);    // value < 0

                                    // update to point one past the \r\n if we're not in an error state
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
                                ref var effectiveInto = ref Unsafe.Subtract(ref currentIntoRef, inErrorState & 4);
                                Debug.Assert(Unsafe.IsAddressLessThan(ref Unsafe.Add(ref effectiveInto, ParsedRespCommandOrArgument.ByteEndIx), ref intoAsIntsAllocatedEnd), "About to read/write past end of intoAsInts");
                                
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
                                currentCommandRef = ref Unsafe.Subtract(ref currentCommandRef, (int)(inErrorState & (uint)(bulkStringLength + 2 + 1)));
                                rollbackCommandRefCount -= (int)(inErrorState & (uint)(bulkStringLength + 2 + 1));

                                // update the number of items read from the array, if appropriate
                                var shouldUpdateRemainingItemCount = ~readAllItems & ~inErrorState;
                                remainingArrayItems -= (int)(shouldUpdateRemainingItemCount & 1);

                                // but we rollback errors and out of data if we fully consumed the array
                                inErrorState = ((readAllItems & oldInErrorState) | (~readAllItems & inErrorState));
                                ranOutOfData = ((readAllItems & oldRanOutOfData) | (~readAllItems & ranOutOfData));

                            }
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

                        // Parse the first string we read as a command
                        //
                        // If we're in an error state, we try to parse a 0 length string which will with no side effects
                        RespCommand cmd;
                        {
                            // We read the command string back into a 32 byte vector, extra padding in the command buffer makes this malformed
                            // We then AND off any extra data, AND off high bits to upper case everything
                            // And then use the vector to calculate a hash, with constants found by a brute force search

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

                            // Now that we have a unique index for a command, we lookup what SHOULD be in the command buffer (after ANDing away lowercase)
                            // and compare to see if the hashed value should actually be parsed as a command
                            var dataStartIx = 8192 * effectiveLength;
                            dataStartIx += (int)(moded * 64);

                            ref var commandAndExpectedValuesRef = ref MemoryMarshal.GetArrayDataReference(UnconditionalParseRespCommandImpl.CommandAndExpectedValues);
                            Debug.Assert(dataStartIx + sizeof(uint) + Vector256<byte>.Count <= UnconditionalParseRespCommandImpl.CommandAndExpectedValues.Length, "About to read past end of CommandAndExpectedValues");
                            var cmdSub = Unsafe.As<byte, int>(ref Unsafe.Add(ref commandAndExpectedValuesRef, dataStartIx));
                            var expectedCommandVector = Vector256.LoadUnsafe(ref Unsafe.Add(ref commandAndExpectedValuesRef, dataStartIx + 4));

                            var matches = Vector256.EqualsAll(upperCommandVector, expectedCommandVector);
                            var allOnesIfMatches = (-Unsafe.As<bool, byte>(ref matches)) >> 31;

                            // cmd is left as None if an error was encountered
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
                } while (inErrorState == 0);

                // At the end of everything, calculate the amount of intoCommandsSpan and commandBufferTotalSpan consumed
                intoCommandsSlotsUsed = (int)(Unsafe.ByteOffset(ref intoAsInts, ref currentIntoRef) / (sizeof(int) * 4));
                bytesConsumed = (int)Unsafe.ByteOffset(ref commandBuffer, ref currentCommandRef);
            }
        }

        /// <summary>
        /// Determine size of buffers and bitmaps to allocate based on a minimum desired size of a network receive buffer.
        /// 
        /// Note that allocation sizes and useable sizes are not the same, as we need the ability
        /// to overread in certain cases.
        /// 
        /// This should be called exactly once per command buffer allocation, it is not needed during the receive loop.
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
    }
}
