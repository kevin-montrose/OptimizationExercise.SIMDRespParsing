using System.Text;

namespace OptimizationExercise.SIMDRespParsing.Tests
{
    public class ParseCommandTests
    {
        [Fact]
        public void RejectTooShort()
        {
            Span<byte> zero = "\r\n\r\n"u8.ToArray();
            Span<byte> one = "\r\nA\r\n"u8.ToArray();
            Span<byte> two = "\r\nAB\r\n"u8.ToArray();

            Assert.False(RespParser.TryParseCommand_Enum(zero, 2, 2, out _));
            Assert.False(RespParser.TryParseCommand_Enum(one, 2, 3, out _));
            Assert.False(RespParser.TryParseCommand_Enum(two, 2, 4, out _));

            Assert.False(RespParser.TryParseCommand_Switch(zero, 2, 2, out _));
            Assert.False(RespParser.TryParseCommand_Switch(one, 2, 3, out _));
            Assert.False(RespParser.TryParseCommand_Switch(two, 2, 4, out _));

            Assert.False(RespParser.TryParseCommand_Hash(zero, 2, 2, out _));
            Assert.False(RespParser.TryParseCommand_Hash(one, 2, 3, out _));
            Assert.False(RespParser.TryParseCommand_Hash(two, 2, 4, out _));

            Assert.False(RespParser.TryParseCommand_Hash2(zero, 2, 2, out _));
            Assert.False(RespParser.TryParseCommand_Hash2(one, 2, 3, out _));
            Assert.False(RespParser.TryParseCommand_Hash2(two, 2, 4, out _));
        }

        [Fact]
        public void ParseAll()
        {
            foreach (var cmd in Enum.GetValues<RespCommand>())
            {
                if (cmd is RespCommand.None or RespCommand.Invalid)
                {
                    continue;
                }

                // Upper
                {
                    var asBytesUpper = Encoding.ASCII.GetBytes($"\r\n{cmd.ToString().ToUpperInvariant()}\r\n");

                    var cp1 = asBytesUpper.ToArray();
                    var cp2 = asBytesUpper.ToArray();
                    var cp3 = asBytesUpper.ToArray();
                    var cp4 = asBytesUpper.ToArray();

                    if (cmd == RespCommand.GETIFNOTMATCH)
                    {
                        Console.WriteLine();
                    }

                    var r1 = RespParser.TryParseCommand_Enum(cp1, 2, cp1.Length - 2, out var p1);
                    var r2 = RespParser.TryParseCommand_Switch(cp2, 2, cp2.Length - 2, out var p2);
                    var r3 = RespParser.TryParseCommand_Hash(cp3, 2, cp3.Length - 2, out var p3);
                    var r4 = RespParser.TryParseCommand_Hash2(cp4, 2, cp4.Length - 2, out var p4);

                    Assert.True(r1);
                    Assert.True(r2);
                    Assert.True(r3);
                    Assert.True(r4);
                    Assert.Equal(cmd, p1);
                    Assert.Equal(cmd, p2);
                    Assert.Equal(cmd, p3);
                    Assert.Equal(cmd, p4);
                }

                // Lower
                {
                    var asBytesLower = Encoding.ASCII.GetBytes($"\r\n{cmd.ToString().ToLowerInvariant()}\r\n");

                    var cp1 = asBytesLower.ToArray();
                    var cp2 = asBytesLower.ToArray();
                    var cp3 = asBytesLower.ToArray();
                    var cp4 = asBytesLower.ToArray();

                    var r1 = RespParser.TryParseCommand_Enum(cp1, 2, cp1.Length - 2, out var p1);
                    var r2 = RespParser.TryParseCommand_Switch(cp2, 2, cp2.Length - 2, out var p2);
                    var r3 = RespParser.TryParseCommand_Hash(cp3, 2, cp3.Length - 2, out var p3);
                    var r4 = RespParser.TryParseCommand_Hash(cp4, 2, cp4.Length - 2, out var p4);

                    Assert.True(r1);
                    Assert.True(r2);
                    Assert.True(r3);
                    Assert.True(r4);
                    Assert.Equal(cmd, p1);
                    Assert.Equal(cmd, p2);
                    Assert.Equal(cmd, p3);
                    Assert.Equal(cmd, p4);
                }
            }
        }

        [Fact]
        public void RejectMangled()
        {
            foreach (var cmd in Enum.GetValues<RespCommand>())
            {
                if (cmd is RespCommand.None or RespCommand.Invalid)
                {
                    continue;
                }

                ReadOnlySpan<byte> valid = Encoding.ASCII.GetBytes($"\r\n{cmd.ToString().ToUpperInvariant()}\r\n");
                Span<byte> invalid = new byte[valid.Length];

                valid.CopyTo(invalid);
                Assert.True(RespParser.TryParseCommand_Enum(invalid, 2, valid.Length - 2, out _));
                Assert.True(RespParser.TryParseCommand_Switch(invalid, 2, valid.Length - 2, out _));
                Assert.True(RespParser.TryParseCommand_Hash(invalid, 2, valid.Length - 2, out _));
                Assert.True(RespParser.TryParseCommand_Hash2(invalid, 2, valid.Length - 2, out _));

                for (var i = 2; i < valid.Length - 2; i++)
                {
                    valid.CopyTo(invalid);

                    invalid[i] = (byte)'!';

                    Assert.False(RespParser.TryParseCommand_Enum(invalid, 2, invalid.Length - 2, out _));
                    Assert.False(RespParser.TryParseCommand_Switch(invalid, 2, invalid.Length - 2, out _));
                    Assert.False(RespParser.TryParseCommand_Hash(invalid, 2, invalid.Length - 2, out _));
                    Assert.False(RespParser.TryParseCommand_Hash2(invalid, 2, invalid.Length - 2, out _));
                }
            }
        }
    }
}
