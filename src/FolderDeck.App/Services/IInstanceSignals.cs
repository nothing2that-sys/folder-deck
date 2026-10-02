using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FolderDeck.App.Services;

public interface IInstanceSignals
{

    IReadOnlyCollection<Guid> WhichAreOpen(IEnumerable<Guid> candidates);

    bool TrySendActivate(Guid workspaceId);
}

public sealed class InstanceSignals : IInstanceSignals
{
    private const string PipeRoot = @"\\.\pipe\";

    public IReadOnlyCollection<Guid> WhichAreOpen(IEnumerable<Guid> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var ids = candidates.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var live = LivePipeNames();
        return live.Count == 0
            ? []
            : [.. ids.Where(id => live.Contains(InstanceNames.WorkspacePipe(id)))];
    }

    public bool TrySendActivate(Guid workspaceId) =>
        ActivationSignal.TrySend(InstanceNames.WorkspacePipe(workspaceId));

    public static bool IsLive(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        return LivePipeNames().Contains(pipeName);
    }

    private static HashSet<string> LivePipeNames()
    {
        try
        {
            return new HashSet<string>(
                Directory.EnumerateFiles(PipeRoot).Select(Path.GetFileName)!,
                StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
