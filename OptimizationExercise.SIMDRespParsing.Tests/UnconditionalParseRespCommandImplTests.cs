using System.Runtime.Intrinsics;
using System.Text;

namespace OptimizationExercise.SIMDRespParsing.Tests
{
    public class UnconditionalParseRespCommandImplTests
    {
        [Fact]
        public void Simple()
        {
            foreach (var cmd in Enum.GetValues<RespCommand>())
            {
                if (cmd is RespCommand.None or RespCommand.Invalid)
                {
                    continue;
                }

                var asUpper = cmd.ToString().ToUpperInvariant();
                var asLower = cmd.ToString().ToLowerInvariant();

                var respUpperBytes = Encoding.UTF8.GetBytes($"*1\r\n${asUpper.Length}\r\n{asUpper}\r\n").Concat(new byte[Vector256<byte>.Count]).ToArray();
                var respLowerBytes = Encoding.UTF8.GetBytes($"*1\r\n${asLower.Length}\r\n{asLower}\r\n").Concat(new byte[Vector256<byte>.Count]).ToArray();

                var ix = $"*1\r\n${asUpper.Length}\r\n".Length;

                var parsedUpper = UnconditionalParseRespCommandImpl.Parse(ref respUpperBytes.AsSpan().EndRef(), ref respUpperBytes.AsSpan().StartRef(), ix, asUpper.Length);
                var parsedLower = UnconditionalParseRespCommandImpl.Parse(ref respLowerBytes.AsSpan().EndRef(), ref respLowerBytes.AsSpan().StartRef(), ix, asLower.Length);

                Assert.Equal(cmd, parsedUpper);
                Assert.Equal(cmd, parsedLower);
            }
        }

        [Fact]
        public void Malformed()
        {
            foreach (var cmd in Enum.GetValues<RespCommand>())
            {
                if (cmd is RespCommand.None or RespCommand.Invalid)
                {
                    continue;
                }

                var asStr = Encoding.UTF8.GetBytes(cmd.ToString());

                var dataLen = 4 + 1 + asStr.Length.ToString().Length + 2 + asStr.Length + 2;
                RespParserV3.CalculateByteBufferSizes(dataLen, out var bufferLength, out var useableBufferLength, out _);

                var bufferRaw = new byte[bufferLength];
                var buffer = bufferRaw.AsSpan()[..useableBufferLength];

                var lenStr = Encoding.UTF8.GetBytes($"${asStr.Length}\r\n");

                for (var i = 0; i < asStr.Length; i++)
                {
                    for (var smashed = 0; smashed <= byte.MaxValue; smashed++)
                    {
                        if (smashed == asStr[i])
                        {
                            continue;
                        }

                        if (smashed == char.ToLowerInvariant((char)asStr[i]))
                        {
                            continue;
                        }

                        var asSpan = buffer[..dataLen];

                        "*1\r\n"u8.CopyTo(asSpan[..4]);
                        asSpan = asSpan[4..];

                        lenStr.CopyTo(asSpan[..lenStr.Length]);
                        asSpan = asSpan[lenStr.Length..];

                        asStr.CopyTo(asSpan);
                        asSpan[i] = (byte)smashed;

                        asSpan = asSpan[asStr.Length..];

                        "\r\n"u8.CopyTo(asSpan);
                        asSpan = asSpan[2..];

                        Assert.True(asSpan.IsEmpty);

                        var ix = "*1\r\n".Length + lenStr.Length;

                        var smashedCmdStr = buffer.Slice(ix, asStr.Length);
                        if (char.IsLetter((char)smashedCmdStr[0]) && char.IsLetter((char)smashedCmdStr[^1]) && Enum.TryParse<RespCommand>(Encoding.UTF8.GetString(smashedCmdStr), ignoreCase: true, result: out var slowParsed))
                        {
                            continue;
                        }

                        var parsed = UnconditionalParseRespCommandImpl.Parse(ref buffer.EndRef(), ref buffer.StartRef(), ix, asStr.Length);
                        Assert.Equal(RespCommand.None, parsed);
                    }
                }
            }
        }
    }
}