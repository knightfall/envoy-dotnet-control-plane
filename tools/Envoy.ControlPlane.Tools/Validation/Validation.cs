namespace Envoy.ControlPlane.Tools;

internal static class Validation
{
    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args is not ["verify"])
            {
                Console.Error.WriteLine("Usage: validation verify (run bindings generate first)");
                return 1;
            }

            await ValidationVerification.RunAsync();
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}
