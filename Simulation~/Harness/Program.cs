using System;

namespace Elemento.Simulation
{
    public static class Program
    {
        public static int Main(string[] inArgs)
        {
            try
            {
                var options = SimulationOptions.Parse(inArgs);
                new SimulationRunner(options).Run();
                return 0;
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine(exception.Message);
                Console.Error.WriteLine("Usage: --games N --seed S --cap N --p1 hard|medium|easy|random|strong|strong-medium|strong-easy --p2 hard|medium|easy|random|strong|strong-medium|strong-easy");
                return 2;
            }
        }
    }

    internal sealed class SimulationOptions
    {
        public int Games = 1000;
        public int Seed = 1;
        public int Cap = 400;
        public string PlayerOne = "hard";
        public string PlayerTwo = "hard";

        public static SimulationOptions Parse(string[] inArgs)
        {
            var options = new SimulationOptions();
            for (int i = 0; i < inArgs.Length; i += 2)
            {
                if (i + 1 >= inArgs.Length)
                    throw new ArgumentException("Every option requires a value.");

                string name = inArgs[i];
                string value = inArgs[i + 1];
                if (name == "--games")
                {
                    if (!int.TryParse(value, out options.Games) || options.Games <= 0)
                        throw new ArgumentException("--games must be a positive integer.");
                }
                else if (name == "--seed")
                {
                    if (!int.TryParse(value, out options.Seed))
                        throw new ArgumentException("--seed must be an integer.");
                }
                else if (name == "--cap")
                {
                    if (!int.TryParse(value, out options.Cap) || options.Cap <= 0)
                        throw new ArgumentException("--cap must be a positive integer.");
                }
                else if (name == "--p1")
                {
                    options.PlayerOne = ParseAi(value);
                }
                else if (name == "--p2")
                {
                    options.PlayerTwo = ParseAi(value);
                }
                else
                {
                    throw new ArgumentException("Unknown option: " + name);
                }
            }

            return options;
        }

        private static string ParseAi(string inValue)
        {
            string value = inValue.ToLowerInvariant();
            if (value != "hard" && value != "medium" && value != "easy" && value != "random"
                && value != "strong" && value != "strong-medium" && value != "strong-easy")
                throw new ArgumentException("Unknown AI: " + inValue);
            return value;
        }
    }
}
