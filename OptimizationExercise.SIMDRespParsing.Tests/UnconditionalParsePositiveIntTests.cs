using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace OptimizationExercise.SIMDRespParsing.Tests
{
    public class UnconditionalParsePositiveIntTests
    {
        [Fact]
        public void Simple()
        {
            // empty
            {
                var data = ""u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.True(parsed < 0);
            }

            // minimum value
            {
                var data = "0"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.Equal(0, parsed);
            }

            // one digit
            {
                var data = "1"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.Equal(1, parsed);
            }

            // two digit
            {
                var data = "12"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.Equal(12, parsed);
            }

            // three digit
            {
                var data = "123"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.Equal(123, parsed);
            }

            // four digit
            {
                var data = "1234"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.Equal(1234, parsed);
            }

            // five digit
            {
                var data = "12345"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.Equal(12345, parsed);
            }

            // six digit
            {
                var data = "123456"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.Equal(123456, parsed);
            }

            // seven digit
            {
                var data = "1234567"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.Equal(1234567, parsed);
            }

            // eight digit
            {
                var data = "12345678"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.Equal(12345678, parsed);
            }

            // nine digit
            {
                var data = "123456789"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.Equal(123456789, parsed);
            }

            // ten digit
            {
                var data = "1234567890"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.Equal(1234567890, parsed);
            }

            // max value
            {
                var data = "2147483647"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.Equal(int.MaxValue, parsed);
            }
        }

        [Fact]
        public void Malformed()
        {
            // double-zero
            {
                var data = "00"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.True(parsed < 0);
            }

            // leading zero, length == 2
            {
                var data = "01"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.True(parsed < 0);
            }

            // leading zero, length == 3
            {
                var data = "001"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.True(parsed < 0);
            }

            // leading zero, length == 4
            {
                var data = "0001"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.True(parsed < 0);
            }

            // leading zero, length == 5
            {
                var data = "00001"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.True(parsed < 0);
            }

            // leading zero, length == 6
            {
                var data = "000001"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.True(parsed < 0);
            }

            // leading zero, length == 7
            {
                var data = "0000001"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.True(parsed < 0);
            }

            // leading zero, length == 8
            {
                var data = "00000001"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.True(parsed < 0);
            }

            // leading zero, length == 9
            {
                var data = "000000001"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.True(parsed < 0);
            }

            // leading zero, length == 10
            {
                var data = "0000000001"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.True(parsed < 0);
            }

            // int overflow (by 1)
            {
                var data = "2147483648"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.True(parsed < 0);
            }

            // int overflow (by 10)
            {
                var data = "2147483657"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.True(parsed < 0);
            }

            // int overflow (by 100)
            {
                var data = "2147483747"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.True(parsed < 0);
            }

            // int overflow (by 1_000)
            {
                var data = "2147484647"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.True(parsed < 0);
            }

            // int overflow (by 10_000)
            {
                var data = "2147493647"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.True(parsed < 0);
            }

            // int overflow (by 100_000)
            {
                var data = "2147583647"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.True(parsed < 0);
            }

            // int overflow (by 1_000_000)
            {
                var data = "2148483647"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.True(parsed < 0);
            }

            // int overflow (by 10_000_000)
            {
                var data = "2157483647"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.True(parsed < 0);
            }

            // int overflow (by 100_000_000)
            {
                var data = "2247483647"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.True(parsed < 0);
            }

            // int overflow (by 1_000_000_000)
            {
                var data = "3147483647"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.True(parsed < 0);
            }

            // maximum ten digit amount
            {
                var data = "9999999999"u8;
                var padded = Pad(data, out var allocatedBufferSize);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref padded.EndRef(allocatedBufferSize), ref padded.StartRef(), ref padded.EndRef());
                Assert.True(parsed < 0);
            }
        }

        [Fact(Skip = "Very long running, skipping by default")]
        public void Exhaustive()
        {
            // 10 for int.MaxValue digits, 2 for \r\n
            RespParserV3.CalculateByteBufferSizes(10 + 2, out var dataLength, out _, out _);

            var data = GC.AllocateUninitializedArray<byte>(dataLength, pinned: true);
            for (var i = 0UL; i <= int.MaxValue; i++)
            {
                var success = i.TryFormat(data, out var written);
                Assert.True(success);

                "\r\n"u8.CopyTo(data.AsSpan()[written..]);

                var parsed = RespParserV3.UnconditionalParsePositiveInt(ref data.AsSpan().EndRef(), ref data.AsSpan().StartRef(), ref data.AsSpan().EndRef(written));

                Assert.Equal((int)i, parsed);
            }
        }

        private static Span<byte> Pad(ReadOnlySpan<byte> data, out int allocatedBufferSize)
        {
            RespParserV3.CalculateByteBufferSizes(data.Length + 2, out allocatedBufferSize, out _, out _);

            var ret = GC.AllocateUninitializedArray<byte>(allocatedBufferSize, pinned: true);

            data.CopyTo(ret);
            "\r\n"u8.CopyTo(ret.AsSpan()[data.Length..]);

            return ret.AsSpan()[..data.Length];
        }
    }
}
