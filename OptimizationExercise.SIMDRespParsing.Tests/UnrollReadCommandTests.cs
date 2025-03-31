using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace OptimizationExercise.SIMDRespParsing.Tests
{
    public class UnrollReadCommandTests
    {
        [Fact]
        public void Simple()
        {
            for (var padding = 0; padding <= Vector512<byte>.Count; padding++)
            {
                Test(padding);
            }

            static void Test(int padding)
            {
                // Just command (1 arg)
                {
                    ReadOnlySpan<byte> unpadded = "*1\r\n$3\r\nGET\r\n"u8;
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[1];
                    Span<int> intoAsInts = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                    ref var cur = ref paddedCommandBuffer[padding];
                    ref var writeCur = ref intoAsInts[0];
                    var failure = RespParserV3.FALSE;
                    var dataIncomplete = RespParserV3.FALSE;
                    var (_, writeFinal) = RespParserV3.UnrollReadCommand(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInts.EndRef(), ref cur, ref writeCur, ref failure, ref dataIncomplete);

                    Assert.False(RespParserV3.MaskBoolToBool(failure));
                    Assert.False(RespParserV3.MaskBoolToBool(dataIncomplete));
                    Assert.Equal(4, Unsafe.ByteOffset(ref intoAsInts[0], ref writeFinal.Unwrap()) / sizeof(int));

                    Assert.True(into[0].IsCommand);
                    Assert.True(paddedCommandBuffer[into[0].ByteStart..into[0].ByteEnd].SequenceEqual("GET\r\n"u8));
                    Assert.Equal(RespCommand.GET, into[0].Command);
                    Assert.Equal(1, into[0].ArgumentCount);
                }

                // Command + 1 (2 args)
                {
                    ReadOnlySpan<byte> unpadded = "*2\r\n$4\r\nAUTH\r\n$5\r\nworld\r\n"u8;
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[2];
                    Span<int> intoAsInts = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                    ref var cur = ref paddedCommandBuffer[padding];
                    ref var writeCur = ref intoAsInts[0];
                    var failure = RespParserV3.FALSE;
                    var dataIncomplete = RespParserV3.FALSE;
                    var (_, writeFinal) = RespParserV3.UnrollReadCommand(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInts.EndRef(), ref cur, ref writeCur, ref failure, ref dataIncomplete);

                    Assert.False(RespParserV3.MaskBoolToBool(failure));
                    Assert.False(RespParserV3.MaskBoolToBool(dataIncomplete));
                    Assert.Equal(8, Unsafe.ByteOffset(ref intoAsInts[0], ref writeFinal.Unwrap()) / sizeof(int));

                    Assert.True(into[0].IsCommand);
                    Assert.True(paddedCommandBuffer[into[0].ByteStart..into[0].ByteEnd].SequenceEqual("AUTH\r\n"u8));
                    Assert.Equal(RespCommand.AUTH, into[0].Command);
                    Assert.Equal(2, into[0].ArgumentCount);

                    Assert.True(into[1].IsArgument);
                    Assert.True(paddedCommandBuffer[into[1].ByteStart..into[1].ByteEnd].SequenceEqual("world\r\n"u8));
                }

                // Command + 2 (3 args)
                {
                    ReadOnlySpan<byte> unpadded = "*3\r\n$5\r\nHSCAN\r\n$6\r\n123456\r\n$7\r\nabcdefg\r\n"u8;
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[3];
                    Span<int> intoAsInts = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                    ref var cur = ref paddedCommandBuffer[padding];
                    ref var writeCur = ref intoAsInts[0];
                    var failure = RespParserV3.FALSE;
                    var dataIncomplete = RespParserV3.FALSE;
                    var (_, writeFinal) = RespParserV3.UnrollReadCommand(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInts.EndRef(), ref cur, ref writeCur, ref failure, ref dataIncomplete);

                    Assert.False(RespParserV3.MaskBoolToBool(failure));
                    Assert.False(RespParserV3.MaskBoolToBool(dataIncomplete));
                    Assert.Equal(12, Unsafe.ByteOffset(ref intoAsInts[0], ref writeFinal.Unwrap()) / sizeof(int));

                    Assert.True(into[0].IsCommand);
                    Assert.True(paddedCommandBuffer[into[0].ByteStart..into[0].ByteEnd].SequenceEqual("HSCAN\r\n"u8));
                    Assert.Equal(RespCommand.HSCAN, into[0].Command);
                    Assert.Equal(3, into[0].ArgumentCount);

                    Assert.True(into[1].IsArgument);
                    Assert.True(paddedCommandBuffer[into[1].ByteStart..into[1].ByteEnd].SequenceEqual("123456\r\n"u8));

                    Assert.True(into[2].IsArgument);
                    Assert.True(paddedCommandBuffer[into[2].ByteStart..into[2].ByteEnd].SequenceEqual("abcdefg\r\n"u8));
                }

                // Command + 3 (4 args)
                {
                    ReadOnlySpan<byte> unpadded = "*4\r\n$6\r\nAPPEND\r\n$0\r\n\r\n$1\r\nA\r\n$2\r\n12\r\n"u8;
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[4];
                    Span<int> intoAsInts = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                    ref var cur = ref paddedCommandBuffer[padding];
                    ref var writeCur = ref intoAsInts[0];
                    var failure = RespParserV3.FALSE;
                    var dataIncomplete = RespParserV3.FALSE;
                    var (_, writeFinal) = RespParserV3.UnrollReadCommand(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInts.EndRef(), ref cur, ref writeCur, ref failure, ref dataIncomplete);

                    Assert.False(RespParserV3.MaskBoolToBool(failure));
                    Assert.False(RespParserV3.MaskBoolToBool(dataIncomplete));
                    Assert.Equal(16, Unsafe.ByteOffset(ref intoAsInts[0], ref writeFinal.Unwrap()) / sizeof(int));

                    Assert.True(into[0].IsCommand);
                    Assert.True(paddedCommandBuffer[into[0].ByteStart..into[0].ByteEnd].SequenceEqual("APPEND\r\n"u8));
                    Assert.Equal(RespCommand.APPEND, into[0].Command);
                    Assert.Equal(4, into[0].ArgumentCount);

                    Assert.True(into[1].IsArgument);
                    Assert.True(paddedCommandBuffer[into[1].ByteStart..into[1].ByteEnd].SequenceEqual("\r\n"u8));

                    Assert.True(into[2].IsArgument);
                    Assert.True(paddedCommandBuffer[into[2].ByteStart..into[2].ByteEnd].SequenceEqual("A\r\n"u8));

                    Assert.True(into[3].IsArgument);
                    Assert.True(paddedCommandBuffer[into[3].ByteStart..into[3].ByteEnd].SequenceEqual("12\r\n"u8));
                }

                // Command + 4 (5 args)
                {
                    ReadOnlySpan<byte> unpadded = "*5\r\n$7\r\nPUBLISH\r\n$0\r\n\r\n$1\r\nA\r\n$2\r\n12\r\n$3\r\nxyz\r\n"u8;
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[5];
                    Span<int> intoAsInts = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                    ref var cur = ref paddedCommandBuffer[padding];
                    ref var writeCur = ref intoAsInts[0];
                    var failure = RespParserV3.FALSE;
                    var dataIncomplete = RespParserV3.FALSE;
                    var (_, writeFinal) = RespParserV3.UnrollReadCommand(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInts.EndRef(), ref cur, ref writeCur, ref failure, ref dataIncomplete);

                    Assert.False(RespParserV3.MaskBoolToBool(failure));
                    Assert.False(RespParserV3.MaskBoolToBool(dataIncomplete));
                    Assert.Equal(20, Unsafe.ByteOffset(ref intoAsInts[0], ref writeFinal.Unwrap()) / sizeof(int));

                    Assert.True(into[0].IsCommand);
                    Assert.True(paddedCommandBuffer[into[0].ByteStart..into[0].ByteEnd].SequenceEqual("PUBLISH\r\n"u8));
                    Assert.Equal(RespCommand.PUBLISH, into[0].Command);
                    Assert.Equal(5, into[0].ArgumentCount);

                    Assert.True(into[1].IsArgument);
                    Assert.True(paddedCommandBuffer[into[1].ByteStart..into[1].ByteEnd].SequenceEqual("\r\n"u8));

                    Assert.True(into[2].IsArgument);
                    Assert.True(paddedCommandBuffer[into[2].ByteStart..into[2].ByteEnd].SequenceEqual("A\r\n"u8));

                    Assert.True(into[3].IsArgument);
                    Assert.True(paddedCommandBuffer[into[3].ByteStart..into[3].ByteEnd].SequenceEqual("12\r\n"u8));

                    Assert.True(into[4].IsArgument);
                    Assert.True(paddedCommandBuffer[into[4].ByteStart..into[4].ByteEnd].SequenceEqual("xyz\r\n"u8));

                }

                // Command + 9 (10 args)
                {
                    ReadOnlySpan<byte> unpadded = "*10\r\n$4\r\nMGET\r\n$0\r\n\r\n$1\r\nA\r\n$2\r\n12\r\n$3\r\nxyz\r\n$4\r\n1234\r\n$5\r\n12345\r\n$6\r\n123456\r\n$7\r\n1234567\r\n$8\r\n12345678\r\n"u8;
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[10];
                    Span<int> intoAsInts = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                    ref var cur = ref paddedCommandBuffer[padding];
                    ref var writeCur = ref intoAsInts[0];
                    var failure = RespParserV3.FALSE;
                    var dataIncomplete = RespParserV3.FALSE;
                    var (_, writeFinal) = RespParserV3.UnrollReadCommand(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInts.EndRef(), ref cur, ref writeCur, ref failure, ref dataIncomplete);

                    Assert.False(RespParserV3.MaskBoolToBool(failure));
                    Assert.False(RespParserV3.MaskBoolToBool(dataIncomplete));
                    Assert.Equal(40, Unsafe.ByteOffset(ref intoAsInts[0], ref writeFinal.Unwrap()) / sizeof(int));

                    Assert.True(into[0].IsCommand);
                    Assert.True(paddedCommandBuffer[into[0].ByteStart..into[0].ByteEnd].SequenceEqual("MGET\r\n"u8));
                    Assert.Equal(RespCommand.MGET, into[0].Command);
                    Assert.Equal(10, into[0].ArgumentCount);

                    Assert.True(into[1].IsArgument);
                    Assert.True(paddedCommandBuffer[into[1].ByteStart..into[1].ByteEnd].SequenceEqual("\r\n"u8));

                    Assert.True(into[2].IsArgument);
                    Assert.True(paddedCommandBuffer[into[2].ByteStart..into[2].ByteEnd].SequenceEqual("A\r\n"u8));

                    Assert.True(into[3].IsArgument);
                    Assert.True(paddedCommandBuffer[into[3].ByteStart..into[3].ByteEnd].SequenceEqual("12\r\n"u8));

                    Assert.True(into[4].IsArgument);
                    Assert.True(paddedCommandBuffer[into[4].ByteStart..into[4].ByteEnd].SequenceEqual("xyz\r\n"u8));

                    Assert.True(into[5].IsArgument);
                    Assert.True(paddedCommandBuffer[into[5].ByteStart..into[5].ByteEnd].SequenceEqual("1234\r\n"u8));

                    Assert.True(into[6].IsArgument);
                    Assert.True(paddedCommandBuffer[into[6].ByteStart..into[6].ByteEnd].SequenceEqual("12345\r\n"u8));

                    Assert.True(into[7].IsArgument);
                    Assert.True(paddedCommandBuffer[into[7].ByteStart..into[7].ByteEnd].SequenceEqual("123456\r\n"u8));

                    Assert.True(into[8].IsArgument);
                    Assert.True(paddedCommandBuffer[into[8].ByteStart..into[8].ByteEnd].SequenceEqual("1234567\r\n"u8));

                    Assert.True(into[9].IsArgument);
                    Assert.True(paddedCommandBuffer[into[9].ByteStart..into[9].ByteEnd].SequenceEqual("12345678\r\n"u8));
                }
            }
        }

        [Fact]
        public void IncompleteData()
        {
            for (var padding = 0; padding <= Vector512<byte>.Count; padding++)
            {
                Test(padding);
            }

            static void Test(int padding)
            {
                // Just command (1 arg)
                {
                    Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[1];

                    ReadOnlySpan<byte> raw = "*1\r\n$3\r\nGET\r\n"u8;
                    for (var i = 1; i < raw.Length; i++)
                    {
                        var unpadded = raw[..^i];
                        var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                        Span<byte> digitsBitmap = new byte[bitmapLength];

                        RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                        Span<int> intoAsInts = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                        ref var cur = ref paddedCommandBuffer[padding];
                        ref var curWrite = ref intoAsInts[0];
                        var failure = RespParserV3.FALSE;
                        var dataIncomplete = RespParserV3.FALSE;
                        var (finalCur, finalWrite) = RespParserV3.UnrollReadCommand(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInts.EndRef(), ref cur, ref curWrite, ref failure, ref dataIncomplete);

                        Assert.True(RespParserV3.MaskBoolToBool(failure));
                        Assert.True(RespParserV3.MaskBoolToBool(dataIncomplete));
                        Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                        Assert.True(Unsafe.AreSame(ref cur, ref finalCur.Unwrap()));
                    }
                }

                // Command + 1 (2 args)
                {
                    Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[2];

                    ReadOnlySpan<byte> raw = "*2\r\n$4\r\nAUTH\r\n$5\r\nworld\r\n"u8;
                    for (var i = 1; i < raw.Length; i++)
                    {
                        var unpadded = raw[..^i];
                        var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                        Span<byte> digitsBitmap = new byte[bitmapLength];

                        RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                        Span<int> intoAsInts = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                        ref var cur = ref paddedCommandBuffer[padding];
                        ref var curWrite = ref intoAsInts[0];
                        var failure = RespParserV3.FALSE;
                        var dataIncomplete = RespParserV3.FALSE;
                        var (finalCur, finalWrite) = RespParserV3.UnrollReadCommand(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInts.EndRef(), ref cur, ref curWrite, ref failure, ref dataIncomplete);

                        Assert.True(RespParserV3.MaskBoolToBool(failure));
                        Assert.True(RespParserV3.MaskBoolToBool(dataIncomplete));
                        Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                        Assert.True(Unsafe.AreSame(ref cur, ref finalCur.Unwrap()));
                    }
                }

                // Command + 2 (3 args)
                {
                    Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[3];

                    ReadOnlySpan<byte> raw = "*3\r\n$5\r\nHSCAN\r\n$6\r\n123456\r\n$7\r\nabcdefg\r\n"u8;
                    for (var i = 1; i < raw.Length; i++)
                    {
                        var unpadded = raw[..^i];
                        var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                        Span<byte> digitsBitmap = new byte[bitmapLength];

                        RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                        Span<int> intoAsInts = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                        ref var cur = ref paddedCommandBuffer[padding];
                        ref var curWrite = ref intoAsInts[0];
                        var failure = RespParserV3.FALSE;
                        var dataIncomplete = RespParserV3.FALSE;
                        var (finalCur, finalWrite) = RespParserV3.UnrollReadCommand(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInts.EndRef(), ref cur, ref curWrite, ref failure, ref dataIncomplete);

                        Assert.True(RespParserV3.MaskBoolToBool(failure));
                        Assert.True(RespParserV3.MaskBoolToBool(dataIncomplete));
                        Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                        Assert.True(Unsafe.AreSame(ref cur, ref finalCur.Unwrap()));
                    }
                }

                // Command + 3 (4 args)
                {
                    Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[4];

                    ReadOnlySpan<byte> raw = "*4\r\n$6\r\nAPPEND\r\n$0\r\n\r\n$1\r\nA\r\n$2\r\n12\r\n"u8;
                    for (var i = 1; i < raw.Length; i++)
                    {
                        var unpadded = raw[..^i];
                        var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                        Span<byte> digitsBitmap = new byte[bitmapLength];

                        RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                        Span<int> intoAsInts = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                        ref var cur = ref paddedCommandBuffer[padding];
                        ref var curWrite = ref intoAsInts[0];
                        var failure = RespParserV3.FALSE;
                        var dataIncomplete = RespParserV3.FALSE;
                        var (finalCur, finalWrite) = RespParserV3.UnrollReadCommand(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInts.EndRef(), ref cur, ref curWrite, ref failure, ref dataIncomplete);

                        Assert.True(RespParserV3.MaskBoolToBool(failure));
                        Assert.True(RespParserV3.MaskBoolToBool(dataIncomplete));
                        Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                        Assert.True(Unsafe.AreSame(ref cur, ref finalCur.Unwrap()));
                    }
                }


                // Command + 4 (5 args)
                {
                    Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[5];

                    ReadOnlySpan<byte> raw = "*5\r\n$7\r\nPUBLISH\r\n$0\r\n\r\n$1\r\nA\r\n$2\r\n12\r\n$3\r\nxyz\r\n"u8;
                    for (var i = 1; i < raw.Length; i++)
                    {
                        var unpadded = raw[..^i];
                        var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                        Span<byte> digitsBitmap = new byte[bitmapLength];

                        RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                        Span<int> intoAsInts = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                        ref var cur = ref paddedCommandBuffer[padding];
                        ref var curWrite = ref intoAsInts[0];
                        var failure = RespParserV3.FALSE;
                        var dataIncomplete = RespParserV3.FALSE;
                        var (finalCur, finalWrite) = RespParserV3.UnrollReadCommand(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInts.EndRef(), ref cur, ref curWrite, ref failure, ref dataIncomplete);

                        Assert.True(RespParserV3.MaskBoolToBool(failure));
                        Assert.True(RespParserV3.MaskBoolToBool(dataIncomplete));
                        Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                        Assert.True(Unsafe.AreSame(ref cur, ref finalCur.Unwrap()));
                    }
                }
            }
        }

        [Fact]
        public void IncompleteInto()
        {
            for (var padding = 0; padding <= Vector512<byte>.Count; padding++)
            {
                Test(padding);
            }

            static void Test(int padding)
            {
                // Just command (1 arg)
                {
                    ReadOnlySpan<byte> unpadded = "*1\r\n$3\r\nGET\r\n"u8;
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[1];
                    Span<int> intoAsInts = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                    for (var i = 1; i < into.Length; i++)
                    {
                        ref var cur = ref paddedCommandBuffer[padding];
                        ref var curWrite = ref intoAsInts[i * 4];
                        var failure = RespParserV3.FALSE;
                        var dataIncomplete = RespParserV3.FALSE;
                        var (finalCur, finalWrite) = RespParserV3.UnrollReadCommand(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInts.EndRef(), ref cur, ref curWrite, ref failure, ref dataIncomplete);

                        Assert.True(RespParserV3.MaskBoolToBool(failure));
                        Assert.True(RespParserV3.MaskBoolToBool(dataIncomplete));
                        Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                        Assert.True(Unsafe.AreSame(ref cur, ref finalCur.Unwrap()));
                    }
                }

                // Command + 1 (2 args)
                {
                    ReadOnlySpan<byte> unpadded = "*2\r\n$4\r\nAUTH\r\n$5\r\nworld\r\n"u8;
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[2];
                    Span<int> intoAsInts = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                    for (var i = 1; i < into.Length; i++)
                    {
                        ref var cur = ref paddedCommandBuffer[padding];
                        ref var curWrite = ref intoAsInts[i * 4];
                        var failure = RespParserV3.FALSE;
                        var dataIncomplete = RespParserV3.FALSE;
                        var (finalCur, finalWrite) = RespParserV3.UnrollReadCommand(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInts.EndRef(), ref cur, ref curWrite, ref failure, ref dataIncomplete);

                        Assert.True(RespParserV3.MaskBoolToBool(failure));
                        Assert.True(RespParserV3.MaskBoolToBool(dataIncomplete));
                        Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                        Assert.True(Unsafe.AreSame(ref cur, ref finalCur.Unwrap()));
                    }
                }

                // Command + 2 (3 args)
                {
                    ReadOnlySpan<byte> unpadded = "*3\r\n$5\r\nHSCAN\r\n$6\r\n123456\r\n$7\r\nabcdefg\r\n"u8;
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[3];
                    Span<int> intoAsInts = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                    for (var i = 1; i < into.Length; i++)
                    {
                        ref var cur = ref paddedCommandBuffer[padding];
                        ref var curWrite = ref intoAsInts[i * 4];
                        var failure = RespParserV3.FALSE;
                        var dataIncomplete = RespParserV3.FALSE;
                        var (finalCur, finalWrite) = RespParserV3.UnrollReadCommand(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInts.EndRef(), ref cur, ref curWrite, ref failure, ref dataIncomplete);

                        Assert.True(RespParserV3.MaskBoolToBool(failure));
                        Assert.True(RespParserV3.MaskBoolToBool(dataIncomplete));
                        Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                        Assert.True(Unsafe.AreSame(ref cur, ref finalCur.Unwrap()));
                    }
                }

                // Command + 3 (4 args)
                {
                    ReadOnlySpan<byte> unpadded = "*4\r\n$6\r\nAPPEND\r\n$0\r\n\r\n$1\r\nA\r\n$2\r\n12\r\n"u8;
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[4];
                    Span<int> intoAsInts = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                    for (var i = 1; i < into.Length; i++)
                    {
                        ref var cur = ref paddedCommandBuffer[padding];
                        ref var curWrite = ref intoAsInts[i * 4];
                        var failure = RespParserV3.FALSE;
                        var dataIncomplete = RespParserV3.FALSE;
                        var (finalCur, finalWrite) = RespParserV3.UnrollReadCommand(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInts.EndRef(), ref cur, ref curWrite, ref failure, ref dataIncomplete);

                        Assert.True(RespParserV3.MaskBoolToBool(failure));
                        Assert.True(RespParserV3.MaskBoolToBool(dataIncomplete));
                        Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                        Assert.True(Unsafe.AreSame(ref cur, ref finalCur.Unwrap()));
                    }
                }


                // Command + 4 (5 args)
                {
                    ReadOnlySpan<byte> unpadded = "*5\r\n$7\r\nPUBLISH\r\n$0\r\n\r\n$1\r\nA\r\n$2\r\n12\r\n$3\r\nxyz\r\n"u8;
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[5];
                    Span<int> intoAsInts = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                    for (var i = 1; i < into.Length; i++)
                    {
                        ref var cur = ref paddedCommandBuffer[padding];
                        ref var curWrite = ref intoAsInts[i * 4];
                        var failure = RespParserV3.FALSE;
                        var dataIncomplete = RespParserV3.FALSE;
                        var (finalCur, finalWrite) = RespParserV3.UnrollReadCommand(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInts.EndRef(), ref cur, ref curWrite, ref failure, ref dataIncomplete);

                        Assert.True(RespParserV3.MaskBoolToBool(failure));
                        Assert.True(RespParserV3.MaskBoolToBool(dataIncomplete));
                        Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                        Assert.True(Unsafe.AreSame(ref cur, ref finalCur.Unwrap()));
                    }
                }
            }
        }

        [Fact]
        public void MalformedData()
        {
            for (var padding = 0; padding <= Vector512<byte>.Count; padding++)
            {
                Test(padding);
            }

            static void Test(int padding)
            {
                // Just command (1 arg)
                {
                    Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[1];
                    Span<int> intoAsInts = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                    ReadOnlySpan<byte> raw = "*1\r\n$3\r\nGET\r\n"u8;
                    for (var i = 0; i < raw.Length; i++)
                    {
                        if (char.IsAsciiDigit((char)raw[i]))
                        {
                            continue;
                        }

                        for (var j = 0; j <= byte.MaxValue; j++)
                        {
                            if (raw[i] == (byte)j || char.ToLowerInvariant((char)raw[i]) == char.ToLowerInvariant((char)j))
                            {
                                continue;
                            }

                            if (raw[i] == (byte)'G' && (j is (byte)'S' or (byte)'s'))
                            {
                                continue;
                            }

                            into.Clear();

                            var unpadded = raw.ToArray();
                            unpadded[i] = (byte)j;

                            var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var cur = ref paddedCommandBuffer[padding];
                            ref var curWrite = ref intoAsInts[0];
                            var failure = RespParserV3.FALSE;
                            var dataIncomplete = RespParserV3.FALSE;
                            var (finalCur, finalWrite) = RespParserV3.UnrollReadCommand(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInts.EndRef(), ref cur, ref curWrite, ref failure, ref dataIncomplete);

                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                            Assert.False(RespParserV3.MaskBoolToBool(dataIncomplete));
                            Assert.True(Unsafe.AreSame(ref intoAsInts.EndRef(), ref finalWrite.Unwrap()));
                            Assert.True(Unsafe.AreSame(ref cur, ref finalCur.Unwrap()));

                            Assert.True(into[0].IsMalformed);
                        }
                    }
                }

                // Command + 1 (2 args)
                {
                    Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[2];
                    Span<int> intoAsInts = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                    ReadOnlySpan<byte> raw = "*2\r\n$4\r\nAUTH\r\n$5\r\naaaaa\r\n"u8;
                    for (var i = 0; i < raw.Length; i++)
                    {
                        if (char.IsAsciiDigit((char)raw[i]))
                        {
                            continue;
                        }

                        if (raw[i] is (byte)'a')
                        {
                            continue;
                        }

                        for (var j = 0; j <= byte.MaxValue; j++)
                        {
                            if (raw[i] == (byte)j || char.ToLowerInvariant((char)raw[i]) == char.ToLowerInvariant((char)j))
                            {
                                continue;
                            }

                            into.Clear();

                            var unpadded = raw.ToArray();
                            unpadded[i] = (byte)j;
                            var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var cur = ref paddedCommandBuffer[padding];
                            ref var curWrite = ref intoAsInts[0];
                            var failure = RespParserV3.FALSE;
                            var dataIncomplete = RespParserV3.FALSE;
                            var (finalCur, finalWrite) = RespParserV3.UnrollReadCommand(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInts.EndRef(), ref cur, ref curWrite, ref failure, ref dataIncomplete);

                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                            Assert.False(RespParserV3.MaskBoolToBool(dataIncomplete));
                            Assert.True(Unsafe.AreSame(ref intoAsInts[4], ref finalWrite.Unwrap()));
                            Assert.True(Unsafe.AreSame(ref cur, ref finalCur.Unwrap()));

                            Assert.True(into[0].IsMalformed);
                        }
                    }
                }

                // Command + 2 (3 args)
                {
                    Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[3];
                    Span<int> intoAsInts = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                    ReadOnlySpan<byte> raw = "*3\r\n$5\r\nHSCAN\r\n$6\r\naaaaaa\r\n$7\r\nbbbbbbb\r\n"u8;
                    for (var i = 0; i < raw.Length; i++)
                    {
                        if (char.IsAsciiDigit((char)raw[i]))
                        {
                            continue;
                        }

                        if (raw[i] is (byte)'a' or (byte)'b')
                        {
                            continue;
                        }

                        for (var j = 0; j <= byte.MaxValue; j++)
                        {
                            if (raw[i] == (byte)j || char.ToLowerInvariant((char)raw[i]) == char.ToLowerInvariant((char)j))
                            {
                                continue;
                            }

                            if (raw[i] == (byte)'H' && (j is (byte)'S' or (byte)'s' or (byte)'Z' or (byte)'z'))
                            {
                                continue;
                            }

                            into.Clear();

                            var unpadded = raw.ToArray();
                            unpadded[i] = (byte)j;
                            var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var cur = ref paddedCommandBuffer[padding];
                            ref var curWrite = ref intoAsInts[0];
                            var failure = RespParserV3.FALSE;
                            var dataIncomplete = RespParserV3.FALSE;
                            var (finalCur, finalWrite) = RespParserV3.UnrollReadCommand(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInts.EndRef(), ref cur, ref curWrite, ref failure, ref dataIncomplete);

                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                            Assert.False(RespParserV3.MaskBoolToBool(dataIncomplete));
                            Assert.True(Unsafe.AreSame(ref intoAsInts[4], ref finalWrite.Unwrap()));
                            Assert.True(Unsafe.AreSame(ref cur, ref finalCur.Unwrap()));

                            Assert.True(into[0].IsMalformed);

                        }
                    }
                }

                // Command + 3 (4 args)
                {
                    Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[4];
                    Span<int> intoAsInts = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                    ReadOnlySpan<byte> raw = "*4\r\n$6\r\nAPPEND\r\n$0\r\n\r\n$1\r\na\r\n$2\r\nbb\r\n"u8;
                    for (var i = 0; i < raw.Length; i++)
                    {
                        if (char.IsAsciiDigit((char)raw[i]))
                        {
                            continue;
                        }

                        if (raw[i] is (byte)'a' or (byte)'b')
                        {
                            continue;
                        }

                        for (var j = 0; j <= byte.MaxValue; j++)
                        {
                            if (raw[i] == (byte)j || char.ToLowerInvariant((char)raw[i]) == char.ToLowerInvariant((char)j))
                            {
                                continue;
                            }

                            into.Clear();

                            var unpadded = raw.ToArray();
                            unpadded[i] = (byte)j;
                            var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var cur = ref paddedCommandBuffer[padding];
                            ref var curWrite = ref intoAsInts[0];
                            var failure = RespParserV3.FALSE;
                            var dataIncomplete = RespParserV3.FALSE;
                            var (finalCur, finalWrite) = RespParserV3.UnrollReadCommand(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInts.EndRef(), ref cur, ref curWrite, ref failure, ref dataIncomplete);

                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                            Assert.False(RespParserV3.MaskBoolToBool(dataIncomplete));
                            Assert.True(Unsafe.AreSame(ref intoAsInts[4], ref finalWrite.Unwrap()));
                            Assert.True(Unsafe.AreSame(ref cur, ref finalCur.Unwrap()));

                            Assert.True(into[0].IsMalformed);
                        }
                    }
                }

                // Command + 4 (5 args)
                {
                    Span<ParsedRespCommandOrArgument> into = stackalloc ParsedRespCommandOrArgument[5];
                    Span<int> intoAsInts = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                    ReadOnlySpan<byte> raw = "*5\r\n$7\r\nPUBLISH\r\n$0\r\n\r\n$1\r\na\r\n$2\r\nbb\r\n$3\r\nccc\r\n"u8;
                    for (var i = 0; i < raw.Length; i++)
                    {
                        if (char.IsAsciiDigit((char)raw[i]))
                        {
                            continue;
                        }

                        if (raw[i] is (byte)'a' or (byte)'b' or (byte)'c')
                        {
                            continue;
                        }

                        for (var j = 0; j <= byte.MaxValue; j++)
                        {
                            if (raw[i] == (byte)j || char.ToLowerInvariant((char)raw[i]) == char.ToLowerInvariant((char)j))
                            {
                                continue;
                            }

                            into.Clear();

                            var unpadded = raw.ToArray();
                            unpadded[i] = (byte)j;
                            var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var cur = ref paddedCommandBuffer[padding];
                            ref var curWrite = ref intoAsInts[0];
                            var failure = RespParserV3.FALSE;
                            var dataIncomplete = RespParserV3.FALSE;
                            var (finalCur, finalWrite) = RespParserV3.UnrollReadCommand(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInts.EndRef(), ref cur, ref curWrite, ref failure, ref dataIncomplete);

                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                            Assert.False(RespParserV3.MaskBoolToBool(dataIncomplete));
                            Assert.True(Unsafe.AreSame(ref intoAsInts[4], ref finalWrite.Unwrap()));
                            Assert.True(Unsafe.AreSame(ref cur, ref finalCur.Unwrap()));

                            Assert.True(into[0].IsMalformed);
                        }
                    }
                }
            }
        }

        private static Span<byte> Pad(int leadingPadding, ReadOnlySpan<byte> buffer, out int allocatedBufferSize, out int allocatedBitmapSize)
        {
            var totalLen = leadingPadding + buffer.Length;

            RespParserV3.CalculateByteBufferSizes(totalLen, out allocatedBufferSize, out var usableBufferSize, out allocatedBitmapSize);

            var padded = GC.AllocateUninitializedArray<byte>(allocatedBufferSize, pinned: true);
            padded.AsSpan().Fill((byte)'x');

            buffer.CopyTo(padded.AsSpan()[leadingPadding..]);

            return padded.AsSpan()[..usableBufferSize];
        }
    }
}
