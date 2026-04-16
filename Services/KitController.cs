using ProjectM;
using Stunlock.Core;
using Unity.Entities;

/// <summary>
/// Applies full kit loadouts from kit.json per mode.
/// Uses PrefabHelper for name→GUID resolution.
/// Hard rollback on failure via PlayerStateController snapshot restore.
/// </summary>
public static class KitController
{
    static readonly Dictionary<string, KitConfig> _kitCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Load kit.json for a mode. Caches result.</summary>
    public static KitConfig? LoadKit(string modeId)
    {
        if (_kitCache.TryGetValue(modeId, out var cached))
            return cached;

        var path = Path.Combine(ConfigLoader.ConfigRoot, modeId, "kit.json");
        if (!File.Exists(path))
        {
            BattleLuckPlugin.LogWarning($"[KitController] Missing kit.json: {path}");
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            var kit = JsonSerializer.Deserialize<KitConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (kit != null)
                _kitCache[modeId] = kit;

            return kit;
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[KitController] Error loading kit.json for {modeId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Clear the kit cache (used on reload).</summary>
    public static void ClearCache() => _kitCache.Clear();

    /// <summary>
    /// Apply a kit to a player by mode ID.
    /// Uses MutationPipeline with snapshot rollback on failure.
    /// </summary>
    public static OperationResult ApplyKit(Entity playerCharacter, string modeId)
    {
        var kit = LoadKit(modeId);
        if (kit == null)
            return OperationResult.Fail($"Kit not found for mode '{modeId}'");

        var snapshot = new PlayerStateController();
        const int KitRollbackZone = -999;
        snapshot.SaveSnapshot(playerCharacter, KitRollbackZone);
        var settings = kit.Settings;

        var pipelineResult = MutationPipeline.Run($"KitApply:{modeId}", p =>
        {
            if (settings.ClearInventoryFirst)
            {
                p.Step("ClearInventory", () =>
                {
                    ClearInventory(playerCharacter);
                });
            }

            if (kit.Armors != null)
            {
                p.Step("ApplyArmor", () =>
                {
                    ApplyArmor(playerCharacter, kit.Armors);
                });
            }

            p.Step("ApplyWeapons", () =>
            {
                foreach (var weapon in kit.Weapons)
                    ApplyWeapon(playerCharacter, weapon);
            });

            p.Step("ApplyItems", () =>
            {
                foreach (var item in kit.Items)
                {
                    var guid = ResolvePrefab(item.Prefab, "item");
                    if (guid.HasValue)
                        playerCharacter.TryGiveItem(guid.Value, item.Amount);
                }
            });

            if (kit.Blood != null)
            {
                p.Step("ApplyBlood", () =>
                {
                    ApplyBlood(playerCharacter, kit.Blood);
                });
            }

            if (kit.Abilities != null)
            {
                p.Step("ApplyAbilities", () =>
                {
                    AbilityController.EquipAbilities(playerCharacter, kit.Abilities);
                });
            }

            if (kit.PassiveSpells.Count > 0)
            {
                p.Step("ApplyPassiveSpells", () =>
                {
                    AbilityController.EquipPassiveSpells(playerCharacter, kit.PassiveSpells);
                });
            }

            if (settings.HealOnApply)
            {
                p.Step("HealToFull", () =>
                {
                    playerCharacter.HealToFull();
                });
            }
        });

        if (!pipelineResult.Success)
        {
            snapshot.RestoreSnapshot(playerCharacter, KitRollbackZone);

            BattleLuckPlugin.LogError(
                $"[KitController] CRITICAL: Kit apply failed for {modeId} at step '{pipelineResult.FailedStep}': {pipelineResult.Error}"
            );

            return OperationResult.Fail(
                $"Kit apply failed at step '{pipelineResult.FailedStep}': {pipelineResult.Error}"
            );
        }

        BattleLuckPlugin.LogInfo(
            $"[KitController] Kit '{modeId}' applied to {playerCharacter.GetSteamId()} with {pipelineResult.Steps.Count} steps."
        );

        return OperationResult.Ok();
    }

    static void ClearInventory(Entity character)
    {
        var em = VRisingCore.EntityManager;
        var sgm = VRisingCore.ServerGameManager;

        if (!sgm.TryGetBuffer<InventoryBuffer>(character, out var inventory))
            return;

        // Collect items first, then remove (avoid modifying buffer during iteration)
        var toRemove = new List<PrefabGUID>();
        for (int i = 0; i < inventory.Length; i++)
        {
            var item = inventory[i];
            if (item.ItemType != PrefabGUID.Empty)
                toRemove.Add(item.ItemType);
        }

        foreach (var prefab in toRemove)
        {
            sgm.TryRemoveInventoryItem(character, prefab, 999);
        }
    }

    static void ApplyArmor(Entity character, ArmorsConfig armors)
    {
        GiveIfResolved(character, armors.Chest, "Chest");
        GiveIfResolved(character, armors.Legs, "Legs");
        GiveIfResolved(character, armors.Gloves, "Gloves");
        GiveIfResolved(character, armors.Boots, "Boots");
        GiveIfResolved(character, armors.Cloak, "Cloak");
        GiveIfResolved(character, armors.Headgear, "Headgear");
        GiveIfResolved(character, armors.MagicSource, "MagicSource");
        GiveIfResolved(character, armors.Bag, "Bag");
    }

    static void ApplyWeapon(Entity character, WeaponConfig weapon)
    {
        var guid = ResolvePrefab(weapon.Prefab, "weapon");
        if (!guid.HasValue) return;

        bool ok = character.TryGiveItem(guid.Value, weapon.Amount);
        if (!ok)
            BattleLuckPlugin.LogWarning($"[KitController] TryGiveItem FAILED for weapon {weapon.Prefab} (guid={guid.Value.GuidHash})");
    }

    static void ApplyBlood(Entity character, BloodConfig blood)
    {
        var bloodGuid = PrefabHelper.GetPrefabGuid($"BloodType_{blood.Type}");
        if (!bloodGuid.HasValue)
        {
            BattleLuckPlugin.LogWarning($"[KitController] Unknown blood type: {blood.Type}");
            return;
        }

        if (character.Has<Blood>())
        {
            character.With((ref Blood b) =>
            {
                b.BloodType = bloodGuid.Value;
                b.Quality = blood.Quality;
                b.Value = blood.Value;
            });
        }
    }

    static void GiveIfResolved(Entity character, string? prefabName, string slotName)
    {
        if (string.IsNullOrEmpty(prefabName)) return;

        var guid = ResolvePrefab(prefabName, slotName);
        if (!guid.HasValue) return;

        bool ok = character.TryGiveItem(guid.Value, 1);
        if (!ok)
            BattleLuckPlugin.LogWarning($"[KitController] TryGiveItem FAILED for {slotName}: {prefabName} (guid={guid.Value.GuidHash})");
    }

    /// <summary>
    /// Resolve a prefab name using both Prefabs.cs AND live game data.
    /// Validates the resolved GUID exists in the game's entity map.
    /// </summary>
    static PrefabGUID? ResolvePrefab(string prefabName, string context)
    {
        // Try strict exact match first
        if (PrefabHelper.TryGetValidPrefabGuidStrict(prefabName, out var strictGuid))
            return strictGuid;

        // Fall back to deep lookup (expanded candidates)
        var guid = PrefabHelper.GetValidPrefabGuidDeep(prefabName);
        if (guid.HasValue)
        {
            BattleLuckPlugin.LogInfo($"[KitController] Resolved {context} '{prefabName}' via deep lookup -> guid {guid.Value.GuidHash}");
            return guid.Value;
        }

        BattleLuckPlugin.LogWarning($"[KitController] Unknown {context} prefab: {prefabName} — not found in strict or deep lookup.");
        return null;
    }

    // ── Backward compatibility shims ────────────────────────────────────

    /// <summary>Apply full kit using "bloodbath" as default (backward compat).</summary>
    public static void ApplyFullKit(Entity playerCharacter) => ApplyKit(playerCharacter, "bloodbath");

    /// <summary>Set equipment level to max (90).</summary>
    public static void SetMaxLevel(Entity playerCharacter) => playerCharacter.SetEquipmentLevel(90f, 90f, 90f);

    /// <summary>Apply only weapons from the default kit.</summary>
    public static void ApplyWeaponsKit(Entity playerCharacter)
    {
        var kit = LoadKit("bloodbath");
        if (kit == null) return;
        foreach (var weapon in kit.Weapons)
        {
            var guid = PrefabHelper.GetValidPrefabGuidDeep(weapon.Prefab);
            if (guid.HasValue) playerCharacter.TryGiveItem(guid.Value, weapon.Amount);
        }
    }

    /// <summary>Apply only armor from the default kit.</summary>
    public static void ApplyArmorKit(Entity playerCharacter)
    {
        var kit = LoadKit("bloodbath");
        if (kit == null) return;
        if (kit.Armors != null) ApplyArmor(playerCharacter, kit.Armors);
    }

    /// <summary>Get all item prefabs from a kit (for inventory clearing).</summary>
    public static List<PrefabGUID> GetKitPrefabs(string kitId = "bloodbath")
    {
        var kit = LoadKit(kitId);
        if (kit == null) return new();

        var result = new List<PrefabGUID>();
        foreach (var w in kit.Weapons)
        {
            var g = PrefabHelper.GetValidPrefabGuidDeep(w.Prefab);
            if (g.HasValue) result.Add(g.Value);
        }
        if (kit.Armors != null)
        {
            AddIfResolved(result, kit.Armors.Chest);
            AddIfResolved(result, kit.Armors.Legs);
            AddIfResolved(result, kit.Armors.Gloves);
            AddIfResolved(result, kit.Armors.Boots);
            AddIfResolved(result, kit.Armors.Cloak);
            AddIfResolved(result, kit.Armors.Headgear);
            AddIfResolved(result, kit.Armors.MagicSource);
            AddIfResolved(result, kit.Armors.Bag);
        }
        foreach (var item in kit.Items)
        {
            var g = PrefabHelper.GetValidPrefabGuidDeep(item.Prefab);
            if (g.HasValue) result.Add(g.Value);
        }
        return result;
    }

    static void AddIfResolved(List<PrefabGUID> list, string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        var g = PrefabHelper.GetValidPrefabGuidDeep(name);
        if (g.HasValue) list.Add(g.Value);
    }
}
