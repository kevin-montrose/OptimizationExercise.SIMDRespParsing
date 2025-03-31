using System.Runtime.Intrinsics;

namespace OptimizationExercise.SIMDRespParsing.Tests
{
    public class CombineCRLFPassTests
    {
        [Fact]
        public void LessThanByte()
        {
            for (var len = 0; len <= 7; len++)
            {
                var buff = new byte[len];
                var asteriks = new byte[1];
                var dollars = new byte[1];
                var crs = new byte[1];
                var lfs = new byte[1];
                var crlfs1 = new byte[1];
                var crlfs2 = new byte[1];

                for (var i = 0; i < len; i++)
                {
                    for (var j = 0; j < len; j++)
                    {
                        if (i == j)
                        {
                            continue;
                        }

                        buff.AsSpan().Clear();
                        buff[i] = (byte)'\r';
                        buff[j] = (byte)'\n';

                        RespParser.ScanForDelimiters(buff, asteriks, dollars, crs, lfs);

                        var iBitIx = i % 8;
                        var jBitIx = j % 8;

                        Assert.Equal((byte)(1 << iBitIx), crs[0]);
                        Assert.Equal((byte)(1 << jBitIx), lfs[0]);

                        crlfs1.AsSpan().Clear();
                        crlfs2.AsSpan().Clear();
                        RespParser.CombineCrLf_Scalar(crs, lfs, crlfs1);
                        CombineCrLf_SIMDImpls(crs, lfs, crlfs2);

                        if (j == (i + 1))
                        {
                            Assert.Equal((byte)(1 << iBitIx), crlfs1[0]);
                        }
                        else
                        {
                            Assert.Equal(0, crlfs1[0]);
                        }

                        Assert.Equal(crlfs1[0], crlfs2[0]);
                    }
                }
            }
        }

        [Fact]
        public void Byte()
        {
            var buff = new byte[8];
            var asteriks = new byte[1];
            var dollars = new byte[1];
            var crs = new byte[1];
            var lfs = new byte[1];
            var crlfs1 = new byte[1];
            var crlfs2 = new byte[1];

            for (var i = 0; i < buff.Length; i++)
            {
                for (var j = 0; j < buff.Length; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    buff.AsSpan().Clear();
                    buff[i] = (byte)'\r';
                    buff[j] = (byte)'\n';

                    RespParser.ScanForDelimiters(buff, asteriks, dollars, crs, lfs);

                    var iBitIx = i % 8;
                    var jBitIx = j % 8;

                    Assert.Equal((byte)(1 << iBitIx), crs[0]);
                    Assert.Equal((byte)(1 << jBitIx), lfs[0]);

                    crlfs1.AsSpan().Clear();
                    crlfs2.AsSpan().Clear();
                    RespParser.CombineCrLf_Scalar(crs, lfs, crlfs1);
                    CombineCrLf_SIMDImpls(crs, lfs, crlfs2);

                    if (j == (i + 1))
                    {
                        Assert.Equal((byte)(1 << iBitIx), crlfs1[0]);
                    }
                    else
                    {
                        Assert.Equal(0, crlfs1[0]);
                    }

                    Assert.Equal(crlfs1[0], crlfs2[0]);
                }
            }
        }

        [Fact]
        public void UShort()
        {
            var buff = new byte[16];
            var asteriks = new byte[buff.Length / 8];
            var dollars = new byte[asteriks.Length];
            var crs = new byte[asteriks.Length];
            var lfs = new byte[asteriks.Length];
            var crlfs1 = new byte[asteriks.Length];
            var crlfs2 = new byte[asteriks.Length];

            var expectedCrLfs = new byte[asteriks.Length];

            for (var i = 0; i < buff.Length; i++)
            {
                for (var j = 0; j < buff.Length; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    buff.AsSpan().Clear();
                    buff[i] = (byte)'\r';
                    buff[j] = (byte)'\n';

                    RespParser.ScanForDelimiters(buff, asteriks, dollars, crs, lfs);

                    var iByteIx = i / 8;
                    var iBitIx = i % 8;

                    var jByteIx = j / 8;
                    var jBitIx = j % 8;

                    Assert.Equal((byte)(1 << iBitIx), crs[iByteIx]);
                    Assert.Equal((byte)(1 << jBitIx), lfs[jByteIx]);

                    crlfs1.AsSpan().Clear();
                    crlfs2.AsSpan().Clear();
                    RespParser.CombineCrLf_Scalar(crs, lfs, crlfs1);
                    CombineCrLf_SIMDImpls(crs, lfs, crlfs2);

                    expectedCrLfs.AsSpan().Clear();

                    if (j == (i + 1))
                    {
                        expectedCrLfs[iByteIx] |= (byte)(1 << iBitIx);
                    }

                    Assert.True(expectedCrLfs.SequenceEqual(crlfs1));

                    Assert.True(crlfs1.SequenceEqual(crlfs2));
                }
            }
        }

        [Fact]
        public void UInt()
        {
            var buff = new byte[32];
            var asteriks = new byte[buff.Length / 8];
            var dollars = new byte[asteriks.Length];
            var crs = new byte[asteriks.Length];
            var lfs = new byte[asteriks.Length];
            var crlfs1 = new byte[asteriks.Length];
            var crlfs2 = new byte[asteriks.Length];

            var expectedCrLfs = new byte[asteriks.Length];

            for (var i = 0; i < buff.Length; i++)
            {
                for (var j = 0; j < buff.Length; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    buff.AsSpan().Clear();
                    buff[i] = (byte)'\r';
                    buff[j] = (byte)'\n';

                    RespParser.ScanForDelimiters(buff, asteriks, dollars, crs, lfs);

                    var iByteIx = i / 8;
                    var iBitIx = i % 8;

                    var jByteIx = j / 8;
                    var jBitIx = j % 8;

                    Assert.Equal((byte)(1 << iBitIx), crs[iByteIx]);
                    Assert.Equal((byte)(1 << jBitIx), lfs[jByteIx]);

                    crlfs1.AsSpan().Clear();
                    crlfs2.AsSpan().Clear();
                    RespParser.CombineCrLf_Scalar(crs, lfs, crlfs1);
                    CombineCrLf_SIMDImpls(crs, lfs, crlfs2);

                    expectedCrLfs.AsSpan().Clear();

                    if (j == (i + 1))
                    {
                        expectedCrLfs[iByteIx] |= (byte)(1 << iBitIx);
                    }

                    Assert.True(expectedCrLfs.SequenceEqual(crlfs1));

                    Assert.True(crlfs1.SequenceEqual(crlfs2));
                }
            }
        }

        [Fact]
        public void ULong()
        {
            var buff = new byte[64];
            var asteriks = new byte[buff.Length / 8];
            var dollars = new byte[asteriks.Length];
            var crs = new byte[asteriks.Length];
            var lfs = new byte[asteriks.Length];
            var crlfs1 = new byte[asteriks.Length];
            var crlfs2 = new byte[asteriks.Length];

            var expectedCrLfs = new byte[asteriks.Length];

            for (var i = 0; i < buff.Length; i++)
            {
                for (var j = 0; j < buff.Length; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    buff.AsSpan().Clear();
                    buff[i] = (byte)'\r';
                    buff[j] = (byte)'\n';

                    RespParser.ScanForDelimiters(buff, asteriks, dollars, crs, lfs);

                    var iByteIx = i / 8;
                    var iBitIx = i % 8;

                    var jByteIx = j / 8;
                    var jBitIx = j % 8;

                    Assert.Equal((byte)(1 << iBitIx), crs[iByteIx]);
                    Assert.Equal((byte)(1 << jBitIx), lfs[jByteIx]);

                    crlfs1.AsSpan().Clear();
                    crlfs2.AsSpan().Clear();
                    RespParser.CombineCrLf_Scalar(crs, lfs, crlfs1);
                    CombineCrLf_SIMDImpls(crs, lfs, crlfs2);

                    expectedCrLfs.AsSpan().Clear();

                    if (j == (i + 1))
                    {
                        expectedCrLfs[iByteIx] |= (byte)(1 << iBitIx);
                    }

                    Assert.True(expectedCrLfs.SequenceEqual(crlfs1));

                    Assert.True(crlfs1.SequenceEqual(crlfs2));
                }
            }
        }

        [Fact]
        public void Vector128()
        {
            var buff = new byte[128];
            var asteriks = new byte[buff.Length / 8];
            var dollars = new byte[asteriks.Length];
            var crs = new byte[asteriks.Length];
            var lfs = new byte[asteriks.Length];
            var crlfs1 = new byte[asteriks.Length];
            var crlfs2 = new byte[asteriks.Length];

            var expectedCrLfs = new byte[asteriks.Length];

            for (var i = 0; i < buff.Length; i++)
            {
                for (var j = 0; j < buff.Length; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    buff.AsSpan().Clear();
                    buff[i] = (byte)'\r';
                    buff[j] = (byte)'\n';

                    RespParser.ScanForDelimiters(buff, asteriks, dollars, crs, lfs);

                    var iByteIx = i / 8;
                    var iBitIx = i % 8;

                    var jByteIx = j / 8;
                    var jBitIx = j % 8;

                    Assert.Equal((byte)(1 << iBitIx), crs[iByteIx]);
                    Assert.Equal((byte)(1 << jBitIx), lfs[jByteIx]);

                    crlfs1.AsSpan().Clear();
                    crlfs2.AsSpan().Clear();
                    RespParser.CombineCrLf_Scalar(crs, lfs, crlfs1);
                    CombineCrLf_SIMDImpls(crs, lfs, crlfs2);

                    expectedCrLfs.AsSpan().Clear();

                    if (j == (i + 1))
                    {
                        expectedCrLfs[iByteIx] |= (byte)(1 << iBitIx);
                    }

                    Assert.True(expectedCrLfs.SequenceEqual(crlfs1));

                    Assert.True(crlfs1.SequenceEqual(crlfs2));
                }
            }
        }

        [Fact]
        public void Vector256()
        {
            var buff = new byte[256];
            var asteriks = new byte[buff.Length / 8];
            var dollars = new byte[asteriks.Length];
            var crs = new byte[asteriks.Length];
            var lfs = new byte[asteriks.Length];
            var crlfs1 = new byte[asteriks.Length];
            var crlfs2 = new byte[asteriks.Length];

            var expectedCrLfs = new byte[asteriks.Length];

            for (var i = 0; i < buff.Length; i++)
            {
                for (var j = 0; j < buff.Length; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    buff.AsSpan().Clear();
                    buff[i] = (byte)'\r';
                    buff[j] = (byte)'\n';

                    RespParser.ScanForDelimiters(buff, asteriks, dollars, crs, lfs);

                    var iByteIx = i / 8;
                    var iBitIx = i % 8;

                    var jByteIx = j / 8;
                    var jBitIx = j % 8;

                    Assert.Equal((byte)(1 << iBitIx), crs[iByteIx]);
                    Assert.Equal((byte)(1 << jBitIx), lfs[jByteIx]);

                    crlfs1.AsSpan().Clear();
                    crlfs2.AsSpan().Clear();
                    RespParser.CombineCrLf_Scalar(crs, lfs, crlfs1);
                    CombineCrLf_SIMDImpls(crs, lfs, crlfs2);

                    expectedCrLfs.AsSpan().Clear();

                    if (j == (i + 1))
                    {
                        expectedCrLfs[iByteIx] |= (byte)(1 << iBitIx);
                    }

                    Assert.True(expectedCrLfs.SequenceEqual(crlfs1));

                    Assert.True(crlfs1.SequenceEqual(crlfs2));
                }
            }
        }

        [Fact]
        public void Vector512()
        {
            var buff = new byte[512];
            var asteriks = new byte[buff.Length / 8];
            var dollars = new byte[asteriks.Length];
            var crs = new byte[asteriks.Length];
            var lfs = new byte[asteriks.Length];
            var crlfs1 = new byte[asteriks.Length];
            var crlfs2 = new byte[asteriks.Length];

            var expectedCrLfs = new byte[asteriks.Length];

            for (var i = 0; i < buff.Length; i++)
            {
                for (var j = 0; j < buff.Length; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    buff.AsSpan().Clear();
                    buff[i] = (byte)'\r';
                    buff[j] = (byte)'\n';

                    RespParser.ScanForDelimiters(buff, asteriks, dollars, crs, lfs);

                    var iByteIx = i / 8;
                    var iBitIx = i % 8;

                    var jByteIx = j / 8;
                    var jBitIx = j % 8;

                    Assert.Equal((byte)(1 << iBitIx), crs[iByteIx]);
                    Assert.Equal((byte)(1 << jBitIx), lfs[jByteIx]);

                    crlfs1.AsSpan().Clear();
                    crlfs2.AsSpan().Clear();
                    RespParser.CombineCrLf_Scalar(crs, lfs, crlfs1);
                    CombineCrLf_SIMDImpls(crs, lfs, crlfs2);

                    expectedCrLfs.AsSpan().Clear();

                    if (j == (i + 1))
                    {
                        expectedCrLfs[iByteIx] |= (byte)(1 << iBitIx);
                    }

                    Assert.True(expectedCrLfs.SequenceEqual(crlfs1));

                    Assert.True(crlfs1.SequenceEqual(crlfs2));
                }
            }
        }

        [Fact]
        public void Vector1024()
        {
            var buff = new byte[1024];
            var asteriks = new byte[buff.Length / 8];
            var dollars = new byte[asteriks.Length];
            var crs = new byte[asteriks.Length];
            var lfs = new byte[asteriks.Length];
            var crlfs1 = new byte[asteriks.Length];
            var crlfs2 = new byte[asteriks.Length];

            var expectedCrLfs = new byte[asteriks.Length];

            for (var i = 0; i < buff.Length; i++)
            {
                for (var j = 0; j < buff.Length; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    buff.AsSpan().Clear();
                    buff[i] = (byte)'\r';
                    buff[j] = (byte)'\n';

                    RespParser.ScanForDelimiters(buff, asteriks, dollars, crs, lfs);

                    var iByteIx = i / 8;
                    var iBitIx = i % 8;

                    var jByteIx = j / 8;
                    var jBitIx = j % 8;

                    Assert.Equal((byte)(1 << iBitIx), crs[iByteIx]);
                    Assert.Equal((byte)(1 << jBitIx), lfs[jByteIx]);

                    crlfs1.AsSpan().Clear();
                    crlfs2.AsSpan().Clear();
                    RespParser.CombineCrLf_Scalar(crs, lfs, crlfs1);
                    CombineCrLf_SIMDImpls(crs, lfs, crlfs2);

                    expectedCrLfs.AsSpan().Clear();

                    if (j == (i + 1))
                    {
                        expectedCrLfs[iByteIx] |= (byte)(1 << iBitIx);
                    }

                    Assert.True(expectedCrLfs.SequenceEqual(crlfs1));

                    Assert.True(crlfs1.SequenceEqual(crlfs2));
                }
            }
        }

        [Fact]
        public void Random()
        {
            const int ITERS = 100_000;
            const int SEED = 2025_03_28_01;

            var rand = new Random(SEED);

            for (var iter = 0; iter < ITERS; iter++)
            {
                var len = rand.Next(2048) + 1;
                var buff = new byte[len];

                for (var i = 0; i < buff.Length; i++)
                {
                    if (i > 0 && buff[i - 1] == (byte)'\r')
                    {
                        if (rand.Next(2) == 0)
                        {
                            buff[i] = (byte)'\n';
                        }
                        else
                        {
                            buff[i] =
                                rand.Next(4) switch
                                {
                                    0 => (byte)'*',
                                    1 => (byte)'$',
                                    2 => (byte)'\r',
                                    _ => 0,
                                };
                        }
                    }
                    else
                    {
                        buff[i] =
                            rand.Next(5) switch
                            {
                                0 => (byte)'*',
                                1 => (byte)'$',
                                2 => (byte)'\r',
                                3 => (byte)'\n',
                                _ => 0,
                            };
                    }
                }

                var asteriks = new byte[(len / 8) + 1];
                var dollars = new byte[asteriks.Length];
                var crs = new byte[asteriks.Length];
                var lfs = new byte[asteriks.Length];
                var crLfs1 = new byte[asteriks.Length];
                var crLfs2 = new byte[asteriks.Length];

                RespParser.ScanForDelimiters(buff, asteriks, dollars, crs, lfs);
                RespParser.CombineCrLf_Scalar(crs, lfs, crLfs1);
                CombineCrLf_SIMDImpls(crs, lfs, crLfs2);

                for (var i = 0; i < buff.Length; i++)
                {
                    var byteIx = i / 8;
                    var bitIx = i % 8;

                    if (buff[i] == (byte)'\r' && (i + 1) < buff.Length && buff[i + 1] == (byte)'\n')
                    {
                        Assert.Equal(1 << bitIx, (1 << bitIx) & crLfs1[byteIx]);
                    }
                    else
                    {
                        Assert.Equal(0, (1 << bitIx) & crLfs1[byteIx]);
                    }
                }

                Assert.True(crLfs1.SequenceEqual(crLfs2));
            }
        }

        private static unsafe void CombineCrLf_SIMDImpls(ReadOnlySpan<byte> crs, ReadOnlySpan<byte> lfs, Span<byte> crLfs)
        {
            var crsCopy = crs.ToArray();
            var lfsCopy = lfs.ToArray();

            RespParser.CombineCrLf_SIMD(crs, lfs, crLfs);

            var expectedCrLfs = crLfs.ToArray();

            fixed (byte* crPtr = crs)
            fixed (byte* lfPtr = lfs)
            fixed (byte* crLfPtr = crLfs)
            {
                RespParserV2.CombineCrLfPointers_SIMD(crs.Length, crPtr, lfPtr, crLfPtr);

                if (!crsCopy.AsSpan().SequenceEqual(new Span<byte>(crPtr, crs.Length)))
                {
                    throw new Exception("Modified input CRs!");
                }

                if (!lfsCopy.AsSpan().SequenceEqual(new Span<byte>(lfPtr, lfs.Length)))
                {
                    throw new Exception("Modified input LFs!");
                }

                if (!expectedCrLfs.AsSpan().SequenceEqual(new Span<byte>(crLfPtr, crLfs.Length)))
                {
                    throw new Exception("Unexpecged result!");
                }
            }
        }
    }
}
