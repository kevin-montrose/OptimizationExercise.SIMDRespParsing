using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace OptimizationExercise.SIMDRespParsing
{
    public static class NaiveParser
    {
        private const byte ArrayStart = (byte)'*';
        private const byte BulkStringStart = (byte)'$';
        private const byte CR = (byte)'\r';
        private const byte LF = (byte)'\n';
        private const byte ZERO = (byte)'0';
        private const byte NINE = (byte)'9';

        public static void Parse(
            ReadOnlySpan<byte> commandBufferTotalSpan,
            Span<ParsedRespCommandOrArgument> intoCommandsSpan,
            out int intoCommandsSlotsUsed,
            out int bytesConsumed
        )
        {
            Debug.Assert(!commandBufferTotalSpan.IsEmpty, "Shouldn't call when empty");
            Debug.Assert(!intoCommandsSpan.IsEmpty, "Must have space for at least one result");

            var currentCommandStartIx = 0;
            var remainingIntoCommandsSpan = intoCommandsSpan;

            while (currentCommandStartIx < commandBufferTotalSpan.Length && !remainingIntoCommandsSpan.IsEmpty)
            {
                if (!TryParseSingleCommand(commandBufferTotalSpan, ref currentCommandStartIx, ref remainingIntoCommandsSpan))
                {
                    break;
                }
            }

            // indicate how much we consumed and how much we created
            intoCommandsSlotsUsed = intoCommandsSpan.Length - remainingIntoCommandsSpan.Length;
            bytesConsumed = intoCommandsSlotsUsed == 0 ? 0 : intoCommandsSpan[intoCommandsSlotsUsed - 1].ByteEnd;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryParseSingleCommand(ReadOnlySpan<byte> commandBuffer, ref int commandStartIx, ref Span<ParsedRespCommandOrArgument> into)
        {
            Debug.Assert(!commandBuffer.IsEmpty, "Need to be able to read at least one byte");
            Debug.Assert(!into.IsEmpty, "Need space to store at least one entry");

            var remainingLength = commandBuffer.Length - commandStartIx;

            if (remainingLength < 4) // *0\r\n
            {
                // incomplete data
                return false;
            }

            if (commandBuffer[commandStartIx] != ArrayStart)
            {
                // missing *
                goto returnMalformed;
            }

            var arrayCrPosition = commandBuffer[(commandStartIx + 1)..].IndexOf(CR);
            if (arrayCrPosition == -1 && commandBuffer.Length > 11) // *0123456789
            {
                // should have found a \r by now
                goto returnMalformed;
            }
            arrayCrPosition += 1 + commandStartIx;

            if (arrayCrPosition + 1 >= commandBuffer.Length)
            {
                // need to wait for \r\n
                return false;
            }

            if (commandBuffer[arrayCrPosition + 1] != LF)
            {
                // missing \n
                goto returnMalformed;
            }

            var arrayItemCountSpan = commandBuffer[(commandStartIx + 1)..arrayCrPosition];
            if (!int.TryParse(arrayItemCountSpan, NumberStyles.None, null, out var arrayItemCount))
            {
                // not a number, or doesn't fit in an integer
                goto returnMalformed;
            }

            if (arrayItemCount <= 0 || arrayItemCountSpan.Length > 1 && arrayItemCountSpan[0] == '0')
            {
                // invalid length, or has a leading 0 (which is illegal!)
                goto returnMalformed;
            }

            if (arrayItemCount > into.Length)
            {
                // insufficient space
                return false;
            }

            var currentBulkStringStartIx = arrayCrPosition + 2;

            for (var i = 0; i < arrayItemCount; i++)
            {
                if (currentBulkStringStartIx >= commandBuffer.Length)
                {
                    // need to wait for more data
                    return false;
                }

                if (commandBuffer[currentBulkStringStartIx] != BulkStringStart)
                {
                    // expected a $
                    goto returnMalformed;
                }

                var bulkStringCrPosition = commandBuffer[(currentBulkStringStartIx + 1)..].IndexOf(CR);
                if (bulkStringCrPosition == -1)
                {
                    var remainingLen = commandBuffer.Length - currentBulkStringStartIx;
                    if (remainingLen > 11)  // $0123456789
                    {
                        // should have a \r by now
                        goto returnMalformed;
                    }

                    // need to wait for more data
                    return false;
                }
                bulkStringCrPosition += (currentBulkStringStartIx + 1);

                if (bulkStringCrPosition + 1 >= commandBuffer.Length)
                {
                    // need to wait for \n
                    return false;
                }

                if (commandBuffer[bulkStringCrPosition + 1] != LF)
                {
                    // missing \n
                    goto returnMalformed;
                }

                var bulkStringLengthSpan = commandBuffer[(currentBulkStringStartIx + 1)..bulkStringCrPosition];
                if (!int.TryParse(bulkStringLengthSpan, NumberStyles.None, null, out var bulkStringLength))
                {
                    // not a number, or doesn't fit in an int
                    goto returnMalformed;
                }

                if (bulkStringLength < 0 || bulkStringLengthSpan.Length > 1 && bulkStringLengthSpan[0] == '0')
                {
                    // invalid length, or leading 0 (which is illegal!)
                    goto returnMalformed;
                }

                var startOfBulkStringData = bulkStringCrPosition + 2;
                var endOfBulkStringData = startOfBulkStringData + bulkStringLength;

                if (endOfBulkStringData + 2 > commandBuffer.Length)
                {
                    // need to wait for rest of data
                    return false;
                }

                if (commandBuffer[endOfBulkStringData] != CR || commandBuffer[endOfBulkStringData + 1] != LF)
                {
                    // missing \r\n
                    goto returnMalformed;
                }

                // we include the \r\n so bytes consumed can be 
                into[i] = ParsedRespCommandOrArgument.ForArgument(startOfBulkStringData, endOfBulkStringData + 2);

                currentBulkStringStartIx = endOfBulkStringData + 2;
            }

            // naively parsing the command kinda stinks, since we have bytes but we have to work in chars
            //
            // so copy, and then exclude digits
            var cmdSpan = commandBuffer[into[0].ByteStart..(into[0].ByteEnd - 2)];
            Span<char> charSpan = stackalloc char[cmdSpan.Length];
            Encoding.ASCII.GetChars(cmdSpan, charSpan);

            if (!Enum.TryParse<RespCommand>(charSpan, ignoreCase: true, out var parsedCmd) || parsedCmd == RespCommand.None || parsedCmd == RespCommand.Invalid)
            {
                goto returnMalformed;
            }

            // Enum will accept the integer equivalent, so reject that
            if (cmdSpan.ContainsAnyInRange((byte)'0', (byte)'9'))
            {
                goto returnMalformed;
            }

            into[0] = ParsedRespCommandOrArgument.ForCommand(parsedCmd, arrayItemCount, into[0].ByteStart, into[0].ByteEnd);

            // advance buffers
            commandStartIx = into[arrayItemCount - 1].ByteEnd;
            into = into[arrayItemCount..];
            return true;

        returnMalformed:
            into[0] = ParsedRespCommandOrArgument.Malformed;
            into = into[1..];
            return false;
        }
    }
}
