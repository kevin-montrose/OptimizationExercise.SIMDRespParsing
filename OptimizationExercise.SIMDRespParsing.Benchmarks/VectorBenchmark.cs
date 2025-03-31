using BenchmarkDotNet.Attributes;
using System.Runtime.Intrinsics;

namespace OptimizationExercise.SIMDRespParsing.Benchmarks
{
    [DisassemblyDiagnoser]
    public class VectorBenchmark
    {
        [Benchmark]
        public void ShiftLeft512()
        {
            Span<byte> input = stackalloc byte[Vector512<byte>.Count * 128];
            Span<byte> output = stackalloc byte[Vector512<byte>.Count * 128];

            Random.Shared.NextBytes(input);

            for (var i = 0; i < input.Length; i += Vector512<byte>.Count)
            {
                var toShift = Vector512.LoadUnsafe(ref input[i]);
                var shifted = Vector512.ShiftLeft(toShift, 7);

                shifted.StoreUnsafe(ref output[i]);
            }
        }

        [Benchmark]
        public void ShiftLeft256()
        {
            Span<byte> input = stackalloc byte[Vector256<byte>.Count * 128];
            Span<byte> output = stackalloc byte[Vector256<byte>.Count * 128];

            Random.Shared.NextBytes(input);

            for (var i = 0; i < input.Length; i += Vector256<byte>.Count)
            {
                var toShift = Vector256.LoadUnsafe(ref input[i]);
                var shifted = Vector256.ShiftLeft(toShift, 7);

                shifted.StoreUnsafe(ref output[i]);
            }
        }

        [Benchmark]
        public void ShiftLeft128()
        {
            Span<byte> input = stackalloc byte[Vector128<byte>.Count * 128];
            Span<byte> output = stackalloc byte[Vector128<byte>.Count * 128];

            Random.Shared.NextBytes(input);

            for (var i = 0; i < input.Length; i += Vector128<byte>.Count)
            {
                var toShift = Vector128.LoadUnsafe(ref input[i]);
                var shifted = Vector128.ShiftLeft(toShift, 7);

                shifted.StoreUnsafe(ref output[i]);
            }
        }

        [Benchmark]
        public void Add512()
        {
            Span<byte> input = stackalloc byte[Vector512<byte>.Count * 128];
            Span<byte> output = stackalloc byte[Vector512<byte>.Count * 128];

            Random.Shared.NextBytes(input);

            for (var i = 0; i < input.Length; i += Vector512<byte>.Count)
            {
                var toShift = Vector512.LoadUnsafe(ref input[i]);
                // b = a + a = a * 2^1
                var b = Vector512.Add(toShift, toShift);
                // c = b + b = (a + a) + (a + a) = a * 2^2
                var c = Vector512.Add(b, b);
                // d = c + c = (b + b) + (b + b) = ((a + a) + (a + a)) + ((a + a) + (a + a)) = a * 2^3
                var d = Vector512.Add(c, c);
                // etc.
                var e = Vector512.Add(d, d);
                var f = Vector512.Add(e, e);
                var g = Vector512.Add(f, f);
                var shifted = Vector512.Add(g, g);

                shifted.StoreUnsafe(ref output[i]);
            }
        }

        [Benchmark]
        public void Add256()
        {
            Span<byte> input = stackalloc byte[Vector256<byte>.Count * 128];
            Span<byte> output = stackalloc byte[Vector256<byte>.Count * 128];

            Random.Shared.NextBytes(input);

            for (var i = 0; i < input.Length; i += Vector256<byte>.Count)
            {
                var toShift = Vector256.LoadUnsafe(ref input[i]);
                // b = a + a = a * 2^1
                var b = Vector256.Add(toShift, toShift);
                // c = b + b = (a + a) + (a + a) = a * 2^2
                var c = Vector256.Add(b, b);
                // d = c + c = (b + b) + (b + b) = ((a + a) + (a + a)) + ((a + a) + (a + a)) = a * 2^3
                var d = Vector256.Add(c, c);
                // etc.
                var e = Vector256.Add(d, d);
                var f = Vector256.Add(e, e);
                var g = Vector256.Add(f, f);
                var shifted = Vector256.Add(g, g);

                shifted.StoreUnsafe(ref output[i]);
            }
        }

        [Benchmark]
        public void Add128()
        {
            Span<byte> input = stackalloc byte[Vector128<byte>.Count * 128];
            Span<byte> output = stackalloc byte[Vector128<byte>.Count * 128];

            Random.Shared.NextBytes(input);

            for (var i = 0; i < input.Length; i += Vector128<byte>.Count)
            {
                var toShift = Vector128.LoadUnsafe(ref input[i]);
                // b = a + a = a * 2^1
                var b = Vector128.Add(toShift, toShift);
                // c = b + b = (a + a) + (a + a) = a * 2^2
                var c = Vector128.Add(b, b);
                // d = c + c = (b + b) + (b + b) = ((a + a) + (a + a)) + ((a + a) + (a + a)) = a * 2^3
                var d = Vector128.Add(c, c);
                // etc.
                var e = Vector128.Add(d, d);
                var f = Vector128.Add(e, e);
                var g = Vector128.Add(f, f);
                var shifted = Vector128.Add(g, g);

                shifted.StoreUnsafe(ref output[i]);
            }
        }

        [Benchmark]
        public void Mult512()
        {
            Span<byte> input = stackalloc byte[Vector512<byte>.Count * 128];
            Span<byte> output = stackalloc byte[Vector512<byte>.Count * 128];

            Random.Shared.NextBytes(input);

            var scaleBy = Vector512.Create<byte>(1 << 7);

            for (var i = 0; i < input.Length; i += Vector512<byte>.Count)
            {
                var toShift = Vector512.LoadUnsafe(ref input[i]);
                var shifted = Vector512.Multiply(toShift, scaleBy);

                shifted.StoreUnsafe(ref output[i]);
            }
        }

        [Benchmark]
        public void Mult256()
        {
            Span<byte> input = stackalloc byte[Vector256<byte>.Count * 128];
            Span<byte> output = stackalloc byte[Vector256<byte>.Count * 128];

            Random.Shared.NextBytes(input);

            var scaleBy = Vector256.Create<byte>(1 << 7);

            for (var i = 0; i < input.Length; i += Vector512<byte>.Count)
            {
                var toShift = Vector256.LoadUnsafe(ref input[i]);
                var shifted = Vector256.Multiply(toShift, scaleBy);

                shifted.StoreUnsafe(ref output[i]);
            }
        }

        [Benchmark]
        public void Mult128()
        {
            Span<byte> input = stackalloc byte[Vector128<byte>.Count * 128];
            Span<byte> output = stackalloc byte[Vector128<byte>.Count * 128];

            Random.Shared.NextBytes(input);

            var scaleBy = Vector128.Create<byte>(1 << 7);

            for (var i = 0; i < input.Length; i += Vector512<byte>.Count)
            {
                var toShift = Vector128.LoadUnsafe(ref input[i]);
                var shifted = Vector128.Multiply(toShift, scaleBy);

                shifted.StoreUnsafe(ref output[i]);
            }
        }
    }
}
