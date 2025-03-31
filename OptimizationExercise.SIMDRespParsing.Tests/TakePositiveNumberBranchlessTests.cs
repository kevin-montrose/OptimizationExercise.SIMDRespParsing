using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace OptimizationExercise.SIMDRespParsing.Tests
{
    public class TakePositiveNumberBranchlessTests
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
                var ignored = 0;

                // 1 digit
                {
                    var paddedCommandBuffer = Pad(padding, "1\r\n"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    ref var end = ref RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 3, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.False(RespParserV3.MaskBoolToBool(failure));
                    Assert.False(RespParserV3.MaskBoolToBool(incomplete));
                    Assert.Equal(1, parsed);
                    Assert.Equal(padding + 3, Unsafe.ByteOffset(ref paddedCommandBuffer[0], ref end));
                }

                // 2 digits
                {
                    var paddedCommandBuffer = Pad(padding, "12\r\n"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    ref var end = ref RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 4, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.False(RespParserV3.MaskBoolToBool(failure));
                    Assert.False(RespParserV3.MaskBoolToBool(incomplete));
                    Assert.Equal(12, parsed);
                    Assert.Equal(padding + 4, Unsafe.ByteOffset(ref paddedCommandBuffer[0], ref end));
                }

                // 3 digits
                {
                    var paddedCommandBuffer = Pad(padding, "123\r\n"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    ref var end = ref RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 5, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.False(RespParserV3.MaskBoolToBool(failure));
                    Assert.False(RespParserV3.MaskBoolToBool(incomplete));
                    Assert.Equal(123, parsed);
                    Assert.Equal(padding + 5, Unsafe.ByteOffset(ref paddedCommandBuffer[0], ref end));
                }

                // 4 digits
                {
                    var paddedCommandBuffer = Pad(padding, "1234\r\n"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    ref var end = ref RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 6, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.False(RespParserV3.MaskBoolToBool(failure));
                    Assert.False(RespParserV3.MaskBoolToBool(incomplete));
                    Assert.Equal(1234, parsed);
                    Assert.Equal(padding + 6, Unsafe.ByteOffset(ref paddedCommandBuffer[0], ref end));
                }

                // 5 digits
                {
                    var paddedCommandBuffer = Pad(padding, "12345\r\n"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    ref var end = ref RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 7, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.False(RespParserV3.MaskBoolToBool(failure));
                    Assert.False(RespParserV3.MaskBoolToBool(incomplete));
                    Assert.Equal(12345, parsed);
                    Assert.Equal(padding + 7, Unsafe.ByteOffset(ref paddedCommandBuffer[0], ref end));
                }

                // 6 digits
                {
                    var paddedCommandBuffer = Pad(padding, "123456\r\n"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    ref var end = ref RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 8, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.False(RespParserV3.MaskBoolToBool(failure));
                    Assert.False(RespParserV3.MaskBoolToBool(incomplete));
                    Assert.Equal(123456, parsed);
                    Assert.Equal(padding + 8, Unsafe.ByteOffset(ref paddedCommandBuffer[0], ref end));
                }

                // 7 digits
                {
                    var paddedCommandBuffer = Pad(padding, "1234567\r\n"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    ref var end = ref RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 9, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.False(RespParserV3.MaskBoolToBool(failure));
                    Assert.False(RespParserV3.MaskBoolToBool(incomplete));
                    Assert.Equal(1234567, parsed);
                    Assert.Equal(padding + 9, Unsafe.ByteOffset(ref paddedCommandBuffer[0], ref end));
                }

                // 8 digits
                {
                    var paddedCommandBuffer = Pad(padding, "12345678\r\n"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    ref var end = ref RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 10, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.False(RespParserV3.MaskBoolToBool(failure));
                    Assert.False(RespParserV3.MaskBoolToBool(incomplete));
                    Assert.Equal(12345678, parsed);
                    Assert.Equal(padding + 10, Unsafe.ByteOffset(ref paddedCommandBuffer[0], ref end));
                }

                // 9 digits
                {
                    var paddedCommandBuffer = Pad(padding, "123456789\r\n"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    ref var end = ref RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 11, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.False(RespParserV3.MaskBoolToBool(failure));
                    Assert.False(RespParserV3.MaskBoolToBool(incomplete));
                    Assert.Equal(123456789, parsed);
                    Assert.Equal(padding + 11, Unsafe.ByteOffset(ref paddedCommandBuffer[0], ref end));
                }

                // 10 digits
                {
                    var paddedCommandBuffer = Pad(padding, "2147483647\r\n"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    ref var end = ref RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 12, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.False(RespParserV3.MaskBoolToBool(failure));
                    Assert.False(RespParserV3.MaskBoolToBool(incomplete));
                    Assert.Equal(int.MaxValue, parsed);
                    Assert.Equal(padding + 12, Unsafe.ByteOffset(ref paddedCommandBuffer[0], ref end));
                }
            }
        }

        [Fact]
        public void Incomplete()
        {
            for (var padding = 0; padding <= Vector512<byte>.Count; padding++)
            {
                Test(padding);
            }

            static void Test(int padding)
            {
                var ignored = 0;

                // 1 digit, missing \n
                {
                    var paddedCommandBuffer = Pad(padding, "1\r"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 2, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));
                }

                // 1 digit, missing \r\n
                {
                    var paddedCommandBuffer = Pad(padding, "1"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 1, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));
                }

                // 2 digit, missing \n
                {
                    var paddedCommandBuffer = Pad(padding, "12\r"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 3, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));
                }

                // 2 digit, missing \r\n
                {
                    var paddedCommandBuffer = Pad(padding, "12"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 2, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));
                }

                // 3 digit, missing \n
                {
                    var paddedCommandBuffer = Pad(padding, "123\r"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 4, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));
                }

                // 3 digit, missing \r\n
                {
                    var paddedCommandBuffer = Pad(padding, "123"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 3, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));
                }

                // 4 digit, missing \n
                {
                    var paddedCommandBuffer = Pad(padding, "1234\r"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 5, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));
                }

                // 4 digit, missing \r\n
                {
                    var paddedCommandBuffer = Pad(padding, "1234"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 4, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));
                }

                // 5 digit, missing \n
                {
                    var paddedCommandBuffer = Pad(padding, "12345\r"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 6, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));
                }

                // 5 digit, missing \r\n
                {
                    var paddedCommandBuffer = Pad(padding, "12345"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 5, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));
                }

                // 6 digit, missing \n
                {
                    var paddedCommandBuffer = Pad(padding, "123456\r"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 7, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));
                }

                // 6 digit, missing \r\n
                {
                    var paddedCommandBuffer = Pad(padding, "123456"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 6, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));
                }

                // 7 digit, missing \n
                {
                    var paddedCommandBuffer = Pad(padding, "1234567\r"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 8, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));
                }

                // 7 digit, missing \r\n
                {
                    var paddedCommandBuffer = Pad(padding, "1234567"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 7, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));
                }

                // 8 digit, missing \n
                {
                    var paddedCommandBuffer = Pad(padding, "12345678\r"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 9, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));
                }

                // 8 digit, missing \r\n
                {
                    var paddedCommandBuffer = Pad(padding, "12345678"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 8, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));
                }

                // 9 digit, missing \n
                {
                    var paddedCommandBuffer = Pad(padding, "123456789\r"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 10, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));
                }

                // 9 digit, missing \r\n
                {
                    var paddedCommandBuffer = Pad(padding, "123456789"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 9, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));
                }

                // 10 digit, missing \n
                {
                    var paddedCommandBuffer = Pad(padding, "1234567890\r"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 11, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));
                }

                // 10 digit, missing \r\n
                {
                    var paddedCommandBuffer = Pad(padding, "1234567890"u8, out var bufferAllocatedSize, out var bitmapLength);

                    Span<byte> digitsBitmap = stackalloc byte[bitmapLength];

                    RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                    ref var start = ref paddedCommandBuffer[padding];
                    var failure = RespParserV3.FALSE;
                    var incomplete = RespParserV3.FALSE;
                    _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + 10, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                    Assert.True(RespParserV3.MaskBoolToBool(failure));
                    Assert.True(RespParserV3.MaskBoolToBool(incomplete));
                }
            }
        }

        [Fact]
        public void Malformed()
        {
            for (var padding = 0; padding <= Vector512<byte>.Count; padding++)
            {
                Test(padding);
            }

            static void Test(int padding)
            {
                var ignored = 0;

                // 1 digit
                {
                    var unpadded = "1\r\n"u8;

                    for (var i = 0; i < unpadded.Length; i++)
                    {
                        for (var smashed = 0; smashed <= byte.MaxValue; smashed++)
                        {
                            if (unpadded[i] == (byte)smashed)
                            {
                                continue;
                            }

                            if (unpadded[i] is >= (byte)'0' and <= (byte)'9' && (byte)smashed is >= (byte)'0' and <= (byte)'9')
                            {
                                continue;
                            }

                            var corrupted = unpadded.ToArray();
                            corrupted[i] = (byte)smashed;

                            var lastNumber = corrupted.AsSpan().IndexOfAnyExceptInRange((byte)'0', (byte)'9');
                            if (lastNumber != -1 && (corrupted.Length - lastNumber) < 2)
                            {
                                // we won't consider this malformed until we get another byte, so skip it
                                continue;
                            }

                            var paddedCommandBuffer = Pad(padding, corrupted, out var bufferAllocatedSize, out var bitmapLength);

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var start = ref paddedCommandBuffer[padding];
                            var failure = RespParserV3.FALSE;
                            var incomplete = RespParserV3.FALSE;
                            _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + corrupted.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                            Assert.False(RespParserV3.MaskBoolToBool(incomplete));
                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                        }
                    }
                }

                // 2 digit
                {
                    var unpadded = "12\r\n"u8;

                    for (var i = 0; i < unpadded.Length; i++)
                    {
                        for (var smashed = 0; smashed <= byte.MaxValue; smashed++)
                        {
                            if (unpadded[i] == (byte)smashed)
                            {
                                continue;
                            }

                            if (unpadded[i] is >= (byte)'0' and <= (byte)'9' && (byte)smashed is >= (byte)'0' and <= (byte)'9')
                            {
                                continue;
                            }

                            var corrupted = unpadded.ToArray();
                            corrupted[i] = (byte)smashed;

                            var lastNumber = corrupted.AsSpan().IndexOfAnyExceptInRange((byte)'0', (byte)'9');
                            if (lastNumber != -1 && (corrupted.Length - lastNumber) < 2)
                            {
                                // we won't consider this malformed until we get another byte, so skip it
                                continue;
                            }

                            var paddedCommandBuffer = Pad(padding, corrupted, out var bufferAllocatedSize, out var bitmapLength);

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var start = ref paddedCommandBuffer[padding];
                            var failure = RespParserV3.FALSE;
                            var incomplete = RespParserV3.FALSE;
                            _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + corrupted.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                            Assert.False(RespParserV3.MaskBoolToBool(incomplete));
                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                        }
                    }
                }

                // 3 digit
                {
                    var unpadded = "123\r\n"u8;

                    for (var i = 0; i < unpadded.Length; i++)
                    {
                        for (var smashed = 0; smashed <= byte.MaxValue; smashed++)
                        {
                            if (unpadded[i] == (byte)smashed)
                            {
                                continue;
                            }

                            if (unpadded[i] is >= (byte)'0' and <= (byte)'9' && (byte)smashed is >= (byte)'0' and <= (byte)'9')
                            {
                                continue;
                            }

                            var corrupted = unpadded.ToArray();
                            corrupted[i] = (byte)smashed;

                            var lastNumber = corrupted.AsSpan().IndexOfAnyExceptInRange((byte)'0', (byte)'9');
                            if (lastNumber != -1 && (corrupted.Length - lastNumber) < 2)
                            {
                                // we won't consider this malformed until we get another byte, so skip it
                                continue;
                            }

                            var paddedCommandBuffer = Pad(padding, corrupted, out var bufferAllocatedSize, out var bitmapLength);

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var start = ref paddedCommandBuffer[padding];
                            var failure = RespParserV3.FALSE;
                            var incomplete = RespParserV3.FALSE;
                            _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + corrupted.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                            Assert.False(RespParserV3.MaskBoolToBool(incomplete));
                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                        }
                    }
                }

                // 4 digit
                {
                    var unpadded = "1234\r\n"u8;

                    for (var i = 0; i < unpadded.Length; i++)
                    {
                        for (var smashed = 0; smashed <= byte.MaxValue; smashed++)
                        {
                            if (unpadded[i] == (byte)smashed)
                            {
                                continue;
                            }

                            if (unpadded[i] is >= (byte)'0' and <= (byte)'9' && (byte)smashed is >= (byte)'0' and <= (byte)'9')
                            {
                                continue;
                            }

                            var corrupted = unpadded.ToArray();
                            corrupted[i] = (byte)smashed;

                            var lastNumber = corrupted.AsSpan().IndexOfAnyExceptInRange((byte)'0', (byte)'9');
                            if (lastNumber != -1 && (corrupted.Length - lastNumber) < 2)
                            {
                                // we won't consider this malformed until we get another byte, so skip it
                                continue;
                            }

                            var paddedCommandBuffer = Pad(padding, corrupted, out var bufferAllocatedSize, out var bitmapLength);

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var start = ref paddedCommandBuffer[padding];
                            var failure = RespParserV3.FALSE;
                            var incomplete = RespParserV3.FALSE;
                            _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + corrupted.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                            Assert.False(RespParserV3.MaskBoolToBool(incomplete));
                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                        }
                    }
                }

                // 5 digit
                {
                    var unpadded = "12345\r\n"u8;

                    for (var i = 0; i < unpadded.Length; i++)
                    {
                        for (var smashed = 0; smashed <= byte.MaxValue; smashed++)
                        {
                            if (unpadded[i] == (byte)smashed)
                            {
                                continue;
                            }

                            if (unpadded[i] is >= (byte)'0' and <= (byte)'9' && (byte)smashed is >= (byte)'0' and <= (byte)'9')
                            {
                                continue;
                            }

                            var corrupted = unpadded.ToArray();
                            corrupted[i] = (byte)smashed;

                            var lastNumber = corrupted.AsSpan().IndexOfAnyExceptInRange((byte)'0', (byte)'9');
                            if (lastNumber != -1 && (corrupted.Length - lastNumber) < 2)
                            {
                                // we won't consider this malformed until we get another byte, so skip it
                                continue;
                            }

                            var paddedCommandBuffer = Pad(padding, corrupted, out var bufferAllocatedSize, out var bitmapLength);

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var start = ref paddedCommandBuffer[padding];
                            var failure = RespParserV3.FALSE;
                            var incomplete = RespParserV3.FALSE;
                            _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + corrupted.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                            Assert.False(RespParserV3.MaskBoolToBool(incomplete));
                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                        }
                    }
                }

                // 6 digit
                {
                    var unpadded = "123456\r\n"u8;

                    for (var i = 0; i < unpadded.Length; i++)
                    {
                        for (var smashed = 0; smashed <= byte.MaxValue; smashed++)
                        {
                            if (unpadded[i] == (byte)smashed)
                            {
                                continue;
                            }

                            if (unpadded[i] is >= (byte)'0' and <= (byte)'9' && (byte)smashed is >= (byte)'0' and <= (byte)'9')
                            {
                                continue;
                            }

                            var corrupted = unpadded.ToArray();
                            corrupted[i] = (byte)smashed;

                            var lastNumber = corrupted.AsSpan().IndexOfAnyExceptInRange((byte)'0', (byte)'9');
                            if (lastNumber != -1 && (corrupted.Length - lastNumber) < 2)
                            {
                                // we won't consider this malformed until we get another byte, so skip it
                                continue;
                            }

                            var paddedCommandBuffer = Pad(padding, corrupted, out var bufferAllocatedSize, out var bitmapLength);

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var start = ref paddedCommandBuffer[padding];
                            var failure = RespParserV3.FALSE;
                            var incomplete = RespParserV3.FALSE;
                            _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + corrupted.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                            Assert.False(RespParserV3.MaskBoolToBool(incomplete));
                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                        }
                    }
                }

                // 7 digit
                {
                    var unpadded = "1234567\r\n"u8;

                    for (var i = 0; i < unpadded.Length; i++)
                    {
                        for (var smashed = 0; smashed <= byte.MaxValue; smashed++)
                        {
                            if (unpadded[i] == (byte)smashed)
                            {
                                continue;
                            }

                            if (unpadded[i] is >= (byte)'0' and <= (byte)'9' && (byte)smashed is >= (byte)'0' and <= (byte)'9')
                            {
                                continue;
                            }

                            var corrupted = unpadded.ToArray();
                            corrupted[i] = (byte)smashed;

                            var lastNumber = corrupted.AsSpan().IndexOfAnyExceptInRange((byte)'0', (byte)'9');
                            if (lastNumber != -1 && (corrupted.Length - lastNumber) < 2)
                            {
                                // we won't consider this malformed until we get another byte, so skip it
                                continue;
                            }

                            var paddedCommandBuffer = Pad(padding, corrupted, out var bufferAllocatedSize, out var bitmapLength);

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var start = ref paddedCommandBuffer[padding];
                            var failure = RespParserV3.FALSE;
                            var incomplete = RespParserV3.FALSE;
                            _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + corrupted.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                            Assert.False(RespParserV3.MaskBoolToBool(incomplete));
                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                        }
                    }
                }

                // 8 digit
                {
                    var unpadded = "12345678\r\n"u8;

                    for (var i = 0; i < unpadded.Length; i++)
                    {
                        for (var smashed = 0; smashed <= byte.MaxValue; smashed++)
                        {
                            if (unpadded[i] == (byte)smashed)
                            {
                                continue;
                            }

                            if (unpadded[i] is >= (byte)'0' and <= (byte)'9' && (byte)smashed is >= (byte)'0' and <= (byte)'9')
                            {
                                continue;
                            }

                            var corrupted = unpadded.ToArray();
                            corrupted[i] = (byte)smashed;

                            var lastNumber = corrupted.AsSpan().IndexOfAnyExceptInRange((byte)'0', (byte)'9');
                            if (lastNumber != -1 && (corrupted.Length - lastNumber) < 2)
                            {
                                // we won't consider this malformed until we get another byte, so skip it
                                continue;
                            }

                            var paddedCommandBuffer = Pad(padding, corrupted, out var bufferAllocatedSize, out var bitmapLength);

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var start = ref paddedCommandBuffer[padding];
                            var failure = RespParserV3.FALSE;
                            var incomplete = RespParserV3.FALSE;
                            _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + corrupted.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                            Assert.False(RespParserV3.MaskBoolToBool(incomplete));
                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                        }
                    }
                }

                // 9 digit
                {
                    var unpadded = "123456789\r\n"u8;

                    for (var i = 0; i < unpadded.Length; i++)
                    {
                        for (var smashed = 0; smashed <= byte.MaxValue; smashed++)
                        {
                            if (unpadded[i] == (byte)smashed)
                            {
                                continue;
                            }

                            if (unpadded[i] is >= (byte)'0' and <= (byte)'9' && (byte)smashed is >= (byte)'0' and <= (byte)'9')
                            {
                                continue;
                            }

                            var corrupted = unpadded.ToArray();
                            corrupted[i] = (byte)smashed;

                            var lastNumber = corrupted.AsSpan().IndexOfAnyExceptInRange((byte)'0', (byte)'9');
                            if (lastNumber != -1 && (corrupted.Length - lastNumber) < 2)
                            {
                                // we won't consider this malformed until we get another byte, so skip it
                                continue;
                            }

                            var paddedCommandBuffer = Pad(padding, corrupted, out var bufferAllocatedSize, out var bitmapLength);

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var start = ref paddedCommandBuffer[padding];
                            var failure = RespParserV3.FALSE;
                            var incomplete = RespParserV3.FALSE;
                            _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + corrupted.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                            Assert.False(RespParserV3.MaskBoolToBool(incomplete));
                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                        }
                    }
                }

                // 10 digit
                {
                    var unpadded = "1234567890\r\n"u8;

                    for (var i = 0; i < unpadded.Length; i++)
                    {
                        for (var smashed = 0; smashed <= byte.MaxValue; smashed++)
                        {
                            if (unpadded[i] == (byte)smashed)
                            {
                                continue;
                            }

                            if (unpadded[i] is >= (byte)'0' and <= (byte)'9' && (byte)smashed is >= (byte)'0' and <= (byte)'9')
                            {
                                continue;
                            }

                            var corrupted = unpadded.ToArray();
                            corrupted[i] = (byte)smashed;

                            var digitsInARow = 0;
                            for (var j = 0; j < corrupted.Length; j++)
                            {
                                if (corrupted[j] is >= (byte)'0' and <= (byte)'9')
                                {
                                    digitsInARow++;
                                }
                                else
                                {
                                    break;
                                }
                            }

                            if (digitsInARow > 10)
                            {
                                continue;
                            }

                            var paddedCommandBuffer = Pad(padding, corrupted, out var bufferAllocatedSize, out var bitmapLength);

                            Span<byte> digitsBitmap = new byte[bitmapLength];

                            RespParserV3.ScanForSigils(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), paddedCommandBuffer.Length);

                            ref var start = ref paddedCommandBuffer[padding];
                            var failure = RespParserV3.FALSE;
                            var incomplete = RespParserV3.FALSE;
                            _ = RespParserV3.TakePositiveNumberBranchless(ref paddedCommandBuffer.EndRef(bufferAllocatedSize), ref paddedCommandBuffer.StartRef(), padding + corrupted.Length, ref digitsBitmap.EndRef(), ref digitsBitmap.StartRef(), ref start, ref ignored, ref failure, ref incomplete, out var parsed);

                            Assert.False(RespParserV3.MaskBoolToBool(incomplete));
                            Assert.True(RespParserV3.MaskBoolToBool(failure));
                        }
                    }
                }
            }
        }

        private static Span<byte> Pad(int leadingPadding, ReadOnlySpan<byte> buffer, out int bufferAllocatedSize, out int bitmapAllocatedSize)
        {
            var totalLen = leadingPadding + buffer.Length;

            RespParserV3.CalculateByteBufferSizes(totalLen, out bufferAllocatedSize, out var bufferAvailableSize, out bitmapAllocatedSize);

            var padded = GC.AllocateUninitializedArray<byte>(bufferAllocatedSize, pinned: true);
            padded.AsSpan().Fill((byte)'x');

            buffer.CopyTo(padded.AsSpan()[leadingPadding..]);

            return padded[..bufferAvailableSize];
        }
    }
}
