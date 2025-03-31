using BenchmarkDotNet.Running;
namespace OptimizationExercise.SIMDRespParsing.Benchmarks
{
    internal sealed class Program
    {
        public static void Main(string[] args)
        {
            //foreach (var c in RespRequestBatchBenchmark.GetConfigs())
            //{
            //    Console.WriteLine($"[{DateTime.UtcNow:u}] {c}");
            //    var q = new RespRequestBatchBenchmark();
            //    q.Config = c;

            //    q.Setup();
            //}

            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
