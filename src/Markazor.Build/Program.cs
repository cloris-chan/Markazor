using System.Text.Json;

namespace Markazor.Build;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2)
        {
            await Console.Error.WriteLineAsync("Usage: Markazor.Build <repository root> <staged web root>").ConfigureAwait(false);
            return 2;
        }

        try
        {
            int count = await StaticSiteGenerator.GenerateAsync(args[0], args[1]).ConfigureAwait(false);
            Console.WriteLine($"Markazor: generated {count} static reader pages.");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or JsonException or InvalidOperationException)
        {
            await Console.Error.WriteLineAsync("Markazor static generation failed: " + exception.Message).ConfigureAwait(false);
            return 1;
        }
    }
}
