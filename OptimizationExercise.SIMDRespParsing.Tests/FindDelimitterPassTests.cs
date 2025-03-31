using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OptimizationExercise.SIMDRespParsing.Tests
{
    public class FindDelimitterPassTests
    {
        [Fact]
        public void LessThanByte()
        {
            for (var len = 1; len <= 7; len++)
            {
                var commandBuffer = new byte[len];
                var asteriks = new byte[1];
                var dollars = new byte[1];
                var crs = new byte[1];
                var lfs = new byte[1];

                // *
                {
                    for (var i = 0; i < len; i++)
                    {
                        commandBuffer.AsSpan().Clear();
                        commandBuffer[i] = (byte)'*';

                        ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                        Assert.Equal((byte)(1 << i), asteriks[0]);
                        Assert.Equal(0, dollars[0]);
                        Assert.Equal(0, crs[0]);
                        Assert.Equal(0, lfs[0]);
                    }
                }

                // $
                {
                    for (var i = 0; i < len; i++)
                    {
                        commandBuffer.AsSpan().Clear();
                        commandBuffer[i] = (byte)'$';

                        ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                        Assert.Equal(0, asteriks[0]);
                        Assert.Equal((byte)(1 << i), dollars[0]);
                        Assert.Equal(0, crs[0]);
                        Assert.Equal(0, lfs[0]);
                    }
                }

                // \r
                {
                    for (var i = 0; i < len; i++)
                    {
                        commandBuffer.AsSpan().Clear();
                        commandBuffer[i] = (byte)'\r';

                        ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                        Assert.Equal(0, asteriks[0]);
                        Assert.Equal(0, dollars[0]);
                        Assert.Equal((byte)(1 << i), crs[0]);
                        Assert.Equal(0, lfs[0]);
                    }
                }

                // \n
                {
                    for (var i = 0; i < len; i++)
                    {
                        commandBuffer.AsSpan().Clear();
                        commandBuffer[i] = (byte)'\n';

                        ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                        Assert.Equal(0, asteriks[0]);
                        Assert.Equal(0, dollars[0]);
                        Assert.Equal(0, crs[0]);
                        Assert.Equal((byte)(1 << i), lfs[0]);
                    }
                }
            }
        }

        [Fact]
        public void Byte()
        {
            var commandBuffer = new byte[8];
            var asteriks = new byte[1];
            var dollars = new byte[1];
            var crs = new byte[1];
            var lfs = new byte[1];

            // *
            {
                for (var i = 0; i < 8; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'*';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    Assert.Equal((byte)(1 << i), asteriks[0]);
                    Assert.Equal(0, dollars[0]);
                    Assert.Equal(0, crs[0]);
                    Assert.Equal(0, lfs[0]);
                }
            }

            // $
            {
                for (var i = 0; i < 8; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'$';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    Assert.Equal(0, asteriks[0]);
                    Assert.Equal((byte)(1 << i), dollars[0]);
                    Assert.Equal(0, crs[0]);
                    Assert.Equal(0, lfs[0]);
                }
            }

            // \r
            {
                for (var i = 0; i < 8; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'\r';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    Assert.Equal(0, asteriks[0]);
                    Assert.Equal(0, dollars[0]);
                    Assert.Equal((byte)(1 << i), crs[0]);
                    Assert.Equal(0, lfs[0]);
                }
            }

            // \n
            {
                for (var i = 0; i < 8; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'\n';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    Assert.Equal(0, asteriks[0]);
                    Assert.Equal(0, dollars[0]);
                    Assert.Equal(0, crs[0]);
                    Assert.Equal((byte)(1 << i), lfs[0]);
                }
            }
        }

        [Fact]
        public void UShort()
        {
            var commandBuffer = new byte[16];
            var asteriks = new byte[2];
            var dollars = new byte[2];
            var crs = new byte[2];
            var lfs = new byte[2];

            // *
            {
                for (var i = 0; i < 16; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'*';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < asteriks.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, asteriks[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), asteriks[j]);
                        }
                    }

                    Assert.All(dollars, static x => Assert.Equal(0, x));
                    Assert.All(crs, static x => Assert.Equal(0, x));
                    Assert.All(lfs, static x => Assert.Equal(0, x));
                }
            }

            // $
            {
                for (var i = 0; i < 16; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'$';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < dollars.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, dollars[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), dollars[j]);
                        }
                    }

                    Assert.All(asteriks, static x => Assert.Equal(0, x));
                    Assert.All(crs, static x => Assert.Equal(0, x));
                    Assert.All(lfs, static x => Assert.Equal(0, x));
                }
            }

            // \r
            {
                for (var i = 0; i < 16; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'\r';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < dollars.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, crs[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), crs[j]);
                        }
                    }

                    Assert.All(asteriks, static x => Assert.Equal(0, x));
                    Assert.All(dollars, static x => Assert.Equal(0, x));
                    Assert.All(lfs, static x => Assert.Equal(0, x));
                }
            }

            // \n
            {
                for (var i = 0; i < 16; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'\n';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < dollars.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, lfs[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), lfs[j]);
                        }
                    }

                    Assert.All(asteriks, static x => Assert.Equal(0, x));
                    Assert.All(dollars, static x => Assert.Equal(0, x));
                    Assert.All(crs, static x => Assert.Equal(0, x));
                }
            }
        }

        [Fact]
        public void UInt()
        {
            var commandBuffer = new byte[32];
            var asteriks = new byte[2];
            var dollars = new byte[2];
            var crs = new byte[2];
            var lfs = new byte[2];

            // *
            {
                for (var i = 0; i < 32; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'*';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < asteriks.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, asteriks[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), asteriks[j]);
                        }
                    }

                    Assert.All(dollars, static x => Assert.Equal(0, x));
                    Assert.All(crs, static x => Assert.Equal(0, x));
                    Assert.All(lfs, static x => Assert.Equal(0, x));
                }
            }

            // $
            {
                for (var i = 0; i < 32; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'$';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < dollars.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, dollars[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), dollars[j]);
                        }
                    }

                    Assert.All(asteriks, static x => Assert.Equal(0, x));
                    Assert.All(crs, static x => Assert.Equal(0, x));
                    Assert.All(lfs, static x => Assert.Equal(0, x));
                }
            }

            // \r
            {
                for (var i = 0; i < 32; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'\r';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < dollars.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, crs[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), crs[j]);
                        }
                    }

                    Assert.All(asteriks, static x => Assert.Equal(0, x));
                    Assert.All(dollars, static x => Assert.Equal(0, x));
                    Assert.All(lfs, static x => Assert.Equal(0, x));
                }
            }

            // \n
            {
                for (var i = 0; i < 32; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'\n';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < dollars.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, lfs[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), lfs[j]);
                        }
                    }

                    Assert.All(asteriks, static x => Assert.Equal(0, x));
                    Assert.All(dollars, static x => Assert.Equal(0, x));
                    Assert.All(crs, static x => Assert.Equal(0, x));
                }
            }
        }

        [Fact]
        public void ULong()
        {
            var commandBuffer = new byte[64];
            var asteriks = new byte[2];
            var dollars = new byte[2];
            var crs = new byte[2];
            var lfs = new byte[2];

            // *
            {
                for (var i = 0; i < 64; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'*';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < asteriks.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, asteriks[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), asteriks[j]);
                        }
                    }

                    Assert.All(dollars, static x => Assert.Equal(0, x));
                    Assert.All(crs, static x => Assert.Equal(0, x));
                    Assert.All(lfs, static x => Assert.Equal(0, x));
                }
            }

            // $
            {
                for (var i = 0; i < 64; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'$';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < dollars.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, dollars[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), dollars[j]);
                        }
                    }

                    Assert.All(asteriks, static x => Assert.Equal(0, x));
                    Assert.All(crs, static x => Assert.Equal(0, x));
                    Assert.All(lfs, static x => Assert.Equal(0, x));
                }
            }

            // \r
            {
                for (var i = 0; i < 64; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'\r';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < dollars.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, crs[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), crs[j]);
                        }
                    }

                    Assert.All(asteriks, static x => Assert.Equal(0, x));
                    Assert.All(dollars, static x => Assert.Equal(0, x));
                    Assert.All(lfs, static x => Assert.Equal(0, x));
                }
            }

            // \n
            {
                for (var i = 0; i < 64; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'\n';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < dollars.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, lfs[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), lfs[j]);
                        }
                    }

                    Assert.All(asteriks, static x => Assert.Equal(0, x));
                    Assert.All(dollars, static x => Assert.Equal(0, x));
                    Assert.All(crs, static x => Assert.Equal(0, x));
                }
            }
        }

        [Fact]
        public void Vector128()
        {
            var commandBuffer = new byte[128 / 8];
            var asteriks = new byte[(128 / 8) / 8];
            var dollars = new byte[(128 / 8) / 8];
            var crs = new byte[(128 / 8) / 8];
            var lfs = new byte[(128 / 8) / 8];

            // *
            {
                for (var i = 0; i < commandBuffer.Length; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'*';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < asteriks.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, asteriks[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), asteriks[j]);
                        }
                    }

                    Assert.All(dollars, static x => Assert.Equal(0, x));
                    Assert.All(crs, static x => Assert.Equal(0, x));
                    Assert.All(lfs, static x => Assert.Equal(0, x));
                }
            }

            // $
            {
                for (var i = 0; i < commandBuffer.Length; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'$';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < dollars.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, dollars[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), dollars[j]);
                        }
                    }

                    Assert.All(asteriks, static x => Assert.Equal(0, x));
                    Assert.All(crs, static x => Assert.Equal(0, x));
                    Assert.All(lfs, static x => Assert.Equal(0, x));
                }
            }

            // \r
            {
                for (var i = 0; i < commandBuffer.Length; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'\r';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < dollars.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, crs[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), crs[j]);
                        }
                    }

                    Assert.All(asteriks, static x => Assert.Equal(0, x));
                    Assert.All(dollars, static x => Assert.Equal(0, x));
                    Assert.All(lfs, static x => Assert.Equal(0, x));
                }
            }

            // \n
            {
                for (var i = 0; i < commandBuffer.Length; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'\n';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < dollars.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, lfs[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), lfs[j]);
                        }
                    }

                    Assert.All(asteriks, static x => Assert.Equal(0, x));
                    Assert.All(dollars, static x => Assert.Equal(0, x));
                    Assert.All(crs, static x => Assert.Equal(0, x));
                }
            }
        }

        [Fact]
        public void Vector256()
        {
            var commandBuffer = new byte[256 / 8];
            var asteriks = new byte[(256 / 8) / 8];
            var dollars = new byte[(256 / 8) / 8];
            var crs = new byte[(256 / 8) / 8];
            var lfs = new byte[(256 / 8) / 8];

            // *
            {
                for (var i = 0; i < commandBuffer.Length; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'*';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < asteriks.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, asteriks[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), asteriks[j]);
                        }
                    }

                    Assert.All(dollars, static x => Assert.Equal(0, x));
                    Assert.All(crs, static x => Assert.Equal(0, x));
                    Assert.All(lfs, static x => Assert.Equal(0, x));
                }
            }

            // $
            {
                for (var i = 0; i < commandBuffer.Length; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'$';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < dollars.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, dollars[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), dollars[j]);
                        }
                    }

                    Assert.All(asteriks, static x => Assert.Equal(0, x));
                    Assert.All(crs, static x => Assert.Equal(0, x));
                    Assert.All(lfs, static x => Assert.Equal(0, x));
                }
            }

            // \r
            {
                for (var i = 0; i < commandBuffer.Length; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'\r';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < dollars.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, crs[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), crs[j]);
                        }
                    }

                    Assert.All(asteriks, static x => Assert.Equal(0, x));
                    Assert.All(dollars, static x => Assert.Equal(0, x));
                    Assert.All(lfs, static x => Assert.Equal(0, x));
                }
            }

            // \n
            {
                for (var i = 0; i < commandBuffer.Length; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'\n';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < dollars.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, lfs[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), lfs[j]);
                        }
                    }

                    Assert.All(asteriks, static x => Assert.Equal(0, x));
                    Assert.All(dollars, static x => Assert.Equal(0, x));
                    Assert.All(crs, static x => Assert.Equal(0, x));
                }
            }
        }

        [Fact]
        public void Vector512()
        {
            var commandBuffer = new byte[512 / 8];
            var asteriks = new byte[(512 / 8) / 8];
            var dollars = new byte[(512 / 8) / 8];
            var crs = new byte[(512 / 8) / 8];
            var lfs = new byte[(512 / 8) / 8];

            // *
            {
                for (var i = 0; i < commandBuffer.Length; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'*';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < asteriks.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, asteriks[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), asteriks[j]);
                        }
                    }

                    Assert.All(dollars, static x => Assert.Equal(0, x));
                    Assert.All(crs, static x => Assert.Equal(0, x));
                    Assert.All(lfs, static x => Assert.Equal(0, x));
                }
            }

            // $
            {
                for (var i = 0; i < commandBuffer.Length; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'$';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < dollars.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, dollars[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), dollars[j]);
                        }
                    }

                    Assert.All(asteriks, static x => Assert.Equal(0, x));
                    Assert.All(crs, static x => Assert.Equal(0, x));
                    Assert.All(lfs, static x => Assert.Equal(0, x));
                }
            }

            // \r
            {
                for (var i = 0; i < commandBuffer.Length; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'\r';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < dollars.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, crs[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), crs[j]);
                        }
                    }

                    Assert.All(asteriks, static x => Assert.Equal(0, x));
                    Assert.All(dollars, static x => Assert.Equal(0, x));
                    Assert.All(lfs, static x => Assert.Equal(0, x));
                }
            }

            // \n
            {
                for (var i = 0; i < commandBuffer.Length; i++)
                {
                    commandBuffer.AsSpan().Clear();
                    commandBuffer[i] = (byte)'\n';

                    ScanForDelimitersImpls(commandBuffer, asteriks, dollars, crs, lfs);

                    var byteIndex = i / 8;
                    var bitIndex = i % 8;

                    for (var j = 0; j < dollars.Length; j++)
                    {
                        if (j != byteIndex)
                        {
                            Assert.Equal(0, lfs[j]);
                        }
                        else
                        {
                            Assert.Equal((byte)(1 << bitIndex), lfs[j]);
                        }
                    }

                    Assert.All(asteriks, static x => Assert.Equal(0, x));
                    Assert.All(dollars, static x => Assert.Equal(0, x));
                    Assert.All(crs, static x => Assert.Equal(0, x));
                }
            }
        }

        [Fact]
        public void Random()
        {
            const int ITERS = 100_000;
            const int SEED = 2025_03_28_00;

            var rand = new Random(SEED);

            for (var iter = 0; iter < ITERS; iter++)
            {
                var len = rand.Next((512 / 8) * 2) + 1;
                var buff = new byte[len];

                for (var i = 0; i < buff.Length; i++)
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

                var asteriks = new byte[(len / 8) + 1];
                var dollars = new byte[asteriks.Length];
                var crs = new byte[asteriks.Length];
                var lfs = new byte[asteriks.Length];

                ScanForDelimitersImpls(buff, asteriks, dollars, crs, lfs);

                for (var i = 0; i < buff.Length; i++)
                {
                    var byteIx = i / 8;
                    var bitIx = i % 8;

                    var into =
                        buff[i] switch
                        {
                            (byte)'*' => asteriks,
                            (byte)'$' => dollars,
                            (byte)'\r' => crs,
                            (byte)'\n' => lfs,
                            _ => [],
                        };

                    if (into.Length != 0)
                    {
                        var inByte = into[byteIx];
                        var mask = (byte)(1 << bitIx);
                        Assert.Equal(mask, inByte & mask);
                    }
                }
            }
        }

        [Fact]
        public void SetMSBsWhereZero64()
        {
            ulong[] toTest =
                Enumerable
                    .Range(0, byte.MaxValue + 1)
                    .Select(
                        static output =>
                        {
                            var outputCopy = output;

                            var input = 0UL;
                            for (var i = 0; i < 8; i++)
                            {
                                input >>= 8;

                                if ((output & 1) == 0)
                                {
                                    input |= 0xFF_00_00_00__00_00_00_00;
                                }

                                output >>= 1;
                            }

                            return input;
                        }
                    )
                    .ToArray();

            var buff = new byte[8];

            for (var fillByte = 1; fillByte <= byte.MaxValue; fillByte++)
            {
                buff.AsSpan().Fill((byte)fillByte);
                var allFill = BinaryPrimitives.ReadUInt64LittleEndian(buff);

                foreach (var inputRaw in toTest)
                {
                    var input = inputRaw & allFill;

                    var actual1 = RespParser.SetMSBsWhereZero64(input);
                    var actual2 = RespParser.SetMSBsWhereZero64_Mult(input);

                    Assert.Equal(actual1, actual2);
                }
            }
        }

        private static unsafe void ScanForDelimitersImpls(ReadOnlySpan<byte> commandBuffer, Span<byte> asteriks, Span<byte> dollars, Span<byte> crs, Span<byte> lfs)
        {
            RespParser.ScanForDelimiters(commandBuffer, asteriks, dollars, crs, lfs);

            var input = commandBuffer.ToArray();

            var expectedAsteriks = asteriks.ToArray();
            var expectedDollars = dollars.ToArray();
            var expectedCrs = crs.ToArray();
            var expectedLfs = lfs.ToArray();

            fixed (byte* ptr = commandBuffer)
            {
                byte* ptrAsteriks = stackalloc byte[asteriks.Length];
                byte* ptrDollars = stackalloc byte[dollars.Length];
                byte* ptrCrs = stackalloc byte[crs.Length];
                byte* ptrLfs = stackalloc byte[lfs.Length];

                RespParserV2.ScanForDelimitersPointers(commandBuffer.Length, ptr, ptrAsteriks, ptrDollars, ptrCrs, ptrLfs);

                if (!input.AsSpan().SequenceEqual(commandBuffer))
                {
                    throw new Exception("Modified input!");
                }

                if (!expectedAsteriks.AsSpan().SequenceEqual(new Span<byte>(ptrAsteriks, asteriks.Length)))
                {
                    throw new Exception("Asteriks don't match");
                }

                if (!expectedDollars.AsSpan().SequenceEqual(new Span<byte>(ptrDollars, dollars.Length)))
                {
                    throw new Exception("Dollars don't match");
                }

                if (!expectedCrs.AsSpan().SequenceEqual(new Span<byte>(ptrCrs, crs.Length)))
                {
                    throw new Exception("CRs don't match");
                }

                if (!expectedLfs.AsSpan().SequenceEqual(new Span<byte>(ptrLfs, lfs.Length)))
                {
                    throw new Exception("LFs don't match");
                }
            }
        }
    }
}