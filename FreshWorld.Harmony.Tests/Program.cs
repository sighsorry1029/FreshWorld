using System;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0] == "--native-save-patch")
            {
                NativeSavePatchRegression.Run(args[1], args[2]);
                return 0;
            }
            HarmonyDiscoveryRegression.Run();
            System.Console.WriteLine("PASS installed Harmony discovers both terrain hooks without invoking terrain-edit helpers before a world loads");
            var commandChecks = CommandPatchRegression.Run();
            var configChecks = ConfigBindingRegression.Run();
            System.Console.WriteLine($"All {1 + commandChecks + configChecks} installed-Harmony/BepInEx regression checks passed.");
            return 0;
        }
        catch (Exception error) { System.Console.Error.WriteLine(error); return 1; }
    }
}
