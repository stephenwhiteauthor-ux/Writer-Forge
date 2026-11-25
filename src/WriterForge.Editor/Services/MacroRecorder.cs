using System.IO;
using System.Text.Json;

namespace WriterForge.Editor.Services;

/// <summary>A single recorded action: a name plus an optional argument.</summary>
public sealed record MacroAction(string Name, string? Arg = null);

/// <summary>
/// Records high-level editor commands (toolbar actions, typography tools)
/// while recording is active. Actions can be saved/loaded as .wmacro JSON
/// files and replayed via <see cref="Controls.EditorControl.PlayMacro"/>.
/// </summary>
public static class MacroRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static bool IsRecording { get; private set; }

    /// <summary>Actions captured in the current recording session.</summary>
    public static List<MacroAction> Actions { get; } = new();

    public static void Start()
    {
        Actions.Clear();
        IsRecording = true;
    }

    public static List<MacroAction> Stop()
    {
        IsRecording = false;
        return Actions.ToList();
    }

    public static void Cancel() => IsRecording = false;

    /// <summary>Record an action; ignored when not recording.</summary>
    public static void Record(string name, string? arg = null)
    {
        if (IsRecording)
            Actions.Add(new MacroAction(name, arg));
    }

    public static void Save(string path, string name, IReadOnlyList<MacroAction> actions)
    {
        var payload = new MacroFile(name, actions.ToList());
        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions));
    }

    public static (string Name, List<MacroAction> Actions)? Load(string path)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<MacroFile>(File.ReadAllText(path));
            return payload is null ? null : (payload.Name, payload.Actions);
        }
        catch
        {
            return null;
        }
    }

    private sealed record MacroFile(string Name, List<MacroAction> Actions);
}
