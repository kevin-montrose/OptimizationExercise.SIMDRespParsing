using System.Buffers.Binary;
using System.Numerics;

namespace OptimizationExercise.SIMDRespParsing.CodeGen
{
    internal static class Program
    {
        public static void Main(string[] args)
        {
            //var x = TryParseCommand.GenerateHash();

            //var x = UnconditionalParseRespCommand.GenerateAddXorRotate();

            BitFuncParseRespCommand.Generate();

            //Console.WriteLine(x);
        }
    }
}
