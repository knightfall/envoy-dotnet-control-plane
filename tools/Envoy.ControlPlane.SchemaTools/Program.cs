namespace Envoy.ControlPlane.Tools;

internal static class Program
{
    internal static int Main(string[] args)
    {
        try
        {
            if (args is ["overlay", var original, var migrated, var output])
            {
                OverlayTransform.Create(original, migrated, output);
                return 0;
            }
            if (args is ["inventory", var inventory])
            {
                ValidationInventory.Write(inventory);
                return 0;
            }
            if (args is ["rules-check", var originalRules, var manifest])
            {
                RecognizedRules.Verify(originalRules, manifest);
                return 0;
            }
            if (args is ["snapshot", var source, var translated, var snapshot])
            {
                var report = DescriptorSnapshot.Create(OverlayTransform.Read(source), OverlayTransform.Read(translated));
                File.WriteAllText(snapshot, report.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
                return 0;
            }

            Console.Error.WriteLine("Usage: overlay <original-descriptors> <migrated-descriptors> <output-descriptors> | inventory <output-json> | rules-check <original-descriptors> <manifest> | snapshot <original-descriptors> <derived-descriptors> <output-json>");
            return 1;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}
