using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OptimizationExercise.SIMDRespParsing
{
    /// <summary>
    /// Based on Garnet's ParseCommand, for comparison purposes.
    /// see: https://github.com/microsoft/garnet/blob/fa9ca3d100f881d4c25d0e30fc33c91fbaeb03a6/libs/server/Resp/Parser/RespCommand.cs#L2631
    /// </summary>
    public unsafe class GarnetParser
    {
        private byte* recvBufferPtr;
        private int bytesRead;
        private int readHead;
        private int endReadHead;

        /// <summary>
        /// Wraps the <see cref="ParseCommand"/> from Garnet in the semantics of the rest of this repo.
        /// </summary>
        public void Parse(
            Span<byte> commandBuffer,
            Span<ParsedRespCommandOrArgument> intoCommands,
            out int intoCommandsSlotsUsed,
            out int bytesConsumed
        )
        {
            fixed (byte* ptr = commandBuffer)
            {
                recvBufferPtr = ptr;
                bytesRead = commandBuffer.Length;
                endReadHead = 0;

                intoCommandsSlotsUsed = 0;
                bool keepGoing;
                do
                {
                    readHead = endReadHead;

                    if ((bytesRead - readHead) < 4)
                    {
                        break;
                    }

                    try
                    {
                        ParseCommand(intoCommands, out var usedSlots, out keepGoing);
                        intoCommandsSlotsUsed += usedSlots;
                        intoCommands = intoCommands[usedSlots..];
                    }
                    catch
                    {
                        intoCommands[0] = ParsedRespCommandOrArgument.Malformed;
                        intoCommandsSlotsUsed += 1;
                        break;
                    }
                }
                while (keepGoing);

                bytesConsumed = endReadHead;
            }
        }

        // --- (slightly tweaked) Garnet code --- //

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ParseCommand(Span<ParsedRespCommandOrArgument> intoCommands, out int usedSlots, out bool success)
        {
            RespCommand cmd = RespCommand.Invalid;

            // Initialize count as -1 (i.e., read head has not been advanced)
            success = true;
            endReadHead = readHead;

            var oldReadHead = readHead;

            // Attempt parsing using fast parse pass for most common operations
            cmd = FastParseCommand(out var count);

            // If we have not found a command, continue parsing on slow path
            if (cmd == RespCommand.None)
            {
                cmd = ArrayParseCommand(ref count, ref success);
                if (!success)
                {
                    success = false;
                    usedSlots = 0;

                    return;
                }
            }

            if ((count + 1) > intoCommands.Length)
            {
                success = false;
                usedSlots = 0;
                return;
            }

            if (cmd == RespCommand.Invalid)
            {
                success = false;
                intoCommands[0] = ParsedRespCommandOrArgument.Malformed;
                usedSlots = 1;
                return;
            }

            // Set up parse state
            intoCommands[0] = ParsedRespCommandOrArgument.ForCommand(cmd, count + 1, oldReadHead, readHead);

            var ptr = recvBufferPtr + readHead;
            for (int i = 0; i < count; i++)
            {
                if (!SessionParseState_Read(i, ref ptr, recvBufferPtr + bytesRead, out var argStart, out var argEnd))
                {
                    success = false;
                    usedSlots = 0;

                    return;
                }

                var argStartIx = (int)(argStart - recvBufferPtr);
                var argEndIx = (int)(argEnd - recvBufferPtr);

                intoCommands[i + 1] = ParsedRespCommandOrArgument.ForArgument(argStartIx, argEndIx);
            }
            endReadHead = (int)(ptr - recvBufferPtr);

            usedSlots = count + 1;

            //if (storeWrapper.serverOptions.EnableAOF && storeWrapper.serverOptions.WaitForCommit)
            //    HandleAofCommitMode(cmd);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private RespCommand FastParseCommand(out int count)
        {
            var ptr = recvBufferPtr + readHead;
            var remainingBytes = bytesRead - readHead;

            // Check if the package starts with "*_\r\n$_\r\n" (_ = masked out),
            // i.e. an array with a single-digit length and single-digit first string length.
            if ((remainingBytes >= 8) && (*(ulong*)ptr & 0xFFFF00FFFFFF00FF) == MemoryMarshal.Read<ulong>("*\0\r\n$\0\r\n"u8))
            {
                // Extract total element count from the array header.
                // NOTE: Subtracting one to account for first token being parsed.
                count = ptr[1] - '1';
                Debug.Assert(count is >= 0 and < 9);

                // Extract length of the first string header
                var length = ptr[5] - '0';
                Debug.Assert(length is > 0 and <= 9);

                var oldReadHead = readHead;

                // Ensure that the complete command string is contained in the package. Otherwise exit early.
                // Include 10 bytes to account for array and command string headers, and terminator
                // 10 bytes = "*_\r\n$_\r\n" (8 bytes) + "\r\n" (2 bytes) at end of command name
                if (remainingBytes >= length + 10)
                {
                    // Optimistically advance read head to the end of the command name
                    readHead += length + 10;

                    // Last 8 byte word of the command name, for quick comparison
                    var lastWord = *(ulong*)(ptr + length + 2);

                    //
                    // Fast path for common commands with fixed numbers of arguments
                    //

                    // Only check against commands with the correct count and length.

                    return ((count << 4) | length) switch
                    {
                        // Commands without arguments
                        4 when lastWord == MemoryMarshal.Read<ulong>("\r\nPING\r\n"u8) => RespCommand.PING,
                        4 when lastWord == MemoryMarshal.Read<ulong>("\r\nEXEC\r\n"u8) => RespCommand.EXEC,
                        5 when lastWord == MemoryMarshal.Read<ulong>("\nMULTI\r\n"u8) => RespCommand.MULTI,
                        6 when lastWord == MemoryMarshal.Read<ulong>("ASKING\r\n"u8) => RespCommand.ASKING,
                        7 when lastWord == MemoryMarshal.Read<ulong>("ISCARD\r\n"u8) && ptr[8] == 'D' => RespCommand.DISCARD,
                        7 when lastWord == MemoryMarshal.Read<ulong>("NWATCH\r\n"u8) && ptr[8] == 'U' => RespCommand.UNWATCH,
                        8 when lastWord == MemoryMarshal.Read<ulong>("ADONLY\r\n"u8) && *(ushort*)(ptr + 8) == MemoryMarshal.Read<ushort>("RE"u8) => RespCommand.READONLY,
                        9 when lastWord == MemoryMarshal.Read<ulong>("DWRITE\r\n"u8) && *(uint*)(ptr + 8) == MemoryMarshal.Read<uint>("READ"u8) => RespCommand.READWRITE,

                        // Commands with fixed number of arguments
                        (1 << 4) | 3 when lastWord == MemoryMarshal.Read<ulong>("3\r\nGET\r\n"u8) => RespCommand.GET,
                        (1 << 4) | 3 when lastWord == MemoryMarshal.Read<ulong>("3\r\nDEL\r\n"u8) => RespCommand.DEL,
                        (1 << 4) | 3 when lastWord == MemoryMarshal.Read<ulong>("3\r\nTTL\r\n"u8) => RespCommand.TTL,
                        (1 << 4) | 4 when lastWord == MemoryMarshal.Read<ulong>("\r\nDUMP\r\n"u8) => RespCommand.DUMP,
                        (1 << 4) | 4 when lastWord == MemoryMarshal.Read<ulong>("\r\nINCR\r\n"u8) => RespCommand.INCR,
                        (1 << 4) | 4 when lastWord == MemoryMarshal.Read<ulong>("\r\nPTTL\r\n"u8) => RespCommand.PTTL,
                        (1 << 4) | 4 when lastWord == MemoryMarshal.Read<ulong>("\r\nDECR\r\n"u8) => RespCommand.DECR,
                        (1 << 4) | 4 when lastWord == MemoryMarshal.Read<ulong>("EXISTS\r\n"u8) => RespCommand.EXISTS,
                        (1 << 4) | 6 when lastWord == MemoryMarshal.Read<ulong>("GETDEL\r\n"u8) => RespCommand.GETDEL,
                        (1 << 4) | 7 when lastWord == MemoryMarshal.Read<ulong>("ERSIST\r\n"u8) && ptr[8] == 'P' => RespCommand.PERSIST,
                        (1 << 4) | 7 when lastWord == MemoryMarshal.Read<ulong>("PFCOUNT\r\n"u8) && ptr[8] == 'P' => RespCommand.PFCOUNT,
                        (2 << 4) | 3 when lastWord == MemoryMarshal.Read<ulong>("3\r\nSET\r\n"u8) => RespCommand.SET,
                        (2 << 4) | 5 when lastWord == MemoryMarshal.Read<ulong>("\nPFADD\r\n"u8) => RespCommand.PFADD,
                        (2 << 4) | 6 when lastWord == MemoryMarshal.Read<ulong>("INCRBY\r\n"u8) => RespCommand.INCRBY,
                        (2 << 4) | 6 when lastWord == MemoryMarshal.Read<ulong>("DECRBY\r\n"u8) => RespCommand.DECRBY,
                        (2 << 4) | 6 when lastWord == MemoryMarshal.Read<ulong>("GETBIT\r\n"u8) => RespCommand.GETBIT,
                        (2 << 4) | 6 when lastWord == MemoryMarshal.Read<ulong>("APPEND\r\n"u8) => RespCommand.APPEND,
                        (2 << 4) | 6 when lastWord == MemoryMarshal.Read<ulong>("GETSET\r\n"u8) => RespCommand.GETSET,
                        (2 << 4) | 7 when lastWord == MemoryMarshal.Read<ulong>("UBLISH\r\n"u8) && ptr[8] == 'P' => RespCommand.PUBLISH,
                        (2 << 4) | 7 when lastWord == MemoryMarshal.Read<ulong>("FMERGE\r\n"u8) && ptr[8] == 'P' => RespCommand.PFMERGE,
                        (2 << 4) | 8 when lastWord == MemoryMarshal.Read<ulong>("UBLISH\r\n"u8) && *(ushort*)(ptr + 8) == MemoryMarshal.Read<ushort>("SP"u8) => RespCommand.SPUBLISH,
                        //(2 << 4) | 5 when lastWord == MemoryMarshal.Read<ulong>("\nSETNX\r\n"u8) => RespCommand.SETNX,
                        (3 << 4) | 5 when lastWord == MemoryMarshal.Read<ulong>("\nSETEX\r\n"u8) => RespCommand.SETEX,
                        (3 << 4) | 6 when lastWord == MemoryMarshal.Read<ulong>("PSETEX\r\n"u8) => RespCommand.PSETEX,
                        (3 << 4) | 6 when lastWord == MemoryMarshal.Read<ulong>("SETBIT\r\n"u8) => RespCommand.SETBIT,
                        (3 << 4) | 6 when lastWord == MemoryMarshal.Read<ulong>("SUBSTR\r\n"u8) => RespCommand.SUBSTR,
                        (3 << 4) | 7 when lastWord == MemoryMarshal.Read<ulong>("ESTORE\r\n"u8) && ptr[8] == 'R' => RespCommand.RESTORE,
                        (3 << 4) | 8 when lastWord == MemoryMarshal.Read<ulong>("TRANGE\r\n"u8) && *(ushort*)(ptr + 8) == MemoryMarshal.Read<ushort>("SE"u8) => RespCommand.SETRANGE,
                        (3 << 4) | 8 when lastWord == MemoryMarshal.Read<ulong>("TRANGE\r\n"u8) && *(ushort*)(ptr + 8) == MemoryMarshal.Read<ushort>("GE"u8) => RespCommand.GETRANGE,

                        _ => ((length << 4) | count) switch
                        {
                            // Commands with dynamic number of arguments
                            >= ((6 << 4) | 2) and <= ((6 << 4) | 3) when lastWord == MemoryMarshal.Read<ulong>("RENAME\r\n"u8) => RespCommand.RENAME,
                            >= ((8 << 4) | 2) and <= ((8 << 4) | 3) when lastWord == MemoryMarshal.Read<ulong>("NAMENX\r\n"u8) && *(ushort*)(ptr + 8) == MemoryMarshal.Read<ushort>("RE"u8) => RespCommand.RENAMENX,
                            //>= ((3 << 4) | 3) and <= ((3 << 4) | 7) when lastWord == MemoryMarshal.Read<ulong>("3\r\nSET\r\n"u8) => RespCommand.SETEXNX,
                            >= ((5 << 4) | 1) and <= ((5 << 4) | 3) when lastWord == MemoryMarshal.Read<ulong>("\nGETEX\r\n"u8) => RespCommand.GETEX,
                            >= ((6 << 4) | 0) and <= ((6 << 4) | 9) when lastWord == MemoryMarshal.Read<ulong>("RUNTXP\r\n"u8) => RespCommand.RUNTXP,
                            >= ((6 << 4) | 2) and <= ((6 << 4) | 3) when lastWord == MemoryMarshal.Read<ulong>("EXPIRE\r\n"u8) => RespCommand.EXPIRE,
                            >= ((6 << 4) | 2) and <= ((6 << 4) | 5) when lastWord == MemoryMarshal.Read<ulong>("BITPOS\r\n"u8) => RespCommand.BITPOS,
                            >= ((7 << 4) | 2) and <= ((7 << 4) | 3) when lastWord == MemoryMarshal.Read<ulong>("EXPIRE\r\n"u8) && ptr[8] == 'P' => RespCommand.PEXPIRE,
                            >= ((8 << 4) | 1) and <= ((8 << 4) | 4) when lastWord == MemoryMarshal.Read<ulong>("TCOUNT\r\n"u8) && *(ushort*)(ptr + 8) == MemoryMarshal.Read<ushort>("BI"u8) => RespCommand.BITCOUNT,
                            _ => MatchedNone(ref readHead, oldReadHead)
                        }
                    };

                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    static RespCommand MatchedNone(ref int readHead, int oldReadHead)
                    {
                        // Backup the read head, if we didn't find a command and need to continue in the more expensive parsing loop
                        readHead = oldReadHead;

                        return RespCommand.None;
                    }
                }
            }
            else
            {
                return FastParseInlineCommand(out count);
            }

            // Couldn't find a matching command in this pass
            count = -1;
            return RespCommand.None;
        }

        private RespCommand FastParseArrayCommand(ref int count)
        {
            // Bytes remaining in the read buffer
            int remainingBytes = bytesRead - readHead;

            // The current read head to continue reading from
            byte* ptr = recvBufferPtr + readHead;

            //
            // Fast-path parsing by (1) command string length, (2) First character of command name (optional) and (3) priority (manual order)
            //

            // NOTE: A valid RESP string is at a minimum 7 characters long "$_\r\n_\r\n"
            if (remainingBytes >= 7)
            {
                var oldReadHead = readHead;

                // Check if this is a string with a single-digit length ("$_\r\n" -> _ omitted)
                if ((*(uint*)ptr & 0xFFFF00FF) == MemoryMarshal.Read<uint>("$\0\r\n"u8))
                {
                    // Extract length from string header
                    var length = ptr[1] - '0';
                    Debug.Assert(length is > 0 and <= 9);

                    // Ensure that the complete command string is contained in the package. Otherwise exit early.
                    // Include 6 bytes to account for command string header and name terminator.
                    // 6 bytes = "$_\r\n" (4 bytes) + "\r\n" (2 bytes) at end of command name
                    if (remainingBytes >= length + 6)
                    {
                        // Optimistically increase read head and decrease the number of remaining elements
                        readHead += length + 6;
                        count -= 1;

                        switch (length)
                        {
                            case 3:
                                if (*(ulong*)(ptr + 1) == MemoryMarshal.Read<ulong>("3\r\nDEL\r\n"u8))
                                {
                                    return RespCommand.DEL;
                                }
                                else if (*(ulong*)(ptr + 1) == MemoryMarshal.Read<ulong>("3\r\nLCS\r\n"u8))
                                {
                                    return RespCommand.LCS;
                                }

                                break;

                            case 4:
                                switch ((ushort)ptr[4])
                                {
                                    case 'E':
                                        if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nEVAL\r\n"u8))
                                        {
                                            return RespCommand.EVAL;
                                        }
                                        break;

                                    case 'H':
                                        if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nHSET\r\n"u8))
                                        {
                                            return RespCommand.HSET;
                                        }
                                        else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nHGET\r\n"u8))
                                        {
                                            return RespCommand.HGET;
                                        }
                                        else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nHDEL\r\n"u8))
                                        {
                                            return RespCommand.HDEL;
                                        }
                                        else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nHLEN\r\n"u8))
                                        {
                                            return RespCommand.HLEN;
                                        }
                                        else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nHTTL\r\n"u8))
                                        {
                                            return RespCommand.HTTL;
                                        }
                                        break;

                                    case 'K':
                                        if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nKEYS\r\n"u8))
                                        {
                                            return RespCommand.KEYS;
                                        }
                                        break;

                                    case 'L':
                                        if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nLPOP\r\n"u8))
                                        {
                                            return RespCommand.LPOP;
                                        }
                                        else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nLLEN\r\n"u8))
                                        {
                                            return RespCommand.LLEN;
                                        }
                                        else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nLREM\r\n"u8))
                                        {
                                            return RespCommand.LREM;
                                        }
                                        else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nLSET\r\n"u8))
                                        {
                                            return RespCommand.LSET;
                                        }
                                        else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nLPOS\r\n"u8))
                                        {
                                            return RespCommand.LPOS;
                                        }
                                        break;

                                    case 'M':
                                        if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nMGET\r\n"u8))
                                        {
                                            return RespCommand.MGET;
                                        }
                                        else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nMSET\r\n"u8))
                                        {
                                            return RespCommand.MSET;
                                        }
                                        break;

                                    case 'R':
                                        if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nRPOP\r\n"u8))
                                        {
                                            return RespCommand.RPOP;
                                        }
                                        break;

                                    case 'S':
                                        if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nSCAN\r\n"u8))
                                        {
                                            return RespCommand.SCAN;
                                        }
                                        else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nSADD\r\n"u8))
                                        {
                                            return RespCommand.SADD;
                                        }
                                        else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nSREM\r\n"u8))
                                        {
                                            return RespCommand.SREM;
                                        }
                                        else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nSPOP\r\n"u8))
                                        {
                                            return RespCommand.SPOP;
                                        }
                                        break;

                                    case 'T':
                                        if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nTYPE\r\n"u8))
                                        {
                                            return RespCommand.TYPE;
                                        }
                                        break;

                                    case 'Z':
                                        if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nZADD\r\n"u8))
                                        {
                                            return RespCommand.ZADD;
                                        }
                                        else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nZREM\r\n"u8))
                                        {
                                            return RespCommand.ZREM;
                                        }
                                        else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nZTTL\r\n"u8))
                                        {
                                            return RespCommand.ZTTL;
                                        }
                                        break;
                                }
                                break;

                            case 5:
                                switch ((ushort)ptr[4])
                                {
                                    case 'B':
                                        if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nBITOP\r\n"u8))
                                        {
                                            // Check for matching bit-operation
                                            if (remainingBytes > length + 6 + 8)
                                            {
                                                // TODO: AND|OR|XOR|NOT may not correctly handle mixed cases?

                                                // 2-character operations
                                                //if (*(uint*)(ptr + 11) == MemoryMarshal.Read<uint>("$2\r\n"u8))
                                                //{
                                                //    if (*(ulong*)(ptr + 11) == MemoryMarshal.Read<ulong>("$2\r\nOR\r\n"u8) || *(ulong*)(ptr + 11) == MemoryMarshal.Read<ulong>("$2\r\nor\r\n"u8))
                                                //    {
                                                //        readHead += 8;
                                                //        count -= 1;
                                                //        return RespCommand.BITOP_OR;
                                                //    }
                                                //}
                                                //// 3-character operations
                                                //else if (remainingBytes > length + 6 + 9)
                                                //{
                                                //    if (*(uint*)(ptr + 11) == MemoryMarshal.Read<uint>("$3\r\n"u8))
                                                //    {
                                                //        // Optimistically adjust read head and count
                                                //        readHead += 9;
                                                //        count -= 1;

                                                //        if (*(ulong*)(ptr + 12) == MemoryMarshal.Read<ulong>("3\r\nAND\r\n"u8) || *(ulong*)(ptr + 12) == MemoryMarshal.Read<ulong>("3\r\nand\r\n"u8))
                                                //        {
                                                //            return RespCommand.BITOP_AND;
                                                //        }
                                                //        else if (*(ulong*)(ptr + 12) == MemoryMarshal.Read<ulong>("3\r\nXOR\r\n"u8) || *(ulong*)(ptr + 12) == MemoryMarshal.Read<ulong>("3\r\nxor\r\n"u8))
                                                //        {
                                                //            return RespCommand.BITOP_XOR;
                                                //        }
                                                //        else if (*(ulong*)(ptr + 12) == MemoryMarshal.Read<ulong>("3\r\nNOT\r\n"u8) || *(ulong*)(ptr + 12) == MemoryMarshal.Read<ulong>("3\r\nnot\r\n"u8))
                                                //        {
                                                //            return RespCommand.BITOP_NOT;
                                                //        }

                                                //        // Reset read head and count if we didn't match operator.
                                                //        readHead -= 9;
                                                //        count += 1;
                                                //    }
                                                //}

                                                // Although we recognize BITOP, the pseudo-subcommand isn't recognized so fail early
                                                //specificErrorMessage = CmdStrings.RESP_SYNTAX_ERROR;
                                                return RespCommand.None;
                                            }
                                        }
                                        else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nBRPOP\r\n"u8))
                                        {
                                            return RespCommand.BRPOP;
                                        }
                                        else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nBLPOP\r\n"u8))
                                        {
                                            return RespCommand.BLPOP;
                                        }
                                        break;

                                    case 'H':
                                        if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nHMSET\r\n"u8))
                                        {
                                            return RespCommand.HMSET;
                                        }
                                        else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nHMGET\r\n"u8))
                                        {
                                            return RespCommand.HMGET;
                                        }
                                        else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nHKEYS\r\n"u8))
                                        {
                                            return RespCommand.HKEYS;
                                        }
                                        else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nHVALS\r\n"u8))
                                        {
                                            return RespCommand.HVALS;
                                        }
                                        else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nHSCAN\r\n"u8))
                                        {
                                            return RespCommand.HSCAN;
                                        }
                                        else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nHPTTL\r\n"u8))
                                        {
                                            return RespCommand.HPTTL;
                                        }
                                        break;

                                    case 'L':
                                        if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nLPUSH\r\n"u8))
                                        {
                                            return RespCommand.LPUSH;
                                        }
                                        else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nLTRIM\r\n"u8))
                                        {
                                            return RespCommand.LTRIM;
                                        }
                                        else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nLMOVE\r\n"u8))
                                        {
                                            return RespCommand.LMOVE;
                                        }
                                        else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nLMPOP\r\n"u8))
                                        {
                                            return RespCommand.LMPOP;
                                        }
                                        break;

                                    case 'P':
                                        if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nPFADD\r\n"u8))
                                        {
                                            return RespCommand.PFADD;
                                        }
                                        break;

                                    case 'R':
                                        if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nRPUSH\r\n"u8))
                                        {
                                            return RespCommand.RPUSH;
                                        }
                                        break;

                                    case 'S':
                                        if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nSCARD\r\n"u8))
                                        {
                                            return RespCommand.SCARD;
                                        }
                                        else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nSSCAN\r\n"u8))
                                        {
                                            return RespCommand.SSCAN;
                                        }
                                        else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nSMOVE\r\n"u8))
                                        {
                                            return RespCommand.SMOVE;
                                        }
                                        else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nSDIFF\r\n"u8))
                                        {
                                            return RespCommand.SDIFF;
                                        }
                                        break;

                                    case 'W':
                                        if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nWATCH\r\n"u8))
                                        {
                                            return RespCommand.WATCH;
                                        }
                                        break;

                                    case 'Z':
                                        if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nZCARD\r\n"u8))
                                        {
                                            return RespCommand.ZCARD;
                                        }
                                        else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nZRANK\r\n"u8))
                                        {
                                            return RespCommand.ZRANK;
                                        }
                                        else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nZDIFF\r\n"u8))
                                        {
                                            return RespCommand.ZDIFF;
                                        }
                                        else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nZSCAN\r\n"u8))
                                        {
                                            return RespCommand.ZSCAN;
                                        }
                                        else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nZMPOP\r\n"u8))
                                        {
                                            return RespCommand.ZMPOP;
                                        }
                                        else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\nZPTTL\r\n"u8))
                                        {
                                            return RespCommand.ZPTTL;
                                        }
                                        break;
                                }
                                break;

                            case 6:
                                switch ((ushort)ptr[4])
                                {
                                    case 'B':
                                        if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("BLMOVE\r\n"u8))
                                        {
                                            return RespCommand.BLMOVE;
                                        }
                                        else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("BLMPOP\r\n"u8))
                                        {
                                            return RespCommand.BLMPOP;
                                        }
                                        else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("BZMPOP\r\n"u8))
                                        {
                                            return RespCommand.BZMPOP;
                                        }
                                        break;
                                    case 'D':
                                        if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("DBSIZE\r\n"u8))
                                        {
                                            return RespCommand.DBSIZE;
                                        }
                                        break;

                                    case 'E':
                                        if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("EXISTS\r\n"u8))
                                        {
                                            return RespCommand.EXISTS;
                                        }
                                        break;

                                    case 'G':
                                        if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("GEOADD\r\n"u8))
                                        {
                                            return RespCommand.GEOADD;
                                        }
                                        else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("GEOPOS\r\n"u8))
                                        {
                                            return RespCommand.GEOPOS;
                                        }
                                        break;

                                    case 'H':
                                        if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("HSETNX\r\n"u8))
                                        {
                                            return RespCommand.HSETNX;
                                        }
                                        break;

                                    case 'L':
                                        if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("LPUSHX\r\n"u8))
                                        {
                                            return RespCommand.LPUSHX;
                                        }
                                        else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("LRANGE\r\n"u8))
                                        {
                                            return RespCommand.LRANGE;
                                        }
                                        else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("LINDEX\r\n"u8))
                                        {
                                            return RespCommand.LINDEX;
                                        }
                                        break;

                                    case 'M':
                                        if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("MSETNX\r\n"u8))
                                        {
                                            return RespCommand.MSETNX;
                                        }
                                        //else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("MEMORY\r\n"u8))
                                        //{
                                        //    // MEMORY USAGE
                                        //    // 11 = "$5\r\nUSAGE\r\n".Length
                                        //    if (remainingBytes >= length + 11)
                                        //    {
                                        //        if (*(ulong*)(ptr + 12) == MemoryMarshal.Read<ulong>("$5\r\nUSAG"u8) && *(ulong*)(ptr + 15) == MemoryMarshal.Read<ulong>("\nUSAGE\r\n"u8))
                                        //        {
                                        //            count--;
                                        //            readHead += 11;
                                        //            return RespCommand.MEMORY_USAGE;
                                        //        }
                                        //    }
                                        //}
                                        break;

                                    case 'R':
                                        if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("RPUSHX\r\n"u8))
                                        {
                                            return RespCommand.RPUSHX;
                                        }
                                        break;

                                    case 'S':
                                        if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("SELECT\r\n"u8))
                                        {
                                            return RespCommand.SELECT;
                                        }
                                        else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("STRLEN\r\n"u8))
                                        {
                                            return RespCommand.STRLEN;
                                        }
                                        else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("SUNION\r\n"u8))
                                        {
                                            return RespCommand.SUNION;
                                        }
                                        else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("SINTER\r\n"u8))
                                        {
                                            return RespCommand.SINTER;
                                        }
                                        //else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("SCRIPT\r\n"u8))
                                        //{
                                        //    // SCRIPT EXISTS => "$6\r\nEXISTS\r\n".Length == 12
                                        //    // SCRIPT FLUSH  => "$5\r\nFLUSH\r\n".Length  == 11
                                        //    // SCRIPT LOAD   => "$4\r\nLOAD\r\n".Length   == 10

                                        //    if (remainingBytes >= length + 10)
                                        //    {
                                        //        if (*(ulong*)(ptr + 4 + 8) == MemoryMarshal.Read<ulong>("$4\r\nLOAD"u8) && *(ulong*)(ptr + 4 + 8 + 2) == MemoryMarshal.Read<ulong>("\r\nLOAD\r\n"u8))
                                        //        {
                                        //            count--;
                                        //            readHead += 10;
                                        //            return RespCommand.SCRIPT_LOAD;
                                        //        }

                                        //        if (remainingBytes >= length + 11)
                                        //        {
                                        //            if (*(ulong*)(ptr + 4 + 8) == MemoryMarshal.Read<ulong>("$5\r\nFLUS"u8) && *(ulong*)(ptr + 4 + 8 + 3) == MemoryMarshal.Read<ulong>("\nFLUSH\r\n"u8))
                                        //            {
                                        //                count--;
                                        //                readHead += 11;
                                        //                return RespCommand.SCRIPT_FLUSH;
                                        //            }

                                        //            if (remainingBytes >= length + 12)
                                        //            {
                                        //                if (*(ulong*)(ptr + 4 + 8) == MemoryMarshal.Read<ulong>("$6\r\nEXIS"u8) && *(ulong*)(ptr + 4 + 8 + 4) == MemoryMarshal.Read<ulong>("EXISTS\r\n"u8))
                                        //                {
                                        //                    count--;
                                        //                    readHead += 12;
                                        //                    return RespCommand.SCRIPT_EXISTS;
                                        //                }
                                        //            }
                                        //        }
                                        //    }
                                        //}
                                        //else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("SWAPDB\r\n"u8))
                                        //{
                                        //    return RespCommand.SWAPDB;
                                        //}
                                        break;

                                    case 'U':
                                        if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("UNLINK\r\n"u8))
                                        {
                                            return RespCommand.UNLINK;
                                        }
                                        break;

                                    case 'Z':
                                        if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("ZCOUNT\r\n"u8))
                                        {
                                            return RespCommand.ZCOUNT;
                                        }
                                        else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("ZRANGE\r\n"u8))
                                        {
                                            return RespCommand.ZRANGE;
                                        }
                                        else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("ZUNION\r\n"u8))
                                        {
                                            return RespCommand.ZUNION;
                                        }
                                        if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("ZSCORE\r\n"u8))
                                        {
                                            return RespCommand.ZSCORE;
                                        }
                                        if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("ZINTER\r\n"u8))
                                        {
                                            return RespCommand.ZINTER;
                                        }
                                        break;
                                }

                                break;
                            case 7:
                                switch ((ushort)ptr[4])
                                {
                                    case 'E':
                                        if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("\r\nEVALSHA\r\n"u8))
                                        {
                                            return RespCommand.EVALSHA;
                                        }
                                        break;

                                    case 'G':
                                        if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("GEOHASH\r"u8) && *(byte*)(ptr + 12) == '\n')
                                        {
                                            return RespCommand.GEOHASH;
                                        }
                                        else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("GEODIST\r"u8) && *(byte*)(ptr + 12) == '\n')
                                        {
                                            return RespCommand.GEODIST;
                                        }
                                        break;

                                    case 'H':
                                        if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("HGETALL\r"u8) && *(byte*)(ptr + 12) == '\n')
                                        {
                                            return RespCommand.HGETALL;
                                        }
                                        else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("HEXISTS\r"u8) && *(byte*)(ptr + 12) == '\n')
                                        {
                                            return RespCommand.HEXISTS;
                                        }
                                        else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("HEXPIRE\r"u8) && *(byte*)(ptr + 12) == '\n')
                                        {
                                            return RespCommand.HEXPIRE;
                                        }
                                        else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("HINCRBY\r"u8) && *(byte*)(ptr + 12) == '\n')
                                        {
                                            return RespCommand.HINCRBY;
                                        }
                                        else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("HSTRLEN\r"u8) && *(byte*)(ptr + 12) == '\n')
                                        {
                                            return RespCommand.HSTRLEN;
                                        }
                                        break;

                                    case 'L':
                                        if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("LINSERT\r"u8) && *(byte*)(ptr + 12) == '\n')
                                        {
                                            return RespCommand.LINSERT;
                                        }
                                        break;

                                    case 'M':
                                        if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("MONITOR\r"u8) && *(byte*)(ptr + 12) == '\n')
                                        {
                                            return RespCommand.MONITOR;
                                        }
                                        break;

                                    case 'P':
                                        if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("PFCOUNT\r"u8) && *(byte*)(ptr + 12) == '\n')
                                        {
                                            return RespCommand.PFCOUNT;
                                        }
                                        else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("PFMERGE\r"u8) && *(byte*)(ptr + 12) == '\n')
                                        {
                                            return RespCommand.PFMERGE;
                                        }
                                        break;
                                    case 'W':
                                        if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("WATCHMS\r"u8) && *(byte*)(ptr + 12) == '\n')
                                        {
                                            return RespCommand.WATCHMS;
                                        }

                                        if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("WATCHOS\r"u8) && *(byte*)(ptr + 12) == '\n')
                                        {
                                            return RespCommand.WATCHOS;
                                        }

                                        break;

                                    case 'Z':
                                        if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("ZPOPMIN\r"u8) && *(byte*)(ptr + 12) == '\n')
                                        {
                                            return RespCommand.ZPOPMIN;
                                        }
                                        else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("ZEXPIRE\r"u8) && *(byte*)(ptr + 12) == '\n')
                                        {
                                            return RespCommand.ZEXPIRE;
                                        }
                                        else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("ZPOPMAX\r"u8) && *(byte*)(ptr + 12) == '\n')
                                        {
                                            return RespCommand.ZPOPMAX;
                                        }
                                        else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("ZINCRBY\r"u8) && *(byte*)(ptr + 12) == '\n')
                                        {
                                            return RespCommand.ZINCRBY;
                                        }
                                        else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("ZMSCORE\r"u8) && *(byte*)(ptr + 12) == '\n')
                                        {
                                            return RespCommand.ZMSCORE;
                                        }
                                        break;
                                }
                                break;
                            case 8:
                                if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("ZREVRANK"u8) && *(ushort*)(ptr + 12) == MemoryMarshal.Read<ushort>("\r\n"u8))
                                {
                                    return RespCommand.ZREVRANK;
                                }
                                else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("SMEMBERS"u8) && *(ushort*)(ptr + 12) == MemoryMarshal.Read<ushort>("\r\n"u8))
                                {
                                    return RespCommand.SMEMBERS;
                                }
                                else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("BITFIELD"u8) && *(ushort*)(ptr + 12) == MemoryMarshal.Read<ushort>("\r\n"u8))
                                {
                                    return RespCommand.BITFIELD;
                                }
                                else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("EXPIREAT"u8) && *(ushort*)(ptr + 12) == MemoryMarshal.Read<ushort>("\r\n"u8))
                                {
                                    return RespCommand.EXPIREAT;
                                }
                                else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("HPEXPIRE"u8) && *(ushort*)(ptr + 12) == MemoryMarshal.Read<ushort>("\r\n"u8))
                                {
                                    return RespCommand.HPEXPIRE;
                                }
                                else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("HPERSIST"u8) && *(ushort*)(ptr + 12) == MemoryMarshal.Read<ushort>("\r\n"u8))
                                {
                                    return RespCommand.HPERSIST;
                                }
                                else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("ZPEXPIRE"u8) && *(ushort*)(ptr + 12) == MemoryMarshal.Read<ushort>("\r\n"u8))
                                {
                                    return RespCommand.ZPEXPIRE;
                                }
                                else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("ZPERSIST"u8) && *(ushort*)(ptr + 12) == MemoryMarshal.Read<ushort>("\r\n"u8))
                                {
                                    return RespCommand.ZPERSIST;
                                }
                                else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("BZPOPMAX"u8) && *(ushort*)(ptr + 12) == MemoryMarshal.Read<ushort>("\r\n"u8))
                                {
                                    return RespCommand.BZPOPMAX;
                                }
                                else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("BZPOPMIN"u8) && *(ushort*)(ptr + 12) == MemoryMarshal.Read<ushort>("\r\n"u8))
                                {
                                    return RespCommand.BZPOPMIN;
                                }
                                else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("SPUBLISH"u8) && *(ushort*)(ptr + 12) == MemoryMarshal.Read<ushort>("\r\n"u8))
                                {
                                    return RespCommand.SPUBLISH;
                                }
                                break;
                            case 9:
                                if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("SUBSCRIB"u8) && *(uint*)(ptr + 11) == MemoryMarshal.Read<uint>("BE\r\n"u8))
                                {
                                    return RespCommand.SUBSCRIBE;
                                }
                                else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("SISMEMBE"u8) && *(uint*)(ptr + 11) == MemoryMarshal.Read<uint>("ER\r\n"u8))
                                {
                                    return RespCommand.SISMEMBER;
                                }
                                else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("ZLEXCOUN"u8) && *(uint*)(ptr + 11) == MemoryMarshal.Read<uint>("NT\r\n"u8))
                                {
                                    return RespCommand.ZLEXCOUNT;
                                }
                                else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("GEOSEARC"u8) && *(uint*)(ptr + 11) == MemoryMarshal.Read<uint>("CH\r\n"u8))
                                {
                                    return RespCommand.GEOSEARCH;
                                }
                                else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("ZREVRANG"u8) && *(uint*)(ptr + 11) == MemoryMarshal.Read<uint>("GE\r\n"u8))
                                {
                                    return RespCommand.ZREVRANGE;
                                }
                                else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("RPOPLPUS"u8) && *(uint*)(ptr + 11) == MemoryMarshal.Read<uint>("SH\r\n"u8))
                                {
                                    return RespCommand.RPOPLPUSH;
                                }
                                else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("PEXPIREA"u8) && *(uint*)(ptr + 11) == MemoryMarshal.Read<uint>("AT\r\n"u8))
                                {
                                    return RespCommand.PEXPIREAT;
                                }
                                else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("HEXPIREA"u8) && *(uint*)(ptr + 11) == MemoryMarshal.Read<uint>("AT\r\n"u8))
                                {
                                    return RespCommand.HEXPIREAT;
                                }
                                else if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("ZEXPIREA"u8) && *(uint*)(ptr + 11) == MemoryMarshal.Read<uint>("AT\r\n"u8))
                                {
                                    return RespCommand.ZEXPIREAT;
                                }
                                break;
                            case 10:
                                if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("SSUBSCRI"u8) && *(uint*)(ptr + 11) == MemoryMarshal.Read<uint>("BE\r\n"u8))
                                {
                                    return RespCommand.SSUBSCRIBE;
                                }
                                break;
                        }

                        // Reset optimistically changed state, if no matching command was found
                        count += 1;
                        readHead = oldReadHead;
                    }
                }
                // Check if this is a string with a double-digit length ("$__\r" -> _ omitted)
                else if ((*(uint*)ptr & 0xFF0000FF) == MemoryMarshal.Read<uint>("$\0\0\r"u8))
                {
                    // Extract length from string header
                    var length = ptr[2] - '0' + 10;
                    Debug.Assert(length is >= 10 and <= 19);

                    // Ensure that the complete command string is contained in the package. Otherwise exit early.
                    // Include 7 bytes to account for command string header and name terminator.
                    // 7 bytes = "$__\r\n" (5 bytes) + "\r\n" (2 bytes) at end of command name
                    if (remainingBytes >= length + 7)
                    {
                        // Optimistically increase read head and decrease the number of remaining elements
                        readHead += length + 7;
                        count -= 1;

                        // Match remaining character by length
                        // NOTE: Check should include the remaining array length terminator '\n'
                        switch (length)
                        {
                            case 10:
                                if (*(ulong*)(ptr + 1) == MemoryMarshal.Read<ulong>("10\r\nPSUB"u8) && *(ulong*)(ptr + 9) == MemoryMarshal.Read<ulong>("SCRIBE\r\n"u8))
                                {
                                    return RespCommand.PSUBSCRIBE;
                                }
                                else if (*(ulong*)(ptr + 1) == MemoryMarshal.Read<ulong>("10\r\nHRAN"u8) && *(ulong*)(ptr + 9) == MemoryMarshal.Read<ulong>("DFIELD\r\n"u8))
                                {
                                    return RespCommand.HRANDFIELD;
                                }
                                else if (*(ulong*)(ptr + 1) == MemoryMarshal.Read<ulong>("10\r\nSDIF"u8) && *(ulong*)(ptr + 9) == MemoryMarshal.Read<ulong>("FSTORE\r\n"u8))
                                {
                                    return RespCommand.SDIFFSTORE;
                                }
                                else if (*(ulong*)(ptr + 1) == MemoryMarshal.Read<ulong>("10\r\nEXPI"u8) && *(ulong*)(ptr + 9) == MemoryMarshal.Read<ulong>("RETIME\r\n"u8))
                                {
                                    return RespCommand.EXPIRETIME;
                                }
                                else if (*(ulong*)(ptr + 1) == MemoryMarshal.Read<ulong>("10\r\nSMIS"u8) && *(ulong*)(ptr + 9) == MemoryMarshal.Read<ulong>("MEMBER\r\n"u8))
                                {
                                    return RespCommand.SMISMEMBER;
                                }
                                else if (*(ulong*)(ptr + 1) == MemoryMarshal.Read<ulong>("10\r\nSINT"u8) && *(ulong*)(ptr + 9) == MemoryMarshal.Read<ulong>("ERCARD\r\n"u8))
                                {
                                    return RespCommand.SINTERCARD;
                                }
                                else if (*(ulong*)(ptr + 1) == MemoryMarshal.Read<ulong>("10\r\nZDIF"u8) && *(uint*)(ptr + 9) == MemoryMarshal.Read<uint>("FSTORE\r\n"u8))
                                {
                                    return RespCommand.ZDIFFSTORE;
                                }
                                else if (*(ulong*)(ptr + 1) == MemoryMarshal.Read<ulong>("10\r\nBRPO"u8) && *(uint*)(ptr + 9) == MemoryMarshal.Read<uint>("PLPUSH\r\n"u8))
                                {
                                    return RespCommand.BRPOPLPUSH;
                                }
                                else if (*(ulong*)(ptr + 1) == MemoryMarshal.Read<ulong>("10\r\nZINT"u8) && *(ulong*)(ptr + 9) == MemoryMarshal.Read<ulong>("ERCARD\r\n"u8))
                                {
                                    return RespCommand.ZINTERCARD;
                                }
                                else if (*(ulong*)(ptr + 1) == MemoryMarshal.Read<ulong>("10\r\nHPEX"u8) && *(uint*)(ptr + 9) == MemoryMarshal.Read<uint>("PIREAT\r\n"u8))
                                {
                                    return RespCommand.HPEXPIREAT;
                                }
                                else if (*(ulong*)(ptr + 1) == MemoryMarshal.Read<ulong>("10\r\nZPEX"u8) && *(uint*)(ptr + 9) == MemoryMarshal.Read<uint>("PIREAT\r\n"u8))
                                {
                                    return RespCommand.ZPEXPIREAT;
                                }
                                break;
                            case 11:
                                if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("1\r\nUNSUB"u8) && *(ulong*)(ptr + 10) == MemoryMarshal.Read<ulong>("SCRIBE\r\n"u8))
                                {
                                    return RespCommand.UNSUBSCRIBE;
                                }
                                else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("1\r\nZRAND"u8) && *(ulong*)(ptr + 10) == MemoryMarshal.Read<ulong>("MEMBER\r\n"u8))
                                {
                                    return RespCommand.ZRANDMEMBER;
                                }
                                else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("1\r\nBITFI"u8) && *(ulong*)(ptr + 10) == MemoryMarshal.Read<ulong>("ELD_RO\r\n"u8))
                                {
                                    return RespCommand.BITFIELD_RO;
                                }
                                else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("1\r\nSRAND"u8) && *(ulong*)(ptr + 10) == MemoryMarshal.Read<ulong>("MEMBER\r\n"u8))
                                {
                                    return RespCommand.SRANDMEMBER;
                                }
                                else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("1\r\nSUNIO"u8) && *(ulong*)(ptr + 10) == MemoryMarshal.Read<ulong>("NSTORE\r\n"u8))
                                {
                                    return RespCommand.SUNIONSTORE;
                                }
                                else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("1\r\nSINTE"u8) && *(ulong*)(ptr + 10) == MemoryMarshal.Read<ulong>("RSTORE\r\n"u8))
                                {
                                    return RespCommand.SINTERSTORE;
                                }
                                else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("1\r\nPEXPI"u8) && *(ulong*)(ptr + 10) == MemoryMarshal.Read<ulong>("RETIME\r\n"u8))
                                {
                                    return RespCommand.PEXPIRETIME;
                                }
                                else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("1\r\nHEXPI"u8) && *(ulong*)(ptr + 10) == MemoryMarshal.Read<ulong>("RETIME\r\n"u8))
                                {
                                    return RespCommand.HEXPIRETIME;
                                }
                                else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("1\r\nINCRB"u8) && *(ulong*)(ptr + 10) == MemoryMarshal.Read<ulong>("YFLOAT\r\n"u8))
                                {
                                    return RespCommand.INCRBYFLOAT;
                                }
                                else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("1\r\nZRANG"u8) && *(ulong*)(ptr + 10) == MemoryMarshal.Read<ulong>("ESTORE\r\n"u8))
                                {
                                    return RespCommand.ZRANGESTORE;
                                }
                                else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("1\r\nZRANG"u8) && *(ulong*)(ptr + 10) == MemoryMarshal.Read<ulong>("EBYLEX\r\n"u8))
                                {
                                    return RespCommand.ZRANGEBYLEX;
                                }
                                else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("1\r\nZINTE"u8) && *(ulong*)(ptr + 10) == MemoryMarshal.Read<ulong>("RSTORE\r\n"u8))
                                {
                                    return RespCommand.ZINTERSTORE;
                                }
                                else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("1\r\nZUNIO"u8) && *(ulong*)(ptr + 10) == MemoryMarshal.Read<ulong>("NSTORE\r\n"u8))
                                {
                                    return RespCommand.ZUNIONSTORE;
                                }
                                else if (*(ulong*)(ptr + 2) == MemoryMarshal.Read<ulong>("1\r\nZEXPI"u8) && *(ulong*)(ptr + 10) == MemoryMarshal.Read<ulong>("RETIME\r\n"u8))
                                {
                                    return RespCommand.ZEXPIRETIME;
                                }
                                break;

                            case 12:
                                if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\r\nPUNSUB"u8) && *(ulong*)(ptr + 11) == MemoryMarshal.Read<ulong>("SCRIBE\r\n"u8))
                                {
                                    return RespCommand.PUNSUBSCRIBE;
                                }
                                else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\r\nHINCRB"u8) && *(ulong*)(ptr + 11) == MemoryMarshal.Read<ulong>("YFLOAT\r\n"u8))
                                {
                                    return RespCommand.HINCRBYFLOAT;
                                }
                                else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\r\nHPEXPI"u8) && *(ulong*)(ptr + 11) == MemoryMarshal.Read<ulong>("RETIME\r\n"u8))
                                {
                                    return RespCommand.HPEXPIRETIME;
                                }
                                else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\r\nZPEXPI"u8) && *(ulong*)(ptr + 11) == MemoryMarshal.Read<ulong>("RETIME\r\n"u8))
                                {
                                    return RespCommand.ZPEXPIRETIME;
                                }
                                break;

                            case 13:
                                if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("\nZRANGEB"u8) && *(ulong*)(ptr + 12) == MemoryMarshal.Read<ulong>("YSCORE\r\n"u8))
                                {
                                    return RespCommand.ZRANGEBYSCORE;
                                }
                                break;

                            case 14:
                                if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\r\nZREMRA"u8) && *(ulong*)(ptr + 11) == MemoryMarshal.Read<ulong>("NGEBYLEX"u8) && *(ushort*)(ptr + 19) == MemoryMarshal.Read<ushort>("\r\n"u8))
                                {
                                    return RespCommand.ZREMRANGEBYLEX;
                                }
                                else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\r\nGEOSEA"u8) && *(ulong*)(ptr + 11) == MemoryMarshal.Read<ulong>("RCHSTORE"u8) && *(ushort*)(ptr + 19) == MemoryMarshal.Read<ushort>("\r\n"u8))
                                {
                                    return RespCommand.GEOSEARCHSTORE;
                                }
                                else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\r\nZREVRA"u8) && *(ulong*)(ptr + 11) == MemoryMarshal.Read<ulong>("NGEBYLEX"u8) && *(ushort*)(ptr + 19) == MemoryMarshal.Read<ushort>("\r\n"u8))
                                {
                                    return RespCommand.ZREVRANGEBYLEX;
                                }
                                break;

                            case 15:
                                if (*(ulong*)(ptr + 4) == MemoryMarshal.Read<ulong>("\nZREMRAN"u8) && *(ulong*)(ptr + 12) == MemoryMarshal.Read<ulong>("GEBYRANK"u8) && *(ushort*)(ptr + 20) == MemoryMarshal.Read<ushort>("\r\n"u8))
                                {
                                    return RespCommand.ZREMRANGEBYRANK;
                                }
                                break;

                            case 16:
                                //if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\r\nCUSTOM"u8) && *(ulong*)(ptr + 11) == MemoryMarshal.Read<ulong>("OBJECTSC"u8) && *(ushort*)(ptr + 19) == MemoryMarshal.Read<ushort>("AN\r\n"u8))
                                //{
                                //    return RespCommand.COSCAN;
                                //}
                                //else
                                if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\r\nZREMRA"u8) && *(ulong*)(ptr + 11) == MemoryMarshal.Read<ulong>("NGEBYSCO"u8) && *(ushort*)(ptr + 19) == MemoryMarshal.Read<ushort>("RE\r\n"u8))
                                {
                                    return RespCommand.ZREMRANGEBYSCORE;
                                }
                                else if (*(ulong*)(ptr + 3) == MemoryMarshal.Read<ulong>("\r\nZREVRA"u8) && *(ulong*)(ptr + 11) == MemoryMarshal.Read<ulong>("NGEBYSCO"u8) && *(ushort*)(ptr + 19) == MemoryMarshal.Read<ushort>("RE\r\n"u8))
                                {
                                    return RespCommand.ZREVRANGEBYSCORE;
                                }
                                break;
                        }

                        // Reset optimistically changed state, if no matching command was found
                        count += 1;
                        readHead = oldReadHead;
                    }
                }
            }

            // No matching command name found in this pass
            return RespCommand.None;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private RespCommand FastParseInlineCommand(out int count)
        {
            byte* ptr = recvBufferPtr + readHead;
            count = 0;

            if (bytesRead - readHead >= 6)
            {
                if ((*(ushort*)(ptr + 4) == MemoryMarshal.Read<ushort>("\r\n"u8)))
                {
                    // Optimistically increase read head
                    readHead += 6;

                    if ((*(uint*)ptr) == MemoryMarshal.Read<uint>("PING"u8))
                    {
                        return RespCommand.PING;
                    }

                    if ((*(uint*)ptr) == MemoryMarshal.Read<uint>("QUIT"u8))
                    {
                        return RespCommand.QUIT;
                    }

                    // Decrease read head, if no match was found
                    readHead -= 6;
                }
            }

            return RespCommand.None;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private RespCommand ArrayParseCommand(ref int count, ref bool success)
        {
            RespCommand cmd = RespCommand.Invalid;
            endReadHead = readHead;
            var ptr = recvBufferPtr + readHead;

            // See if input command is all upper-case. If not, convert and try fast parse pass again.
            if (MakeUpperCase(ptr))
            {
                cmd = FastParseCommand(out count);
                if (cmd != RespCommand.None)
                {
                    return cmd;
                }
            }

            // Ensure we are attempting to read a RESP array header
            if (recvBufferPtr[readHead] != '*')
            {
                // We might have received an inline command package. Skip until the end of the line in the input package.
                success = AttemptSkipLine();
                return RespCommand.Invalid;
            }

            // Read the array length
            if (!RespReadUtils_TryReadUnsignedArrayLength(out count, ref ptr, recvBufferPtr + bytesRead))
            {
                success = false;
                return RespCommand.Invalid;
            }

            // Move readHead to start of command payload
            readHead = (int)(ptr - recvBufferPtr);

            // Try parsing the most important variable-length commands
            cmd = FastParseArrayCommand(ref count);

            if (cmd == RespCommand.None)
            {
                cmd = SlowParseCommand(ref count, out success);
            }

            // Parsing for command name was successful, but the command is unknown
            //if (writeErrorOnFailure && success && cmd == RespCommand.INVALID)
            //{
            //    if (!specificErrorMessage.IsEmpty)
            //    {
            //        while (!RespWriteUtils.TryWriteError(specificErrorMessage, ref dcurr, dend))
            //            SendAndReset();
            //    }
            //    else
            //    {
            //        // Return "Unknown RESP Command" message
            //        while (!RespWriteUtils.TryWriteError(CmdStrings.RESP_ERR_GENERIC_UNK_CMD, ref dcurr, dend))
            //            SendAndReset();
            //    }
            //}
            return cmd;
        }

        private bool MakeUpperCase(byte* ptr)
        {
            // Assume most commands are already upper case.
            // Assume most commands are 2-8 bytes long.
            // If that's the case, we would see the following bit patterns:
            //  *.\r\n$2\r\n..\r\n        = 12 bytes
            //  *.\r\n$3\r\n...\r\n       = 13 bytes
            //  ...
            //  *.\r\n$8\r\n........\r\n  = 18 bytes
            //
            // Where . is <= 95
            // 
            // Note that _all_ of these bytes are <= 95 in the common case
            // and there's no need to scan the whole string in those cases.

            var len = bytesRead - readHead;
            if (len >= 12)
            {
                var cmdLen = (uint)(*(ptr + 5) - '2');
                if (cmdLen <= 6 && (ptr + 4 + cmdLen + sizeof(ulong)) <= (ptr + len))
                {
                    var firstUlong = *(ulong*)(ptr + 4);
                    var secondUlong = *((ulong*)ptr + 4 + cmdLen);

                    // Ye olde bit twiddling to check if any sub-byte is > 95
                    // See: https://graphics.stanford.edu/~seander/bithacks.html#HasMoreInWord
                    var firstAllUpper = (((firstUlong + (~0UL / 255 * (127 - 95))) | (firstUlong)) & (~0UL / 255 * 128)) == 0;
                    var secondAllUpper = (((secondUlong + (~0UL / 255 * (127 - 95))) | (secondUlong)) & (~0UL / 255 * 128)) == 0;

                    var allLower = firstAllUpper && secondAllUpper;
                    if (allLower)
                    {
                        // Nothing in the "command" part of the string would be upper cased, so return early
                        return false;
                    }
                }
            }

            // If we're in a weird case, or there are lower case bytes, do the full scan

            var tmp = ptr;

            while (tmp < (ptr + len))
            {
                if (*tmp > 64) // found string
                {
                    var ret = false;
                    while (*tmp > 64 && *tmp < 123 && tmp < (ptr + len))
                    {
                        if (*tmp > 96) { ret = true; *tmp -= 32; }
                        tmp++;
                    }
                    return ret;
                }
                tmp++;
            }
            return false;
        }

        private RespCommand SlowParseCommand(ref int count, out bool success)
        {
            // Try to extract the current string from the front of the read head
            var command = GetCommand(out success);

            if (!success)
            {
                return RespCommand.Invalid;
            }

            // Account for the command name being taken off the read head
            count -= 1;

            //if (TryParseCustomCommand(command, out var cmd))
            //{
            //    return cmd;
            //}
            //else
            {
                return SlowParseCommand(command, ref count, out success);
            }
        }

        private static RespCommand SlowParseCommand(ReadOnlySpan<byte> command, ref int count, out bool success)
        {
            success = true;
            if (command.SequenceEqual(CmdStrings.SUBSCRIBE))
            {
                return RespCommand.SUBSCRIBE;
            }
            else if (command.SequenceEqual(CmdStrings.SSUBSCRIBE))
            {
                return RespCommand.SSUBSCRIBE;
            }
            else if (command.SequenceEqual(CmdStrings.RUNTXP))
            {
                return RespCommand.RUNTXP;
            }
            //else if (command.SequenceEqual(CmdStrings.SCRIPT))
            //{
            //    if (count == 0)
            //    {
            //        specificErrorMsg = Encoding.ASCII.GetBytes(string.Format(CmdStrings.GenericErrWrongNumArgs,
            //            nameof(RespCommand.SCRIPT)));
            //        return RespCommand.Invalid;
            //    }

            //    var subCommand = GetUpperCaseCommand(out var gotSubCommand);
            //    if (!gotSubCommand)
            //    {
            //        success = false;
            //        return RespCommand.NONE;
            //    }

            //    count--;

            //    if (subCommand.SequenceEqual(CmdStrings.LOAD))
            //    {
            //        return RespCommand.SCRIPT_LOAD;
            //    }

            //    if (subCommand.SequenceEqual(CmdStrings.FLUSH))
            //    {
            //        return RespCommand.SCRIPT_FLUSH;
            //    }

            //    if (subCommand.SequenceEqual(CmdStrings.EXISTS))
            //    {
            //        return RespCommand.SCRIPT_EXISTS;
            //    }

            //    string errMsg = string.Format(CmdStrings.GenericErrUnknownSubCommandNoHelp,
            //                                  Encoding.UTF8.GetString(subCommand),
            //                                  nameof(RespCommand.SCRIPT));
            //    specificErrorMsg = Encoding.UTF8.GetBytes(errMsg);
            //    return RespCommand.INVALID;
            //}
            else if (command.SequenceEqual(CmdStrings.ECHO))
            {
                return RespCommand.ECHO;
            }
            else if (command.SequenceEqual(CmdStrings.GEORADIUS))
            {
                return RespCommand.GEORADIUS;
            }
            else if (command.SequenceEqual(CmdStrings.GEORADIUS_RO))
            {
                return RespCommand.GEORADIUS_RO;
            }
            else if (command.SequenceEqual(CmdStrings.GEORADIUSBYMEMBER))
            {
                return RespCommand.GEORADIUSBYMEMBER;
            }
            else if (command.SequenceEqual(CmdStrings.GEORADIUSBYMEMBER_RO))
            {
                return RespCommand.GEORADIUSBYMEMBER_RO;
            }
            else if (command.SequenceEqual(CmdStrings.REPLICAOF))
            {
                return RespCommand.REPLICAOF;
            }
            else if (command.SequenceEqual(CmdStrings.SECONDARYOF) || command.SequenceEqual(CmdStrings.SLAVEOF))
            {
                return RespCommand.SECONDARYOF;
            }
            //else if (command.SequenceEqual(CmdStrings.CONFIG))
            //{
            //    if (count == 0)
            //    {
            //        specificErrorMsg = Encoding.ASCII.GetBytes(string.Format(CmdStrings.GenericErrWrongNumArgs,
            //            nameof(RespCommand.CONFIG)));
            //        return RespCommand.INVALID;
            //    }

            //    var subCommand = GetUpperCaseCommand(out var gotSubCommand);
            //    if (!gotSubCommand)
            //    {
            //        success = false;
            //        return RespCommand.NONE;
            //    }

            //    count--;

            //    if (subCommand.SequenceEqual(CmdStrings.GET))
            //    {
            //        return RespCommand.CONFIG_GET;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.REWRITE))
            //    {
            //        return RespCommand.CONFIG_REWRITE;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.SET))
            //    {
            //        return RespCommand.CONFIG_SET;
            //    }

            //    string errMsg = string.Format(CmdStrings.GenericErrUnknownSubCommandNoHelp,
            //                                  Encoding.UTF8.GetString(subCommand),
            //                                  nameof(RespCommand.CONFIG));
            //    specificErrorMsg = Encoding.UTF8.GetBytes(errMsg);
            //    return RespCommand.INVALID;
            //}
            //else if (command.SequenceEqual(CmdStrings.CLIENT))
            //{
            //    if (count == 0)
            //    {
            //        specificErrorMsg = Encoding.ASCII.GetBytes(string.Format(CmdStrings.GenericErrWrongNumArgs,
            //            nameof(RespCommand.CLIENT)));
            //        return RespCommand.INVALID;
            //    }

            //    var subCommand = GetUpperCaseCommand(out var gotSubCommand);
            //    if (!gotSubCommand)
            //    {
            //        success = false;
            //        return RespCommand.NONE;
            //    }

            //    count--;

            //    if (subCommand.SequenceEqual(CmdStrings.ID))
            //    {
            //        return RespCommand.CLIENT_ID;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.INFO))
            //    {
            //        return RespCommand.CLIENT_INFO;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.LIST))
            //    {
            //        return RespCommand.CLIENT_LIST;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.KILL))
            //    {
            //        return RespCommand.CLIENT_KILL;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.GETNAME))
            //    {
            //        return RespCommand.CLIENT_GETNAME;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.SETNAME))
            //    {
            //        return RespCommand.CLIENT_SETNAME;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.SETINFO))
            //    {
            //        return RespCommand.CLIENT_SETINFO;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.UNBLOCK))
            //    {
            //        return RespCommand.CLIENT_UNBLOCK;
            //    }

            //    string errMsg = string.Format(CmdStrings.GenericErrUnknownSubCommandNoHelp,
            //                                  Encoding.UTF8.GetString(subCommand),
            //                                  nameof(RespCommand.CLIENT));
            //    specificErrorMsg = Encoding.UTF8.GetBytes(errMsg);
            //    return RespCommand.INVALID;
            //}
            else if (command.SequenceEqual(CmdStrings.AUTH))
            {
                return RespCommand.AUTH;
            }
            else if (command.SequenceEqual(CmdStrings.INFO))
            {
                return RespCommand.INFO;
            }
            else if (command.SequenceEqual(CmdStrings.ROLE))
            {
                return RespCommand.ROLE;
            }
            //else if (command.SequenceEqual(CmdStrings.COMMAND))
            //{
            //    if (count == 0)
            //    {
            //        return RespCommand.COMMAND;
            //    }

            //    var subCommand = GetUpperCaseCommand(out var gotSubCommand);
            //    if (!gotSubCommand)
            //    {
            //        success = false;
            //        return RespCommand.NONE;
            //    }

            //    count--;

            //    if (subCommand.SequenceEqual(CmdStrings.COUNT))
            //    {
            //        return RespCommand.COMMAND_COUNT;
            //    }

            //    if (subCommand.SequenceEqual(CmdStrings.INFO))
            //    {
            //        return RespCommand.COMMAND_INFO;
            //    }

            //    if (subCommand.SequenceEqual(CmdStrings.DOCS))
            //    {
            //        return RespCommand.COMMAND_DOCS;
            //    }

            //    if (subCommand.EqualsUpperCaseSpanIgnoringCase(CmdStrings.GETKEYS))
            //    {
            //        return RespCommand.COMMAND_GETKEYS;
            //    }

            //    if (subCommand.EqualsUpperCaseSpanIgnoringCase(CmdStrings.GETKEYSANDFLAGS))
            //    {
            //        return RespCommand.COMMAND_GETKEYSANDFLAGS;
            //    }

            //    string errMsg = string.Format(CmdStrings.GenericErrUnknownSubCommandNoHelp,
            //                                  Encoding.UTF8.GetString(subCommand),
            //                                  nameof(RespCommand.COMMAND));
            //    specificErrorMsg = Encoding.UTF8.GetBytes(errMsg);
            //    return RespCommand.INVALID;
            //}
            else if (command.SequenceEqual(CmdStrings.PING))
            {
                return RespCommand.PING;
            }
            else if (command.SequenceEqual(CmdStrings.HELLO))
            {
                return RespCommand.HELLO;
            }
            //else if (command.SequenceEqual(CmdStrings.CLUSTER))
            //{
            //    if (count == 0)
            //    {
            //        specificErrorMsg = Encoding.ASCII.GetBytes(string.Format(CmdStrings.GenericErrWrongNumArgs,
            //            nameof(RespCommand.CLUSTER)));
            //        return RespCommand.INVALID;
            //    }

            //    var subCommand = GetUpperCaseCommand(out var gotSubCommand);
            //    if (!gotSubCommand)
            //    {
            //        success = false;
            //        return RespCommand.NONE;
            //    }

            //    count--;

            //    if (subCommand.SequenceEqual(CmdStrings.BUMPEPOCH))
            //    {
            //        return RespCommand.CLUSTER_BUMPEPOCH;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.FORGET))
            //    {
            //        return RespCommand.CLUSTER_FORGET;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.gossip))
            //    {
            //        return RespCommand.CLUSTER_GOSSIP;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.INFO))
            //    {
            //        return RespCommand.CLUSTER_INFO;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.MEET))
            //    {
            //        return RespCommand.CLUSTER_MEET;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.MYID))
            //    {
            //        return RespCommand.CLUSTER_MYID;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.myparentid))
            //    {
            //        return RespCommand.CLUSTER_MYPARENTID;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.NODES))
            //    {
            //        return RespCommand.CLUSTER_NODES;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.SHARDS))
            //    {
            //        return RespCommand.CLUSTER_SHARDS;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.RESET))
            //    {
            //        return RespCommand.CLUSTER_RESET;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.FAILOVER))
            //    {
            //        return RespCommand.CLUSTER_FAILOVER;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.ADDSLOTS))
            //    {
            //        return RespCommand.CLUSTER_ADDSLOTS;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.ADDSLOTSRANGE))
            //    {
            //        return RespCommand.CLUSTER_ADDSLOTSRANGE;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.COUNTKEYSINSLOT))
            //    {
            //        return RespCommand.CLUSTER_COUNTKEYSINSLOT;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.DELSLOTS))
            //    {
            //        return RespCommand.CLUSTER_DELSLOTS;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.DELSLOTSRANGE))
            //    {
            //        return RespCommand.CLUSTER_DELSLOTSRANGE;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.GETKEYSINSLOT))
            //    {
            //        return RespCommand.CLUSTER_GETKEYSINSLOT;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.HELP))
            //    {
            //        return RespCommand.CLUSTER_HELP;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.KEYSLOT))
            //    {
            //        return RespCommand.CLUSTER_KEYSLOT;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.SETSLOT))
            //    {
            //        return RespCommand.CLUSTER_SETSLOT;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.SLOTS))
            //    {
            //        return RespCommand.CLUSTER_SLOTS;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.REPLICAS))
            //    {
            //        return RespCommand.CLUSTER_REPLICAS;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.REPLICATE))
            //    {
            //        return RespCommand.CLUSTER_REPLICATE;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.delkeysinslot))
            //    {
            //        return RespCommand.CLUSTER_DELKEYSINSLOT;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.delkeysinslotrange))
            //    {
            //        return RespCommand.CLUSTER_DELKEYSINSLOTRANGE;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.setslotsrange))
            //    {
            //        return RespCommand.CLUSTER_SETSLOTSRANGE;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.slotstate))
            //    {
            //        return RespCommand.CLUSTER_SLOTSTATE;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.publish))
            //    {
            //        return RespCommand.CLUSTER_PUBLISH;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.spublish))
            //    {
            //        return RespCommand.CLUSTER_SPUBLISH;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.MIGRATE))
            //    {
            //        return RespCommand.CLUSTER_MIGRATE;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.mtasks))
            //    {
            //        return RespCommand.CLUSTER_MTASKS;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.aofsync))
            //    {
            //        return RespCommand.CLUSTER_AOFSYNC;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.appendlog))
            //    {
            //        return RespCommand.CLUSTER_APPENDLOG;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.attach_sync))
            //    {
            //        return RespCommand.CLUSTER_ATTACH_SYNC;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.banlist))
            //    {
            //        return RespCommand.CLUSTER_BANLIST;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.begin_replica_recover))
            //    {
            //        return RespCommand.CLUSTER_BEGIN_REPLICA_RECOVER;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.endpoint))
            //    {
            //        return RespCommand.CLUSTER_ENDPOINT;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.failreplicationoffset))
            //    {
            //        return RespCommand.CLUSTER_FAILREPLICATIONOFFSET;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.failstopwrites))
            //    {
            //        return RespCommand.CLUSTER_FAILSTOPWRITES;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.FLUSHALL))
            //    {
            //        return RespCommand.CLUSTER_FLUSHALL;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.SETCONFIGEPOCH))
            //    {
            //        return RespCommand.CLUSTER_SETCONFIGEPOCH;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.initiate_replica_sync))
            //    {
            //        return RespCommand.CLUSTER_INITIATE_REPLICA_SYNC;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.send_ckpt_file_segment))
            //    {
            //        return RespCommand.CLUSTER_SEND_CKPT_FILE_SEGMENT;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.send_ckpt_metadata))
            //    {
            //        return RespCommand.CLUSTER_SEND_CKPT_METADATA;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.cluster_sync))
            //    {
            //        return RespCommand.CLUSTER_SYNC;
            //    }

            //    string errMsg = string.Format(CmdStrings.GenericErrUnknownSubCommand,
            //                                  Encoding.UTF8.GetString(subCommand),
            //                                  nameof(RespCommand.CLUSTER));
            //    specificErrorMsg = Encoding.UTF8.GetBytes(errMsg);
            //    return RespCommand.INVALID;
            //}
            //else if (command.SequenceEqual(CmdStrings.LATENCY))
            //{
            //    if (count == 0)
            //    {
            //        specificErrorMsg = Encoding.ASCII.GetBytes(string.Format(CmdStrings.GenericErrWrongNumArgs,
            //            nameof(RespCommand.LATENCY)));
            //        return RespCommand.INVALID;
            //    }

            //    var subCommand = GetUpperCaseCommand(out var gotSubCommand);
            //    if (!gotSubCommand)
            //    {
            //        success = false;
            //        return RespCommand.NONE;
            //    }

            //    count--;

            //    if (subCommand.SequenceEqual(CmdStrings.HELP))
            //    {
            //        return RespCommand.LATENCY_HELP;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.HISTOGRAM))
            //    {
            //        return RespCommand.LATENCY_HISTOGRAM;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.RESET))
            //    {
            //        return RespCommand.LATENCY_RESET;
            //    }

            //    string errMsg = string.Format(CmdStrings.GenericErrUnknownSubCommand,
            //                                  Encoding.UTF8.GetString(subCommand),
            //                                  nameof(RespCommand.LATENCY));
            //    specificErrorMsg = Encoding.UTF8.GetBytes(errMsg);
            //    return RespCommand.INVALID;
            //}
            //else if (command.SequenceEqual(CmdStrings.SLOWLOG))
            //{
            //    if (count == 0)
            //    {
            //        specificErrorMsg = Encoding.ASCII.GetBytes(string.Format(CmdStrings.GenericErrWrongNumArgs,
            //            nameof(RespCommand.SLOWLOG)));
            //    }
            //    else if (count >= 1)
            //    {
            //        var subCommand = GetUpperCaseCommand(out var gotSubCommand);
            //        if (!gotSubCommand)
            //        {
            //            success = false;
            //            return RespCommand.NONE;
            //        }

            //        count--;

            //        if (subCommand.SequenceEqual(CmdStrings.HELP))
            //        {
            //            return RespCommand.SLOWLOG_HELP;
            //        }
            //        else if (subCommand.SequenceEqual(CmdStrings.GET))
            //        {
            //            return RespCommand.SLOWLOG_GET;
            //        }
            //        else if (subCommand.SequenceEqual(CmdStrings.LEN))
            //        {
            //            return RespCommand.SLOWLOG_LEN;
            //        }
            //        else if (subCommand.SequenceEqual(CmdStrings.RESET))
            //        {
            //            return RespCommand.SLOWLOG_RESET;
            //        }
            //    }
            //}
            else if (command.SequenceEqual(CmdStrings.TIME))
            {
                return RespCommand.TIME;
            }
            else if (command.SequenceEqual(CmdStrings.QUIT))
            {
                return RespCommand.QUIT;
            }
            else if (command.SequenceEqual(CmdStrings.SAVE))
            {
                return RespCommand.SAVE;
            }
            else if (command.SequenceEqual(CmdStrings.LASTSAVE))
            {
                return RespCommand.LASTSAVE;
            }
            else if (command.SequenceEqual(CmdStrings.BGSAVE))
            {
                return RespCommand.BGSAVE;
            }
            else if (command.SequenceEqual(CmdStrings.COMMITAOF))
            {
                return RespCommand.COMMITAOF;
            }
            else if (command.SequenceEqual(CmdStrings.FLUSHALL))
            {
                return RespCommand.FLUSHALL;
            }
            else if (command.SequenceEqual(CmdStrings.FLUSHDB))
            {
                return RespCommand.FLUSHDB;
            }
            else if (command.SequenceEqual(CmdStrings.FORCEGC))
            {
                return RespCommand.FORCEGC;
            }
            else if (command.SequenceEqual(CmdStrings.MIGRATE))
            {
                return RespCommand.MIGRATE;
            }
            else if (command.SequenceEqual(CmdStrings.PURGEBP))
            {
                return RespCommand.PURGEBP;
            }
            else if (command.SequenceEqual(CmdStrings.FAILOVER))
            {
                return RespCommand.FAILOVER;
            }
            //else if (command.SequenceEqual(CmdStrings.MEMORY))
            //{
            //    if (count == 0)
            //    {
            //        specificErrorMsg = Encoding.ASCII.GetBytes(string.Format(CmdStrings.GenericErrWrongNumArgs,
            //            nameof(RespCommand.MEMORY)));
            //        return RespCommand.INVALID;
            //    }

            //    var subCommand = GetUpperCaseCommand(out var gotSubCommand);
            //    if (!gotSubCommand)
            //    {
            //        success = false;
            //        return RespCommand.NONE;
            //    }

            //    count--;

            //    if (subCommand.EqualsUpperCaseSpanIgnoringCase(CmdStrings.USAGE))
            //    {
            //        return RespCommand.MEMORY_USAGE;
            //    }

            //    string errMsg = string.Format(CmdStrings.GenericErrUnknownSubCommandNoHelp,
            //                                  Encoding.UTF8.GetString(subCommand),
            //                                  nameof(RespCommand.MEMORY));
            //    specificErrorMsg = Encoding.UTF8.GetBytes(errMsg);
            //    return RespCommand.INVALID;
            //}
            else if (command.SequenceEqual(CmdStrings.MONITOR))
            {
                return RespCommand.MONITOR;
            }
            //else if (command.SequenceEqual(CmdStrings.ACL))
            //{
            //    if (count == 0)
            //    {
            //        specificErrorMsg = Encoding.ASCII.GetBytes(string.Format(CmdStrings.GenericErrWrongNumArgs,
            //            nameof(RespCommand.ACL)));
            //        return RespCommand.INVALID;
            //    }

            //    var subCommand = GetUpperCaseCommand(out var gotSubCommand);
            //    if (!gotSubCommand)
            //    {
            //        success = false;
            //        return RespCommand.NONE;
            //    }

            //    count--;

            //    if (subCommand.SequenceEqual(CmdStrings.CAT))
            //    {
            //        return RespCommand.ACL_CAT;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.DELUSER))
            //    {
            //        return RespCommand.ACL_DELUSER;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.GETUSER))
            //    {
            //        return RespCommand.ACL_GETUSER;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.LIST))
            //    {
            //        return RespCommand.ACL_LIST;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.LOAD))
            //    {
            //        return RespCommand.ACL_LOAD;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.SAVE))
            //    {
            //        return RespCommand.ACL_SAVE;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.SETUSER))
            //    {
            //        return RespCommand.ACL_SETUSER;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.USERS))
            //    {
            //        return RespCommand.ACL_USERS;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.WHOAMI))
            //    {
            //        return RespCommand.ACL_WHOAMI;
            //    }

            //    string errMsg = string.Format(CmdStrings.GenericErrUnknownSubCommandNoHelp,
            //                                  Encoding.UTF8.GetString(subCommand),
            //                                  nameof(RespCommand.ACL));
            //    specificErrorMsg = Encoding.UTF8.GetBytes(errMsg);
            //    return RespCommand.INVALID;
            //}
            //else if (command.SequenceEqual(CmdStrings.REGISTERCS))
            //{
            //    return RespCommand.REGISTERCS;
            //}
            //else if (command.SequenceEqual(CmdStrings.ASYNC))
            //{
            //    return RespCommand.ASYNC;
            //}
            //else if (command.SequenceEqual(CmdStrings.MODULE))
            //{
            //    if (count == 0)
            //    {
            //        specificErrorMsg = Encoding.ASCII.GetBytes(string.Format(CmdStrings.GenericErrWrongNumArgs,
            //            nameof(RespCommand.MODULE)));
            //        return RespCommand.INVALID;
            //    }

            //    var subCommand = GetUpperCaseCommand(out var gotSubCommand);
            //    if (!gotSubCommand)
            //    {
            //        success = false;
            //        return RespCommand.NONE;
            //    }

            //    count--;

            //    if (subCommand.SequenceEqual(CmdStrings.LOADCS))
            //    {
            //        return RespCommand.MODULE_LOADCS;
            //    }

            //    string errMsg = string.Format(CmdStrings.GenericErrUnknownSubCommandNoHelp,
            //                                  Encoding.UTF8.GetString(subCommand),
            //                                  nameof(RespCommand.MODULE));
            //    specificErrorMsg = Encoding.UTF8.GetBytes(errMsg);
            //    return RespCommand.INVALID;
            //}
            //else if (command.SequenceEqual(CmdStrings.PUBSUB))
            //{
            //    if (count == 0)
            //    {
            //        specificErrorMsg = Encoding.ASCII.GetBytes(string.Format(CmdStrings.GenericErrWrongNumArgs,
            //            nameof(RespCommand.PUBSUB)));
            //        return RespCommand.INVALID;
            //    }

            //    var subCommand = GetUpperCaseCommand(out var gotSubCommand);
            //    if (!gotSubCommand)
            //    {
            //        success = false;
            //        return RespCommand.NONE;
            //    }

            //    count--;

            //    if (subCommand.SequenceEqual(CmdStrings.CHANNELS))
            //    {
            //        return RespCommand.PUBSUB_CHANNELS;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.NUMSUB))
            //    {
            //        return RespCommand.PUBSUB_NUMSUB;
            //    }
            //    else if (subCommand.SequenceEqual(CmdStrings.NUMPAT))
            //    {
            //        return RespCommand.PUBSUB_NUMPAT;
            //    }

            //    string errMsg = string.Format(CmdStrings.GenericErrUnknownSubCommandNoHelp,
            //                                  Encoding.UTF8.GetString(subCommand),
            //                                  nameof(RespCommand.PUBSUB));
            //    specificErrorMsg = Encoding.UTF8.GetBytes(errMsg);
            //    return RespCommand.INVALID;
            //}
            else if (command.SequenceEqual(CmdStrings.HCOLLECT))
            {
                return RespCommand.HCOLLECT;
            }
            else if (command.SequenceEqual(CmdStrings.DEBUG))
            {
                return RespCommand.DEBUG;
            }
            else if (command.SequenceEqual(CmdStrings.ZCOLLECT))
            {
                return RespCommand.ZCOLLECT;
            }
            // Note: The commands below are not slow path commands, so they should probably move to earlier.
            //else if (command.SequenceEqual(CmdStrings.SETIFMATCH))
            //{
            //    return RespCommand.SETIFMATCH;
            //}
            //else if (command.SequenceEqual(CmdStrings.SETIFGREATER))
            //{
            //    return RespCommand.SETIFGREATER;
            //}
            else if (command.SequenceEqual(CmdStrings.GETWITHETAG))
            {
                return RespCommand.GETWITHETAG;
            }
            else if (command.SequenceEqual(CmdStrings.GETIFNOTMATCH))
            {
                return RespCommand.GETIFNOTMATCH;
            }

            // If this command name was not known to the slow pass, we are out of options and the command is unknown.
            return RespCommand.Invalid;
        }

        /// <summary>
        /// Attempts to skip to the end of the line ("\r\n") under the current read head.
        /// </summary>
        /// <returns>True if string terminator was found and readHead and endReadHead was changed, otherwise false. </returns>
        private bool AttemptSkipLine()
        {
            // We might have received an inline command package.Try to find the end of the line.

            for (int stringEnd = readHead; stringEnd < bytesRead - 1; stringEnd++)
            {
                if (recvBufferPtr[stringEnd] == '\r' && recvBufferPtr[stringEnd + 1] == '\n')
                {
                    // Skip to the end of the string
                    readHead = endReadHead = stringEnd + 2;
                    return true;
                }
            }

            // We received an incomplete string and require more input.
            return false;
        }

        private ReadOnlySpan<byte> GetCommand(out bool success)
        {
            var ptr = recvBufferPtr + readHead;
            var end = recvBufferPtr + bytesRead;

            // Try the command length
            if (!RespReadUtils_TryReadUnsignedLengthHeader(out int length, ref ptr, end))
            {
                success = false;
                return default;
            }

            readHead = (int)(ptr - recvBufferPtr);

            // Try to read the command value
            ptr += length;
            if (ptr + 2 > end)
            {
                success = false;
                return default;
            }

            if (*(ushort*)ptr != MemoryMarshal.Read<ushort>("\r\n"u8))
            {
                //RespParsingException.ThrowUnexpectedToken(*ptr);
                throw new Exception($"Unexpected token: {(char)*ptr}");
            }

            var result = new ReadOnlySpan<byte>(recvBufferPtr + readHead, length);
            readHead += length + 2;
            success = true;

            return result;
        }

        public static bool RespReadUtils_TryReadUnsignedArrayLength(out int length, ref byte* ptr, byte* end)
            => RespReadUtils_TryReadUnsignedLengthHeader(out length, ref ptr, end, expectedSigil: '*');

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RespReadUtils_TryReadUnsignedLengthHeader(out int length, ref byte* ptr, byte* end, char expectedSigil = '$')
        {
            length = -1;
            if (ptr + 3 > end)
                return false;

            var readHead = ptr + 1;
            var negative = *readHead == '-';

            if (negative)
            {
                //RespParsingException.ThrowInvalidStringLength(length);
                throw new Exception($"Invalid string length: {length}");
            }

            if (!RespReadUtils_TryReadSignedLengthHeader(out length, ref ptr, end, expectedSigil))
                return false;

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RespReadUtils_TryReadSignedLengthHeader(out int length, ref byte* ptr, byte* end, char expectedSigil = '$')
        {
            length = -1;
            if (ptr + 3 > end)
                return false;

            var readHead = ptr + 1;
            var negative = *readHead == '-';

            // String length headers must start with a '$', array headers with '*'
            if (*ptr != expectedSigil)
            {
                throw new Exception($"Unexpected token: {(char)*ptr}");
                //RespParsingException.ThrowUnexpectedToken(*ptr);
            }

            // Special case: '$-1' (NULL value)
            if (negative)
            {
                if (readHead + 4 > end)
                {
                    return false;
                }

                if (*(uint*)readHead == MemoryMarshal.Read<uint>("-1\r\n"u8))
                {
                    ptr = readHead + 4;
                    return true;
                }
                readHead++;
            }

            // Parse length
            if (!RespReadUtils_TryReadUInt64(ref readHead, end, out var value, out var digitsRead))
            {
                return false;
            }

            if (digitsRead == 0)
            {
                //RespParsingException.ThrowUnexpectedToken(*readHead);
                throw new Exception($"Unexpected token: {(char)*ptr}");
            }

            // Validate length
            if (value > int.MaxValue && (!negative || value > int.MaxValue + (ulong)1)) // int.MinValue = -(int.MaxValue + 1)
            {
                //RespParsingException.ThrowIntegerOverflow(readHead - digitsRead, (int)digitsRead);
                throw new Exception("Integer overflow");
            }

            // Convert to signed value
            length = negative ? -(int)value : (int)value;

            // Ensure terminator has been received
            ptr = readHead + 2;
            if (ptr > end)
            {
                return false;
            }

            if (*(ushort*)readHead != MemoryMarshal.Read<ushort>("\r\n"u8))
            {
                //RespParsingException.ThrowUnexpectedToken(*ptr);
                throw new Exception($"Unexpected token: {(char)*ptr}");
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RespReadUtils_TryReadUInt64(ref byte* ptr, byte* end, out ulong value, out ulong bytesRead)
        {
            bytesRead = 0;
            value = 0;
            var readHead = ptr;

            // Fast path for the first 19 digits.
            // NOTE: UINT64 overflows can only happen on digit 20 or later (if integer contains leading zeros).
            var fastPathEnd = ptr + 19;
            while (readHead < fastPathEnd)
            {
                if (readHead > end)
                {
                    return false;
                }

                var nextDigit = (uint)(*readHead - '0');
                if (nextDigit > 9 || readHead == end)
                {
                    goto Done;
                }

                value = (10 * value) + nextDigit;

                readHead++;
            }

            // Parse remaining digits, while checking for overflows.
            while (true)
            {
                if (readHead > end)
                {
                    return false;
                }

                var nextDigit = (uint)(*readHead - '0');
                if (nextDigit > 9 || readHead == end)
                {
                    goto Done;
                }

                if ((value == 1844674407370955161UL && ((int)nextDigit > 5)) || (value > 1844674407370955161UL))
                {
                    //RespParsingException.ThrowIntegerOverflow(ptr, (int)(readHead - ptr));
                    throw new Exception("Integer overflow");
                }

                value = (10 * value) + nextDigit;

                readHead++;
            }

        Done:
            bytesRead = (ulong)(readHead - ptr);
            ptr = readHead;

            // this is added, as Garnet doesn't reject leading 0s today
            // ulong.MaxValue = 18_446_744_073_709_551_615 = 
            var minimumValue =
                bytesRead switch
                {
                    2 => 10UL,
                    3 => 100UL,
                    4 => 1_000UL,
                    5 => 10_000UL,
                    6 => 100_000UL,
                    7 => 1_000_000UL,
                    8 => 10_000_000UL,
                    9 => 100_000_000UL,
                    10 => 1_000_000_000UL,
                    11 => 10_000_000_000UL,
                    12 => 100_000_000_000UL,
                    13 => 1_000_000_000_000UL,
                    14 => 10_000_000_000_000UL,
                    15 => 100_000_000_000_000UL,
                    16 => 1_000_000_000_000_000UL,
                    17 => 10_000_000_000_000_000UL,
                    18 => 100_000_000_000_000_000UL,
                    19 => 1_000_000_000_000_000_000UL,
                    20 => 10_000_000_000_000_000_000UL,
                    _ => 0U,
                };

            if (value < minimumValue)
            {
                throw new Exception("Leading zero");
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SessionParseState_Read(int i, ref byte* ptr, byte* end, out byte* argDataStart, out byte* argDataEnd)
        {
            //Debug.Assert(i < Count);
            //ref var slice = ref Unsafe.AsRef<ArgSlice>(bufferPtr + i);

            // Parse RESP string header

            if (!RespReadUtils_TryReadUnsignedLengthHeader(out var len, ref ptr, end))
            {
                argDataStart = null;
                argDataEnd = null;
                return false;
            }

            argDataStart = ptr;

            // Parse content: ensure that input contains key + '\r\n'
            ptr += len + 2;
            if (ptr > end)
            {
                argDataStart = null;
                argDataEnd = null;
                return false;
            }

            if (*(ushort*)(ptr - 2) != MemoryMarshal.Read<ushort>("\r\n"u8))
            {
                throw new Exception($"Unexpected token {(char)*(ptr - 2)}");
                //RespParsingException.ThrowUnexpectedToken(*(ptr - 2));
            }

            argDataEnd = ptr;

            return true;
        }
    }
}
