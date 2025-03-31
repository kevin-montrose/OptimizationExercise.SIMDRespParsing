using System.Runtime.Intrinsics;
using System.Text;

namespace OptimizationExercise.SIMDRespParsing.Tests
{
    public class TakeMultipleCommandsTests
    {
        [Fact]
        public void Simple()
        {
            // 1 command
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[1];

                var rawCmd = MakeBuffer("PING", []);
                var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                Span<byte> digitsBitmap = new byte[bitmapLength];

                RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], rawCmd.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                Assert.Equal(1, usedSlots);
                Assert.Equal(rawCmd.Length, bytesConsumed);

                // PING
                Assert.True(into[0].IsCommand);
                Assert.Equal(1, into[0].ArgumentCount);
                Assert.Equal(RespCommand.PING, into[0].Command);
                Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));
            }

            // 2 commands
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[3];

                var rawCmd = MakeBuffer("PING", [], ("GET", ["hello"]));
                var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                Span<byte> digitsBitmap = new byte[bitmapLength];

                RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], rawCmd.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                Assert.Equal(3, usedSlots);
                Assert.Equal(rawCmd.Length, bytesConsumed);

                // PING
                Assert.True(into[0].IsCommand);
                Assert.Equal(1, into[0].ArgumentCount);
                Assert.Equal(RespCommand.PING, into[0].Command);
                Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));

                // GET
                Assert.True(into[1].IsCommand);
                Assert.Equal(2, into[1].ArgumentCount);
                Assert.Equal(RespCommand.GET, into[1].Command);
                Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                Assert.True(into[2].IsArgument);
                Assert.True("hello\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));
            }

            // 3 commands
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[6];

                var rawCmd = MakeBuffer("PING", [], ("GET", ["hello"]), ("SET", ["world", "fizz"]));
                var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                Span<byte> digitsBitmap = new byte[bitmapLength];

                RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], rawCmd.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                Assert.Equal(6, usedSlots);
                Assert.Equal(rawCmd.Length, bytesConsumed);

                // PING
                Assert.True(into[0].IsCommand);
                Assert.Equal(1, into[0].ArgumentCount);
                Assert.Equal(RespCommand.PING, into[0].Command);
                Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));

                // GET
                Assert.True(into[1].IsCommand);
                Assert.Equal(2, into[1].ArgumentCount);
                Assert.Equal(RespCommand.GET, into[1].Command);
                Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                Assert.True(into[2].IsArgument);
                Assert.True("hello\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));

                // SET
                Assert.True(into[3].IsCommand);
                Assert.Equal(3, into[3].ArgumentCount);
                Assert.Equal(RespCommand.SET, into[3].Command);
                Assert.True("SET\r\n"u8.SequenceEqual(padded[into[3].ByteStart..into[3].ByteEnd]));

                Assert.True(into[4].IsArgument);
                Assert.True("world\r\n"u8.SequenceEqual(padded[into[4].ByteStart..into[4].ByteEnd]));

                Assert.True(into[5].IsArgument);
                Assert.True("fizz\r\n"u8.SequenceEqual(padded[into[5].ByteStart..into[5].ByteEnd]));
            }

            // 4 commands
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[10];

                var rawCmd = MakeBuffer("PING", [], ("GET", ["hello"]), ("SET", ["world", "fizz"]), ("HMGET", ["buzz", "foo", "bar"]));
                var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                Span<byte> digitsBitmap = new byte[bitmapLength];

                RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], rawCmd.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                Assert.Equal(10, usedSlots);
                Assert.Equal(rawCmd.Length, bytesConsumed);

                // PING
                Assert.True(into[0].IsCommand);
                Assert.Equal(1, into[0].ArgumentCount);
                Assert.Equal(RespCommand.PING, into[0].Command);
                Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));

                // GET
                Assert.True(into[1].IsCommand);
                Assert.Equal(2, into[1].ArgumentCount);
                Assert.Equal(RespCommand.GET, into[1].Command);
                Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                Assert.True(into[2].IsArgument);
                Assert.True("hello\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));

                // SET
                Assert.True(into[3].IsCommand);
                Assert.Equal(3, into[3].ArgumentCount);
                Assert.Equal(RespCommand.SET, into[3].Command);
                Assert.True("SET\r\n"u8.SequenceEqual(padded[into[3].ByteStart..into[3].ByteEnd]));

                Assert.True(into[4].IsArgument);
                Assert.True("world\r\n"u8.SequenceEqual(padded[into[4].ByteStart..into[4].ByteEnd]));

                Assert.True(into[5].IsArgument);
                Assert.True("fizz\r\n"u8.SequenceEqual(padded[into[5].ByteStart..into[5].ByteEnd]));

                // HMGET
                Assert.True(into[6].IsCommand);
                Assert.Equal(4, into[6].ArgumentCount);
                Assert.Equal(RespCommand.HMGET, into[6].Command);
                Assert.True("HMGET\r\n"u8.SequenceEqual(padded[into[6].ByteStart..into[6].ByteEnd]));

                Assert.True(into[7].IsArgument);
                Assert.True("buzz\r\n"u8.SequenceEqual(padded[into[7].ByteStart..into[7].ByteEnd]));

                Assert.True(into[8].IsArgument);
                Assert.True("foo\r\n"u8.SequenceEqual(padded[into[8].ByteStart..into[8].ByteEnd]));

                Assert.True(into[9].IsArgument);
                Assert.True("bar\r\n"u8.SequenceEqual(padded[into[9].ByteStart..into[9].ByteEnd]));
            }

            // 5 commands
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[15];

                var rawCmd = MakeBuffer(
                    "PING", [],
                    ("GET", ["hello"]),
                    ("SET", ["world", "fizz"]),
                    ("HMGET", ["buzz", "foo", "bar"]),
                    ("HMSET", ["abc", "def", "ghi", "jkl"])
                );
                var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                Span<byte> digitsBitmap = new byte[bitmapLength];

                RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], rawCmd.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                Assert.Equal(15, usedSlots);
                Assert.Equal(rawCmd.Length, bytesConsumed);

                // PING
                Assert.True(into[0].IsCommand);
                Assert.Equal(1, into[0].ArgumentCount);
                Assert.Equal(RespCommand.PING, into[0].Command);
                Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));

                // GET
                Assert.True(into[1].IsCommand);
                Assert.Equal(2, into[1].ArgumentCount);
                Assert.Equal(RespCommand.GET, into[1].Command);
                Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                Assert.True(into[2].IsArgument);
                Assert.True("hello\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));

                // SET
                Assert.True(into[3].IsCommand);
                Assert.Equal(3, into[3].ArgumentCount);
                Assert.Equal(RespCommand.SET, into[3].Command);
                Assert.True("SET\r\n"u8.SequenceEqual(padded[into[3].ByteStart..into[3].ByteEnd]));

                Assert.True(into[4].IsArgument);
                Assert.True("world\r\n"u8.SequenceEqual(padded[into[4].ByteStart..into[4].ByteEnd]));

                Assert.True(into[5].IsArgument);
                Assert.True("fizz\r\n"u8.SequenceEqual(padded[into[5].ByteStart..into[5].ByteEnd]));

                // HMGET
                Assert.True(into[6].IsCommand);
                Assert.Equal(4, into[6].ArgumentCount);
                Assert.Equal(RespCommand.HMGET, into[6].Command);
                Assert.True("HMGET\r\n"u8.SequenceEqual(padded[into[6].ByteStart..into[6].ByteEnd]));

                Assert.True(into[7].IsArgument);
                Assert.True("buzz\r\n"u8.SequenceEqual(padded[into[7].ByteStart..into[7].ByteEnd]));

                Assert.True(into[8].IsArgument);
                Assert.True("foo\r\n"u8.SequenceEqual(padded[into[8].ByteStart..into[8].ByteEnd]));

                Assert.True(into[9].IsArgument);
                Assert.True("bar\r\n"u8.SequenceEqual(padded[into[9].ByteStart..into[9].ByteEnd]));

                // HMSET
                Assert.True(into[10].IsCommand);
                Assert.Equal(5, into[10].ArgumentCount);
                Assert.Equal(RespCommand.HMSET, into[10].Command);
                Assert.True("HMSET\r\n"u8.SequenceEqual(padded[into[10].ByteStart..into[10].ByteEnd]));

                Assert.True(into[11].IsArgument);
                Assert.True("abc\r\n"u8.SequenceEqual(padded[into[11].ByteStart..into[11].ByteEnd]));

                Assert.True(into[12].IsArgument);
                Assert.True("def\r\n"u8.SequenceEqual(padded[into[12].ByteStart..into[12].ByteEnd]));

                Assert.True(into[13].IsArgument);
                Assert.True("ghi\r\n"u8.SequenceEqual(padded[into[13].ByteStart..into[13].ByteEnd]));

                Assert.True(into[14].IsArgument);
                Assert.True("jkl\r\n"u8.SequenceEqual(padded[into[14].ByteStart..into[14].ByteEnd]));
            }

            // 6 commands
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[21];

                var rawCmd = MakeBuffer(
                    "PING", [],
                    ("GET", ["hello"]),
                    ("SET", ["world", "fizz"]),
                    ("HMGET", ["buzz", "foo", "bar"]),
                    ("HMSET", ["abc", "def", "ghi", "jkl"]),
                    ("APPEND", ["mno", "pqr", "stu", "vwx", "yz"])
                );
                var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                Span<byte> digitsBitmap = new byte[bitmapLength];

                RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], rawCmd.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                Assert.Equal(21, usedSlots);
                Assert.Equal(rawCmd.Length, bytesConsumed);

                // PING
                Assert.True(into[0].IsCommand);
                Assert.Equal(1, into[0].ArgumentCount);
                Assert.Equal(RespCommand.PING, into[0].Command);
                Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));

                // GET
                Assert.True(into[1].IsCommand);
                Assert.Equal(2, into[1].ArgumentCount);
                Assert.Equal(RespCommand.GET, into[1].Command);
                Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                Assert.True(into[2].IsArgument);
                Assert.True("hello\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));

                // SET
                Assert.True(into[3].IsCommand);
                Assert.Equal(3, into[3].ArgumentCount);
                Assert.Equal(RespCommand.SET, into[3].Command);
                Assert.True("SET\r\n"u8.SequenceEqual(padded[into[3].ByteStart..into[3].ByteEnd]));

                Assert.True(into[4].IsArgument);
                Assert.True("world\r\n"u8.SequenceEqual(padded[into[4].ByteStart..into[4].ByteEnd]));

                Assert.True(into[5].IsArgument);
                Assert.True("fizz\r\n"u8.SequenceEqual(padded[into[5].ByteStart..into[5].ByteEnd]));

                // HMGET
                Assert.True(into[6].IsCommand);
                Assert.Equal(4, into[6].ArgumentCount);
                Assert.Equal(RespCommand.HMGET, into[6].Command);
                Assert.True("HMGET\r\n"u8.SequenceEqual(padded[into[6].ByteStart..into[6].ByteEnd]));

                Assert.True(into[7].IsArgument);
                Assert.True("buzz\r\n"u8.SequenceEqual(padded[into[7].ByteStart..into[7].ByteEnd]));

                Assert.True(into[8].IsArgument);
                Assert.True("foo\r\n"u8.SequenceEqual(padded[into[8].ByteStart..into[8].ByteEnd]));

                Assert.True(into[9].IsArgument);
                Assert.True("bar\r\n"u8.SequenceEqual(padded[into[9].ByteStart..into[9].ByteEnd]));

                // HMSET
                Assert.True(into[10].IsCommand);
                Assert.Equal(5, into[10].ArgumentCount);
                Assert.Equal(RespCommand.HMSET, into[10].Command);
                Assert.True("HMSET\r\n"u8.SequenceEqual(padded[into[10].ByteStart..into[10].ByteEnd]));

                Assert.True(into[11].IsArgument);
                Assert.True("abc\r\n"u8.SequenceEqual(padded[into[11].ByteStart..into[11].ByteEnd]));

                Assert.True(into[12].IsArgument);
                Assert.True("def\r\n"u8.SequenceEqual(padded[into[12].ByteStart..into[12].ByteEnd]));

                Assert.True(into[13].IsArgument);
                Assert.True("ghi\r\n"u8.SequenceEqual(padded[into[13].ByteStart..into[13].ByteEnd]));

                Assert.True(into[14].IsArgument);
                Assert.True("jkl\r\n"u8.SequenceEqual(padded[into[14].ByteStart..into[14].ByteEnd]));

                // APPEND
                Assert.True(into[15].IsCommand);
                Assert.Equal(6, into[15].ArgumentCount);
                Assert.Equal(RespCommand.APPEND, into[15].Command);
                Assert.True("APPEND\r\n"u8.SequenceEqual(padded[into[15].ByteStart..into[15].ByteEnd]));

                Assert.True(into[16].IsArgument);
                Assert.True("mno\r\n"u8.SequenceEqual(padded[into[16].ByteStart..into[16].ByteEnd]));

                Assert.True(into[17].IsArgument);
                Assert.True("pqr\r\n"u8.SequenceEqual(padded[into[17].ByteStart..into[17].ByteEnd]));

                Assert.True(into[18].IsArgument);
                Assert.True("stu\r\n"u8.SequenceEqual(padded[into[18].ByteStart..into[18].ByteEnd]));

                Assert.True(into[19].IsArgument);
                Assert.True("vwx\r\n"u8.SequenceEqual(padded[into[19].ByteStart..into[19].ByteEnd]));

                Assert.True(into[20].IsArgument);
                Assert.True("yz\r\n"u8.SequenceEqual(padded[into[20].ByteStart..into[20].ByteEnd]));
            }

            // 7 commands
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[28];

                var rawCmd = MakeBuffer(
                    "PING", [],
                    ("GET", ["hello"]),
                    ("SET", ["world", "fizz"]),
                    ("HMGET", ["buzz", "foo", "bar"]),
                    ("HMSET", ["abc", "def", "ghi", "jkl"]),
                    ("APPEND", ["mno", "pqr", "stu", "vwx", "yz"]),
                    ("HEXPIRE", ["", "1", "12", "123", "1234", "12345"])
                );
                var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                Span<byte> digitsBitmap = new byte[bitmapLength];

                RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], rawCmd.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                Assert.Equal(28, usedSlots);
                Assert.Equal(rawCmd.Length, bytesConsumed);

                // PING
                Assert.True(into[0].IsCommand);
                Assert.Equal(1, into[0].ArgumentCount);
                Assert.Equal(RespCommand.PING, into[0].Command);
                Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));

                // GET
                Assert.True(into[1].IsCommand);
                Assert.Equal(2, into[1].ArgumentCount);
                Assert.Equal(RespCommand.GET, into[1].Command);
                Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                Assert.True(into[2].IsArgument);
                Assert.True("hello\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));

                // SET
                Assert.True(into[3].IsCommand);
                Assert.Equal(3, into[3].ArgumentCount);
                Assert.Equal(RespCommand.SET, into[3].Command);
                Assert.True("SET\r\n"u8.SequenceEqual(padded[into[3].ByteStart..into[3].ByteEnd]));

                Assert.True(into[4].IsArgument);
                Assert.True("world\r\n"u8.SequenceEqual(padded[into[4].ByteStart..into[4].ByteEnd]));

                Assert.True(into[5].IsArgument);
                Assert.True("fizz\r\n"u8.SequenceEqual(padded[into[5].ByteStart..into[5].ByteEnd]));

                // HMGET
                Assert.True(into[6].IsCommand);
                Assert.Equal(4, into[6].ArgumentCount);
                Assert.Equal(RespCommand.HMGET, into[6].Command);
                Assert.True("HMGET\r\n"u8.SequenceEqual(padded[into[6].ByteStart..into[6].ByteEnd]));

                Assert.True(into[7].IsArgument);
                Assert.True("buzz\r\n"u8.SequenceEqual(padded[into[7].ByteStart..into[7].ByteEnd]));

                Assert.True(into[8].IsArgument);
                Assert.True("foo\r\n"u8.SequenceEqual(padded[into[8].ByteStart..into[8].ByteEnd]));

                Assert.True(into[9].IsArgument);
                Assert.True("bar\r\n"u8.SequenceEqual(padded[into[9].ByteStart..into[9].ByteEnd]));

                // HMSET
                Assert.True(into[10].IsCommand);
                Assert.Equal(5, into[10].ArgumentCount);
                Assert.Equal(RespCommand.HMSET, into[10].Command);
                Assert.True("HMSET\r\n"u8.SequenceEqual(padded[into[10].ByteStart..into[10].ByteEnd]));

                Assert.True(into[11].IsArgument);
                Assert.True("abc\r\n"u8.SequenceEqual(padded[into[11].ByteStart..into[11].ByteEnd]));

                Assert.True(into[12].IsArgument);
                Assert.True("def\r\n"u8.SequenceEqual(padded[into[12].ByteStart..into[12].ByteEnd]));

                Assert.True(into[13].IsArgument);
                Assert.True("ghi\r\n"u8.SequenceEqual(padded[into[13].ByteStart..into[13].ByteEnd]));

                Assert.True(into[14].IsArgument);
                Assert.True("jkl\r\n"u8.SequenceEqual(padded[into[14].ByteStart..into[14].ByteEnd]));

                // APPEND
                Assert.True(into[15].IsCommand);
                Assert.Equal(6, into[15].ArgumentCount);
                Assert.Equal(RespCommand.APPEND, into[15].Command);
                Assert.True("APPEND\r\n"u8.SequenceEqual(padded[into[15].ByteStart..into[15].ByteEnd]));

                Assert.True(into[16].IsArgument);
                Assert.True("mno\r\n"u8.SequenceEqual(padded[into[16].ByteStart..into[16].ByteEnd]));

                Assert.True(into[17].IsArgument);
                Assert.True("pqr\r\n"u8.SequenceEqual(padded[into[17].ByteStart..into[17].ByteEnd]));

                Assert.True(into[18].IsArgument);
                Assert.True("stu\r\n"u8.SequenceEqual(padded[into[18].ByteStart..into[18].ByteEnd]));

                Assert.True(into[19].IsArgument);
                Assert.True("vwx\r\n"u8.SequenceEqual(padded[into[19].ByteStart..into[19].ByteEnd]));

                Assert.True(into[20].IsArgument);
                Assert.True("yz\r\n"u8.SequenceEqual(padded[into[20].ByteStart..into[20].ByteEnd]));

                // HEXPIRE
                Assert.True(into[21].IsCommand);
                Assert.Equal(7, into[21].ArgumentCount);
                Assert.Equal(RespCommand.HEXPIRE, into[21].Command);
                Assert.True("HEXPIRE\r\n"u8.SequenceEqual(padded[into[21].ByteStart..into[21].ByteEnd]));

                Assert.True(into[22].IsArgument);
                Assert.True("\r\n"u8.SequenceEqual(padded[into[22].ByteStart..into[22].ByteEnd]));

                Assert.True(into[23].IsArgument);
                Assert.True("1\r\n"u8.SequenceEqual(padded[into[23].ByteStart..into[23].ByteEnd]));

                Assert.True(into[24].IsArgument);
                Assert.True("12\r\n"u8.SequenceEqual(padded[into[24].ByteStart..into[24].ByteEnd]));

                Assert.True(into[25].IsArgument);
                Assert.True("123\r\n"u8.SequenceEqual(padded[into[25].ByteStart..into[25].ByteEnd]));

                Assert.True(into[26].IsArgument);
                Assert.True("1234\r\n"u8.SequenceEqual(padded[into[26].ByteStart..into[26].ByteEnd]));

                Assert.True(into[27].IsArgument);
                Assert.True("12345\r\n"u8.SequenceEqual(padded[into[27].ByteStart..into[27].ByteEnd]));
            }

            // 8 commands
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[36];

                var rawCmd = MakeBuffer(
                    "PING", [],
                    ("GET", ["hello"]),
                    ("SET", ["world", "fizz"]),
                    ("HMGET", ["buzz", "foo", "bar"]),
                    ("HMSET", ["abc", "def", "ghi", "jkl"]),
                    ("APPEND", ["mno", "pqr", "stu", "vwx", "yz"]),
                    ("HEXPIRE", ["", "1", "12", "123", "1234", "12345"]),
                    ("HPEXPIRE", ["000000", "11111", "2222", "333", "44", "5", ""])
                );
                var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                Span<byte> digitsBitmap = new byte[bitmapLength];

                RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], rawCmd.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                Assert.Equal(36, usedSlots);
                Assert.Equal(rawCmd.Length, bytesConsumed);

                // PING
                Assert.True(into[0].IsCommand);
                Assert.Equal(1, into[0].ArgumentCount);
                Assert.Equal(RespCommand.PING, into[0].Command);
                Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));

                // GET
                Assert.True(into[1].IsCommand);
                Assert.Equal(2, into[1].ArgumentCount);
                Assert.Equal(RespCommand.GET, into[1].Command);
                Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                Assert.True(into[2].IsArgument);
                Assert.True("hello\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));

                // SET
                Assert.True(into[3].IsCommand);
                Assert.Equal(3, into[3].ArgumentCount);
                Assert.Equal(RespCommand.SET, into[3].Command);
                Assert.True("SET\r\n"u8.SequenceEqual(padded[into[3].ByteStart..into[3].ByteEnd]));

                Assert.True(into[4].IsArgument);
                Assert.True("world\r\n"u8.SequenceEqual(padded[into[4].ByteStart..into[4].ByteEnd]));

                Assert.True(into[5].IsArgument);
                Assert.True("fizz\r\n"u8.SequenceEqual(padded[into[5].ByteStart..into[5].ByteEnd]));

                // HMGET
                Assert.True(into[6].IsCommand);
                Assert.Equal(4, into[6].ArgumentCount);
                Assert.Equal(RespCommand.HMGET, into[6].Command);
                Assert.True("HMGET\r\n"u8.SequenceEqual(padded[into[6].ByteStart..into[6].ByteEnd]));

                Assert.True(into[7].IsArgument);
                Assert.True("buzz\r\n"u8.SequenceEqual(padded[into[7].ByteStart..into[7].ByteEnd]));

                Assert.True(into[8].IsArgument);
                Assert.True("foo\r\n"u8.SequenceEqual(padded[into[8].ByteStart..into[8].ByteEnd]));

                Assert.True(into[9].IsArgument);
                Assert.True("bar\r\n"u8.SequenceEqual(padded[into[9].ByteStart..into[9].ByteEnd]));

                // HMSET
                Assert.True(into[10].IsCommand);
                Assert.Equal(5, into[10].ArgumentCount);
                Assert.Equal(RespCommand.HMSET, into[10].Command);
                Assert.True("HMSET\r\n"u8.SequenceEqual(padded[into[10].ByteStart..into[10].ByteEnd]));

                Assert.True(into[11].IsArgument);
                Assert.True("abc\r\n"u8.SequenceEqual(padded[into[11].ByteStart..into[11].ByteEnd]));

                Assert.True(into[12].IsArgument);
                Assert.True("def\r\n"u8.SequenceEqual(padded[into[12].ByteStart..into[12].ByteEnd]));

                Assert.True(into[13].IsArgument);
                Assert.True("ghi\r\n"u8.SequenceEqual(padded[into[13].ByteStart..into[13].ByteEnd]));

                Assert.True(into[14].IsArgument);
                Assert.True("jkl\r\n"u8.SequenceEqual(padded[into[14].ByteStart..into[14].ByteEnd]));

                // APPEND
                Assert.True(into[15].IsCommand);
                Assert.Equal(6, into[15].ArgumentCount);
                Assert.Equal(RespCommand.APPEND, into[15].Command);
                Assert.True("APPEND\r\n"u8.SequenceEqual(padded[into[15].ByteStart..into[15].ByteEnd]));

                Assert.True(into[16].IsArgument);
                Assert.True("mno\r\n"u8.SequenceEqual(padded[into[16].ByteStart..into[16].ByteEnd]));

                Assert.True(into[17].IsArgument);
                Assert.True("pqr\r\n"u8.SequenceEqual(padded[into[17].ByteStart..into[17].ByteEnd]));

                Assert.True(into[18].IsArgument);
                Assert.True("stu\r\n"u8.SequenceEqual(padded[into[18].ByteStart..into[18].ByteEnd]));

                Assert.True(into[19].IsArgument);
                Assert.True("vwx\r\n"u8.SequenceEqual(padded[into[19].ByteStart..into[19].ByteEnd]));

                Assert.True(into[20].IsArgument);
                Assert.True("yz\r\n"u8.SequenceEqual(padded[into[20].ByteStart..into[20].ByteEnd]));

                // HEXPIRE
                Assert.True(into[21].IsCommand);
                Assert.Equal(7, into[21].ArgumentCount);
                Assert.Equal(RespCommand.HEXPIRE, into[21].Command);
                Assert.True("HEXPIRE\r\n"u8.SequenceEqual(padded[into[21].ByteStart..into[21].ByteEnd]));

                Assert.True(into[22].IsArgument);
                Assert.True("\r\n"u8.SequenceEqual(padded[into[22].ByteStart..into[22].ByteEnd]));

                Assert.True(into[23].IsArgument);
                Assert.True("1\r\n"u8.SequenceEqual(padded[into[23].ByteStart..into[23].ByteEnd]));

                Assert.True(into[24].IsArgument);
                Assert.True("12\r\n"u8.SequenceEqual(padded[into[24].ByteStart..into[24].ByteEnd]));

                Assert.True(into[25].IsArgument);
                Assert.True("123\r\n"u8.SequenceEqual(padded[into[25].ByteStart..into[25].ByteEnd]));

                Assert.True(into[26].IsArgument);
                Assert.True("1234\r\n"u8.SequenceEqual(padded[into[26].ByteStart..into[26].ByteEnd]));

                Assert.True(into[27].IsArgument);
                Assert.True("12345\r\n"u8.SequenceEqual(padded[into[27].ByteStart..into[27].ByteEnd]));

                // HPEXPIRE
                Assert.True(into[28].IsCommand);
                Assert.Equal(8, into[28].ArgumentCount);
                Assert.Equal(RespCommand.HPEXPIRE, into[28].Command);
                Assert.True("HPEXPIRE\r\n"u8.SequenceEqual(padded[into[28].ByteStart..into[28].ByteEnd]));

                Assert.True(into[29].IsArgument);
                Assert.True("000000\r\n"u8.SequenceEqual(padded[into[29].ByteStart..into[29].ByteEnd]));

                Assert.True(into[30].IsArgument);
                Assert.True("11111\r\n"u8.SequenceEqual(padded[into[30].ByteStart..into[30].ByteEnd]));

                Assert.True(into[31].IsArgument);
                Assert.True("2222\r\n"u8.SequenceEqual(padded[into[31].ByteStart..into[31].ByteEnd]));

                Assert.True(into[32].IsArgument);
                Assert.True("333\r\n"u8.SequenceEqual(padded[into[32].ByteStart..into[32].ByteEnd]));

                Assert.True(into[33].IsArgument);
                Assert.True("44\r\n"u8.SequenceEqual(padded[into[33].ByteStart..into[33].ByteEnd]));

                Assert.True(into[34].IsArgument);
                Assert.True("5\r\n"u8.SequenceEqual(padded[into[34].ByteStart..into[34].ByteEnd]));

                Assert.True(into[35].IsArgument);
                Assert.True("\r\n"u8.SequenceEqual(padded[into[35].ByteStart..into[35].ByteEnd]));
            }
        }

        [Fact]
        public void IncompleteData()
        {
            // 1 command
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[1];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[1];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", []
                );
                for (var i = 1; i < rawCmd.Length; i++)
                {
                    var truncated = rawCmd[..^i];
                    var truncatedLength = truncated.Length;

                    var expected = boundaries.ToArray().TakeWhile(x => x.StopByte <= truncatedLength).ToArray();

                    var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                    RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], truncated.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                    var expectedSlots = expected.Sum(static x => x.ArgumentCount);
                    var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                    Assert.Equal(expectedSlots, usedSlots);
                    Assert.Equal(expectedConsumed, bytesConsumed);

                    // PING
                    if (expected.Length > 0)
                    {
                        Assert.Fail("Shouldn't have parsed command");
                    }
                }
            }

            // 2 commands
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[3];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[2];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", [],
                    ("GET", ["hello"])
                );
                for (var i = 1; i < rawCmd.Length; i++)
                {
                    var truncated = rawCmd[..^i];
                    var truncatedLength = truncated.Length;

                    var expected = boundaries.ToArray().TakeWhile(x => x.StopByte <= truncatedLength).ToArray();

                    var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                    RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], truncated.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                    var expectedSlots = expected.Sum(static x => x.ArgumentCount);
                    var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                    Assert.Equal(expectedSlots, usedSlots);
                    Assert.Equal(expectedConsumed, bytesConsumed);

                    // PING
                    if (expected.Length > 0)
                    {
                        Assert.True(into[0].IsCommand);
                        Assert.Equal(1, into[0].ArgumentCount);
                        Assert.Equal(RespCommand.PING, into[0].Command);
                        Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));
                    }

                    // GET
                    if (expected.Length > 1)
                    {
                        Assert.Fail("Should not have parsed command");
                    }
                }
            }

            // 3 commands
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[6];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[3];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", [],
                    ("GET", ["hello"]),
                    ("SET", ["world", "fizz"])
                );
                for (var i = 1; i < rawCmd.Length; i++)
                {
                    var truncated = rawCmd[..^i];
                    var truncatedLength = truncated.Length;

                    var expected = boundaries.ToArray().TakeWhile(x => x.StopByte <= truncatedLength).ToArray();

                    var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                    RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], truncated.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                    var expectedSlots = expected.Sum(static x => x.ArgumentCount);
                    var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                    Assert.Equal(expectedSlots, usedSlots);
                    Assert.Equal(expectedConsumed, bytesConsumed);

                    // PING
                    if (expected.Length > 0)
                    {
                        Assert.True(into[0].IsCommand);
                        Assert.Equal(1, into[0].ArgumentCount);
                        Assert.Equal(RespCommand.PING, into[0].Command);
                        Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));
                    }

                    // GET
                    if (expected.Length > 1)
                    {
                        Assert.True(into[1].IsCommand);
                        Assert.Equal(2, into[1].ArgumentCount);
                        Assert.Equal(RespCommand.GET, into[1].Command);
                        Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                        Assert.True(into[2].IsArgument);
                        Assert.True("hello\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));
                    }

                    // SET
                    if (expected.Length > 2)
                    {
                        Assert.Fail("Should't have parsed command");
                    }
                }
            }

            // 4 commands
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[10];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[4];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", [],
                    ("GET", ["hello"]),
                    ("SET", ["world", "fizz"]),
                    ("HMGET", ["buzz", "foo", "bar"])
                );
                for (var i = 1; i < rawCmd.Length; i++)
                {
                    var truncated = rawCmd[..^i];
                    var truncatedLength = truncated.Length;

                    var expected = boundaries.ToArray().TakeWhile(x => x.StopByte <= truncatedLength).ToArray();

                    var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                    RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], truncated.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                    var expectedSlots = expected.Sum(static x => x.ArgumentCount);
                    var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                    Assert.Equal(expectedSlots, usedSlots);
                    Assert.Equal(expectedConsumed, bytesConsumed);

                    // PING
                    if (expected.Length > 0)
                    {
                        Assert.True(into[0].IsCommand);
                        Assert.Equal(1, into[0].ArgumentCount);
                        Assert.Equal(RespCommand.PING, into[0].Command);
                        Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));
                    }

                    // GET
                    if (expected.Length > 1)
                    {
                        Assert.True(into[1].IsCommand);
                        Assert.Equal(2, into[1].ArgumentCount);
                        Assert.Equal(RespCommand.GET, into[1].Command);
                        Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                        Assert.True(into[2].IsArgument);
                        Assert.True("hello\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));
                    }

                    // SET
                    if (expected.Length > 2)
                    {
                        Assert.True(into[3].IsCommand);
                        Assert.Equal(3, into[3].ArgumentCount);
                        Assert.Equal(RespCommand.SET, into[3].Command);
                        Assert.True("SET\r\n"u8.SequenceEqual(padded[into[3].ByteStart..into[3].ByteEnd]));

                        Assert.True(into[4].IsArgument);
                        Assert.True("world\r\n"u8.SequenceEqual(padded[into[4].ByteStart..into[4].ByteEnd]));

                        Assert.True(into[5].IsArgument);
                        Assert.True("fizz\r\n"u8.SequenceEqual(padded[into[5].ByteStart..into[5].ByteEnd]));
                    }

                    // HMGET
                    if (expected.Length > 3)
                    {
                        Assert.Fail("Shouldn't have parsed command");
                    }
                }
            }

            // 5 commands
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[15];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[5];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", [],
                    ("GET", ["hello"]),
                    ("SET", ["world", "fizz"]),
                    ("HMGET", ["buzz", "foo", "bar"]),
                    ("HMSET", ["abc", "def", "ghi", "jkl"])
                );
                for (var i = 1; i < rawCmd.Length; i++)
                {
                    var truncated = rawCmd[..^i];
                    var truncatedLength = truncated.Length;

                    var expected = boundaries.ToArray().TakeWhile(x => x.StopByte <= truncatedLength).ToArray();

                    var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                    RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], truncated.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                    var expectedSlots = expected.Sum(static x => x.ArgumentCount);
                    var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                    Assert.Equal(expectedSlots, usedSlots);
                    Assert.Equal(expectedConsumed, bytesConsumed);

                    // PING
                    if (expected.Length > 0)
                    {
                        Assert.True(into[0].IsCommand);
                        Assert.Equal(1, into[0].ArgumentCount);
                        Assert.Equal(RespCommand.PING, into[0].Command);
                        Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));
                    }

                    // GET
                    if (expected.Length > 1)
                    {
                        Assert.True(into[1].IsCommand);
                        Assert.Equal(2, into[1].ArgumentCount);
                        Assert.Equal(RespCommand.GET, into[1].Command);
                        Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                        Assert.True(into[2].IsArgument);
                        Assert.True("hello\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));
                    }

                    // SET
                    if (expected.Length > 2)
                    {
                        Assert.True(into[3].IsCommand);
                        Assert.Equal(3, into[3].ArgumentCount);
                        Assert.Equal(RespCommand.SET, into[3].Command);
                        Assert.True("SET\r\n"u8.SequenceEqual(padded[into[3].ByteStart..into[3].ByteEnd]));

                        Assert.True(into[4].IsArgument);
                        Assert.True("world\r\n"u8.SequenceEqual(padded[into[4].ByteStart..into[4].ByteEnd]));

                        Assert.True(into[5].IsArgument);
                        Assert.True("fizz\r\n"u8.SequenceEqual(padded[into[5].ByteStart..into[5].ByteEnd]));
                    }

                    // HMGET
                    if (expected.Length > 3)
                    {
                        Assert.True(into[6].IsCommand);
                        Assert.Equal(4, into[6].ArgumentCount);
                        Assert.Equal(RespCommand.HMGET, into[6].Command);
                        Assert.True("HMGET\r\n"u8.SequenceEqual(padded[into[6].ByteStart..into[6].ByteEnd]));

                        Assert.True(into[7].IsArgument);
                        Assert.True("buzz\r\n"u8.SequenceEqual(padded[into[7].ByteStart..into[7].ByteEnd]));

                        Assert.True(into[8].IsArgument);
                        Assert.True("foo\r\n"u8.SequenceEqual(padded[into[8].ByteStart..into[8].ByteEnd]));

                        Assert.True(into[9].IsArgument);
                        Assert.True("bar\r\n"u8.SequenceEqual(padded[into[9].ByteStart..into[9].ByteEnd]));
                    }

                    // HMSET
                    if (expected.Length > 4)
                    {
                        Assert.Fail("Shouldn't have parsed command");
                    }
                }
            }

            // 6 commands
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[21];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[6];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", [],
                    ("GET", ["hello"]),
                    ("SET", ["world", "fizz"]),
                    ("HMGET", ["buzz", "foo", "bar"]),
                    ("HMSET", ["abc", "def", "ghi", "jkl"]),
                    ("APPEND", ["mno", "pqr", "stu", "vwx", "yz"])
                );
                for (var i = 1; i < rawCmd.Length; i++)
                {
                    var truncated = rawCmd[..^i];
                    var truncatedLength = truncated.Length;

                    var expected = boundaries.ToArray().TakeWhile(x => x.StopByte <= truncatedLength).ToArray();

                    var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                    RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], truncated.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                    var expectedSlots = expected.Sum(static x => x.ArgumentCount);
                    var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                    Assert.Equal(expectedSlots, usedSlots);
                    Assert.Equal(expectedConsumed, bytesConsumed);

                    // PING
                    if (expected.Length > 0)
                    {
                        Assert.True(into[0].IsCommand);
                        Assert.Equal(1, into[0].ArgumentCount);
                        Assert.Equal(RespCommand.PING, into[0].Command);
                        Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));
                    }

                    // GET
                    if (expected.Length > 1)
                    {
                        Assert.True(into[1].IsCommand);
                        Assert.Equal(2, into[1].ArgumentCount);
                        Assert.Equal(RespCommand.GET, into[1].Command);
                        Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                        Assert.True(into[2].IsArgument);
                        Assert.True("hello\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));
                    }

                    // SET
                    if (expected.Length > 2)
                    {
                        Assert.True(into[3].IsCommand);
                        Assert.Equal(3, into[3].ArgumentCount);
                        Assert.Equal(RespCommand.SET, into[3].Command);
                        Assert.True("SET\r\n"u8.SequenceEqual(padded[into[3].ByteStart..into[3].ByteEnd]));

                        Assert.True(into[4].IsArgument);
                        Assert.True("world\r\n"u8.SequenceEqual(padded[into[4].ByteStart..into[4].ByteEnd]));

                        Assert.True(into[5].IsArgument);
                        Assert.True("fizz\r\n"u8.SequenceEqual(padded[into[5].ByteStart..into[5].ByteEnd]));
                    }

                    // HMGET
                    if (expected.Length > 3)
                    {
                        Assert.True(into[6].IsCommand);
                        Assert.Equal(4, into[6].ArgumentCount);
                        Assert.Equal(RespCommand.HMGET, into[6].Command);
                        Assert.True("HMGET\r\n"u8.SequenceEqual(padded[into[6].ByteStart..into[6].ByteEnd]));

                        Assert.True(into[7].IsArgument);
                        Assert.True("buzz\r\n"u8.SequenceEqual(padded[into[7].ByteStart..into[7].ByteEnd]));

                        Assert.True(into[8].IsArgument);
                        Assert.True("foo\r\n"u8.SequenceEqual(padded[into[8].ByteStart..into[8].ByteEnd]));

                        Assert.True(into[9].IsArgument);
                        Assert.True("bar\r\n"u8.SequenceEqual(padded[into[9].ByteStart..into[9].ByteEnd]));
                    }

                    // HMSET
                    if (expected.Length > 4)
                    {
                        Assert.True(into[10].IsCommand);
                        Assert.Equal(5, into[10].ArgumentCount);
                        Assert.Equal(RespCommand.HMSET, into[10].Command);
                        Assert.True("HMSET\r\n"u8.SequenceEqual(padded[into[10].ByteStart..into[10].ByteEnd]));

                        Assert.True(into[11].IsArgument);
                        Assert.True("abc\r\n"u8.SequenceEqual(padded[into[11].ByteStart..into[11].ByteEnd]));

                        Assert.True(into[12].IsArgument);
                        Assert.True("def\r\n"u8.SequenceEqual(padded[into[12].ByteStart..into[12].ByteEnd]));

                        Assert.True(into[13].IsArgument);
                        Assert.True("ghi\r\n"u8.SequenceEqual(padded[into[13].ByteStart..into[13].ByteEnd]));

                        Assert.True(into[14].IsArgument);
                        Assert.True("jkl\r\n"u8.SequenceEqual(padded[into[14].ByteStart..into[14].ByteEnd]));
                    }

                    // APPEND
                    if (expected.Length > 5)
                    {
                        Assert.Fail("Command shouldn't have been parsed");
                    }
                }
            }

            // 7 commands
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[28];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[7];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", [],
                    ("GET", ["hello"]),
                    ("SET", ["world", "fizz"]),
                    ("HMGET", ["buzz", "foo", "bar"]),
                    ("HMSET", ["abc", "def", "ghi", "jkl"]),
                    ("APPEND", ["mno", "pqr", "stu", "vwx", "yz"]),
                    ("HEXPIRE", ["", "1", "12", "123", "1234", "12345"])
                );
                for (var i = 1; i < rawCmd.Length; i++)
                {
                    var truncated = rawCmd[..^i];
                    var truncatedLength = truncated.Length;

                    var expected = boundaries.ToArray().TakeWhile(x => x.StopByte <= truncatedLength).ToArray();

                    var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                    RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], truncated.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                    var expectedSlots = expected.Sum(static x => x.ArgumentCount);
                    var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                    Assert.Equal(expectedSlots, usedSlots);
                    Assert.Equal(expectedConsumed, bytesConsumed);

                    // PING
                    if (expected.Length > 0)
                    {
                        Assert.True(into[0].IsCommand);
                        Assert.Equal(1, into[0].ArgumentCount);
                        Assert.Equal(RespCommand.PING, into[0].Command);
                        Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));
                    }

                    // GET
                    if (expected.Length > 1)
                    {
                        Assert.True(into[1].IsCommand);
                        Assert.Equal(2, into[1].ArgumentCount);
                        Assert.Equal(RespCommand.GET, into[1].Command);
                        Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                        Assert.True(into[2].IsArgument);
                        Assert.True("hello\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));
                    }

                    // SET
                    if (expected.Length > 2)
                    {
                        Assert.True(into[3].IsCommand);
                        Assert.Equal(3, into[3].ArgumentCount);
                        Assert.Equal(RespCommand.SET, into[3].Command);
                        Assert.True("SET\r\n"u8.SequenceEqual(padded[into[3].ByteStart..into[3].ByteEnd]));

                        Assert.True(into[4].IsArgument);
                        Assert.True("world\r\n"u8.SequenceEqual(padded[into[4].ByteStart..into[4].ByteEnd]));

                        Assert.True(into[5].IsArgument);
                        Assert.True("fizz\r\n"u8.SequenceEqual(padded[into[5].ByteStart..into[5].ByteEnd]));
                    }

                    // HMGET
                    if (expected.Length > 3)
                    {
                        Assert.True(into[6].IsCommand);
                        Assert.Equal(4, into[6].ArgumentCount);
                        Assert.Equal(RespCommand.HMGET, into[6].Command);
                        Assert.True("HMGET\r\n"u8.SequenceEqual(padded[into[6].ByteStart..into[6].ByteEnd]));

                        Assert.True(into[7].IsArgument);
                        Assert.True("buzz\r\n"u8.SequenceEqual(padded[into[7].ByteStart..into[7].ByteEnd]));

                        Assert.True(into[8].IsArgument);
                        Assert.True("foo\r\n"u8.SequenceEqual(padded[into[8].ByteStart..into[8].ByteEnd]));

                        Assert.True(into[9].IsArgument);
                        Assert.True("bar\r\n"u8.SequenceEqual(padded[into[9].ByteStart..into[9].ByteEnd]));
                    }

                    // HMSET
                    if (expected.Length > 4)
                    {
                        Assert.True(into[10].IsCommand);
                        Assert.Equal(5, into[10].ArgumentCount);
                        Assert.Equal(RespCommand.HMSET, into[10].Command);
                        Assert.True("HMSET\r\n"u8.SequenceEqual(padded[into[10].ByteStart..into[10].ByteEnd]));

                        Assert.True(into[11].IsArgument);
                        Assert.True("abc\r\n"u8.SequenceEqual(padded[into[11].ByteStart..into[11].ByteEnd]));

                        Assert.True(into[12].IsArgument);
                        Assert.True("def\r\n"u8.SequenceEqual(padded[into[12].ByteStart..into[12].ByteEnd]));

                        Assert.True(into[13].IsArgument);
                        Assert.True("ghi\r\n"u8.SequenceEqual(padded[into[13].ByteStart..into[13].ByteEnd]));

                        Assert.True(into[14].IsArgument);
                        Assert.True("jkl\r\n"u8.SequenceEqual(padded[into[14].ByteStart..into[14].ByteEnd]));
                    }

                    // APPEND
                    if (expected.Length > 5)
                    {
                        Assert.True(into[15].IsCommand);
                        Assert.Equal(6, into[15].ArgumentCount);
                        Assert.Equal(RespCommand.APPEND, into[15].Command);
                        Assert.True("APPEND\r\n"u8.SequenceEqual(padded[into[15].ByteStart..into[15].ByteEnd]));

                        Assert.True(into[16].IsArgument);
                        Assert.True("mno\r\n"u8.SequenceEqual(padded[into[16].ByteStart..into[16].ByteEnd]));

                        Assert.True(into[17].IsArgument);
                        Assert.True("pqr\r\n"u8.SequenceEqual(padded[into[17].ByteStart..into[17].ByteEnd]));

                        Assert.True(into[18].IsArgument);
                        Assert.True("stu\r\n"u8.SequenceEqual(padded[into[18].ByteStart..into[18].ByteEnd]));

                        Assert.True(into[19].IsArgument);
                        Assert.True("vwx\r\n"u8.SequenceEqual(padded[into[19].ByteStart..into[19].ByteEnd]));

                        Assert.True(into[20].IsArgument);
                        Assert.True("yz\r\n"u8.SequenceEqual(padded[into[20].ByteStart..into[20].ByteEnd]));
                    }

                    // HEXPIRE
                    if (expected.Length > 6)
                    {
                        Assert.Fail("Shouldn't have parsed command");
                    }
                }
            }

            // 8 commands
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[36];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[8];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", [],
                    ("GET", ["hello"]),
                    ("SET", ["world", "fizz"]),
                    ("HMGET", ["buzz", "foo", "bar"]),
                    ("HMSET", ["abc", "def", "ghi", "jkl"]),
                    ("APPEND", ["mno", "pqr", "stu", "vwx", "yz"]),
                    ("HEXPIRE", ["", "1", "12", "123", "1234", "12345"]),
                    ("HPEXPIRE", ["000000", "11111", "2222", "333", "44", "5", ""])
                );
                for (var i = 1; i < rawCmd.Length; i++)
                {
                    var truncated = rawCmd[..^i];
                    var truncatedLength = truncated.Length;

                    var expected = boundaries.ToArray().TakeWhile(x => x.StopByte <= truncatedLength).ToArray();

                    var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                    RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], truncated.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                    var expectedSlots = expected.Sum(static x => x.ArgumentCount);
                    var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                    Assert.Equal(expectedSlots, usedSlots);
                    Assert.Equal(expectedConsumed, bytesConsumed);

                    // PING
                    if (expected.Length > 0)
                    {
                        Assert.True(into[0].IsCommand);
                        Assert.Equal(1, into[0].ArgumentCount);
                        Assert.Equal(RespCommand.PING, into[0].Command);
                        Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));
                    }

                    // GET
                    if (expected.Length > 1)
                    {
                        Assert.True(into[1].IsCommand);
                        Assert.Equal(2, into[1].ArgumentCount);
                        Assert.Equal(RespCommand.GET, into[1].Command);
                        Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                        Assert.True(into[2].IsArgument);
                        Assert.True("hello\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));
                    }

                    // SET
                    if (expected.Length > 2)
                    {
                        Assert.True(into[3].IsCommand);
                        Assert.Equal(3, into[3].ArgumentCount);
                        Assert.Equal(RespCommand.SET, into[3].Command);
                        Assert.True("SET\r\n"u8.SequenceEqual(padded[into[3].ByteStart..into[3].ByteEnd]));

                        Assert.True(into[4].IsArgument);
                        Assert.True("world\r\n"u8.SequenceEqual(padded[into[4].ByteStart..into[4].ByteEnd]));

                        Assert.True(into[5].IsArgument);
                        Assert.True("fizz\r\n"u8.SequenceEqual(padded[into[5].ByteStart..into[5].ByteEnd]));
                    }

                    // HMGET
                    if (expected.Length > 3)
                    {
                        Assert.True(into[6].IsCommand);
                        Assert.Equal(4, into[6].ArgumentCount);
                        Assert.Equal(RespCommand.HMGET, into[6].Command);
                        Assert.True("HMGET\r\n"u8.SequenceEqual(padded[into[6].ByteStart..into[6].ByteEnd]));

                        Assert.True(into[7].IsArgument);
                        Assert.True("buzz\r\n"u8.SequenceEqual(padded[into[7].ByteStart..into[7].ByteEnd]));

                        Assert.True(into[8].IsArgument);
                        Assert.True("foo\r\n"u8.SequenceEqual(padded[into[8].ByteStart..into[8].ByteEnd]));

                        Assert.True(into[9].IsArgument);
                        Assert.True("bar\r\n"u8.SequenceEqual(padded[into[9].ByteStart..into[9].ByteEnd]));
                    }

                    // HMSET
                    if (expected.Length > 4)
                    {
                        Assert.True(into[10].IsCommand);
                        Assert.Equal(5, into[10].ArgumentCount);
                        Assert.Equal(RespCommand.HMSET, into[10].Command);
                        Assert.True("HMSET\r\n"u8.SequenceEqual(padded[into[10].ByteStart..into[10].ByteEnd]));

                        Assert.True(into[11].IsArgument);
                        Assert.True("abc\r\n"u8.SequenceEqual(padded[into[11].ByteStart..into[11].ByteEnd]));

                        Assert.True(into[12].IsArgument);
                        Assert.True("def\r\n"u8.SequenceEqual(padded[into[12].ByteStart..into[12].ByteEnd]));

                        Assert.True(into[13].IsArgument);
                        Assert.True("ghi\r\n"u8.SequenceEqual(padded[into[13].ByteStart..into[13].ByteEnd]));

                        Assert.True(into[14].IsArgument);
                        Assert.True("jkl\r\n"u8.SequenceEqual(padded[into[14].ByteStart..into[14].ByteEnd]));
                    }

                    // APPEND
                    if (expected.Length > 5)
                    {
                        Assert.True(into[15].IsCommand);
                        Assert.Equal(6, into[15].ArgumentCount);
                        Assert.Equal(RespCommand.APPEND, into[15].Command);
                        Assert.True("APPEND\r\n"u8.SequenceEqual(padded[into[15].ByteStart..into[15].ByteEnd]));

                        Assert.True(into[16].IsArgument);
                        Assert.True("mno\r\n"u8.SequenceEqual(padded[into[16].ByteStart..into[16].ByteEnd]));

                        Assert.True(into[17].IsArgument);
                        Assert.True("pqr\r\n"u8.SequenceEqual(padded[into[17].ByteStart..into[17].ByteEnd]));

                        Assert.True(into[18].IsArgument);
                        Assert.True("stu\r\n"u8.SequenceEqual(padded[into[18].ByteStart..into[18].ByteEnd]));

                        Assert.True(into[19].IsArgument);
                        Assert.True("vwx\r\n"u8.SequenceEqual(padded[into[19].ByteStart..into[19].ByteEnd]));

                        Assert.True(into[20].IsArgument);
                        Assert.True("yz\r\n"u8.SequenceEqual(padded[into[20].ByteStart..into[20].ByteEnd]));
                    }

                    // HEXPIRE
                    if (expected.Length > 6)
                    {
                        Assert.True(into[21].IsCommand);
                        Assert.Equal(7, into[21].ArgumentCount);
                        Assert.Equal(RespCommand.HEXPIRE, into[21].Command);
                        Assert.True("HEXPIRE\r\n"u8.SequenceEqual(padded[into[21].ByteStart..into[21].ByteEnd]));

                        Assert.True(into[22].IsArgument);
                        Assert.True("\r\n"u8.SequenceEqual(padded[into[22].ByteStart..into[22].ByteEnd]));

                        Assert.True(into[23].IsArgument);
                        Assert.True("1\r\n"u8.SequenceEqual(padded[into[23].ByteStart..into[23].ByteEnd]));

                        Assert.True(into[24].IsArgument);
                        Assert.True("12\r\n"u8.SequenceEqual(padded[into[24].ByteStart..into[24].ByteEnd]));

                        Assert.True(into[25].IsArgument);
                        Assert.True("123\r\n"u8.SequenceEqual(padded[into[25].ByteStart..into[25].ByteEnd]));

                        Assert.True(into[26].IsArgument);
                        Assert.True("1234\r\n"u8.SequenceEqual(padded[into[26].ByteStart..into[26].ByteEnd]));

                        Assert.True(into[27].IsArgument);
                        Assert.True("12345\r\n"u8.SequenceEqual(padded[into[27].ByteStart..into[27].ByteEnd]));
                    }

                    // HPEXPIRE
                    if (expected.Length > 7)
                    {
                        Assert.Fail("Shouldn't have parsed command");
                    }
                }
            }
        }

        [Fact]
        public void IncompleteInto()
        {
            // will always have enough space for 1 command with no arguments

            // 2 commands
            {
                Span<ParsedRespCommandOrArgument> rawInto = stackalloc ParsedRespCommandOrArgument[3];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[2];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", [],
                    ("GET", ["hello"])
                );
                for (var intoSize = 1; intoSize < rawInto.Length; intoSize++)
                {
                    rawInto.Clear();
                    var into = rawInto[..intoSize];
                    var spaceTaken = 0;
                    var expected = boundaries.ToArray().TakeWhile(x => { var take = (spaceTaken + x.ArgumentCount <= intoSize); spaceTaken += x.ArgumentCount; return take; }).ToArray();

                    var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                    RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], rawCmd.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                    var expectedSlots = expected.Sum(static x => x.ArgumentCount);
                    var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                    Assert.Equal(expectedSlots, usedSlots);
                    Assert.Equal(expectedConsumed, bytesConsumed);

                    // PING
                    if (expected.Length > 0)
                    {
                        Assert.True(into[0].IsCommand);
                        Assert.Equal(1, into[0].ArgumentCount);
                        Assert.Equal(RespCommand.PING, into[0].Command);
                        Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));
                    }

                    // GET
                    if (expected.Length > 1)
                    {
                        Assert.Fail("Should not have parsed command");
                    }
                }
            }

            // 3 commands
            {
                Span<ParsedRespCommandOrArgument> rawInto = stackalloc ParsedRespCommandOrArgument[6];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[3];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", [],
                    ("GET", ["hello"]),
                    ("SET", ["world", "fizz"])
                );
                for (var intoSize = 1; intoSize < rawInto.Length; intoSize++)
                {
                    rawInto.Clear();
                    var into = rawInto[..intoSize];
                    var spaceTaken = 0;
                    var expected = boundaries.ToArray().TakeWhile(x => { var take = (spaceTaken + x.ArgumentCount <= intoSize); spaceTaken += x.ArgumentCount; return take; }).ToArray();

                    var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                    RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], rawCmd.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                    var expectedSlots = expected.Sum(static x => x.ArgumentCount);
                    var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                    Assert.Equal(expectedSlots, usedSlots);
                    Assert.Equal(expectedConsumed, bytesConsumed);

                    // PING
                    if (expected.Length > 0)
                    {
                        Assert.True(into[0].IsCommand);
                        Assert.Equal(1, into[0].ArgumentCount);
                        Assert.Equal(RespCommand.PING, into[0].Command);
                        Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));
                    }

                    // GET
                    if (expected.Length > 1)
                    {
                        Assert.True(into[1].IsCommand);
                        Assert.Equal(2, into[1].ArgumentCount);
                        Assert.Equal(RespCommand.GET, into[1].Command);
                        Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                        Assert.True(into[2].IsArgument);
                        Assert.True("hello\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));
                    }

                    // SET
                    if (expected.Length > 2)
                    {
                        Assert.Fail("Should't have parsed command");
                    }
                }
            }

            // 4 commands
            {
                Span<ParsedRespCommandOrArgument> rawInto = stackalloc ParsedRespCommandOrArgument[10];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[4];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", [],
                    ("GET", ["hello"]),
                    ("SET", ["world", "fizz"]),
                    ("HMGET", ["buzz", "foo", "bar"])
                );
                for (var intoSize = 1; intoSize < rawInto.Length; intoSize++)
                {
                    rawInto.Clear();
                    var into = rawInto[..intoSize];
                    var spaceTaken = 0;
                    var expected = boundaries.ToArray().TakeWhile(x => { var take = (spaceTaken + x.ArgumentCount <= intoSize); spaceTaken += x.ArgumentCount; return take; }).ToArray();

                    var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                    RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], rawCmd.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                    var expectedSlots = expected.Sum(static x => x.ArgumentCount);
                    var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                    Assert.Equal(expectedSlots, usedSlots);
                    Assert.Equal(expectedConsumed, bytesConsumed);

                    // PING
                    if (expected.Length > 0)
                    {
                        Assert.True(into[0].IsCommand);
                        Assert.Equal(1, into[0].ArgumentCount);
                        Assert.Equal(RespCommand.PING, into[0].Command);
                        Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));
                    }

                    // GET
                    if (expected.Length > 1)
                    {
                        Assert.True(into[1].IsCommand);
                        Assert.Equal(2, into[1].ArgumentCount);
                        Assert.Equal(RespCommand.GET, into[1].Command);
                        Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                        Assert.True(into[2].IsArgument);
                        Assert.True("hello\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));
                    }

                    // SET
                    if (expected.Length > 2)
                    {
                        Assert.True(into[3].IsCommand);
                        Assert.Equal(3, into[3].ArgumentCount);
                        Assert.Equal(RespCommand.SET, into[3].Command);
                        Assert.True("SET\r\n"u8.SequenceEqual(padded[into[3].ByteStart..into[3].ByteEnd]));

                        Assert.True(into[4].IsArgument);
                        Assert.True("world\r\n"u8.SequenceEqual(padded[into[4].ByteStart..into[4].ByteEnd]));

                        Assert.True(into[5].IsArgument);
                        Assert.True("fizz\r\n"u8.SequenceEqual(padded[into[5].ByteStart..into[5].ByteEnd]));
                    }

                    // HMGET
                    if (expected.Length > 3)
                    {
                        Assert.Fail("Shouldn't have parsed command");
                    }
                }
            }

            // 5 commands
            {
                Span<ParsedRespCommandOrArgument> rawInto = stackalloc ParsedRespCommandOrArgument[15];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[5];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", [],
                    ("GET", ["hello"]),
                    ("SET", ["world", "fizz"]),
                    ("HMGET", ["buzz", "foo", "bar"]),
                    ("HMSET", ["abc", "def", "ghi", "jkl"])
                );
                for (var intoSize = 1; intoSize < rawInto.Length; intoSize++)
                {
                    rawInto.Clear();
                    var into = rawInto[..intoSize];
                    var spaceTaken = 0;
                    var expected = boundaries.ToArray().TakeWhile(x => { var take = (spaceTaken + x.ArgumentCount <= intoSize); spaceTaken += x.ArgumentCount; return take; }).ToArray();

                    var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                    RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], rawCmd.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                    var expectedSlots = expected.Sum(static x => x.ArgumentCount);
                    var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                    Assert.Equal(expectedSlots, usedSlots);
                    Assert.Equal(expectedConsumed, bytesConsumed);

                    // PING
                    if (expected.Length > 0)
                    {
                        Assert.True(into[0].IsCommand);
                        Assert.Equal(1, into[0].ArgumentCount);
                        Assert.Equal(RespCommand.PING, into[0].Command);
                        Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));
                    }

                    // GET
                    if (expected.Length > 1)
                    {
                        Assert.True(into[1].IsCommand);
                        Assert.Equal(2, into[1].ArgumentCount);
                        Assert.Equal(RespCommand.GET, into[1].Command);
                        Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                        Assert.True(into[2].IsArgument);
                        Assert.True("hello\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));
                    }

                    // SET
                    if (expected.Length > 2)
                    {
                        Assert.True(into[3].IsCommand);
                        Assert.Equal(3, into[3].ArgumentCount);
                        Assert.Equal(RespCommand.SET, into[3].Command);
                        Assert.True("SET\r\n"u8.SequenceEqual(padded[into[3].ByteStart..into[3].ByteEnd]));

                        Assert.True(into[4].IsArgument);
                        Assert.True("world\r\n"u8.SequenceEqual(padded[into[4].ByteStart..into[4].ByteEnd]));

                        Assert.True(into[5].IsArgument);
                        Assert.True("fizz\r\n"u8.SequenceEqual(padded[into[5].ByteStart..into[5].ByteEnd]));
                    }

                    // HMGET
                    if (expected.Length > 3)
                    {
                        Assert.True(into[6].IsCommand);
                        Assert.Equal(4, into[6].ArgumentCount);
                        Assert.Equal(RespCommand.HMGET, into[6].Command);
                        Assert.True("HMGET\r\n"u8.SequenceEqual(padded[into[6].ByteStart..into[6].ByteEnd]));

                        Assert.True(into[7].IsArgument);
                        Assert.True("buzz\r\n"u8.SequenceEqual(padded[into[7].ByteStart..into[7].ByteEnd]));

                        Assert.True(into[8].IsArgument);
                        Assert.True("foo\r\n"u8.SequenceEqual(padded[into[8].ByteStart..into[8].ByteEnd]));

                        Assert.True(into[9].IsArgument);
                        Assert.True("bar\r\n"u8.SequenceEqual(padded[into[9].ByteStart..into[9].ByteEnd]));
                    }

                    // HMSET
                    if (expected.Length > 4)
                    {
                        Assert.Fail("Shouldn't have parsed command");
                    }
                }
            }

            // 6 commands
            {
                Span<ParsedRespCommandOrArgument> rawInto = stackalloc ParsedRespCommandOrArgument[21];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[6];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", [],
                    ("GET", ["hello"]),
                    ("SET", ["world", "fizz"]),
                    ("HMGET", ["buzz", "foo", "bar"]),
                    ("HMSET", ["abc", "def", "ghi", "jkl"]),
                    ("APPEND", ["mno", "pqr", "stu", "vwx", "yz"])
                );
                for (var intoSize = 1; intoSize < rawInto.Length; intoSize++)
                {
                    rawInto.Clear();
                    var into = rawInto[..intoSize];
                    var spaceTaken = 0;
                    var expected = boundaries.ToArray().TakeWhile(x => { var take = (spaceTaken + x.ArgumentCount <= intoSize); spaceTaken += x.ArgumentCount; return take; }).ToArray();

                    var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                    RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], rawCmd.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                    var expectedSlots = expected.Sum(static x => x.ArgumentCount);
                    var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                    Assert.Equal(expectedSlots, usedSlots);
                    Assert.Equal(expectedConsumed, bytesConsumed);

                    // PING
                    if (expected.Length > 0)
                    {
                        Assert.True(into[0].IsCommand);
                        Assert.Equal(1, into[0].ArgumentCount);
                        Assert.Equal(RespCommand.PING, into[0].Command);
                        Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));
                    }

                    // GET
                    if (expected.Length > 1)
                    {
                        Assert.True(into[1].IsCommand);
                        Assert.Equal(2, into[1].ArgumentCount);
                        Assert.Equal(RespCommand.GET, into[1].Command);
                        Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                        Assert.True(into[2].IsArgument);
                        Assert.True("hello\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));
                    }

                    // SET
                    if (expected.Length > 2)
                    {
                        Assert.True(into[3].IsCommand);
                        Assert.Equal(3, into[3].ArgumentCount);
                        Assert.Equal(RespCommand.SET, into[3].Command);
                        Assert.True("SET\r\n"u8.SequenceEqual(padded[into[3].ByteStart..into[3].ByteEnd]));

                        Assert.True(into[4].IsArgument);
                        Assert.True("world\r\n"u8.SequenceEqual(padded[into[4].ByteStart..into[4].ByteEnd]));

                        Assert.True(into[5].IsArgument);
                        Assert.True("fizz\r\n"u8.SequenceEqual(padded[into[5].ByteStart..into[5].ByteEnd]));
                    }

                    // HMGET
                    if (expected.Length > 3)
                    {
                        Assert.True(into[6].IsCommand);
                        Assert.Equal(4, into[6].ArgumentCount);
                        Assert.Equal(RespCommand.HMGET, into[6].Command);
                        Assert.True("HMGET\r\n"u8.SequenceEqual(padded[into[6].ByteStart..into[6].ByteEnd]));

                        Assert.True(into[7].IsArgument);
                        Assert.True("buzz\r\n"u8.SequenceEqual(padded[into[7].ByteStart..into[7].ByteEnd]));

                        Assert.True(into[8].IsArgument);
                        Assert.True("foo\r\n"u8.SequenceEqual(padded[into[8].ByteStart..into[8].ByteEnd]));

                        Assert.True(into[9].IsArgument);
                        Assert.True("bar\r\n"u8.SequenceEqual(padded[into[9].ByteStart..into[9].ByteEnd]));
                    }

                    // HMSET
                    if (expected.Length > 4)
                    {
                        Assert.True(into[10].IsCommand);
                        Assert.Equal(5, into[10].ArgumentCount);
                        Assert.Equal(RespCommand.HMSET, into[10].Command);
                        Assert.True("HMSET\r\n"u8.SequenceEqual(padded[into[10].ByteStart..into[10].ByteEnd]));

                        Assert.True(into[11].IsArgument);
                        Assert.True("abc\r\n"u8.SequenceEqual(padded[into[11].ByteStart..into[11].ByteEnd]));

                        Assert.True(into[12].IsArgument);
                        Assert.True("def\r\n"u8.SequenceEqual(padded[into[12].ByteStart..into[12].ByteEnd]));

                        Assert.True(into[13].IsArgument);
                        Assert.True("ghi\r\n"u8.SequenceEqual(padded[into[13].ByteStart..into[13].ByteEnd]));

                        Assert.True(into[14].IsArgument);
                        Assert.True("jkl\r\n"u8.SequenceEqual(padded[into[14].ByteStart..into[14].ByteEnd]));
                    }

                    // APPEND
                    if (expected.Length > 5)
                    {
                        Assert.Fail("Command shouldn't have been parsed");
                    }
                }
            }

            // 7 commands
            {
                Span<ParsedRespCommandOrArgument> rawInto = stackalloc ParsedRespCommandOrArgument[28];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[7];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", [],
                    ("GET", ["hello"]),
                    ("SET", ["world", "fizz"]),
                    ("HMGET", ["buzz", "foo", "bar"]),
                    ("HMSET", ["abc", "def", "ghi", "jkl"]),
                    ("APPEND", ["mno", "pqr", "stu", "vwx", "yz"]),
                    ("HEXPIRE", ["", "1", "12", "123", "1234", "12345"])
                );
                for (var intoSize = 1; intoSize < rawInto.Length; intoSize++)
                {
                    rawInto.Clear();
                    var into = rawInto[..intoSize];
                    var spaceTaken = 0;
                    var expected = boundaries.ToArray().TakeWhile(x => { var take = (spaceTaken + x.ArgumentCount <= intoSize); spaceTaken += x.ArgumentCount; return take; }).ToArray();

                    var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                    RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], rawCmd.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                    var expectedSlots = expected.Sum(static x => x.ArgumentCount);
                    var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                    Assert.Equal(expectedSlots, usedSlots);
                    Assert.Equal(expectedConsumed, bytesConsumed);

                    // PING
                    if (expected.Length > 0)
                    {
                        Assert.True(into[0].IsCommand);
                        Assert.Equal(1, into[0].ArgumentCount);
                        Assert.Equal(RespCommand.PING, into[0].Command);
                        Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));
                    }

                    // GET
                    if (expected.Length > 1)
                    {
                        Assert.True(into[1].IsCommand);
                        Assert.Equal(2, into[1].ArgumentCount);
                        Assert.Equal(RespCommand.GET, into[1].Command);
                        Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                        Assert.True(into[2].IsArgument);
                        Assert.True("hello\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));
                    }

                    // SET
                    if (expected.Length > 2)
                    {
                        Assert.True(into[3].IsCommand);
                        Assert.Equal(3, into[3].ArgumentCount);
                        Assert.Equal(RespCommand.SET, into[3].Command);
                        Assert.True("SET\r\n"u8.SequenceEqual(padded[into[3].ByteStart..into[3].ByteEnd]));

                        Assert.True(into[4].IsArgument);
                        Assert.True("world\r\n"u8.SequenceEqual(padded[into[4].ByteStart..into[4].ByteEnd]));

                        Assert.True(into[5].IsArgument);
                        Assert.True("fizz\r\n"u8.SequenceEqual(padded[into[5].ByteStart..into[5].ByteEnd]));
                    }

                    // HMGET
                    if (expected.Length > 3)
                    {
                        Assert.True(into[6].IsCommand);
                        Assert.Equal(4, into[6].ArgumentCount);
                        Assert.Equal(RespCommand.HMGET, into[6].Command);
                        Assert.True("HMGET\r\n"u8.SequenceEqual(padded[into[6].ByteStart..into[6].ByteEnd]));

                        Assert.True(into[7].IsArgument);
                        Assert.True("buzz\r\n"u8.SequenceEqual(padded[into[7].ByteStart..into[7].ByteEnd]));

                        Assert.True(into[8].IsArgument);
                        Assert.True("foo\r\n"u8.SequenceEqual(padded[into[8].ByteStart..into[8].ByteEnd]));

                        Assert.True(into[9].IsArgument);
                        Assert.True("bar\r\n"u8.SequenceEqual(padded[into[9].ByteStart..into[9].ByteEnd]));
                    }

                    // HMSET
                    if (expected.Length > 4)
                    {
                        Assert.True(into[10].IsCommand);
                        Assert.Equal(5, into[10].ArgumentCount);
                        Assert.Equal(RespCommand.HMSET, into[10].Command);
                        Assert.True("HMSET\r\n"u8.SequenceEqual(padded[into[10].ByteStart..into[10].ByteEnd]));

                        Assert.True(into[11].IsArgument);
                        Assert.True("abc\r\n"u8.SequenceEqual(padded[into[11].ByteStart..into[11].ByteEnd]));

                        Assert.True(into[12].IsArgument);
                        Assert.True("def\r\n"u8.SequenceEqual(padded[into[12].ByteStart..into[12].ByteEnd]));

                        Assert.True(into[13].IsArgument);
                        Assert.True("ghi\r\n"u8.SequenceEqual(padded[into[13].ByteStart..into[13].ByteEnd]));

                        Assert.True(into[14].IsArgument);
                        Assert.True("jkl\r\n"u8.SequenceEqual(padded[into[14].ByteStart..into[14].ByteEnd]));
                    }

                    // APPEND
                    if (expected.Length > 5)
                    {
                        Assert.True(into[15].IsCommand);
                        Assert.Equal(6, into[15].ArgumentCount);
                        Assert.Equal(RespCommand.APPEND, into[15].Command);
                        Assert.True("APPEND\r\n"u8.SequenceEqual(padded[into[15].ByteStart..into[15].ByteEnd]));

                        Assert.True(into[16].IsArgument);
                        Assert.True("mno\r\n"u8.SequenceEqual(padded[into[16].ByteStart..into[16].ByteEnd]));

                        Assert.True(into[17].IsArgument);
                        Assert.True("pqr\r\n"u8.SequenceEqual(padded[into[17].ByteStart..into[17].ByteEnd]));

                        Assert.True(into[18].IsArgument);
                        Assert.True("stu\r\n"u8.SequenceEqual(padded[into[18].ByteStart..into[18].ByteEnd]));

                        Assert.True(into[19].IsArgument);
                        Assert.True("vwx\r\n"u8.SequenceEqual(padded[into[19].ByteStart..into[19].ByteEnd]));

                        Assert.True(into[20].IsArgument);
                        Assert.True("yz\r\n"u8.SequenceEqual(padded[into[20].ByteStart..into[20].ByteEnd]));
                    }

                    // HEXPIRE
                    if (expected.Length > 6)
                    {
                        Assert.Fail("Shouldn't have parsed command");
                    }
                }
            }

            // 8 commands
            {
                Span<ParsedRespCommandOrArgument> rawInto = stackalloc ParsedRespCommandOrArgument[36];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[8];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", [],
                    ("GET", ["hello"]),
                    ("SET", ["world", "fizz"]),
                    ("HMGET", ["buzz", "foo", "bar"]),
                    ("HMSET", ["abc", "def", "ghi", "jkl"]),
                    ("APPEND", ["mno", "pqr", "stu", "vwx", "yz"]),
                    ("HEXPIRE", ["", "1", "12", "123", "1234", "12345"]),
                    ("HPEXPIRE", ["000000", "11111", "2222", "333", "44", "5", ""])
                );
                for (var intoSize = 1; intoSize < rawInto.Length; intoSize++)
                {
                    rawInto.Clear();
                    var into = rawInto[..intoSize];
                    var spaceTaken = 0;
                    var expected = boundaries.ToArray().TakeWhile(x => { var take = (spaceTaken + x.ArgumentCount <= intoSize); spaceTaken += x.ArgumentCount; return take; }).ToArray();

                    var padded = Pad(rawCmd, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                    RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], rawCmd.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                    var expectedSlots = expected.Sum(static x => x.ArgumentCount);
                    var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                    Assert.Equal(expectedSlots, usedSlots);
                    Assert.Equal(expectedConsumed, bytesConsumed);

                    // PING
                    if (expected.Length > 0)
                    {
                        Assert.True(into[0].IsCommand);
                        Assert.Equal(1, into[0].ArgumentCount);
                        Assert.Equal(RespCommand.PING, into[0].Command);
                        Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));
                    }

                    // GET
                    if (expected.Length > 1)
                    {
                        Assert.True(into[1].IsCommand);
                        Assert.Equal(2, into[1].ArgumentCount);
                        Assert.Equal(RespCommand.GET, into[1].Command);
                        Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                        Assert.True(into[2].IsArgument);
                        Assert.True("hello\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));
                    }

                    // SET
                    if (expected.Length > 2)
                    {
                        Assert.True(into[3].IsCommand);
                        Assert.Equal(3, into[3].ArgumentCount);
                        Assert.Equal(RespCommand.SET, into[3].Command);
                        Assert.True("SET\r\n"u8.SequenceEqual(padded[into[3].ByteStart..into[3].ByteEnd]));

                        Assert.True(into[4].IsArgument);
                        Assert.True("world\r\n"u8.SequenceEqual(padded[into[4].ByteStart..into[4].ByteEnd]));

                        Assert.True(into[5].IsArgument);
                        Assert.True("fizz\r\n"u8.SequenceEqual(padded[into[5].ByteStart..into[5].ByteEnd]));
                    }

                    // HMGET
                    if (expected.Length > 3)
                    {
                        Assert.True(into[6].IsCommand);
                        Assert.Equal(4, into[6].ArgumentCount);
                        Assert.Equal(RespCommand.HMGET, into[6].Command);
                        Assert.True("HMGET\r\n"u8.SequenceEqual(padded[into[6].ByteStart..into[6].ByteEnd]));

                        Assert.True(into[7].IsArgument);
                        Assert.True("buzz\r\n"u8.SequenceEqual(padded[into[7].ByteStart..into[7].ByteEnd]));

                        Assert.True(into[8].IsArgument);
                        Assert.True("foo\r\n"u8.SequenceEqual(padded[into[8].ByteStart..into[8].ByteEnd]));

                        Assert.True(into[9].IsArgument);
                        Assert.True("bar\r\n"u8.SequenceEqual(padded[into[9].ByteStart..into[9].ByteEnd]));
                    }

                    // HMSET
                    if (expected.Length > 4)
                    {
                        Assert.True(into[10].IsCommand);
                        Assert.Equal(5, into[10].ArgumentCount);
                        Assert.Equal(RespCommand.HMSET, into[10].Command);
                        Assert.True("HMSET\r\n"u8.SequenceEqual(padded[into[10].ByteStart..into[10].ByteEnd]));

                        Assert.True(into[11].IsArgument);
                        Assert.True("abc\r\n"u8.SequenceEqual(padded[into[11].ByteStart..into[11].ByteEnd]));

                        Assert.True(into[12].IsArgument);
                        Assert.True("def\r\n"u8.SequenceEqual(padded[into[12].ByteStart..into[12].ByteEnd]));

                        Assert.True(into[13].IsArgument);
                        Assert.True("ghi\r\n"u8.SequenceEqual(padded[into[13].ByteStart..into[13].ByteEnd]));

                        Assert.True(into[14].IsArgument);
                        Assert.True("jkl\r\n"u8.SequenceEqual(padded[into[14].ByteStart..into[14].ByteEnd]));
                    }

                    // APPEND
                    if (expected.Length > 5)
                    {
                        Assert.True(into[15].IsCommand);
                        Assert.Equal(6, into[15].ArgumentCount);
                        Assert.Equal(RespCommand.APPEND, into[15].Command);
                        Assert.True("APPEND\r\n"u8.SequenceEqual(padded[into[15].ByteStart..into[15].ByteEnd]));

                        Assert.True(into[16].IsArgument);
                        Assert.True("mno\r\n"u8.SequenceEqual(padded[into[16].ByteStart..into[16].ByteEnd]));

                        Assert.True(into[17].IsArgument);
                        Assert.True("pqr\r\n"u8.SequenceEqual(padded[into[17].ByteStart..into[17].ByteEnd]));

                        Assert.True(into[18].IsArgument);
                        Assert.True("stu\r\n"u8.SequenceEqual(padded[into[18].ByteStart..into[18].ByteEnd]));

                        Assert.True(into[19].IsArgument);
                        Assert.True("vwx\r\n"u8.SequenceEqual(padded[into[19].ByteStart..into[19].ByteEnd]));

                        Assert.True(into[20].IsArgument);
                        Assert.True("yz\r\n"u8.SequenceEqual(padded[into[20].ByteStart..into[20].ByteEnd]));
                    }

                    // HEXPIRE
                    if (expected.Length > 6)
                    {
                        Assert.True(into[21].IsCommand);
                        Assert.Equal(7, into[21].ArgumentCount);
                        Assert.Equal(RespCommand.HEXPIRE, into[21].Command);
                        Assert.True("HEXPIRE\r\n"u8.SequenceEqual(padded[into[21].ByteStart..into[21].ByteEnd]));

                        Assert.True(into[22].IsArgument);
                        Assert.True("\r\n"u8.SequenceEqual(padded[into[22].ByteStart..into[22].ByteEnd]));

                        Assert.True(into[23].IsArgument);
                        Assert.True("1\r\n"u8.SequenceEqual(padded[into[23].ByteStart..into[23].ByteEnd]));

                        Assert.True(into[24].IsArgument);
                        Assert.True("12\r\n"u8.SequenceEqual(padded[into[24].ByteStart..into[24].ByteEnd]));

                        Assert.True(into[25].IsArgument);
                        Assert.True("123\r\n"u8.SequenceEqual(padded[into[25].ByteStart..into[25].ByteEnd]));

                        Assert.True(into[26].IsArgument);
                        Assert.True("1234\r\n"u8.SequenceEqual(padded[into[26].ByteStart..into[26].ByteEnd]));

                        Assert.True(into[27].IsArgument);
                        Assert.True("12345\r\n"u8.SequenceEqual(padded[into[27].ByteStart..into[27].ByteEnd]));
                    }

                    // HPEXPIRE
                    if (expected.Length > 7)
                    {
                        Assert.Fail("Shouldn't have parsed command");
                    }
                }
            }
        }

        [Fact]
        public void MalformedData()
        {
            // 1 command
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[1];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[1];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", []
                );
                for (var i = 0; i < rawCmd.Length; i++)
                {
                    if (char.IsAsciiDigit((char)rawCmd[i]))
                    {
                        continue;
                    }

                    var expected = boundaries.ToArray().TakeWhile(x => x.StopByte <= i).ToArray();

                    for (var j = 0; j <= byte.MaxValue; j++)
                    {
                        if (j == rawCmd[i])
                        {
                            continue;
                        }

                        if (char.ToLowerInvariant((char)j) == char.ToLowerInvariant((char)rawCmd[i]))
                        {
                            continue;
                        }

                        var smashed = rawCmd.ToArray();
                        smashed[i] = (byte)j;

                        var padded = Pad(smashed, out var allocatedBufferSize, out var bitmapLength);

                        Span<byte> digitsBitmap = new byte[bitmapLength];

                        RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                        RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], smashed.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                        var expectedSlots = expected.Sum(static x => x.ArgumentCount) + 1;
                        var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                        Assert.Equal(expectedSlots, usedSlots);
                        Assert.Equal(expectedConsumed, bytesConsumed);

                        Assert.True(into[usedSlots - 1].IsMalformed);

                        // PING
                        if (expected.Length > 0)
                        {
                            Assert.Fail("Shouldn't have parsed command");
                        }
                    }
                }
            }

            // 2 commands
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[3];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[2];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", [],
                    ("GET", ["a"])
                );
                for (var i = 0; i < rawCmd.Length; i++)
                {
                    if (char.IsAsciiDigit((char)rawCmd[i]))
                    {
                        continue;
                    }

                    if (rawCmd[i] == 'a')
                    {
                        continue;
                    }

                    var expected = boundaries.ToArray().TakeWhile(x => x.StopByte <= i).ToArray();

                    for (var j = 0; j <= byte.MaxValue; j++)
                    {
                        if (j == rawCmd[i])
                        {
                            continue;
                        }

                        if (rawCmd[i] == 'G' && j is (byte)'S' or 's')
                        {
                            continue;
                        }

                        if (char.ToLowerInvariant((char)j) == char.ToLowerInvariant((char)rawCmd[i]))
                        {
                            continue;
                        }

                        var smashed = rawCmd.ToArray();
                        smashed[i] = (byte)j;

                        var padded = Pad(smashed, out var allocatedBufferSize, out var bitmapLength);

                        Span<byte> digitsBitmap = new byte[bitmapLength];

                        RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                        RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], smashed.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                        var expectedSlots = expected.Sum(static x => x.ArgumentCount) + 1;
                        var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                        Assert.Equal(expectedSlots, usedSlots);
                        Assert.Equal(expectedConsumed, bytesConsumed);

                        Assert.True(into[usedSlots - 1].IsMalformed);

                        // PING
                        if (expected.Length > 0)
                        {
                            Assert.True(into[0].IsCommand);
                            Assert.Equal(1, into[0].ArgumentCount);
                            Assert.Equal(RespCommand.PING, into[0].Command);
                            Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));
                        }

                        // GET
                        if (expected.Length > 1)
                        {
                            Assert.Fail("Should not have parsed command");
                        }
                    }
                }
            }

            // 3 commands
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[6];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[3];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", [],
                    ("GET", ["a"]),
                    ("SET", ["b", "bb"])
                );
                for (var i = 0; i < rawCmd.Length; i++)
                {
                    if (char.IsAsciiDigit((char)rawCmd[i]))
                    {
                        continue;
                    }

                    if (rawCmd[i] is (byte)'a' or (byte)'b')
                    {
                        continue;
                    }

                    var expected = boundaries.ToArray().TakeWhile(x => x.StopByte <= i).ToArray();

                    for (var j = 0; j <= byte.MaxValue; j++)
                    {
                        if (j == rawCmd[i])
                        {
                            continue;
                        }

                        if (rawCmd[i] == 'G' && j is (byte)'S' or 's')
                        {
                            continue;
                        }

                        if (rawCmd[i] == 'S' && j is (byte)'G' or 'g')
                        {
                            continue;
                        }

                        if (char.ToLowerInvariant((char)j) == char.ToLowerInvariant((char)rawCmd[i]))
                        {
                            continue;
                        }

                        var smashed = rawCmd.ToArray();
                        smashed[i] = (byte)j;

                        var padded = Pad(smashed, out var allocatedBufferSize, out var bitmapLength);

                        Span<byte> digitsBitmap = new byte[bitmapLength];

                        RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                        RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], smashed.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                        var expectedSlots = expected.Sum(static x => x.ArgumentCount) + 1;
                        var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                        Assert.Equal(expectedSlots, usedSlots);
                        Assert.Equal(expectedConsumed, bytesConsumed);

                        Assert.True(into[usedSlots - 1].IsMalformed);

                        // PING
                        if (expected.Length > 0)
                        {
                            Assert.True(into[0].IsCommand);
                            Assert.Equal(1, into[0].ArgumentCount);
                            Assert.Equal(RespCommand.PING, into[0].Command);
                            Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));
                        }

                        // GET
                        if (expected.Length > 1)
                        {
                            Assert.True(into[1].IsCommand);
                            Assert.Equal(2, into[1].ArgumentCount);
                            Assert.Equal(RespCommand.GET, into[1].Command);
                            Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                            Assert.True(into[2].IsArgument);
                            Assert.True("a\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));
                        }

                        // SET
                        if (expected.Length > 2)
                        {
                            Assert.Fail("Should't have parsed command");
                        }
                    }
                }
            }

            // 4 commands
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[10];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[4];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", [],
                    ("GET", ["a"]),
                    ("SET", ["b", "bb"]),
                    ("HMGET", ["c", "cc", "ccc"])
                );
                for (var i = 0; i < rawCmd.Length; i++)
                {
                    if (char.IsAsciiDigit((char)rawCmd[i]))
                    {
                        continue;
                    }

                    if (rawCmd[i] is (byte)'a' or (byte)'b' or (byte)'c')
                    {
                        continue;
                    }

                    var expected = boundaries.ToArray().TakeWhile(x => x.StopByte <= i).ToArray();

                    for (var j = 0; j <= byte.MaxValue; j++)
                    {
                        if (j == rawCmd[i])
                        {
                            continue;
                        }

                        if (rawCmd[i] == 'G' && j is (byte)'S' or 's')
                        {
                            continue;
                        }

                        if (rawCmd[i] == 'S' && j is (byte)'G' or 'g')
                        {
                            continue;
                        }

                        if (char.ToLowerInvariant((char)j) == char.ToLowerInvariant((char)rawCmd[i]))
                        {
                            continue;
                        }

                        var smashed = rawCmd.ToArray();
                        smashed[i] = (byte)j;

                        var padded = Pad(smashed, out var allocatedBufferSize, out var bitmapLength);

                        Span<byte> digitsBitmap = new byte[bitmapLength];

                        RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                        RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], smashed.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                        var expectedSlots = expected.Sum(static x => x.ArgumentCount) + 1;
                        var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                        Assert.Equal(expectedSlots, usedSlots);
                        Assert.Equal(expectedConsumed, bytesConsumed);

                        Assert.True(into[usedSlots - 1].IsMalformed);

                        // PING
                        if (expected.Length > 0)
                        {
                            Assert.True(into[0].IsCommand);
                            Assert.Equal(1, into[0].ArgumentCount);
                            Assert.Equal(RespCommand.PING, into[0].Command);
                            Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));
                        }

                        // GET
                        if (expected.Length > 1)
                        {
                            Assert.True(into[1].IsCommand);
                            Assert.Equal(2, into[1].ArgumentCount);
                            Assert.Equal(RespCommand.GET, into[1].Command);
                            Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                            Assert.True(into[2].IsArgument);
                            Assert.True("a\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));
                        }

                        // SET
                        if (expected.Length > 2)
                        {
                            Assert.True(into[3].IsCommand);
                            Assert.Equal(3, into[3].ArgumentCount);
                            Assert.Equal(RespCommand.SET, into[3].Command);
                            Assert.True("SET\r\n"u8.SequenceEqual(padded[into[3].ByteStart..into[3].ByteEnd]));

                            Assert.True(into[4].IsArgument);
                            Assert.True("b\r\n"u8.SequenceEqual(padded[into[4].ByteStart..into[4].ByteEnd]));

                            Assert.True(into[5].IsArgument);
                            Assert.True("bb\r\n"u8.SequenceEqual(padded[into[5].ByteStart..into[5].ByteEnd]));
                        }

                        // HMGET
                        if (expected.Length > 3)
                        {
                            Assert.Fail("Shouldn't have parsed command");
                        }
                    }
                }
            }

            // 5 commands
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[15];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[5];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", [],
                    ("GET", ["a"]),
                    ("SET", ["b", "bb"]),
                    ("HMGET", ["c", "cc", "ccc"]),
                    ("HMSET", ["d", "dd", "ddd", "dddd"])
                );
                for (var i = 0; i < rawCmd.Length; i++)
                {
                    if (char.IsAsciiDigit((char)rawCmd[i]))
                    {
                        continue;
                    }

                    if (rawCmd[i] is (byte)'a' or (byte)'b' or (byte)'c' or (byte)'d')
                    {
                        continue;
                    }

                    var expected = boundaries.ToArray().TakeWhile(x => x.StopByte <= i).ToArray();

                    for (var j = 0; j <= byte.MaxValue; j++)
                    {
                        if (j == rawCmd[i])
                        {
                            continue;
                        }

                        if (rawCmd[i] == 'G' && j is (byte)'S' or 's')
                        {
                            continue;
                        }

                        if (rawCmd[i] == 'S' && j is (byte)'G' or 'g')
                        {
                            continue;
                        }

                        if (char.ToLowerInvariant((char)j) == char.ToLowerInvariant((char)rawCmd[i]))
                        {
                            continue;
                        }

                        var smashed = rawCmd.ToArray();
                        smashed[i] = (byte)j;

                        var padded = Pad(smashed, out var allocatedBufferSize, out var bitmapLength);

                        Span<byte> digitsBitmap = new byte[bitmapLength];

                        RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                        RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], smashed.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                        var expectedSlots = expected.Sum(static x => x.ArgumentCount) + 1;
                        var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                        Assert.Equal(expectedSlots, usedSlots);
                        Assert.Equal(expectedConsumed, bytesConsumed);

                        Assert.True(into[usedSlots - 1].IsMalformed);

                        // PING
                        if (expected.Length > 0)
                        {
                            Assert.True(into[0].IsCommand);
                            Assert.Equal(1, into[0].ArgumentCount);
                            Assert.Equal(RespCommand.PING, into[0].Command);
                            Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));
                        }

                        // GET
                        if (expected.Length > 1)
                        {
                            Assert.True(into[1].IsCommand);
                            Assert.Equal(2, into[1].ArgumentCount);
                            Assert.Equal(RespCommand.GET, into[1].Command);
                            Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                            Assert.True(into[2].IsArgument);
                            Assert.True("a\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));
                        }

                        // SET
                        if (expected.Length > 2)
                        {
                            Assert.True(into[3].IsCommand);
                            Assert.Equal(3, into[3].ArgumentCount);
                            Assert.Equal(RespCommand.SET, into[3].Command);
                            Assert.True("SET\r\n"u8.SequenceEqual(padded[into[3].ByteStart..into[3].ByteEnd]));

                            Assert.True(into[4].IsArgument);
                            Assert.True("b\r\n"u8.SequenceEqual(padded[into[4].ByteStart..into[4].ByteEnd]));

                            Assert.True(into[5].IsArgument);
                            Assert.True("bb\r\n"u8.SequenceEqual(padded[into[5].ByteStart..into[5].ByteEnd]));
                        }

                        // HMGET
                        if (expected.Length > 3)
                        {
                            Assert.True(into[6].IsCommand);
                            Assert.Equal(4, into[6].ArgumentCount);
                            Assert.Equal(RespCommand.HMGET, into[6].Command);
                            Assert.True("HMGET\r\n"u8.SequenceEqual(padded[into[6].ByteStart..into[6].ByteEnd]));

                            Assert.True(into[7].IsArgument);
                            Assert.True("c\r\n"u8.SequenceEqual(padded[into[7].ByteStart..into[7].ByteEnd]));

                            Assert.True(into[8].IsArgument);
                            Assert.True("cc\r\n"u8.SequenceEqual(padded[into[8].ByteStart..into[8].ByteEnd]));

                            Assert.True(into[9].IsArgument);
                            Assert.True("ccc\r\n"u8.SequenceEqual(padded[into[9].ByteStart..into[9].ByteEnd]));
                        }

                        // HMSET
                        if (expected.Length > 4)
                        {
                            Assert.Fail("Shouldn't have parsed command");
                        }
                    }
                }
            }

            // 6 commands
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[21];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[6];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", [],
                    ("GET", ["a"]),
                    ("SET", ["b", "bb"]),
                    ("HMGET", ["c", "cc", "ccc"]),
                    ("HMSET", ["d", "dd", "ddd", "dddd"]),
                    ("APPEND", ["e", "ee", "eee", "eeee", "eeeee"])
                );
                for (var i = 0; i < rawCmd.Length; i++)
                {
                    if (char.IsAsciiDigit((char)rawCmd[i]))
                    {
                        continue;
                    }

                    if (rawCmd[i] is (byte)'a' or (byte)'b' or (byte)'c' or (byte)'d' or (byte)'e')
                    {
                        continue;
                    }

                    var expected = boundaries.ToArray().TakeWhile(x => x.StopByte <= i).ToArray();

                    for (var j = 0; j <= byte.MaxValue; j++)
                    {
                        if (j == rawCmd[i])
                        {
                            continue;
                        }

                        if (rawCmd[i] == 'G' && j is (byte)'S' or 's')
                        {
                            continue;
                        }

                        if (rawCmd[i] == 'S' && j is (byte)'G' or 'g')
                        {
                            continue;
                        }

                        if (char.ToLowerInvariant((char)j) == char.ToLowerInvariant((char)rawCmd[i]))
                        {
                            continue;
                        }

                        var smashed = rawCmd.ToArray();
                        smashed[i] = (byte)j;

                        var padded = Pad(smashed, out var allocatedBufferSize, out var bitmapLength);

                        Span<byte> digitsBitmap = new byte[bitmapLength];

                        RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                        RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], smashed.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                        var expectedSlots = expected.Sum(static x => x.ArgumentCount) + 1;
                        var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                        Assert.Equal(expectedSlots, usedSlots);
                        Assert.Equal(expectedConsumed, bytesConsumed);

                        Assert.True(into[usedSlots - 1].IsMalformed);

                        // PING
                        if (expected.Length > 0)
                        {
                            Assert.True(into[0].IsCommand);
                            Assert.Equal(1, into[0].ArgumentCount);
                            Assert.Equal(RespCommand.PING, into[0].Command);
                            Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));
                        }

                        // GET
                        if (expected.Length > 1)
                        {
                            Assert.True(into[1].IsCommand);
                            Assert.Equal(2, into[1].ArgumentCount);
                            Assert.Equal(RespCommand.GET, into[1].Command);
                            Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                            Assert.True(into[2].IsArgument);
                            Assert.True("a\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));
                        }

                        // SET
                        if (expected.Length > 2)
                        {
                            Assert.True(into[3].IsCommand);
                            Assert.Equal(3, into[3].ArgumentCount);
                            Assert.Equal(RespCommand.SET, into[3].Command);
                            Assert.True("SET\r\n"u8.SequenceEqual(padded[into[3].ByteStart..into[3].ByteEnd]));

                            Assert.True(into[4].IsArgument);
                            Assert.True("b\r\n"u8.SequenceEqual(padded[into[4].ByteStart..into[4].ByteEnd]));

                            Assert.True(into[5].IsArgument);
                            Assert.True("bb\r\n"u8.SequenceEqual(padded[into[5].ByteStart..into[5].ByteEnd]));
                        }

                        // HMGET
                        if (expected.Length > 3)
                        {
                            Assert.True(into[6].IsCommand);
                            Assert.Equal(4, into[6].ArgumentCount);
                            Assert.Equal(RespCommand.HMGET, into[6].Command);
                            Assert.True("HMGET\r\n"u8.SequenceEqual(padded[into[6].ByteStart..into[6].ByteEnd]));

                            Assert.True(into[7].IsArgument);
                            Assert.True("c\r\n"u8.SequenceEqual(padded[into[7].ByteStart..into[7].ByteEnd]));

                            Assert.True(into[8].IsArgument);
                            Assert.True("cc\r\n"u8.SequenceEqual(padded[into[8].ByteStart..into[8].ByteEnd]));

                            Assert.True(into[9].IsArgument);
                            Assert.True("ccc\r\n"u8.SequenceEqual(padded[into[9].ByteStart..into[9].ByteEnd]));
                        }

                        // HMSET
                        if (expected.Length > 4)
                        {
                            Assert.True(into[10].IsCommand);
                            Assert.Equal(5, into[10].ArgumentCount);
                            Assert.Equal(RespCommand.HMSET, into[10].Command);
                            Assert.True("HMSET\r\n"u8.SequenceEqual(padded[into[10].ByteStart..into[10].ByteEnd]));

                            Assert.True(into[11].IsArgument);
                            Assert.True("d\r\n"u8.SequenceEqual(padded[into[11].ByteStart..into[11].ByteEnd]));

                            Assert.True(into[12].IsArgument);
                            Assert.True("dd\r\n"u8.SequenceEqual(padded[into[12].ByteStart..into[12].ByteEnd]));

                            Assert.True(into[13].IsArgument);
                            Assert.True("ddd\r\n"u8.SequenceEqual(padded[into[13].ByteStart..into[13].ByteEnd]));

                            Assert.True(into[14].IsArgument);
                            Assert.True("dddd\r\n"u8.SequenceEqual(padded[into[14].ByteStart..into[14].ByteEnd]));
                        }

                        // APPEND
                        if (expected.Length > 5)
                        {
                            Assert.Fail("Command shouldn't have been parsed");
                        }
                    }
                }
            }

            // 7 commands
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[28];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[7];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", [],
                    ("GET", ["a"]),
                    ("SET", ["b", "bb"]),
                    ("HMGET", ["c", "cc", "ccc"]),
                    ("HMSET", ["d", "dd", "ddd", "dddd"]),
                    ("APPEND", ["e", "ee", "eee", "eeee", "eeeee"]),
                    ("HEXPIRE", ["f", "ff", "fff", "ffff", "fffff", "ffffff"])
                );
                for (var i = 0; i < rawCmd.Length; i++)
                {
                    if (char.IsAsciiDigit((char)rawCmd[i]))
                    {
                        continue;
                    }

                    if (rawCmd[i] is (byte)'a' or (byte)'b' or (byte)'c' or (byte)'d' or (byte)'e' or (byte)'f')
                    {
                        continue;
                    }

                    var expected = boundaries.ToArray().TakeWhile(x => x.StopByte <= i).ToArray();

                    for (var j = 0; j <= byte.MaxValue; j++)
                    {
                        if (j == rawCmd[i])
                        {
                            continue;
                        }

                        if (rawCmd[i] == 'G' && j is (byte)'S' or 's')
                        {
                            continue;
                        }

                        if (rawCmd[i] == 'S' && j is (byte)'G' or 'g')
                        {
                            continue;
                        }

                        if (rawCmd[i] == 'H' && j is 'P' or 'p' or 'Z' or 'z')
                        {
                            continue;
                        }

                        if (char.ToLowerInvariant((char)j) == char.ToLowerInvariant((char)rawCmd[i]))
                        {
                            continue;
                        }

                        var smashed = rawCmd.ToArray();
                        smashed[i] = (byte)j;

                        var padded = Pad(smashed, out var allocatedBufferSize, out var bitmapLength);

                        Span<byte> digitsBitmap = new byte[bitmapLength];

                        RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                        RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], smashed.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                        var expectedSlots = expected.Sum(static x => x.ArgumentCount) + 1;
                        var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                        Assert.Equal(expectedSlots, usedSlots);
                        Assert.Equal(expectedConsumed, bytesConsumed);

                        Assert.True(into[usedSlots - 1].IsMalformed);

                        // PING
                        if (expected.Length > 0)
                        {
                            Assert.True(into[0].IsCommand);
                            Assert.Equal(1, into[0].ArgumentCount);
                            Assert.Equal(RespCommand.PING, into[0].Command);
                            Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));
                        }

                        // GET
                        if (expected.Length > 1)
                        {
                            Assert.True(into[1].IsCommand);
                            Assert.Equal(2, into[1].ArgumentCount);
                            Assert.Equal(RespCommand.GET, into[1].Command);
                            Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                            Assert.True(into[2].IsArgument);
                            Assert.True("a\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));
                        }

                        // SET
                        if (expected.Length > 2)
                        {
                            Assert.True(into[3].IsCommand);
                            Assert.Equal(3, into[3].ArgumentCount);
                            Assert.Equal(RespCommand.SET, into[3].Command);
                            Assert.True("SET\r\n"u8.SequenceEqual(padded[into[3].ByteStart..into[3].ByteEnd]));

                            Assert.True(into[4].IsArgument);
                            Assert.True("b\r\n"u8.SequenceEqual(padded[into[4].ByteStart..into[4].ByteEnd]));

                            Assert.True(into[5].IsArgument);
                            Assert.True("bb\r\n"u8.SequenceEqual(padded[into[5].ByteStart..into[5].ByteEnd]));
                        }

                        // HMGET
                        if (expected.Length > 3)
                        {
                            Assert.True(into[6].IsCommand);
                            Assert.Equal(4, into[6].ArgumentCount);
                            Assert.Equal(RespCommand.HMGET, into[6].Command);
                            Assert.True("HMGET\r\n"u8.SequenceEqual(padded[into[6].ByteStart..into[6].ByteEnd]));

                            Assert.True(into[7].IsArgument);
                            Assert.True("c\r\n"u8.SequenceEqual(padded[into[7].ByteStart..into[7].ByteEnd]));

                            Assert.True(into[8].IsArgument);
                            Assert.True("cc\r\n"u8.SequenceEqual(padded[into[8].ByteStart..into[8].ByteEnd]));

                            Assert.True(into[9].IsArgument);
                            Assert.True("ccc\r\n"u8.SequenceEqual(padded[into[9].ByteStart..into[9].ByteEnd]));
                        }

                        // HMSET
                        if (expected.Length > 4)
                        {
                            Assert.True(into[10].IsCommand);
                            Assert.Equal(5, into[10].ArgumentCount);
                            Assert.Equal(RespCommand.HMSET, into[10].Command);
                            Assert.True("HMSET\r\n"u8.SequenceEqual(padded[into[10].ByteStart..into[10].ByteEnd]));

                            Assert.True(into[11].IsArgument);
                            Assert.True("d\r\n"u8.SequenceEqual(padded[into[11].ByteStart..into[11].ByteEnd]));

                            Assert.True(into[12].IsArgument);
                            Assert.True("dd\r\n"u8.SequenceEqual(padded[into[12].ByteStart..into[12].ByteEnd]));

                            Assert.True(into[13].IsArgument);
                            Assert.True("ddd\r\n"u8.SequenceEqual(padded[into[13].ByteStart..into[13].ByteEnd]));

                            Assert.True(into[14].IsArgument);
                            Assert.True("dddd\r\n"u8.SequenceEqual(padded[into[14].ByteStart..into[14].ByteEnd]));
                        }

                        // APPEND
                        if (expected.Length > 5)
                        {
                            Assert.True(into[15].IsCommand);
                            Assert.Equal(6, into[15].ArgumentCount);
                            Assert.Equal(RespCommand.APPEND, into[15].Command);
                            Assert.True("APPEND\r\n"u8.SequenceEqual(padded[into[15].ByteStart..into[15].ByteEnd]));

                            Assert.True(into[16].IsArgument);
                            Assert.True("e\r\n"u8.SequenceEqual(padded[into[16].ByteStart..into[16].ByteEnd]));

                            Assert.True(into[17].IsArgument);
                            Assert.True("ee\r\n"u8.SequenceEqual(padded[into[17].ByteStart..into[17].ByteEnd]));

                            Assert.True(into[18].IsArgument);
                            Assert.True("eee\r\n"u8.SequenceEqual(padded[into[18].ByteStart..into[18].ByteEnd]));

                            Assert.True(into[19].IsArgument);
                            Assert.True("eeee\r\n"u8.SequenceEqual(padded[into[19].ByteStart..into[19].ByteEnd]));

                            Assert.True(into[20].IsArgument);
                            Assert.True("eeeee\r\n"u8.SequenceEqual(padded[into[20].ByteStart..into[20].ByteEnd]));
                        }

                        // HEXPIRE
                        if (expected.Length > 6)
                        {
                            Assert.Fail("Shouldn't have parsed command");
                        }
                    }
                }
            }

            // 8 commands
            {
                Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[36];
                Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries = stackalloc (int StartByte, int StopByte, int ArgumentCount)[8];

                var rawCmd = MakeBufferWithBoundaries(
                    ref boundaries,
                    "PING", [],
                    ("GET", ["a"]),
                    ("SET", ["b", "bb"]),
                    ("HMGET", ["c", "cc", "ccc"]),
                    ("HMSET", ["d", "dd", "ddd", "dddd"]),
                    ("APPEND", ["e", "ee", "eee", "eeee", "eeeee"]),
                    ("HEXPIRE", ["f", "ff", "fff", "ffff", "fffff", "ffffff"]),
                    ("HPEXPIRE", ["g", "gg", "ggg", "gggg", "ggggg", "gggggg", "ggggggg"])
                );
                for (var i = 0; i < rawCmd.Length; i++)
                {
                    if (char.IsAsciiDigit((char)rawCmd[i]))
                    {
                        continue;
                    }

                    if (rawCmd[i] is (byte)'a' or (byte)'b' or (byte)'c' or (byte)'d' or (byte)'e' or (byte)'f' or (byte)'g')
                    {
                        continue;
                    }

                    var expected = boundaries.ToArray().TakeWhile(x => x.StopByte <= i).ToArray();

                    for (var j = 0; j <= byte.MaxValue; j++)
                    {
                        if (j == rawCmd[i])
                        {
                            continue;
                        }

                        if (rawCmd[i] == 'G' && j is (byte)'S' or 's')
                        {
                            continue;
                        }

                        if (rawCmd[i] == 'S' && j is (byte)'G' or 'g')
                        {
                            continue;
                        }

                        if (rawCmd[i] == 'H' && j is 'P' or 'p' or 'Z' or 'z')
                        {
                            continue;
                        }

                        if (char.ToLowerInvariant((char)j) == char.ToLowerInvariant((char)rawCmd[i]))
                        {
                            continue;
                        }

                        var smashed = rawCmd.ToArray();
                        smashed[i] = (byte)j;

                        var padded = Pad(smashed, out var allocatedBufferSize, out var bitmapLength);

                        Span<byte> digitsBitmap = new byte[bitmapLength];

                        RespParserV3.ScanForSigils(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), rawCmd.Length);

                        RespParserV3.TakeMultipleCommands(allocatedBufferSize, ref padded[0], smashed.Length, bitmapLength, ref digitsBitmap[0], into.Length, ref into[0], out var usedSlots, out var bytesConsumed);

                        var expectedSlots = expected.Sum(static x => x.ArgumentCount) + 1;
                        var expectedConsumed = expected.Length == 0 ? 0 : expected.Max(static x => x.StopByte);

                        Assert.Equal(expectedSlots, usedSlots);
                        Assert.Equal(expectedConsumed, bytesConsumed);

                        Assert.True(into[usedSlots - 1].IsMalformed);

                        // PING
                        if (expected.Length > 0)
                        {
                            Assert.True(into[0].IsCommand);
                            Assert.Equal(1, into[0].ArgumentCount);
                            Assert.Equal(RespCommand.PING, into[0].Command);
                            Assert.True("PING\r\n"u8.SequenceEqual(padded[into[0].ByteStart..into[0].ByteEnd]));
                        }

                        // GET
                        if (expected.Length > 1)
                        {
                            Assert.True(into[1].IsCommand);
                            Assert.Equal(2, into[1].ArgumentCount);
                            Assert.Equal(RespCommand.GET, into[1].Command);
                            Assert.True("GET\r\n"u8.SequenceEqual(padded[into[1].ByteStart..into[1].ByteEnd]));

                            Assert.True(into[2].IsArgument);
                            Assert.True("a\r\n"u8.SequenceEqual(padded[into[2].ByteStart..into[2].ByteEnd]));
                        }

                        // SET
                        if (expected.Length > 2)
                        {
                            Assert.True(into[3].IsCommand);
                            Assert.Equal(3, into[3].ArgumentCount);
                            Assert.Equal(RespCommand.SET, into[3].Command);
                            Assert.True("SET\r\n"u8.SequenceEqual(padded[into[3].ByteStart..into[3].ByteEnd]));

                            Assert.True(into[4].IsArgument);
                            Assert.True("b\r\n"u8.SequenceEqual(padded[into[4].ByteStart..into[4].ByteEnd]));

                            Assert.True(into[5].IsArgument);
                            Assert.True("bb\r\n"u8.SequenceEqual(padded[into[5].ByteStart..into[5].ByteEnd]));
                        }

                        // HMGET
                        if (expected.Length > 3)
                        {
                            Assert.True(into[6].IsCommand);
                            Assert.Equal(4, into[6].ArgumentCount);
                            Assert.Equal(RespCommand.HMGET, into[6].Command);
                            Assert.True("HMGET\r\n"u8.SequenceEqual(padded[into[6].ByteStart..into[6].ByteEnd]));

                            Assert.True(into[7].IsArgument);
                            Assert.True("c\r\n"u8.SequenceEqual(padded[into[7].ByteStart..into[7].ByteEnd]));

                            Assert.True(into[8].IsArgument);
                            Assert.True("cc\r\n"u8.SequenceEqual(padded[into[8].ByteStart..into[8].ByteEnd]));

                            Assert.True(into[9].IsArgument);
                            Assert.True("ccc\r\n"u8.SequenceEqual(padded[into[9].ByteStart..into[9].ByteEnd]));
                        }

                        // HMSET
                        if (expected.Length > 4)
                        {
                            Assert.True(into[10].IsCommand);
                            Assert.Equal(5, into[10].ArgumentCount);
                            Assert.Equal(RespCommand.HMSET, into[10].Command);
                            Assert.True("HMSET\r\n"u8.SequenceEqual(padded[into[10].ByteStart..into[10].ByteEnd]));

                            Assert.True(into[11].IsArgument);
                            Assert.True("d\r\n"u8.SequenceEqual(padded[into[11].ByteStart..into[11].ByteEnd]));

                            Assert.True(into[12].IsArgument);
                            Assert.True("dd\r\n"u8.SequenceEqual(padded[into[12].ByteStart..into[12].ByteEnd]));

                            Assert.True(into[13].IsArgument);
                            Assert.True("ddd\r\n"u8.SequenceEqual(padded[into[13].ByteStart..into[13].ByteEnd]));

                            Assert.True(into[14].IsArgument);
                            Assert.True("dddd\r\n"u8.SequenceEqual(padded[into[14].ByteStart..into[14].ByteEnd]));
                        }

                        // APPEND
                        if (expected.Length > 5)
                        {
                            Assert.True(into[15].IsCommand);
                            Assert.Equal(6, into[15].ArgumentCount);
                            Assert.Equal(RespCommand.APPEND, into[15].Command);
                            Assert.True("APPEND\r\n"u8.SequenceEqual(padded[into[15].ByteStart..into[15].ByteEnd]));

                            Assert.True(into[16].IsArgument);
                            Assert.True("e\r\n"u8.SequenceEqual(padded[into[16].ByteStart..into[16].ByteEnd]));

                            Assert.True(into[17].IsArgument);
                            Assert.True("ee\r\n"u8.SequenceEqual(padded[into[17].ByteStart..into[17].ByteEnd]));

                            Assert.True(into[18].IsArgument);
                            Assert.True("eee\r\n"u8.SequenceEqual(padded[into[18].ByteStart..into[18].ByteEnd]));

                            Assert.True(into[19].IsArgument);
                            Assert.True("eeee\r\n"u8.SequenceEqual(padded[into[19].ByteStart..into[19].ByteEnd]));

                            Assert.True(into[20].IsArgument);
                            Assert.True("eeeee\r\n"u8.SequenceEqual(padded[into[20].ByteStart..into[20].ByteEnd]));
                        }

                        // HEXPIRE
                        if (expected.Length > 6)
                        {
                            Assert.True(into[21].IsCommand);
                            Assert.Equal(7, into[21].ArgumentCount);
                            Assert.Equal(RespCommand.HEXPIRE, into[21].Command);
                            Assert.True("HEXPIRE\r\n"u8.SequenceEqual(padded[into[21].ByteStart..into[21].ByteEnd]));

                            Assert.True(into[22].IsArgument);
                            Assert.True("f\r\n"u8.SequenceEqual(padded[into[22].ByteStart..into[22].ByteEnd]));

                            Assert.True(into[23].IsArgument);
                            Assert.True("ff\r\n"u8.SequenceEqual(padded[into[23].ByteStart..into[23].ByteEnd]));

                            Assert.True(into[24].IsArgument);
                            Assert.True("fff\r\n"u8.SequenceEqual(padded[into[24].ByteStart..into[24].ByteEnd]));

                            Assert.True(into[25].IsArgument);
                            Assert.True("ffff\r\n"u8.SequenceEqual(padded[into[25].ByteStart..into[25].ByteEnd]));

                            Assert.True(into[26].IsArgument);
                            Assert.True("fffff\r\n"u8.SequenceEqual(padded[into[26].ByteStart..into[26].ByteEnd]));

                            Assert.True(into[27].IsArgument);
                            Assert.True("ffffff\r\n"u8.SequenceEqual(padded[into[27].ByteStart..into[27].ByteEnd]));
                        }

                        // HPEXPIRE
                        if (expected.Length > 7)
                        {
                            Assert.Fail("Shouldn't have parsed command");
                        }
                    }
                }
            }
        }

        private static ReadOnlySpan<byte> MakeBuffer(
            string firstCommand,
            scoped ReadOnlySpan<string> firstCommandArgs,
            params scoped ReadOnlySpan<(string Command, string[] Args)> nextCommands
        )
        {
            var buffer = new List<byte>();
            Encode(buffer, firstCommand, firstCommandArgs);

            foreach (var command in nextCommands)
            {
                Encode(buffer, command.Command, command.Args);
            }

            return buffer.ToArray();

            static void Encode(List<byte> buffer, string command, scoped ReadOnlySpan<string> args)
            {
                var commandEncoded = Encoding.UTF8.GetBytes(command);

                buffer.AddRange(Encoding.UTF8.GetBytes($"*{args.Length + 1}\r\n${commandEncoded.Length}\r\n"));
                buffer.AddRange(commandEncoded);
                buffer.AddRange("\r\n"u8);

                foreach (var arg in args)
                {
                    var argEncoded = Encoding.UTF8.GetBytes(arg);

                    buffer.AddRange(Encoding.UTF8.GetBytes($"${argEncoded.Length}\r\n"));
                    buffer.AddRange(argEncoded);
                    buffer.AddRange("\r\n"u8);
                }
            }
        }

        private static ReadOnlySpan<byte> MakeBufferWithBoundaries(
            scoped ref Span<(int StartByte, int StopByte, int ArgumentCount)> boundaries,
            string firstCommand,
            scoped ReadOnlySpan<string> firstCommandArgs,
            params scoped ReadOnlySpan<(string Command, string[] Args)> nextCommands
        )
        {
            var ix = 0;

            var lastStart = 0;

            var buffer = new List<byte>();
            Encode(buffer, firstCommand, firstCommandArgs);

            boundaries[ix] = (lastStart, buffer.Count, firstCommandArgs.Length + 1);
            lastStart = buffer.Count;
            ix++;

            foreach (var command in nextCommands)
            {
                Encode(buffer, command.Command, command.Args);

                boundaries[ix] = (lastStart, buffer.Count, command.Args.Length + 1);
                lastStart = buffer.Count;
                ix++;
            }

            boundaries = boundaries[..ix];

            return buffer.ToArray();

            static void Encode(List<byte> buffer, string command, scoped ReadOnlySpan<string> args)
            {
                var commandEncoded = Encoding.UTF8.GetBytes(command);

                buffer.AddRange(Encoding.UTF8.GetBytes($"*{args.Length + 1}\r\n${commandEncoded.Length}\r\n"));
                buffer.AddRange(commandEncoded);
                buffer.AddRange("\r\n"u8);

                foreach (var arg in args)
                {
                    var argEncoded = Encoding.UTF8.GetBytes(arg);

                    buffer.AddRange(Encoding.UTF8.GetBytes($"${argEncoded.Length}\r\n"));
                    buffer.AddRange(argEncoded);
                    buffer.AddRange("\r\n"u8);
                }
            }
        }

        private static Span<byte> Pad(scoped ReadOnlySpan<byte> rawCommandBuffer, out int allocatedBufferSize, out int allocatedBitmapSize)
        {
            Assert.False(rawCommandBuffer.IsEmpty);

            RespParserV3.CalculateByteBufferSizes(rawCommandBuffer.Length, out allocatedBufferSize, out _, out allocatedBitmapSize);

            var ret = GC.AllocateArray<byte>(allocatedBufferSize, pinned: true);
            rawCommandBuffer.CopyTo(ret);

            return ret;
        }
    }
}
