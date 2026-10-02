using System.Collections.Generic;

namespace FolderDeck.App.Services;

public sealed record CommandLineOptions
{
    public const string WorkspaceSwitch = "--workspace";
    public const string LauncherSwitch = "--launcher";

    public Guid? WorkspaceId { get; private init; }

    public bool ForceLauncher { get; private init; }

    public string? Error { get; private init; }

    public static CommandLineOptions Parse(IReadOnlyList<string>? args)
    {
        Guid? workspaceId = null;
        var forceLauncher = false;
        string? error = null;

        for (var i = 0; i < (args?.Count ?? 0); i++)
        {
            var arg = args![i];

            if (Matches(arg, LauncherSwitch))
            {
                forceLauncher = true;
                continue;
            }

            if (Matches(arg, WorkspaceSwitch))
            {
                if (i + 1 >= args.Count)
                {
                    error = Combine(error, $"{WorkspaceSwitch} 뒤에 작업 관리 id 가 없다.");
                    break;
                }

                error = Combine(error, ReadGuid(args[++i], ref workspaceId));
                continue;
            }

            if (arg.StartsWith(WorkspaceSwitch + "=", StringComparison.OrdinalIgnoreCase))
            {
                error = Combine(error, ReadGuid(arg[(WorkspaceSwitch.Length + 1)..], ref workspaceId));
                continue;
            }

            error = Combine(error, $"알 수 없는 인자: {arg}");
        }

        return new CommandLineOptions
        {
            WorkspaceId = workspaceId,
            ForceLauncher = forceLauncher,
            Error = error,
        };
    }

    private static bool Matches(string arg, string name) =>
        string.Equals(arg, name, StringComparison.OrdinalIgnoreCase);

    private static string? ReadGuid(string text, ref Guid? target)
    {
        if (Guid.TryParse(text, out var parsed))
        {
            target = parsed;
            return null;
        }

        return $"작업 관리 id 로 읽을 수 없다: {text}";
    }

    private static string? Combine(string? existing, string? added) =>
        (existing, added) switch
        {
            (null, _) => added,
            (_, null) => existing,
            _ => $"{existing} {added}",
        };
}
