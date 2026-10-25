namespace Envoy.ControlPlane.Tools;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args is ["--help"])
        {
            Console.WriteLine("Commands: upstream <sync|inspect|export>, bindings <generate|verify>, validation verify, patch <output-directory> [binding-lock]");
            return 0;
        }

        switch (args[0])
        {
            case "upstream":
                return await Upstream.RunAsync(args[1..]);
            case "bindings":
                return await Bindings.RunAsync(args[1..]);
            case "validation":
                return await Validation.RunAsync(args[1..]);
            case "patch":
                return PatchBindings.Run(args[1..]);
            default:
                Console.Error.WriteLine("Unknown command: " + args[0]);
                return 1;
        }
    }
}
