using Stunlock.Core;

/// <summary>
/// Item -> kit grant rules for crafting completion hooks.
///
/// Rules are loaded from config/BattleLuck/kit_grant_rules.json.
/// Use either itemGuid or itemPrefab in each rule entry.
/// </summary>
public static class KitRules
{
    static readonly Dictionary<PrefabGUID, string> _rules = new();
    static bool _loaded;

    public static bool TryGetKitForItem(PrefabGUID item, out string kitId)
    {
        EnsureLoaded();
        return _rules.TryGetValue(item, out kitId!);
    }

    public static void Reload()
    {
        _loaded = false;
        _rules.Clear();
        EnsureLoaded();
    }

    static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        var path = Path.Combine(ConfigLoader.ConfigRoot, "kit_grant_rules.json");
        if (!File.Exists(path))
        {
            BattleLuckPlugin.LogInfo($"[KitRules] No kit grant rules file found at {path}. Craft grants are disabled until configured.");
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<KitGrantRulesConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (config == null || !config.Enabled || config.Rules.Count == 0)
                return;

            foreach (var rule in config.Rules)
            {
                if (string.IsNullOrWhiteSpace(rule.KitId))
                    continue;

                PrefabGUID itemGuid;
                if (rule.ItemGuid.HasValue && rule.ItemGuid.Value != 0)
                {
                    itemGuid = new PrefabGUID(rule.ItemGuid.Value);
                }
                else if (!string.IsNullOrWhiteSpace(rule.ItemPrefab) && PrefabHelper.TryGetPrefabGuid(rule.ItemPrefab, out var resolved))
                {
                    itemGuid = resolved;
                }
                else
                {
                    BattleLuckPlugin.LogWarning($"[KitRules] Skipping invalid rule (kit={rule.KitId}): missing/invalid itemGuid and itemPrefab.");
                    continue;
                }

                _rules[itemGuid] = rule.KitId;
            }

            BattleLuckPlugin.LogInfo($"[KitRules] Loaded {_rules.Count} item->kit rule(s).");
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[KitRules] Failed to load rules: {ex.Message}");
        }
    }

    sealed class KitGrantRulesConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("rules")]
        public List<KitGrantRule> Rules { get; set; } = new();
    }

    sealed class KitGrantRule
    {
        [JsonPropertyName("itemGuid")]
        public int? ItemGuid { get; set; }

        [JsonPropertyName("itemPrefab")]
        public string? ItemPrefab { get; set; }

        [JsonPropertyName("kitId")]
        public string KitId { get; set; } = "";
    }
}
