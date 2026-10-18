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
                if (options.Online)
                    new OnlineSimulationRunner(options).RunAsync().GetAwaiter().GetResult();
                else
                    new SimulationRunner(options).Run();
                return 0;
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine(exception.Message);
                Console.Error.WriteLine("Usage: --games N --seed S --cap N --p1 hard|medium|easy|random|strong|strong-hard|strong-medium|strong-easy --p2 hard|medium|easy|random|strong|strong-hard|strong-medium|strong-easy");
                Console.Error.WriteLine("Online: online [--transport] --games N --seed S --cap N");
                return 2;
            }
            catch (InvalidOperationException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
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
        public bool Online;
        public bool Transport;

        public static SimulationOptions Parse(string[] inArgs)
        {
            var options = new SimulationOptions();
            int start = 0;
            if (inArgs.Length > 0 && inArgs[0] == "online")
            {
                options.Online = true;
                options.Cap = 24;
                start = 1;
            }
            for (int i = start; i < inArgs.Length; i += 2)
            {
                if (options.Online && inArgs[i] == "--transport")
                {
                    options.Transport = true;
                    --i;
                    continue;
                }
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
                && value != "strong" && value != "strong-hard"
                && value != "strong-medium" && value != "strong-easy")
                throw new ArgumentException("Unknown AI: " + inValue);
            return value;
        }
    }
}
