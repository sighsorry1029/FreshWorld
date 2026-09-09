using System;

namespace FreshWorld.Commands;

internal enum FreshWorldCommandAction { Run, Status }

internal static class CommandSyntax
{
    public const string Name = "freshworld";
    public const string Usage = "freshworld — run the host cfg; freshworld status — show status.";
    public const int MaximumLength = 64;

    // Match the entire command token. Invalid arguments to our command must still be intercepted
    // before the game's general command interpreter can lose the authenticated remote origin.
    public static bool IsFreshWorld(string? line)
    {
        if (line == null || line.Length < Name.Length ||
            !line.StartsWith(Name, StringComparison.OrdinalIgnoreCase)) return false;
        return line.Length == Name.Length || char.IsWhiteSpace(line[Name.Length]);
    }

    public static bool TryParse(string? line, out FreshWorldCommandAction action)
    {
        action = FreshWorldCommandAction.Run;
        if (!IsFreshWorld(line) || line!.Length > MaximumLength) return false;
        // Only ordinary spaces delimit arguments. Control characters and shell-like separators
        // never become commands, cfg overrides, or payloads for another interpreter.
        foreach (var character in line)
            if (!(character == ' ' || character >= 'a' && character <= 'z' ||
                  character >= 'A' && character <= 'Z')) return false;
        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return true;
        if (parts.Length != 2) return false;
        if (parts[1].Equals("status", StringComparison.OrdinalIgnoreCase)) action = FreshWorldCommandAction.Status;
        else return false;
        return true;
    }
}
