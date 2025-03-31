using System.Text;

namespace OptimizationExercise.SIMDRespParsing.Tests
{
    public class ParseMultipleTests
    {
        [Fact]
        public void Simple()
        {
            Span<byte> commandBuffer = "*1\r\n$4\r\nPING\r\n*2\r\n$3\r\nGET\r\n$5\r\nworld\r\n"u8.ToArray();

            var into = new ParsedRespCommandOrArgument[5];

            // Our impl
            {
                into.AsSpan().Clear();

                RespParser.Parse(commandBuffer, into, out var slotsUsed, out var bytesConsumed);

                // fully consumed
                Assert.Equal(5, slotsUsed);
                Assert.Equal(commandBuffer.Length, bytesConsumed);

                // PING
                Assert.True(into[0].IsCommand);
                Assert.Equal(1, into[0].ArgumentCount);
                Assert.True("*1\r\n"u8.SequenceEqual(commandBuffer[into[0].ByteStart..into[0].ByteEnd]));

                Assert.True(into[1].IsArgument);
                Assert.True("PING\r\n"u8.SequenceEqual(commandBuffer[into[1].ByteStart..into[1].ByteEnd]));

                // GET world
                Assert.True(into[2].IsCommand);
                Assert.Equal(2, into[2].ArgumentCount);
                Assert.True("*2\r\n"u8.SequenceEqual(commandBuffer[into[2].ByteStart..into[2].ByteEnd]));

                Assert.True(into[3].IsArgument);
                Assert.True("GET\r\n"u8.SequenceEqual(commandBuffer[into[3].ByteStart..into[3].ByteEnd]));

                Assert.True(into[4].IsArgument);
                Assert.True("world\r\n"u8.SequenceEqual(commandBuffer[into[4].ByteStart..into[4].ByteEnd]));
            }

            // Garnet impl
            {
                into.AsSpan().Clear();

                var garnetParser = new GarnetParser();

                garnetParser.Parse(commandBuffer, into, out var slotsUsed, out var bytesConsumed);

                // fully consumed
                Assert.Equal(3, slotsUsed);
                Assert.Equal(commandBuffer.Length, bytesConsumed);

                // PING
                Assert.True(into[0].IsCommand);
                Assert.Equal(1, into[0].ArgumentCount);
                Assert.True("*1\r\n$4\r\nPING\r\n"u8.SequenceEqual(commandBuffer[into[0].ByteStart..into[0].ByteEnd]));

                // GET world
                Assert.True(into[1].IsCommand);
                Assert.Equal(2, into[1].ArgumentCount);
                Assert.True("*2\r\n$3\r\nGET\r\n"u8.SequenceEqual(commandBuffer[into[1].ByteStart..into[1].ByteEnd]));

                Assert.True(into[2].IsArgument);
                Assert.True("world\r\n"u8.SequenceEqual(commandBuffer[into[2].ByteStart..into[2].ByteEnd]));
            }
        }

        [Fact]
        public void AllCommands()
        {
            var garnetParser = new GarnetParser();

            foreach (var cmd in Enum.GetValues<RespCommand>())
            {
                if (cmd is RespCommand.None or RespCommand.Invalid)
                {
                    continue;
                }

                var asStr = cmd.ToString();

                Span<byte> commandBuffer = Encoding.UTF8.GetBytes($"*1\r\n${asStr.Length}\r\n{asStr}\r\n*2\r\n${asStr.Length}\r\n{asStr}\r\n$3\r\nbar\r\n");

                var into = new ParsedRespCommandOrArgument[5];

                // Our impl
                {
                    into.AsSpan().Clear();

                    RespParser.Parse(commandBuffer, into, out var slotsUsed, out var bytesConsumed);

                    // fully consumed
                    Assert.Equal(5, slotsUsed);
                    Assert.Equal(commandBuffer.Length, bytesConsumed);

                    // First, no arg
                    Assert.True(into[0].IsCommand);
                    Assert.Equal(1, into[0].ArgumentCount);
                    Assert.True("*1\r\n"u8.SequenceEqual(commandBuffer[into[0].ByteStart..into[0].ByteEnd]));

                    Assert.True(into[1].IsArgument);
                    Assert.True(Encoding.UTF8.GetBytes($"{asStr}\r\n").AsSpan().SequenceEqual(commandBuffer[into[1].ByteStart..into[1].ByteEnd]));

                    // Second, with arg
                    Assert.True(into[2].IsCommand);
                    Assert.Equal(2, into[2].ArgumentCount);
                    Assert.True("*2\r\n"u8.SequenceEqual(commandBuffer[into[2].ByteStart..into[2].ByteEnd]));

                    Assert.True(into[3].IsArgument);
                    Assert.True(Encoding.UTF8.GetBytes($"{asStr}\r\n").AsSpan().SequenceEqual(commandBuffer[into[3].ByteStart..into[3].ByteEnd]));

                    Assert.True(into[4].IsArgument);
                    Assert.True("bar\r\n"u8.SequenceEqual(commandBuffer[into[4].ByteStart..into[4].ByteEnd]));
                }

                // Garnet impl

                // these all care about argument counts, so just skip them
                var isSpecialCased =
                    cmd is RespCommand.PING or RespCommand.EXEC or RespCommand.MULTI
                    or RespCommand.ASKING or RespCommand.DISCARD or RespCommand.UNWATCH or RespCommand.READONLY
                    or RespCommand.READWRITE or RespCommand.GET or RespCommand.DEL or RespCommand.TTL
                    or RespCommand.DUMP or RespCommand.INCR or RespCommand.DECR or RespCommand.PTTL or RespCommand.EXISTS
                    or RespCommand.GETDEL or RespCommand.PERSIST or RespCommand.PFCOUNT or RespCommand.SET
                    or RespCommand.PFADD or RespCommand.INCRBY or RespCommand.DECRBY or RespCommand.GETBIT
                    or RespCommand.APPEND or RespCommand.GETSET or RespCommand.PUBLISH or RespCommand.PFMERGE
                    or RespCommand.PUBLISH or RespCommand.SETEX or RespCommand.PSETEX or RespCommand.SETBIT
                    or RespCommand.SUBSTR or RespCommand.RESTORE or RespCommand.SETRANGE or RespCommand.GETRANGE
                    or RespCommand.RENAME or RespCommand.RENAMENX or RespCommand.GETEX or RespCommand.EXPIRE or RespCommand.BITPOS
                    or RespCommand.PEXPIRE or RespCommand.BITCOUNT;
                if (!isSpecialCased)
                {
                    into.AsSpan().Clear();

                    garnetParser.Parse(commandBuffer, into, out var slotsUsed, out var bytesConsumed);

                    // fully consumed
                    Assert.Equal(3, slotsUsed);
                    Assert.Equal(commandBuffer.Length, bytesConsumed);

                    // 1st, no arg
                    Assert.True(into[0].IsCommand);
                    Assert.Equal(1, into[0].ArgumentCount);
                    Assert.True(Encoding.UTF8.GetBytes($"*1\r\n${asStr.Length}\r\n{asStr}\r\n").AsSpan().SequenceEqual(commandBuffer[into[0].ByteStart..into[0].ByteEnd]));

                    // 2nd, with arg
                    Assert.True(into[1].IsCommand);
                    Assert.Equal(2, into[1].ArgumentCount);
                    Assert.True(Encoding.UTF8.GetBytes($"*2\r\n${asStr.Length}\r\n{asStr}\r\n").AsSpan().SequenceEqual(commandBuffer[into[1].ByteStart..into[1].ByteEnd]));

                    Assert.True(into[2].IsArgument);
                    Assert.True("bar\r\n"u8.SequenceEqual(commandBuffer[into[2].ByteStart..into[2].ByteEnd]));
                }
            }
        }

        [Fact]
        public void Partial()
        {
            Span<byte> fullBuffer = "*1\r\n$4\r\nPING\r\n*2\r\n$3\r\nGET\r\n$5\r\nworld\r\n"u8.ToArray();

            var into = new ParsedRespCommandOrArgument[5];

            var garnetParser = new GarnetParser();

            for (var truncate = 0; truncate < fullBuffer.Length; truncate++)
            {
                var commandBuffer = fullBuffer[..^truncate];

                // Our impl
                {
                    into.AsSpan().Clear();

                    RespParser.Parse(commandBuffer, into, out var slotsUsed, out var bytesConsumed);

                    Assert.True(slotsUsed is 0 or 2 or 5);

                    if (slotsUsed == 5)
                    {
                        // fully consumed
                        Assert.Equal(5, slotsUsed);
                        Assert.Equal(commandBuffer.Length, bytesConsumed);

                        // PING
                        Assert.True(into[0].IsCommand);
                        Assert.Equal(1, into[0].ArgumentCount);
                        Assert.True("*1\r\n"u8.SequenceEqual(commandBuffer[into[0].ByteStart..into[0].ByteEnd]));

                        Assert.True(into[1].IsArgument);
                        Assert.True("PING\r\n"u8.SequenceEqual(commandBuffer[into[1].ByteStart..into[1].ByteEnd]));

                        // GET world
                        Assert.True(into[2].IsCommand);
                        Assert.Equal(2, into[2].ArgumentCount);
                        Assert.True("*2\r\n"u8.SequenceEqual(commandBuffer[into[2].ByteStart..into[2].ByteEnd]));

                        Assert.True(into[3].IsArgument);
                        Assert.True("GET\r\n"u8.SequenceEqual(commandBuffer[into[3].ByteStart..into[3].ByteEnd]));

                        Assert.True(into[4].IsArgument);
                        Assert.True("world\r\n"u8.SequenceEqual(commandBuffer[into[4].ByteStart..into[4].ByteEnd]));
                    }
                    else if (slotsUsed == 2)
                    {
                        // just PING consumed
                        Assert.Equal(2, slotsUsed);
                        Assert.Equal(14, bytesConsumed);

                        // PING
                        Assert.True(into[0].IsCommand);
                        Assert.Equal(1, into[0].ArgumentCount);
                        Assert.True("*1\r\n"u8.SequenceEqual(commandBuffer[into[0].ByteStart..into[0].ByteEnd]));

                        Assert.True(into[1].IsArgument);
                        Assert.True("PING\r\n"u8.SequenceEqual(commandBuffer[into[1].ByteStart..into[1].ByteEnd]));
                    }
                    else
                    {
                        // nothing consumed
                        Assert.Equal(0, slotsUsed);
                        Assert.Equal(0, bytesConsumed);
                    }
                }

                // Garnet impl
                {
                    into.AsSpan().Clear();

                    garnetParser.Parse(commandBuffer, into, out var slotsUsed, out var bytesConsumed);

                    Assert.True(slotsUsed is 0 or 1 or 3);

                    if (slotsUsed == 3)
                    {
                        // fully consumed
                        Assert.Equal(3, slotsUsed);
                        Assert.Equal(commandBuffer.Length, bytesConsumed);

                        // PING
                        Assert.True(into[0].IsCommand);
                        Assert.Equal(1, into[0].ArgumentCount);
                        Assert.True("*1\r\n$4\r\nPING\r\n"u8.SequenceEqual(commandBuffer[into[0].ByteStart..into[0].ByteEnd]));

                        // GET world
                        Assert.True(into[1].IsCommand);
                        Assert.Equal(2, into[1].ArgumentCount);
                        Assert.True("*2\r\n$3\r\nGET\r\n"u8.SequenceEqual(commandBuffer[into[1].ByteStart..into[1].ByteEnd]));

                        Assert.True(into[2].IsArgument);
                        Assert.True("world\r\n"u8.SequenceEqual(commandBuffer[into[2].ByteStart..into[2].ByteEnd]));
                    }
                    else if (slotsUsed == 1)
                    {
                        // just PING consumed
                        Assert.Equal(1, slotsUsed);
                        Assert.Equal(14, bytesConsumed);

                        // PING
                        Assert.True(into[0].IsCommand);
                        Assert.Equal(1, into[0].ArgumentCount);
                        Assert.True("*1\r\n$4\r\nPING\r\n"u8.SequenceEqual(commandBuffer[into[0].ByteStart..into[0].ByteEnd]));
                    }
                    else
                    {
                        // nothing consumed
                        Assert.Equal(0, slotsUsed);
                        Assert.Equal(0, bytesConsumed);
                    }
                }
            }
        }
    }
}
