using System;

internal static class Program
{
    private static int Main()
    {
        try
        {
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
