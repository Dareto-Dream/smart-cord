using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartCord.Ui;

/// <summary>
/// Writes user edits to <c>appsettings.local.json</c> next to the executable. The
/// base <c>appsettings.json</c> (with the preset library) is never touched — the
/// local file just layers on top, exactly like the configuration builder stacks
/// them, and <see cref="SettingsProvider"/>'s file watcher picks the change up live.
/// </summary>
public static class SettingsWriter
{
    private static readonly string LocalPath =
        Path.Combine(AppContext.BaseDirectory, "appsettings.local.json");

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static void Patch(Action<JsonObject> mutate)
    {
        JsonObject root;
        try
        {
            root = File.Exists(LocalPath)
                ? JsonNode.Parse(File.ReadAllText(LocalPath)) as JsonObject ?? new JsonObject()
                : new JsonObject();
        }
        catch
        {
            root = new JsonObject();
        }

        mutate(root);

        var tmp = LocalPath + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(WriteOptions));
        File.Move(tmp, LocalPath, overwrite: true);
    }

    public static JsonObject Section(this JsonObject root, string name)
    {
        if (root[name] is JsonObject existing)
        {
            return existing;
        }
        var created = new JsonObject();
        root[name] = created;
        return created;
    }

    public static string LocalFilePath => LocalPath;
}
