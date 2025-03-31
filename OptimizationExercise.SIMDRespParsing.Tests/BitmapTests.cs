namespace OptimizationExercise.SIMDRespParsing.Tests
{
    public class BitmapTests
    {
        [Fact]
        public void FindNext24()
        {
            // we need to successfully scan for \r\n after $ followed by a number of up to length 10
            for (var len = 1; len <= 10; len++)
            {
                // we may have up to 7 bit to skip as well
                for (var padding = 0; padding < 8; padding++)
                {
                    var buffer = Enumerable.Repeat((byte)0, padding).Append((byte)'$').Concat(Enumerable.Repeat((byte)'1', len)).Concat([(byte)'\r', (byte)'\n']).ToArray();
                    var bitmapLen = (buffer.Length / 8) + 1;
                    var asteriks = new byte[bitmapLen];
                    var dollars = new byte[asteriks.Length];
                    var crs = new byte[asteriks.Length];
                    var lfs = new byte[asteriks.Length];
                    var crLfs = new byte[asteriks.Length];

                    RespParser.ScanForDelimiters(buffer, asteriks, dollars, crs, lfs);
                    RespParser.CombineCrLf_Scalar(crs, lfs, crLfs);

                    var res = RespParser.FindNext24((byte)padding, crLfs);
                    Assert.Equal(len + padding + 1, res); // + 1 for the $
                }
            }
        }

        [Fact]
        public void Advance()
        {
            const int ITERS = 500_000;
            const int SEED = 2025_03_29_00;

            var rand = new Random(SEED);

            for (var iter = 0; iter < ITERS; iter++)
            {
                var len = rand.Next(512) + 1;
                var b1 = new byte[(len / 8) + 1];
                var b2 = new byte[b1.Length];
                var b3 = new byte[b2.Length];

                rand.NextBytes(b1);
                rand.NextBytes(b2);
                rand.NextBytes(b3);

                var b1Span = (ReadOnlySpan<byte>)b1;
                var b2Span = (ReadOnlySpan<byte>)b2;
                var b3Span = (ReadOnlySpan<byte>)b3;

                var startByte = 0;
                var startBit = (byte)0;

                var advancedBits = 0;
                while (!b1Span.IsEmpty)
                {
                    Assert.True(advancedBits < (b1.Length * 8));

                    var advanceByMax = b1Span.Length * 8 - startBit;

                    var advanceBy = rand.Next(Math.Min(12, advanceByMax)) + 1;

                    var oldStartByte = startByte;

                    RespParser.Advance(advanceBy, ref startByte, ref startBit, ref b1Span, ref b2Span, ref b3Span);

                    Assert.True(startBit < 8);
                    Assert.True(oldStartByte <= startByte);

                    advancedBits += advanceBy;
                }

                Assert.True(b1Span.IsEmpty);
                Assert.True(b2Span.IsEmpty);
                Assert.True(b3Span.IsEmpty);

                Assert.Equal(advancedBits, b1.Length * 8);
            }
        }
    }
}
