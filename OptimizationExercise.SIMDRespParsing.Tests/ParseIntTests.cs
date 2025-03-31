using System.Runtime.CompilerServices;
using System.Text;

namespace OptimizationExercise.SIMDRespParsing.Tests
{
    public class ParseIntTests
    {
        [Fact]
        public void Basic()
        {
            Assert.True(RespParser.TryParsePositiveInt("1"u8, out var d1));
            Assert.Equal(1, d1);

            Assert.True(RespParser.TryParsePositiveInt("12"u8, out var d2));
            Assert.Equal(12, d2);

            Assert.True(RespParser.TryParsePositiveInt("123"u8, out var d3));
            Assert.Equal(123, d3);

            Assert.True(RespParser.TryParsePositiveInt("1234"u8, out var d4));
            Assert.Equal(1234, d4);

            Assert.True(RespParser.TryParsePositiveInt("12345"u8, out var d5));
            Assert.Equal(12345, d5);

            Assert.True(RespParser.TryParsePositiveInt("123456"u8, out var d6));
            Assert.Equal(123456, d6);

            Assert.True(RespParser.TryParsePositiveInt("1234567"u8, out var d7));
            Assert.Equal(1234567, d7);

            Assert.True(RespParser.TryParsePositiveInt("12345678"u8, out var d8));
            Assert.Equal(12345678, d8);

            Assert.True(RespParser.TryParsePositiveInt("123456789"u8, out var d9));
            Assert.Equal(123456789, d9);

            Assert.True(RespParser.TryParsePositiveInt("1234567890"u8, out var d10));
            Assert.Equal(1234567890, d10);

            Assert.True(RespParser.TryParsePositiveInt("2147483647"u8, out var dMax));
            Assert.Equal(2147483647, dMax);
        }

        [Fact]
        public void AllOneDigit()
        {
            for (var i = 1; i <= 9; i++)
            {
                var asBytes = Encoding.UTF8.GetBytes(i.ToString());
                Assert.True(RespParser.TryParsePositiveInt(asBytes, out var parsed));

                Assert.Equal(i, parsed);
            }
        }

        [Fact]
        public void AllTwoDigit()
        {
            for (var i = 1; i <= 99; i++)
            {
                var asBytes = Encoding.UTF8.GetBytes(i.ToString());
                Assert.True(RespParser.TryParsePositiveInt(asBytes, out var parsed));

                Assert.Equal(i, parsed);
            }
        }

        [Fact]
        public void RejectZero()
        {
            Assert.False(RespParser.TryParsePositiveInt("0"u8, out _));
            Assert.False(RespParser.TryParsePositiveInt("00"u8, out _));
            Assert.False(RespParser.TryParsePositiveInt("000"u8, out _));
            Assert.False(RespParser.TryParsePositiveInt("0000"u8, out _));
            Assert.False(RespParser.TryParsePositiveInt("00000"u8, out _));
            Assert.False(RespParser.TryParsePositiveInt("000000"u8, out _));
            Assert.False(RespParser.TryParsePositiveInt("0000000"u8, out _));
            Assert.False(RespParser.TryParsePositiveInt("00000000"u8, out _));
            Assert.False(RespParser.TryParsePositiveInt("000000000"u8, out _));
            Assert.False(RespParser.TryParsePositiveInt("0000000000"u8, out _));
        }

        [Fact]
        public void RejectAllNonDigitComobs()
        {
            var b1 = new byte[1];

            for (var i = 0; i <= byte.MaxValue; i++)
            {
                var isDigit = i >= '1' && i <= '9';
                if (isDigit)
                {
                    continue;
                }

                b1[0] = (byte)i;

                Assert.False(RespParser.TryParsePositiveInt(b1, out _));
            }

            var b2 = new byte[2];

            for (var i = 0; i <= byte.MaxValue; i++)
            {
                var iIsDigit = i >= '0' && i <= '9';

                for (var j = 0; j <= byte.MaxValue; j++)
                {
                    var jIsDigit = j >= '0' && j <= '9';
                    if (iIsDigit && jIsDigit)
                    {
                        continue;
                    }

                    b2[0] = (byte)i;
                    b2[1] = (byte)j;

                    Assert.False(RespParser.TryParsePositiveInt(b2, out _));
                }
            }
        }

        [Fact(Skip = "Very long running, skipping by default")]
        public void ParseAllPositiveInts()
        {
            Span<byte> buff = stackalloc byte[64];

            for (var i = 1L; i <= int.MaxValue; i++)
            {
                Assert.True(i.TryFormat(buff, out var written));

                Assert.True(RespParser.TryParsePositiveInt(buff[..written], out var parsed));
                Assert.Equal(i, parsed);
            }
        }
    }
}
