using System.Text.Json;
using System.Text.Json.Serialization;

namespace GK2Trainer.App.Core;

/// <summary>Command document written to <c>Trainer\command.json</c>.</summary>
public sealed class CommandDoc
{
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    [JsonPropertyName("seq")] public long Seq { get; set; }
    [JsonPropertyName("ops")] public List<Op> Ops { get; set; } = new();
}

/// <summary>One operation. Unused properties are omitted from the JSON.</summary>
public sealed class Op
{
    [JsonPropertyName("op")] public string OpName { get; set; } = "";

    /// <summary>Resource id / perk id / tech id / item id, depending on the operation.</summary>
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("id")] public string? Id { get; set; }

    [JsonPropertyName("value")] public double? Value { get; set; }

    [JsonPropertyName("amount")] public double? Amount { get; set; }

    [JsonPropertyName("count")] public int? Count { get; set; }

    [JsonPropertyName("kind")] public string? Kind { get; set; }

    [JsonPropertyName("filter")] public string? Filter { get; set; }

    [JsonPropertyName("limit")] public int? Limit { get; set; }

    [JsonPropertyName("message")] public string? Message { get; set; }
}

/// <summary>State document written by the plugin to <c>Trainer\state.json</c>.</summary>
public sealed class StateDoc
{
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("token")] public string? Token { get; set; }
    [JsonPropertyName("seq")] public long Seq { get; set; }
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("inGame")] public bool InGame { get; set; }
    [JsonPropertyName("timestamp")] public double Timestamp { get; set; }
    [JsonPropertyName("errors")] public List<string> Errors { get; set; } = new();
    [JsonPropertyName("results")] public List<OpResult> Results { get; set; } = new();
    [JsonPropertyName("game")] public GameInfo Game { get; set; } = new();
    [JsonPropertyName("targets")] public List<TargetInfo> Targets { get; set; } = new();

    public TargetInfo? Player => Targets.FirstOrDefault();
}

public sealed class GameInfo
{
    [JsonPropertyName("unity")] public string? Unity { get; set; }
    [JsonPropertyName("saveVersion")] public string? SaveVersion { get; set; }
    [JsonPropertyName("day")] public int? Day { get; set; }
}

public sealed class OpResult
{
    [JsonPropertyName("op")] public string? Op { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
    [JsonPropertyName("ids")] public List<string> Ids { get; set; } = new();
    [JsonPropertyName("systems")] public Dictionary<string, JsonElement> Systems { get; set; } = new();
}

public sealed class TargetInfo
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("isHost")] public bool IsHost { get; set; }

    [JsonPropertyName("resources")] public Dictionary<string, ResourceInfo> Resources { get; set; } = new();
    [JsonPropertyName("vitals")] public VitalsInfo Vitals { get; set; } = new();
    [JsonPropertyName("moveSpeed")] public MoveSpeedInfo MoveSpeed { get; set; } = new();
    [JsonPropertyName("gameSpeed")] public GameSpeedInfo GameSpeed { get; set; } = new();
    [JsonPropertyName("perks")] public List<string> Perks { get; set; } = new();
    [JsonPropertyName("knowledge")] public KnowledgeInfo Knowledge { get; set; } = new();

    public string Display => string.IsNullOrWhiteSpace(Name) ? "守墓人" : Name!;
}

public sealed class ResourceInfo
{
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("value")] public double? Value { get; set; }
    [JsonPropertyName("min")] public double? Min { get; set; }
    [JsonPropertyName("max")] public double? Max { get; set; }
}

public sealed class VitalsInfo
{
    [JsonPropertyName("hp")] public int? Hp { get; set; }
    [JsonPropertyName("maxHp")] public int? MaxHp { get; set; }
    [JsonPropertyName("immune")] public bool? Immune { get; set; }
    [JsonPropertyName("timeWithoutSleep")] public double? TimeWithoutSleep { get; set; }
}

public sealed class MoveSpeedInfo
{
    [JsonPropertyName("multiplier")] public double? Multiplier { get; set; }
    [JsonPropertyName("base")] public double? Base { get; set; }
    [JsonPropertyName("effective")] public double? Effective { get; set; }
    [JsonPropertyName("locked")] public bool Locked { get; set; }
    [JsonPropertyName("lockValue")] public double? LockValue { get; set; }
}

public sealed class GameSpeedInfo
{
    [JsonPropertyName("current")] public double? Current { get; set; }
    [JsonPropertyName("locked")] public bool Locked { get; set; }
    [JsonPropertyName("lockValue")] public double? LockValue { get; set; }
    [JsonPropertyName("paused")] public bool Paused { get; set; }
}

public sealed class KnowledgeInfo
{
    [JsonPropertyName("unlockedTechs")] public int? UnlockedTechs { get; set; }
    [JsonPropertyName("unlockedCrafts")] public int? UnlockedCrafts { get; set; }
    [JsonPropertyName("unlockedBuildings")] public int? UnlockedBuildings { get; set; }
    [JsonPropertyName("unlockedTalentIds")] public int? UnlockedTalentIds { get; set; }
    [JsonPropertyName("totalTechs")] public int? TotalTechs { get; set; }
    [JsonPropertyName("totalPerkDefs")] public int? TotalPerkDefs { get; set; }
    [JsonPropertyName("totalItemDefs")] public int? TotalItemDefs { get; set; }
}

/// <summary>Persisted app settings.</summary>
public sealed class AppSettings
{
    public string? GameDirectory { get; set; }
    public long LastSequence { get; set; }
    public bool AutoDeployPlugin { get; set; } = true;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string SettingsPath => Path.Combine(Paths.AppData, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), Options);
                if (loaded != null) return loaded;
            }
        }
        catch
        {
            // A broken settings file is not worth failing over.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, Options));
        }
        catch
        {
            // Best effort.
        }
    }
}
