using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OptimizationExercise.SIMDRespParsing.Tests
{
    public class ParseSingleTests
    {
        [Fact]
        public void SingleArg()
        {
            var basicCommandBuffer = "*1\r\n$12\r\nabcdefghijkl\r\n"u8;

            for (var padding = 0; padding < 8; padding++)
            {
                var endPaddingByte = (padding / 8) * 8;
                var endPaddingBit = (byte)(padding % 8);

                var commandBuffer = Enumerable.Repeat((byte)0, padding).Concat(basicCommandBuffer.ToArray()).ToArray().AsSpan();

                var asteriks = new byte[(commandBuffer.Length / 8) + 1];
                var dollars = new byte[asteriks.Length];
                var crs = new byte[asteriks.Length];
                var lfs = new byte[asteriks.Length];
                var crLfs = new byte[asteriks.Length];

                RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);
                RespParser.CombineCrLf_Scalar(crs, lfs, crLfs);

                var asteriksSpan = (ReadOnlySpan<byte>)asteriks;
                var dollarsSpan = (ReadOnlySpan<byte>)dollars;
                var crLfsSpan = (ReadOnlySpan<byte>)crLfs;

                var into = new ParsedRespCommandOrArgument[2];

                var res = TryParseSingleCommandImpls(commandBuffer, endPaddingByte, endPaddingBit, into, ref asteriksSpan, ref dollarsSpan, ref crLfsSpan, out var slotsUsed);

                Assert.True(res);
                Assert.Equal(2, slotsUsed);

                // *1\r\n
                Assert.False(into[0].IsMalformed);
                Assert.True(into[0].IsCommand);
                Assert.False(into[0].IsArgument);
                Assert.Equal(1, into[0].ArgumentCount);
                Assert.True("*1\r\n"u8.SequenceEqual(commandBuffer[into[0].ByteStart..into[0].ByteEnd]));

                // $12\r\nabcdefghijkl\r\n
                Assert.False(into[1].IsMalformed);
                Assert.False(into[1].IsCommand);
                Assert.True(into[1].IsArgument);
                Assert.True("abcdefghijkl\r\n"u8.SequenceEqual(commandBuffer[into[1].ByteStart..into[1].ByteEnd]));

                // command buffer completely consumed
                Assert.Equal(padding, into[0].ByteStart);
                Assert.Equal(commandBuffer.Length, into[1].ByteEnd);
            }
        }

        [Fact]
        public void MultipleArgs()
        {
            var basicCommandBuffer = "*3\r\n$3\r\nSET\r\n$4\r\nfizz\r\n$5\r\nworld\r\n"u8;

            for (var padding = 0; padding < 8; padding++)
            {
                var endPaddingByte = (padding / 8) * 8;
                var endPaddingBit = (byte)(padding % 8);

                var commandBuffer = Enumerable.Repeat((byte)0, padding).Concat(basicCommandBuffer.ToArray()).ToArray().AsSpan();

                var asteriks = new byte[(commandBuffer.Length / 8) + 1];
                var dollars = new byte[asteriks.Length];
                var crs = new byte[asteriks.Length];
                var lfs = new byte[asteriks.Length];
                var crLfs = new byte[asteriks.Length];

                RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);
                RespParser.CombineCrLf_Scalar(crs, lfs, crLfs);

                var asteriksSpan = (ReadOnlySpan<byte>)asteriks;
                var dollarsSpan = (ReadOnlySpan<byte>)dollars;
                var crLfsSpan = (ReadOnlySpan<byte>)crLfs;

                var into = new ParsedRespCommandOrArgument[4];

                var res = TryParseSingleCommandImpls(commandBuffer, endPaddingByte, endPaddingBit, into, ref asteriksSpan, ref dollarsSpan, ref crLfsSpan, out var slotsUsed);

                Assert.True(res);
                Assert.Equal(4, slotsUsed);

                // *3\r\n
                Assert.False(into[0].IsMalformed);
                Assert.True(into[0].IsCommand);
                Assert.False(into[0].IsArgument);
                Assert.Equal(3, into[0].ArgumentCount);
                Assert.True("*3\r\n"u8.SequenceEqual(commandBuffer[into[0].ByteStart..into[0].ByteEnd]));

                // $3\r\SET\r\n
                Assert.False(into[1].IsMalformed);
                Assert.False(into[1].IsCommand);
                Assert.True(into[1].IsArgument);
                Assert.True("SET\r\n"u8.SequenceEqual(commandBuffer[into[1].ByteStart..into[1].ByteEnd]));

                // $4\r\nfizz\r\n
                Assert.False(into[2].IsMalformed);
                Assert.False(into[2].IsCommand);
                Assert.True(into[2].IsArgument);
                Assert.True("fizz\r\n"u8.SequenceEqual(commandBuffer[into[2].ByteStart..into[2].ByteEnd]));

                // $5\r\nworld\r\n
                Assert.False(into[3].IsMalformed);
                Assert.False(into[3].IsCommand);
                Assert.True(into[3].IsArgument);
                Assert.True("world\r\n"u8.SequenceEqual(commandBuffer[into[3].ByteStart..into[3].ByteEnd]));

                // command buffer completely consumed
                Assert.Equal(padding, into[0].ByteStart);
                Assert.Equal(commandBuffer.Length, into[3].ByteEnd);
            }
        }

        [Fact]
        public void Malformed()
        {
            for (var padding = 0; padding < 8; padding++)
            {
                var endPaddingByte = (padding / 8) * 8;
                var endPaddingBit = (byte)(padding % 8);

                // no leading *
                {
                    var commandBuffer = Enumerable.Repeat((byte)0, padding).Concat("$1\r\n$1\r\na\r\n"u8.ToArray()).ToArray().AsSpan();

                    var asteriks = new byte[(commandBuffer.Length / 8) + 1];
                    var dollars = new byte[asteriks.Length];
                    var crs = new byte[asteriks.Length];
                    var lfs = new byte[asteriks.Length];
                    var crLfs = new byte[asteriks.Length];

                    RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);
                    RespParser.CombineCrLf_Scalar(crs, lfs, crLfs);

                    var asteriksSpan = (ReadOnlySpan<byte>)asteriks;
                    var dollarsSpan = (ReadOnlySpan<byte>)dollars;
                    var crLfsSpan = (ReadOnlySpan<byte>)crLfs;

                    var into = new ParsedRespCommandOrArgument[4];

                    var res = TryParseSingleCommandImpls(commandBuffer, endPaddingByte, endPaddingBit, into, ref asteriksSpan, ref dollarsSpan, ref crLfsSpan, out var slotsUsed);

                    Assert.False(res);

                    Assert.Equal(1, slotsUsed);
                    Assert.True(into[0].IsMalformed);
                }

                // no digit after *
                {
                    var commandBuffer = Enumerable.Repeat((byte)0, padding).Concat("*X\r\n$1\r\na\r\n"u8.ToArray()).ToArray().AsSpan();

                    var asteriks = new byte[(commandBuffer.Length / 8) + 1];
                    var dollars = new byte[asteriks.Length];
                    var crs = new byte[asteriks.Length];
                    var lfs = new byte[asteriks.Length];
                    var crLfs = new byte[asteriks.Length];

                    RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);
                    RespParser.CombineCrLf_Scalar(crs, lfs, crLfs);

                    var asteriksSpan = (ReadOnlySpan<byte>)asteriks;
                    var dollarsSpan = (ReadOnlySpan<byte>)dollars;
                    var crLfsSpan = (ReadOnlySpan<byte>)crLfs;

                    var into = new ParsedRespCommandOrArgument[4];

                    var res = TryParseSingleCommandImpls(commandBuffer, endPaddingByte, endPaddingBit, into, ref asteriksSpan, ref dollarsSpan, ref crLfsSpan, out var slotsUsed);

                    Assert.False(res);

                    Assert.Equal(1, slotsUsed);
                    Assert.True(into[0].IsMalformed);
                }

                // invalid array length
                {
                    var commandBuffer = Enumerable.Repeat((byte)0, padding).Concat("*0\r\n$1\r\na\r\n"u8.ToArray()).ToArray().AsSpan();

                    var asteriks = new byte[(commandBuffer.Length / 8) + 1];
                    var dollars = new byte[asteriks.Length];
                    var crs = new byte[asteriks.Length];
                    var lfs = new byte[asteriks.Length];
                    var crLfs = new byte[asteriks.Length];

                    RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);
                    RespParser.CombineCrLf_Scalar(crs, lfs, crLfs);

                    var asteriksSpan = (ReadOnlySpan<byte>)asteriks;
                    var dollarsSpan = (ReadOnlySpan<byte>)dollars;
                    var crLfsSpan = (ReadOnlySpan<byte>)crLfs;

                    var into = new ParsedRespCommandOrArgument[4];

                    var res = TryParseSingleCommandImpls(commandBuffer, endPaddingByte, endPaddingBit, into, ref asteriksSpan, ref dollarsSpan, ref crLfsSpan, out var slotsUsed);

                    Assert.False(res);

                    Assert.Equal(1, slotsUsed);
                    Assert.True(into[0].IsMalformed);
                }

                // missing \r after array
                {
                    var commandBuffer = Enumerable.Repeat((byte)0, padding).Concat("*1\n\n$1\r\na\r\n"u8.ToArray()).ToArray().AsSpan();

                    var asteriks = new byte[(commandBuffer.Length / 8) + 1];
                    var dollars = new byte[asteriks.Length];
                    var crs = new byte[asteriks.Length];
                    var lfs = new byte[asteriks.Length];
                    var crLfs = new byte[asteriks.Length];

                    RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);
                    RespParser.CombineCrLf_Scalar(crs, lfs, crLfs);

                    var asteriksSpan = (ReadOnlySpan<byte>)asteriks;
                    var dollarsSpan = (ReadOnlySpan<byte>)dollars;
                    var crLfsSpan = (ReadOnlySpan<byte>)crLfs;

                    var into = new ParsedRespCommandOrArgument[4];

                    var res = TryParseSingleCommandImpls(commandBuffer, endPaddingByte, endPaddingBit, into, ref asteriksSpan, ref dollarsSpan, ref crLfsSpan, out var slotsUsed);

                    Assert.False(res);

                    Assert.Equal(1, slotsUsed);
                    Assert.True(into[0].IsMalformed);
                }

                // missing \n after array
                {
                    var commandBuffer = Enumerable.Repeat((byte)0, padding).Concat("*1\r\r$1\r\na\r\n"u8.ToArray()).ToArray().AsSpan();

                    var asteriks = new byte[(commandBuffer.Length / 8) + 1];
                    var dollars = new byte[asteriks.Length];
                    var crs = new byte[asteriks.Length];
                    var lfs = new byte[asteriks.Length];
                    var crLfs = new byte[asteriks.Length];

                    RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);
                    RespParser.CombineCrLf_Scalar(crs, lfs, crLfs);

                    var asteriksSpan = (ReadOnlySpan<byte>)asteriks;
                    var dollarsSpan = (ReadOnlySpan<byte>)dollars;
                    var crLfsSpan = (ReadOnlySpan<byte>)crLfs;

                    var into = new ParsedRespCommandOrArgument[4];

                    var res = TryParseSingleCommandImpls(commandBuffer, endPaddingByte, endPaddingBit, into, ref asteriksSpan, ref dollarsSpan, ref crLfsSpan, out var slotsUsed);

                    Assert.False(res);

                    Assert.Equal(1, slotsUsed);
                    Assert.True(into[0].IsMalformed);
                }

                // valid array, missing $
                {
                    var commandBuffer = Enumerable.Repeat((byte)0, padding).Concat("*1\r\n!1\r\na\r\n"u8.ToArray()).ToArray().AsSpan();

                    var asteriks = new byte[(commandBuffer.Length / 8) + 1];
                    var dollars = new byte[asteriks.Length];
                    var crs = new byte[asteriks.Length];
                    var lfs = new byte[asteriks.Length];
                    var crLfs = new byte[asteriks.Length];

                    RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);
                    RespParser.CombineCrLf_Scalar(crs, lfs, crLfs);

                    var asteriksSpan = (ReadOnlySpan<byte>)asteriks;
                    var dollarsSpan = (ReadOnlySpan<byte>)dollars;
                    var crLfsSpan = (ReadOnlySpan<byte>)crLfs;

                    var into = new ParsedRespCommandOrArgument[4];

                    var res = TryParseSingleCommandImpls(commandBuffer, endPaddingByte, endPaddingBit, into, ref asteriksSpan, ref dollarsSpan, ref crLfsSpan, out var slotsUsed);

                    Assert.False(res);

                    Assert.Equal(2, slotsUsed);

                    Assert.True(into[0].IsCommand);
                    Assert.True(into[1].IsMalformed);
                }

                // valid array, string length not digit
                {
                    var commandBuffer = Enumerable.Repeat((byte)0, padding).Concat("*1\r\n$X\r\na\r\n"u8.ToArray()).ToArray().AsSpan();

                    var asteriks = new byte[(commandBuffer.Length / 8) + 1];
                    var dollars = new byte[asteriks.Length];
                    var crs = new byte[asteriks.Length];
                    var lfs = new byte[asteriks.Length];
                    var crLfs = new byte[asteriks.Length];

                    RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);
                    RespParser.CombineCrLf_Scalar(crs, lfs, crLfs);

                    var asteriksSpan = (ReadOnlySpan<byte>)asteriks;
                    var dollarsSpan = (ReadOnlySpan<byte>)dollars;
                    var crLfsSpan = (ReadOnlySpan<byte>)crLfs;

                    var into = new ParsedRespCommandOrArgument[4];

                    var res = TryParseSingleCommandImpls(commandBuffer, endPaddingByte, endPaddingBit, into, ref asteriksSpan, ref dollarsSpan, ref crLfsSpan, out var slotsUsed);

                    Assert.False(res);

                    Assert.Equal(2, slotsUsed);

                    Assert.True(into[0].IsCommand);
                    Assert.True(into[1].IsMalformed);
                }

                // valid array, string length too short
                {
                    var commandBuffer = Enumerable.Repeat((byte)0, padding).Concat("*1\r\n$0\r\na\r\n"u8.ToArray()).ToArray().AsSpan();

                    var asteriks = new byte[(commandBuffer.Length / 8) + 1];
                    var dollars = new byte[asteriks.Length];
                    var crs = new byte[asteriks.Length];
                    var lfs = new byte[asteriks.Length];
                    var crLfs = new byte[asteriks.Length];

                    RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);
                    RespParser.CombineCrLf_Scalar(crs, lfs, crLfs);

                    var asteriksSpan = (ReadOnlySpan<byte>)asteriks;
                    var dollarsSpan = (ReadOnlySpan<byte>)dollars;
                    var crLfsSpan = (ReadOnlySpan<byte>)crLfs;

                    var into = new ParsedRespCommandOrArgument[4];

                    var res = TryParseSingleCommandImpls(commandBuffer, endPaddingByte, endPaddingBit, into, ref asteriksSpan, ref dollarsSpan, ref crLfsSpan, out var slotsUsed);

                    Assert.False(res);

                    Assert.Equal(2, slotsUsed);

                    Assert.True(into[0].IsCommand);
                    Assert.True(into[1].IsMalformed);
                }

                // valid array, string missing \r
                {
                    var commandBuffer = Enumerable.Repeat((byte)0, padding).Concat("*1\r\n$1\n\na\r\n"u8.ToArray()).ToArray().AsSpan();

                    var asteriks = new byte[(commandBuffer.Length / 8) + 1];
                    var dollars = new byte[asteriks.Length];
                    var crs = new byte[asteriks.Length];
                    var lfs = new byte[asteriks.Length];
                    var crLfs = new byte[asteriks.Length];

                    RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);
                    RespParser.CombineCrLf_Scalar(crs, lfs, crLfs);

                    var asteriksSpan = (ReadOnlySpan<byte>)asteriks;
                    var dollarsSpan = (ReadOnlySpan<byte>)dollars;
                    var crLfsSpan = (ReadOnlySpan<byte>)crLfs;

                    var into = new ParsedRespCommandOrArgument[4];

                    var res = TryParseSingleCommandImpls(commandBuffer, endPaddingByte, endPaddingBit, into, ref asteriksSpan, ref dollarsSpan, ref crLfsSpan, out var slotsUsed);

                    Assert.False(res);

                    Assert.Equal(2, slotsUsed);

                    Assert.True(into[0].IsCommand);
                    Assert.True(into[1].IsMalformed);
                }

                // valid array, string missing \n
                {
                    var commandBuffer = Enumerable.Repeat((byte)0, padding).Concat("*1\r\n$1\r\ra\r\n"u8.ToArray()).ToArray().AsSpan();

                    var asteriks = new byte[(commandBuffer.Length / 8) + 1];
                    var dollars = new byte[asteriks.Length];
                    var crs = new byte[asteriks.Length];
                    var lfs = new byte[asteriks.Length];
                    var crLfs = new byte[asteriks.Length];

                    RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);
                    RespParser.CombineCrLf_Scalar(crs, lfs, crLfs);

                    var asteriksSpan = (ReadOnlySpan<byte>)asteriks;
                    var dollarsSpan = (ReadOnlySpan<byte>)dollars;
                    var crLfsSpan = (ReadOnlySpan<byte>)crLfs;

                    var into = new ParsedRespCommandOrArgument[4];

                    var res = TryParseSingleCommandImpls(commandBuffer, endPaddingByte, endPaddingBit, into, ref asteriksSpan, ref dollarsSpan, ref crLfsSpan, out var slotsUsed);

                    Assert.False(res);

                    Assert.Equal(2, slotsUsed);

                    Assert.True(into[0].IsCommand);
                    Assert.True(into[1].IsMalformed);
                }

                // valid array, string length valid, missing trailing \r
                {
                    var commandBuffer = Enumerable.Repeat((byte)0, padding).Concat("*1\r\n$1\r\na\n\n"u8.ToArray()).ToArray().AsSpan();

                    var asteriks = new byte[(commandBuffer.Length / 8) + 1];
                    var dollars = new byte[asteriks.Length];
                    var crs = new byte[asteriks.Length];
                    var lfs = new byte[asteriks.Length];
                    var crLfs = new byte[asteriks.Length];

                    RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);
                    RespParser.CombineCrLf_Scalar(crs, lfs, crLfs);

                    var asteriksSpan = (ReadOnlySpan<byte>)asteriks;
                    var dollarsSpan = (ReadOnlySpan<byte>)dollars;
                    var crLfsSpan = (ReadOnlySpan<byte>)crLfs;

                    var into = new ParsedRespCommandOrArgument[4];

                    var res = TryParseSingleCommandImpls(commandBuffer, endPaddingByte, endPaddingBit, into, ref asteriksSpan, ref dollarsSpan, ref crLfsSpan, out var slotsUsed);

                    Assert.False(res);

                    Assert.Equal(2, slotsUsed);

                    Assert.True(into[0].IsCommand);
                    Assert.True(into[1].IsMalformed);
                }

                // valid array, string length valid, missing trailing \n
                {
                    var commandBuffer = Enumerable.Repeat((byte)0, padding).Concat("*1\r\n$1\r\na\r\r"u8.ToArray()).ToArray().AsSpan();

                    var asteriks = new byte[(commandBuffer.Length / 8) + 1];
                    var dollars = new byte[asteriks.Length];
                    var crs = new byte[asteriks.Length];
                    var lfs = new byte[asteriks.Length];
                    var crLfs = new byte[asteriks.Length];

                    RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);
                    RespParser.CombineCrLf_Scalar(crs, lfs, crLfs);

                    var asteriksSpan = (ReadOnlySpan<byte>)asteriks;
                    var dollarsSpan = (ReadOnlySpan<byte>)dollars;
                    var crLfsSpan = (ReadOnlySpan<byte>)crLfs;

                    var into = new ParsedRespCommandOrArgument[4];

                    var res = TryParseSingleCommandImpls(commandBuffer, endPaddingByte, endPaddingBit, into, ref asteriksSpan, ref dollarsSpan, ref crLfsSpan, out var slotsUsed);

                    Assert.False(res);

                    Assert.Equal(2, slotsUsed);

                    Assert.True(into[0].IsCommand);
                    Assert.True(into[1].IsMalformed);
                }

                // array length too long, no trailing \r\n
                {
                    var commandBuffer = Enumerable.Repeat((byte)0, padding).Concat("*12345678901"u8.ToArray()).ToArray().AsSpan();

                    var asteriks = new byte[(commandBuffer.Length / 8) + 1];
                    var dollars = new byte[asteriks.Length];
                    var crs = new byte[asteriks.Length];
                    var lfs = new byte[asteriks.Length];
                    var crLfs = new byte[asteriks.Length];

                    RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);
                    RespParser.CombineCrLf_Scalar(crs, lfs, crLfs);

                    var asteriksSpan = (ReadOnlySpan<byte>)asteriks;
                    var dollarsSpan = (ReadOnlySpan<byte>)dollars;
                    var crLfsSpan = (ReadOnlySpan<byte>)crLfs;

                    var into = new ParsedRespCommandOrArgument[4];

                    var res = TryParseSingleCommandImpls(commandBuffer, endPaddingByte, endPaddingBit, into, ref asteriksSpan, ref dollarsSpan, ref crLfsSpan, out var slotsUsed);

                    Assert.False(res);

                    Assert.Equal(1, slotsUsed);

                    Assert.True(into[0].IsMalformed);
                }

                // array length too long, with trailing \r\n
                {
                    var commandBuffer = Enumerable.Repeat((byte)0, padding).Concat("*12345678901\r\n"u8.ToArray()).ToArray().AsSpan();

                    var asteriks = new byte[(commandBuffer.Length / 8) + 1];
                    var dollars = new byte[asteriks.Length];
                    var crs = new byte[asteriks.Length];
                    var lfs = new byte[asteriks.Length];
                    var crLfs = new byte[asteriks.Length];

                    RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);
                    RespParser.CombineCrLf_Scalar(crs, lfs, crLfs);

                    var asteriksSpan = (ReadOnlySpan<byte>)asteriks;
                    var dollarsSpan = (ReadOnlySpan<byte>)dollars;
                    var crLfsSpan = (ReadOnlySpan<byte>)crLfs;

                    var into = new ParsedRespCommandOrArgument[4];

                    var res = TryParseSingleCommandImpls(commandBuffer, endPaddingByte, endPaddingBit, into, ref asteriksSpan, ref dollarsSpan, ref crLfsSpan, out var slotsUsed);

                    Assert.False(res);

                    Assert.Equal(1, slotsUsed);

                    Assert.True(into[0].IsMalformed);
                }

                // string length too long, no trailing \r\n
                {
                    var commandBuffer = Enumerable.Repeat((byte)0, padding).Concat("*1\r\n$12345678901"u8.ToArray()).ToArray().AsSpan();

                    var asteriks = new byte[(commandBuffer.Length / 8) + 1];
                    var dollars = new byte[asteriks.Length];
                    var crs = new byte[asteriks.Length];
                    var lfs = new byte[asteriks.Length];
                    var crLfs = new byte[asteriks.Length];

                    RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);
                    RespParser.CombineCrLf_Scalar(crs, lfs, crLfs);

                    var asteriksSpan = (ReadOnlySpan<byte>)asteriks;
                    var dollarsSpan = (ReadOnlySpan<byte>)dollars;
                    var crLfsSpan = (ReadOnlySpan<byte>)crLfs;

                    var into = new ParsedRespCommandOrArgument[4];

                    var res = TryParseSingleCommandImpls(commandBuffer, endPaddingByte, endPaddingBit, into, ref asteriksSpan, ref dollarsSpan, ref crLfsSpan, out var slotsUsed);

                    Assert.False(res);

                    Assert.Equal(2, slotsUsed);

                    Assert.True(into[0].IsCommand);
                    Assert.True(into[1].IsMalformed);
                }

                // string length too long, with trailing \r\n
                {
                    var commandBuffer = Enumerable.Repeat((byte)0, padding).Concat("*1\r\n$12345678901\r\n"u8.ToArray()).ToArray().AsSpan();

                    var asteriks = new byte[(commandBuffer.Length / 8) + 1];
                    var dollars = new byte[asteriks.Length];
                    var crs = new byte[asteriks.Length];
                    var lfs = new byte[asteriks.Length];
                    var crLfs = new byte[asteriks.Length];

                    RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);
                    RespParser.CombineCrLf_Scalar(crs, lfs, crLfs);

                    var asteriksSpan = (ReadOnlySpan<byte>)asteriks;
                    var dollarsSpan = (ReadOnlySpan<byte>)dollars;
                    var crLfsSpan = (ReadOnlySpan<byte>)crLfs;

                    var into = new ParsedRespCommandOrArgument[4];

                    var res = TryParseSingleCommandImpls(commandBuffer, endPaddingByte, endPaddingBit, into, ref asteriksSpan, ref dollarsSpan, ref crLfsSpan, out var slotsUsed);

                    Assert.False(res);

                    Assert.Equal(2, slotsUsed);

                    Assert.True(into[0].IsCommand);
                    Assert.True(into[1].IsMalformed);
                }
            }
        }

        [Fact]
        public void Incomplete()
        {
            var basicCommandBuffer = "*3\r\n$3\r\nSET\r\n$4\r\nfizz\r\n$5\r\nworld\r\n"u8;
            for (var padding = 0; padding < 8; padding++)
            {
                for (var missing = 1; missing <= basicCommandBuffer.Length; missing++)
                {
                    var endPaddingByte = (padding / 8) * 8;
                    var endPaddingBit = (byte)(padding % 8);

                    var commandBuffer = Enumerable.Repeat((byte)0, padding).Concat(basicCommandBuffer[..^missing].ToArray()).ToArray().AsSpan();

                    var asteriks = new byte[(commandBuffer.Length / 8) + 1];
                    var dollars = new byte[asteriks.Length];
                    var crs = new byte[asteriks.Length];
                    var lfs = new byte[asteriks.Length];
                    var crLfs = new byte[asteriks.Length];

                    RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);
                    RespParser.CombineCrLf_Scalar(crs, lfs, crLfs);

                    var asteriksSpan = (ReadOnlySpan<byte>)asteriks;
                    var dollarsSpan = (ReadOnlySpan<byte>)dollars;
                    var crLfsSpan = (ReadOnlySpan<byte>)crLfs;

                    var into = new ParsedRespCommandOrArgument[4];

                    var res = TryParseSingleCommandImpls(commandBuffer, endPaddingByte, endPaddingBit, into, ref asteriksSpan, ref dollarsSpan, ref crLfsSpan, out var slotsUsed);
                    Assert.False(res);
                    Assert.Equal(0, slotsUsed);
                }
            }
        }

        [Fact]
        public void InsufficientResultSpace()
        {
            Span<byte> commandBuffer = "*3\r\n$3\r\nSET\r\n$4\r\nfizz\r\n$5\r\nworld\r\n"u8.ToArray();

            for (var space = 1; space < 4; space++)
            {
                var asteriks = new byte[(commandBuffer.Length / 8) + 1];
                var dollars = new byte[asteriks.Length];
                var crs = new byte[asteriks.Length];
                var lfs = new byte[asteriks.Length];
                var crLfs = new byte[asteriks.Length];

                RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);
                RespParser.CombineCrLf_Scalar(crs, lfs, crLfs);

                var asteriksSpan = (ReadOnlySpan<byte>)asteriks;
                var dollarsSpan = (ReadOnlySpan<byte>)dollars;
                var crLfsSpan = (ReadOnlySpan<byte>)crLfs;

                var into = new ParsedRespCommandOrArgument[space];

                var res = TryParseSingleCommandImpls(commandBuffer, 0, 0, into, ref asteriksSpan, ref dollarsSpan, ref crLfsSpan, out var slotsUsed);

                Assert.False(res);
                Assert.Equal(0, slotsUsed);
            }
        }

        private static unsafe bool TryParseSingleCommandImpls(
            Span<byte> commandBuffer,
            int byteStart,
            byte bitStart,
            Span<ParsedRespCommandOrArgument> parsed,
            ref ReadOnlySpan<byte> remainingAsteriks,
            ref ReadOnlySpan<byte> remainingDollars,
            ref ReadOnlySpan<byte> remainingCrLfs,
            out int slotsUsed)
        {
            var oldRemainingAsteriks = remainingAsteriks.ToArray().AsSpan();
            var oldRemainingDollars = remainingDollars.ToArray().AsSpan();
            var oldRemainingCrLfs = remainingCrLfs.ToArray().AsSpan();

            var expectedRes = RespParser.TryParseSingleCommand(commandBuffer, byteStart, bitStart, parsed, ref remainingAsteriks, ref remainingDollars, ref remainingCrLfs, out slotsUsed);

            var expectedParsed = parsed.ToArray();

            parsed.Clear();

            fixed (byte* commandPtr = commandBuffer)
            fixed (ParsedRespCommandOrArgument* parsedPtr = parsed)
            fixed (byte* aPtr = oldRemainingAsteriks)
            fixed (byte* dPtr = oldRemainingDollars)
            fixed (byte* cPtr = oldRemainingCrLfs)
            {
                var asteriskPtr = aPtr;
                var dollarsPtr = dPtr;
                var crLfPtr = cPtr;

                var ptrRes = RespParserV2.TryParseSingleCommandPointers(commandBuffer.Length, commandPtr, byteStart, bitStart, parsed.Length, parsedPtr, oldRemainingAsteriks.Length, ref asteriskPtr, ref dollarsPtr, ref crLfPtr, out var ptrSlotsUsed);

                if (expectedRes != ptrRes)
                {
                    throw new Exception($"Different results!  {expectedRes} != {ptrRes}");
                }

                if (slotsUsed != ptrSlotsUsed)
                {
                    throw new Exception($"Different slots! {slotsUsed} != {ptrSlotsUsed}");
                }

                var ptrSlotsBytes = MemoryMarshal.Cast<ParsedRespCommandOrArgument, byte>(parsed[..ptrSlotsUsed]);
                var expectedSlotsBytes = MemoryMarshal.Cast<ParsedRespCommandOrArgument, byte>(expectedParsed.AsSpan()[..ptrSlotsUsed]);

                if (!ptrSlotsBytes.SequenceEqual(expectedSlotsBytes))
                {
                    throw new Exception("Different parsed slots results!");
                }

                return expectedRes;
            }
        }
    }
}
