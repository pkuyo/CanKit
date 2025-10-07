using System;
using System.Linq;
using CanKit.Core.Endpoints;

namespace CanKit.Sample.ListEndpoints
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            // Usage: ListEndpoints [--scheme socketcan] [--scheme virtual] [...]
            var schemes = args
                .Where((v, i) => string.Equals(v, "--scheme", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                .Select((v, i) => args[Array.IndexOf(args, v) + 1])
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();

            var items = (schemes.Length == 0)
                ? BusEndpointEntry.Enumerate()
                : BusEndpointEntry.Enumerate(schemes);

            Console.WriteLine("Discovered Endpoints:");
            foreach (var ep in items)
            {
                Console.Write("- ");
                Console.Write(ep.Endpoint);
                if (!string.IsNullOrWhiteSpace(ep.Title))
                {
                    Console.Write("  ");
                    Console.Write(ep.Title);
                }
                if (ep.Meta != null && ep.Meta.Count > 0)
                {
                    Console.Write("  [");
                    Console.Write(string.Join(", ", ep.Meta.Select(kv => kv.Key + "=" + kv.Value)));
                    Console.Write("]");
                }
                Console.WriteLine();
            }

            if (schemes.Length == 0)
            {
                Console.WriteLine("Hint: pass --scheme <name> to filter (e.g., socketcan, virtual, pcan, kvaser, zlg)");
            }
        }
    }
}

