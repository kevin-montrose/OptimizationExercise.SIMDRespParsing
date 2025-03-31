using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OptimizationExercise.SIMDRespParsing.Tests
{
    public static class Extensions
    {
        public static ref byte StartRef(this Span<byte> buffer)
        => ref MemoryMarshal.GetReference(buffer);

        public static ref byte EndRef(this Span<byte> buffer, int? allocatedSize = null)
        => ref Unsafe.Add(ref buffer.StartRef(), allocatedSize ?? buffer.Length);

        public static ref int StartRef(this Span<int> buffer)
        => ref MemoryMarshal.GetReference(buffer);

        public static ref int EndRef(this Span<int> buffer, int? allocatedSize = null)
        => ref Unsafe.Add(ref buffer.StartRef(), allocatedSize ?? buffer.Length);
    }
}
