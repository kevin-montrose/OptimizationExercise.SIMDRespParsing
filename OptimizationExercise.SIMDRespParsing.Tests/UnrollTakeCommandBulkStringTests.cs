using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;

namespace OptimizationExercise.SIMDRespParsing.Tests
{
    public class UnrollTakeCommandBulkStringTests
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
                var into = new ParsedRespCommandOrArgument[1];
                var intoAsInt = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                var ignored = 0;

                // 1 digit of length
                {
                    var unpadded = "$1\r\na\r\ntrash"u8;
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var curCommand = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    ref var curWrite = ref intoAsInt[0];
                    var remainingItemsInArray = 1;
                    var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                    Assert.False(RespParserV3.MaskBoolToBool(failure));
                    Assert.False(RespParserV3.MaskBoolToBool(incomplete));

                    Assert.Equal(4, Unsafe.ByteOffset(ref intoAsInt[0], ref finalWrite.Unwrap()) / sizeof(int));
                    Assert.Equal(0, remainingItemsInArray);

                    Assert.True("a\r\n"u8.SequenceEqual(paddedCommandBuffer[into[0].ByteStart..into[0].ByteEnd]));
                }

                // 2 digit of length
                {
                    var unpadded = "$10\r\n0123456789\r\ntrash"u8;
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var curCommand = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    ref var curWrite = ref intoAsInt[0];
                    var remainingItemsInArray = 1;
                    var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                    Assert.False(RespParserV3.MaskBoolToBool(failure));
                    Assert.False(RespParserV3.MaskBoolToBool(incomplete));

                    Assert.Equal(4, Unsafe.ByteOffset(ref intoAsInt[0], ref finalWrite.Unwrap()) / sizeof(int));
                    Assert.Equal(0, remainingItemsInArray);

                    Assert.True("0123456789\r\n"u8.SequenceEqual(paddedCommandBuffer[into[0].ByteStart..into[0].ByteEnd]));
                }

                // 3 digit of length
                {
                    var unpadded =
                        Encoding.UTF8.GetBytes(
                            "$100\r\n" +
                            new string('b', 100) +
                            "\r\n" +
                            "trash"
                        );
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var curCommand = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    ref var curWrite = ref intoAsInt[0];
                    var remainingItemsInArray = 1;
                    var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                    Assert.False(RespParserV3.MaskBoolToBool(failure));
                    Assert.False(RespParserV3.MaskBoolToBool(incomplete));

                    Assert.Equal(4, Unsafe.ByteOffset(ref intoAsInt[0], ref finalWrite.Unwrap()) / sizeof(int));
                    Assert.Equal(0, remainingItemsInArray);

                    Assert.True(Encoding.UTF8.GetBytes(new string('b', 100) + "\r\n").AsSpan().SequenceEqual(paddedCommandBuffer[into[0].ByteStart..into[0].ByteEnd]));
                }

                // 4 digit of length
                {
                    var unpadded =
                        Encoding.UTF8.GetBytes(
                            "$1234\r\n" +
                            new string('c', 1234) +
                            "\r\n" +
                            "trash"
                        );
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var curCommand = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    ref var curWrite = ref intoAsInt[0];
                    var remainingItemsInArray = 1;
                    var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                    Assert.False(RespParserV3.MaskBoolToBool(failure));
                    Assert.False(RespParserV3.MaskBoolToBool(incomplete));

                    Assert.Equal(4, Unsafe.ByteOffset(ref intoAsInt[0], ref finalWrite.Unwrap()) / sizeof(int));
                    Assert.Equal(0, remainingItemsInArray);

                    Assert.True(Encoding.UTF8.GetBytes(new string('c', 1234) + "\r\n").AsSpan().SequenceEqual(paddedCommandBuffer[into[0].ByteStart..into[0].ByteEnd]));
                }

                // 5 digit of length
                {
                    var unpadded =
                        Encoding.UTF8.GetBytes(
                            "$11111\r\n" +
                            new string('d', 11111) +
                            "\r\n" +
                            "trash"
                        );
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var curCommand = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    ref var curWrite = ref intoAsInt[0];
                    var remainingItemsInArray = 1;
                    var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                    Assert.False(RespParserV3.MaskBoolToBool(failure));
                    Assert.False(RespParserV3.MaskBoolToBool(incomplete));

                    Assert.Equal(4, Unsafe.ByteOffset(ref intoAsInt[0], ref finalWrite.Unwrap()) / sizeof(int));
                    Assert.Equal(0, remainingItemsInArray);

                    Assert.True(Encoding.UTF8.GetBytes(new string('d', 11111) + "\r\n").AsSpan().SequenceEqual(paddedCommandBuffer[into[0].ByteStart..into[0].ByteEnd]));
                }

                // 6 digit of length
                {
                    var unpadded =
                        Encoding.UTF8.GetBytes(
                            "$222222\r\n" +
                            new string('e', 222222) +
                            "\r\n" +
                            "trash"
                        );
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var curCommand = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    ref var curWrite = ref intoAsInt[0];
                    var remainingItemsInArray = 1;
                    var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                    Assert.False(RespParserV3.MaskBoolToBool(failure));
                    Assert.False(RespParserV3.MaskBoolToBool(incomplete));

                    Assert.Equal(4, Unsafe.ByteOffset(ref intoAsInt[0], ref finalWrite.Unwrap()) / sizeof(int));
                    Assert.Equal(0, remainingItemsInArray);

                    Assert.True(Encoding.UTF8.GetBytes(new string('e', 222222) + "\r\n").AsSpan().SequenceEqual(paddedCommandBuffer[into[0].ByteStart..into[0].ByteEnd]));
                }

                // 7+ digits is _a lot_ of data, so we'll just assume it's working
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
                var into = new ParsedRespCommandOrArgument[1];
                var intoAsInt = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                var ignored = 0;

                // 1 digit of length
                {
                    var unpadded = "$1\r\na\r\ntrash"u8;
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var curCommand = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    ref var curWrite = ref intoAsInt.EndRef();
                    var remainingItemsInArray = 1;
                    var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));

                    Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                    Assert.Equal(1, remainingItemsInArray);
                }

                // 2 digit of length
                {
                    var unpadded = "$10\r\n0123456789\r\ntrash"u8;
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var curCommand = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    ref var curWrite = ref intoAsInt.EndRef();
                    var remainingItemsInArray = 1;
                    var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));

                    Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                    Assert.Equal(1, remainingItemsInArray);
                }

                // 3 digit of length
                {
                    var unpadded =
                        Encoding.UTF8.GetBytes(
                            "$100\r\n" +
                            new string('b', 100) +
                            "\r\n" +
                            "trash"
                        );
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var curCommand = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    ref var curWrite = ref intoAsInt.EndRef();
                    var remainingItemsInArray = 1;
                    var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));

                    Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                    Assert.Equal(1, remainingItemsInArray);
                }

                // 4 digit of length
                {
                    var unpadded =
                        Encoding.UTF8.GetBytes(
                            "$1234\r\n" +
                            new string('c', 1234) +
                            "\r\n" +
                            "trash"
                        );
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var curCommand = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    ref var curWrite = ref intoAsInt.EndRef();
                    var remainingItemsInArray = 1;
                    var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));

                    Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                    Assert.Equal(1, remainingItemsInArray);
                }

                // 5 digit of length
                {
                    var unpadded =
                        Encoding.UTF8.GetBytes(
                            "$11111\r\n" +
                            new string('d', 11111) +
                            "\r\n" +
                            "trash"
                        );
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var curCommand = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    ref var curWrite = ref intoAsInt.EndRef();
                    var remainingItemsInArray = 1;
                    var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));

                    Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                    Assert.Equal(1, remainingItemsInArray);
                }

                // 6 digit of length
                {
                    var unpadded =
                        Encoding.UTF8.GetBytes(
                            "$222222\r\n" +
                            new string('e', 222222) +
                            "\r\n" +
                            "trash"
                        );
                    var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                    Span<byte> digitsBitmap = new byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var curCommand = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    ref var curWrite = ref intoAsInt.EndRef();
                    var remainingItemsInArray = 1;
                    var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));

                    Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                    Assert.Equal(1, remainingItemsInArray);
                }

                // 7+ digits is _a lot_ of data, so we'll just assume it's working
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
                var into = new ParsedRespCommandOrArgument[1];
                var intoAsInt = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                var ignored = 0;

                // 1 digit of length
                {
                    var raw = "$1\r\na\r\n"u8;
                    for (var j = 1; j < raw.Length; j++)
                    {
                        var unpadded = raw[..^j];
                        var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                        Span<byte> digitsBitmap = new byte[bitmapLength];

                        RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                        ref var curCommand = ref paddedCommandBuffer[padding];
                        var failure = RespParserV3.FALSE;
                        var incomplete = RespParserV3.FALSE;
                        ref var curWrite = ref intoAsInt[0];
                        var remainingItemsInArray = 1;
                        var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                        Assert.True(RespParserV3.MaskBoolToBool(failure));
                        Assert.True(RespParserV3.MaskBoolToBool(incomplete));

                        Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                        Assert.Equal(1, remainingItemsInArray);
                    }
                }

                // 2 digit of length
                {
                    var raw = "$10\r\n0123456789\r\n"u8;
                    for (var j = 1; j < raw.Length; j++)
                    {
                        var unpadded = raw[..^j];
                        var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                        Span<byte> digitsBitmap = new byte[bitmapLength];

                        RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                        ref var curCommand = ref paddedCommandBuffer[padding];
                        var failure = RespParserV3.FALSE;
                        var incomplete = RespParserV3.FALSE;
                        ref var curWrite = ref intoAsInt[0];
                        var remainingItemsInArray = 1;
                        var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                        Assert.True(RespParserV3.MaskBoolToBool(failure));
                        Assert.True(RespParserV3.MaskBoolToBool(incomplete));

                        Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                        Assert.Equal(1, remainingItemsInArray);
                    }
                }

                // 3 digit of length
                {
                    var raw =
                        Encoding.UTF8.GetBytes(
                            "$100\r\n" +
                            new string('b', 100) +
                            "\r\n"
                        )
                        .AsSpan();
                    for (var j = 1; j < raw.Length; j++)
                    {
                        var unpadded = raw[..^j];
                        var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                        Span<byte> digitsBitmap = new byte[bitmapLength];

                        RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                        ref var curCommand = ref paddedCommandBuffer[padding];
                        var failure = RespParserV3.FALSE;
                        var incomplete = RespParserV3.FALSE;
                        ref var curWrite = ref intoAsInt[0];
                        var remainingItemsInArray = 1;
                        var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                        Assert.True(RespParserV3.MaskBoolToBool(failure));
                        Assert.True(RespParserV3.MaskBoolToBool(incomplete));

                        Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                        Assert.Equal(1, remainingItemsInArray);
                    }
                }

                // 4 digit of length
                {
                    var raw =
                        Encoding.UTF8.GetBytes(
                            "$1234\r\n" +
                            new string('c', 1234) +
                            "\r\n"
                        )
                        .AsSpan();
                    for (var j = 1; j < raw.Length; j++)
                    {
                        var unpadded = raw[..^j];
                        var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                        Span<byte> digitsBitmap = new byte[bitmapLength];

                        RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                        ref var curCommand = ref paddedCommandBuffer[padding];
                        var failure = RespParserV3.FALSE;
                        var incomplete = RespParserV3.FALSE;
                        ref var curWrite = ref intoAsInt[0];
                        var remainingItemsInArray = 1;
                        var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                        Assert.True(RespParserV3.MaskBoolToBool(failure));
                        Assert.True(RespParserV3.MaskBoolToBool(incomplete));

                        Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                        Assert.Equal(1, remainingItemsInArray);
                    }
                }

                // 5 digit of length
                {
                    var raw =
                        Encoding.UTF8.GetBytes(
                            "$11111\r\n" +
                            new string('d', 11111) +
                            "\r\n"
                        )
                        .AsSpan();
                    for (var j = 1; j < raw.Length; j++)
                    {
                        var unpadded = raw[..^j];
                        var paddedCommandBuffer = Pad(padding, unpadded, out var allocatedBufferSize, out var bitmapLength);

                        Span<byte> digitsBitmap = new byte[bitmapLength];

                        RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                        ref var curCommand = ref paddedCommandBuffer[padding];
                        var failure = RespParserV3.FALSE;
                        var incomplete = RespParserV3.FALSE;
                        ref var curWrite = ref intoAsInt[0];
                        var remainingItemsInArray = 1;
                        var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + unpadded.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                        Assert.True(RespParserV3.MaskBoolToBool(failure));
                        Assert.True(RespParserV3.MaskBoolToBool(incomplete));

                        Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                        Assert.Equal(1, remainingItemsInArray);
                    }
                }

                // 6+ digits is _a lot_ of data, so we'll just assume it's working
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
                var into = new ParsedRespCommandOrArgument[1];
                var intoAsInt = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                var ignored = 0;

                // 1 digit of length
                {
                    var raw = "$1\r\na\r\n"u8;
                    for (var j = 0; j < raw.Length; j++)
                    {
                        if (char.IsAsciiDigit((char)raw[j]))
                        {
                            continue;
                        }

                        if (raw[j] is (byte)'a')
                        {
                            continue;
                        }

                        for (var smashed = 0; smashed <= byte.MaxValue; smashed++)
                        {
                            if (raw[j] == (byte)smashed)
                            {
                                continue;
                            }

                            var paddedCommandBuffer = Pad(padding, raw, out var allocatedBufferSize, out var bitmapLength);

                            paddedCommandBuffer[padding + j] = (byte)smashed;

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var curCommand = ref paddedCommandBuffer[padding];
                            var failure = RespParserV3.FALSE;
                            var incomplete = RespParserV3.FALSE;
                            ref var curWrite = ref intoAsInt[0];
                            var remainingItemsInArray = 1;
                            var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + raw.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                            Assert.False(RespParserV3.MaskBoolToBool(incomplete));

                            Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                            Assert.Equal(1, remainingItemsInArray);
                        }
                    }
                }

                // 2 digit of length
                {
                    var raw = "$10\r\n0123456789\r\n"u8;
                    for (var j = 0; j < raw.Length; j++)
                    {
                        if (char.IsAsciiDigit((char)raw[j]))
                        {
                            continue;
                        }

                        for (var smashed = 0; smashed <= byte.MaxValue; smashed++)
                        {
                            if (raw[j] == (byte)smashed)
                            {
                                continue;
                            }

                            var paddedCommandBuffer = Pad(padding, raw, out var allocatedBufferSize, out var bitmapLength);

                            paddedCommandBuffer[padding + j] = (byte)smashed;

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var curCommand = ref paddedCommandBuffer[padding];
                            var failure = RespParserV3.FALSE;
                            var incomplete = RespParserV3.FALSE;
                            ref var curWrite = ref intoAsInt[0];
                            var remainingItemsInArray = 1;
                            var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + raw.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                            Assert.False(RespParserV3.MaskBoolToBool(incomplete));

                            Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                            Assert.Equal(1, remainingItemsInArray);
                        }
                    }
                }

                // 3 digit of length
                {
                    var raw =
                        Encoding.UTF8.GetBytes(
                            "$100\r\n" +
                            new string('b', 100) +
                            "\r\n"
                        )
                        .AsSpan();
                    for (var j = 0; j < raw.Length; j++)
                    {
                        if (char.IsAsciiDigit((char)raw[j]))
                        {
                            continue;
                        }

                        if (raw[j] is (byte)'b')
                        {
                            continue;
                        }

                        for (var smashed = 0; smashed <= byte.MaxValue; smashed++)
                        {
                            if (raw[j] == (byte)smashed)
                            {
                                continue;
                            }

                            var paddedCommandBuffer = Pad(padding, raw, out var allocatedBufferSize, out var bitmapLength);

                            paddedCommandBuffer[padding + j] = (byte)smashed;

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var curCommand = ref paddedCommandBuffer[padding];
                            var failure = RespParserV3.FALSE;
                            var incomplete = RespParserV3.FALSE;
                            ref var curWrite = ref intoAsInt[0];
                            var remainingItemsInArray = 1;
                            var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + raw.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                            Assert.False(RespParserV3.MaskBoolToBool(incomplete));

                            Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                            Assert.Equal(1, remainingItemsInArray);
                        }
                    }
                }

                // 4 digit of length
                {
                    var raw =
                        Encoding.UTF8.GetBytes(
                            "$1234\r\n" +
                            new string('c', 1234) +
                            "\r\n"
                        )
                        .AsSpan();
                    for (var j = 0; j < raw.Length; j++)
                    {
                        if (char.IsAsciiDigit((char)raw[j]))
                        {
                            continue;
                        }

                        if (raw[j] is (byte)'c')
                        {
                            continue;
                        }

                        for (var smashed = 0; smashed <= byte.MaxValue; smashed++)
                        {
                            if (raw[j] == (byte)smashed)
                            {
                                continue;
                            }

                            var paddedCommandBuffer = Pad(padding, raw, out var allocatedBufferSize, out var bitmapLength);

                            paddedCommandBuffer[padding + j] = (byte)smashed;

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var curCommand = ref paddedCommandBuffer[padding];
                            var failure = RespParserV3.FALSE;
                            var incomplete = RespParserV3.FALSE;
                            ref var curWrite = ref intoAsInt[0];
                            var remainingItemsInArray = 1;
                            var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + raw.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                            Assert.False(RespParserV3.MaskBoolToBool(incomplete));

                            Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                            Assert.Equal(1, remainingItemsInArray);
                        }
                    }
                }

                // 5 digit of length
                {
                    var raw =
                        Encoding.UTF8.GetBytes(
                            "$11111\r\n" +
                            new string('d', 11111) +
                            "\r\n"
                        )
                        .AsSpan();
                    for (var j = 0; j < raw.Length; j++)
                    {
                        if (char.IsAsciiDigit((char)raw[j]))
                        {
                            continue;
                        }

                        if (raw[j] is (byte)'d')
                        {
                            continue;
                        }

                        for (var smashed = 0; smashed <= byte.MaxValue; smashed++)
                        {
                            if (raw[j] == (byte)smashed)
                            {
                                continue;
                            }

                            var paddedCommandBuffer = Pad(padding, raw, out var allocatedBufferSize, out var bitmapLength);

                            paddedCommandBuffer[padding + j] = (byte)smashed;

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var curCommand = ref paddedCommandBuffer[padding];
                            var failure = RespParserV3.FALSE;
                            var incomplete = RespParserV3.FALSE;
                            ref var curWrite = ref intoAsInt[0];
                            var remainingItemsInArray = 1;
                            var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + raw.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                            Assert.False(RespParserV3.MaskBoolToBool(incomplete));

                            Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                            Assert.Equal(1, remainingItemsInArray);
                        }
                    }
                }

                // 6+ digits is _a lot_ of data, so we'll just assume it's working
            }
        }

        [Fact]
        public void MalformedDataAndIncompleteInto()
        {
            for (var padding = 0; padding <= Vector512<byte>.Count; padding++)
            {
                Test(padding);
            }

            static void Test(int padding)
            {
                var into = new ParsedRespCommandOrArgument[1];
                var intoAsInt = MemoryMarshal.Cast<ParsedRespCommandOrArgument, int>(into);

                var ignored = 0;

                // 1 digit of length
                {
                    var raw = "$1\r\na\r\n"u8;
                    for (var j = 0; j < raw.Length; j++)
                    {
                        if (char.IsAsciiDigit((char)raw[j]))
                        {
                            continue;
                        }

                        if (raw[j] is (byte)'a')
                        {
                            continue;
                        }

                        for (var smashed = 0; smashed <= byte.MaxValue; smashed++)
                        {
                            if (raw[j] == (byte)smashed)
                            {
                                continue;
                            }

                            var paddedCommandBuffer = Pad(padding, raw, out var allocatedBufferSize, out var bitmapLength);

                            paddedCommandBuffer[padding + j] = (byte)smashed;

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var curCommand = ref paddedCommandBuffer[padding];
                            var failure = RespParserV3.FALSE;
                            var incomplete = RespParserV3.FALSE;
                            ref var curWrite = ref intoAsInt.EndRef();
                            var remainingItemsInArray = 1;
                            var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + raw.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                            Assert.True(RespParserV3.MaskBoolToBool(incomplete));

                            Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                            Assert.Equal(1, remainingItemsInArray);
                        }
                    }
                }

                // 2 digit of length
                {
                    var raw = "$10\r\n0123456789\r\n"u8;
                    for (var j = 0; j < raw.Length; j++)
                    {
                        if (char.IsAsciiDigit((char)raw[j]))
                        {
                            continue;
                        }

                        for (var smashed = 0; smashed <= byte.MaxValue; smashed++)
                        {
                            if (raw[j] == (byte)smashed)
                            {
                                continue;
                            }

                            var paddedCommandBuffer = Pad(padding, raw, out var allocatedBufferSize, out var bitmapLength);

                            paddedCommandBuffer[padding + j] = (byte)smashed;

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var curCommand = ref paddedCommandBuffer[padding];
                            var failure = RespParserV3.FALSE;
                            var incomplete = RespParserV3.FALSE;
                            ref var curWrite = ref intoAsInt.EndRef();
                            var remainingItemsInArray = 1;
                            var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + raw.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                            Assert.True(RespParserV3.MaskBoolToBool(incomplete));

                            Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                            Assert.Equal(1, remainingItemsInArray);
                        }
                    }
                }

                // 3 digit of length
                {
                    var raw =
                        Encoding.UTF8.GetBytes(
                            "$100\r\n" +
                            new string('b', 100) +
                            "\r\n"
                        )
                        .AsSpan();
                    for (var j = 0; j < raw.Length; j++)
                    {
                        if (char.IsAsciiDigit((char)raw[j]))
                        {
                            continue;
                        }

                        if (raw[j] is (byte)'b')
                        {
                            continue;
                        }

                        for (var smashed = 0; smashed <= byte.MaxValue; smashed++)
                        {
                            if (raw[j] == (byte)smashed)
                            {
                                continue;
                            }

                            var paddedCommandBuffer = Pad(padding, raw, out var allocatedBufferSize, out var bitmapLength);

                            paddedCommandBuffer[padding + j] = (byte)smashed;

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var curCommand = ref paddedCommandBuffer[padding];
                            var failure = RespParserV3.FALSE;
                            var incomplete = RespParserV3.FALSE;
                            ref var curWrite = ref intoAsInt.EndRef();
                            var remainingItemsInArray = 1;
                            var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + raw.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                            Assert.True(RespParserV3.MaskBoolToBool(incomplete));

                            Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                            Assert.Equal(1, remainingItemsInArray);
                        }
                    }
                }

                // 4 digit of length
                {
                    var raw =
                        Encoding.UTF8.GetBytes(
                            "$1234\r\n" +
                            new string('c', 1234) +
                            "\r\n"
                        )
                        .AsSpan();
                    for (var j = 0; j < raw.Length; j++)
                    {
                        if (char.IsAsciiDigit((char)raw[j]))
                        {
                            continue;
                        }

                        if (raw[j] is (byte)'c')
                        {
                            continue;
                        }

                        for (var smashed = 0; smashed <= byte.MaxValue; smashed++)
                        {
                            if (raw[j] == (byte)smashed)
                            {
                                continue;
                            }

                            var paddedCommandBuffer = Pad(padding, raw, out var allocatedBufferSize, out var bitmapLength);

                            paddedCommandBuffer[padding + j] = (byte)smashed;

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var curCommand = ref paddedCommandBuffer[padding];
                            var failure = RespParserV3.FALSE;
                            var incomplete = RespParserV3.FALSE;
                            ref var curWrite = ref intoAsInt.EndRef();
                            var remainingItemsInArray = 1;
                            var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + raw.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                            Assert.True(RespParserV3.MaskBoolToBool(incomplete));

                            Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                            Assert.Equal(1, remainingItemsInArray);
                        }
                    }
                }

                // 5 digit of length
                {
                    var raw =
                        Encoding.UTF8.GetBytes(
                            "$11111\r\n" +
                            new string('d', 11111) +
                            "\r\n"
                        )
                        .AsSpan();
                    for (var j = 0; j < raw.Length; j++)
                    {
                        if (char.IsAsciiDigit((char)raw[j]))
                        {
                            continue;
                        }

                        if (raw[j] is (byte)'d')
                        {
                            continue;
                        }

                        for (var smashed = 0; smashed <= byte.MaxValue; smashed++)
                        {
                            if (raw[j] == (byte)smashed)
                            {
                                continue;
                            }

                            var paddedCommandBuffer = Pad(padding, raw, out var allocatedBufferSize, out var bitmapLength);

                            paddedCommandBuffer[padding + j] = (byte)smashed;

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var curCommand = ref paddedCommandBuffer[padding];
                            var failure = RespParserV3.FALSE;
                            var incomplete = RespParserV3.FALSE;
                            ref var curWrite = ref intoAsInt.EndRef();
                            var remainingItemsInArray = 1;
                            var (_, finalWrite) = RespParserV3.UnrollTakeCommandBulkString(ref paddedCommandBuffer.EndRef(allocatedBufferSize), ref paddedCommandBuffer.StartRef(), padding + raw.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref intoAsInt.EndRef(), ref curCommand, ref curWrite, ref remainingItemsInArray, ref ignored, ref ignored, ref failure, ref incomplete);

                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                            Assert.True(RespParserV3.MaskBoolToBool(incomplete));

                            Assert.True(Unsafe.AreSame(ref curWrite, ref finalWrite.Unwrap()));
                            Assert.Equal(1, remainingItemsInArray);
                        }
                    }
                }

                // 6+ digits is _a lot_ of data, so we'll just assume it's working
            }
        }

        private static Span<byte> Pad(int leadingPadding, ReadOnlySpan<byte> buffer, out int allocatedBufferSize, out int allocatedBitmapSize)
        {
            var totalLen = leadingPadding + buffer.Length;


            RespParserV3.CalculateByteBufferSizes(totalLen, out allocatedBufferSize, out var apparentSize, out allocatedBitmapSize);

            var padded = GC.AllocateUninitializedArray<byte>(allocatedBufferSize, pinned: true);
            padded.AsSpan().Fill((byte)'x');

            buffer.CopyTo(padded.AsSpan()[leadingPadding..]);

            return padded.AsSpan()[..apparentSize];
        }
    }
}
