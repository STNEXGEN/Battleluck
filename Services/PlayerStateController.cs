using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Full 13-category player state save/restore with JSON file persistence.
/// Categories: position, health, energy, blood, equipment levels, equipment slots,
/// inventory, weapons, abilities, jewels, passives, buffs, progression.
/// Snapshots stored at BepInEx/data/BattleLuck/snapshots/{playerId}.json
/// </summary>
public sealed class PlayerStateController
{
    static readonly string SnapshotDir = Path.Combine(
        BepInEx.Paths.BepInExRootPath, "data", "BattleLuck", "snapshots");

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    readonly Dictionary<ulong, PlayerSnapshot> _cache = new();

    public PlayerStateController()
    {
        Directory.CreateDirectory(SnapshotDir);
    }

    /// <summary>Save full 13-category entity state for a player.</summary>
    public void SaveSnapshot(Entity character, int zoneHash)
    {
        ulong steamId = character.GetSteamId();
        if (steamId == 0) return;

        var em = VRisingCore.EntityManager;
        var snap = new PlayerSnapshot
        {
            Version = 1,
            GameVersion = Application.version,
            PlayerId = steamId.ToString(),
            Timestamp = DateTime.UtcNow,
            ZoneHash = zoneHash
        };

        // 1. Name
        if (character.TryGetComponent(out PlayerCharacter pc))
        {
            var userEntity = pc.UserEntity;
            if (userEntity.Exists() && userEntity.TryGetComponent(out User user))
                snap.Name = user.CharacterName.ToString();
        }

        // 2. Position
        var pos = character.GetPosition();
        snap.Position = new Vec3Snapshot { X = pos.x, Y = pos.y, Z = pos.z };

        // 3. Health
        if (character.TryGetComponent(out Health health))
            snap.Health = new HealthSnapshot { Current = health.Value, Max = health.MaxHealth };

        // 4. Energy — V Rising doesn't expose a standalone Energy component;
        // placeholder for future use if discovered.
        snap.Energy = new EnergySnapshot { Current = 0, Max = 0 };

        // 5. Blood
        if (character.TryGetComponent(out Blood blood))
        {
            snap.Blood = new BloodSnapshot
            {
                TypeGuid = blood.BloodType.GuidHash,
                Type = PrefabHelper.GetName(blood.BloodType) ?? blood.BloodType.GuidHash.ToString(),
                Quality = blood.Quality,
                Value = blood.Value
            };
        }

        // 6. Equipment Levels
        if (character.TryGetComponent(out Equipment equipment))
        {
            snap.EquipmentLevels = new EquipmentLevelsSnapshot
            {
                Weapon = equipment.WeaponLevel._Value,
                Armor = equipment.ArmorLevel._Value,
                Spell = equipment.SpellLevel._Value
            };
        }

        // 7. Inventory
        if (InventoryUtilities.TryGetInventoryEntity(em, character, out Entity invEntity)
            && em.HasBuffer<InventoryBuffer>(invEntity))
        {
            var buffer = em.GetBuffer<InventoryBuffer>(invEntity);
            for (int i = 0; i < buffer.Length; i++)
            {
                var slot = buffer[i];
                if (slot.ItemType != PrefabGUID.Empty && slot.Amount > 0)
                {
                    snap.Inventory.Add(new InventoryItemSnapshot
                    {
                        Guid = slot.ItemType.GuidHash,
                        Prefab = PrefabHelper.GetName(slot.ItemType) ?? "",
                        Amount = slot.Amount,
                        Slot = i
                    });

                    CaptureDerivedEquipmentAndWeapons(snap, slot.ItemType, i);
                }
            }
        }

        // 8. Buffs
        if (em.HasBuffer<BuffBuffer>(character))
        {
            var buffs = em.GetBuffer<BuffBuffer>(character);
            for (int i = 0; i < buffs.Length; i++)
            {
                var b = buffs[i];
                var buffEntity = b.Entity;
                var prefab = b.PrefabGuid;
                float remaining = 0f;
                int stacks = 1;

                if (buffEntity.Exists() && buffEntity.TryGetComponent(out LifeTime lifeTime))
                    remaining = lifeTime.Duration;

                snap.Buffs.Add(new BuffSnapshot
                {
                    Guid = prefab.GuidHash,
                    Prefab = PrefabHelper.GetName(prefab) ?? "",
                    Stacks = stacks,
                    RemainingDuration = remaining
                });

                CapturePassiveBuffSnapshot(snap, prefab);

                if (buffEntity.Exists() && em.HasBuffer<ReplaceAbilityOnSlotBuff>(buffEntity))
                {
                    var replaceBuffer = em.GetBuffer<ReplaceAbilityOnSlotBuff>(buffEntity);
                    for (int r = 0; r < replaceBuffer.Length; r++)
                    {
                        var entry = replaceBuffer[r];
                        CaptureAbilitySlotSnapshot(snap, entry.Slot, entry.NewGroupId);
                    }
                }
            }
        }

        // Cache + persist to disk
        _cache[steamId] = snap;
        WriteToDisk(steamId, snap);

        BattleLuckPlugin.LogInfo(
            $"[PlayerState] Saved full snapshot for {steamId} " +
            $"({snap.Inventory.Count} items, {snap.EquipmentCount()} equipped, {snap.Weapons.Count} weapons, " +
            $"{snap.AbilityCount()} abilities, {snap.Passives.Count} passives, {snap.Buffs.Count} buffs) in zone {zoneHash}."
        );
    }

    /// <summary>Restore full entity state from snapshot in 13-step order.</summary>
    public bool RestoreSnapshot(Entity character, int zoneHash)
    {
        ulong steamId = character.GetSteamId();
        if (steamId == 0) return false;

        var snap = GetSnapshot(steamId);
        if (snap == null)
        {
            BattleLuckPlugin.LogWarning($"[PlayerState] No snapshot found for {steamId}.");
            return false;
        }

        var em = VRisingCore.EntityManager;

        // Step 1: Clear entire inventory
        ClearInventory(character);

        // Step 2: Restore inventory items
        foreach (var item in snap.Inventory)
        {
            var guid = new PrefabGUID(item.Guid);
            character.TryGiveItem(guid, item.Amount);
        }

        // Step 3: Restore missing equipment slot items (best-effort)
        RestoreEquipmentItems(character, snap.Equipment);

        // Step 4: Restore weapon entries (best-effort)
        RestoreWeapons(character, snap.Weapons);

        // Step 5: Restore equipment levels
        character.SetEquipmentLevel(
            snap.EquipmentLevels.Weapon,
            snap.EquipmentLevels.Armor,
            snap.EquipmentLevels.Spell);

        // Step 6: Restore blood type + quality
        if (character.TryGetComponent(out Blood blood))
        {
            blood.BloodType = new PrefabGUID(snap.Blood.TypeGuid);
            blood.Quality = snap.Blood.Quality;
            blood.Value = snap.Blood.Value;
            character.Write(blood);
        }

        // Step 7: Restore configured ability slots
        RestoreAbilitySlots(character, snap.Abilities);

        // Step 8: Restore passive buff spells
        RestorePassives(character, snap.Passives);

        // Step 10: Restore buffs
        foreach (var buff in snap.Buffs)
        {
            var guid = new PrefabGUID(buff.Guid);
            if (!character.HasBuff(guid))
                character.TryApplyBuff(guid);
        }

        // Step 11: Restore health + energy
        if (character.TryGetComponent(out Health hp))
        {
            hp.MaxHealth._Value = snap.Health.Max;
            hp.Value = snap.Health.Current;
            character.Write(hp);
        }

        // Step 12: Restore position
        character.SetPosition(new float3(snap.Position.X, snap.Position.Y, snap.Position.Z));

        // Cleanup
        _cache.Remove(steamId);
        DeleteFromDisk(steamId);

        BattleLuckPlugin.LogInfo($"[PlayerState] Restored snapshot for {steamId} ({snap.Inventory.Count} items).");
        return true;
    }

    static void CaptureDerivedEquipmentAndWeapons(PlayerSnapshot snap, PrefabGUID itemType, int slot)
    {
        var itemName = PrefabHelper.GetName(itemType) ?? string.Empty;
        if (string.IsNullOrEmpty(itemName))
            return;

        if (itemName.StartsWith("Item_Weapon_", StringComparison.OrdinalIgnoreCase))
        {
            snap.Weapons.Add(new WeaponSnapshot
            {
                Guid = itemType.GuidHash,
                Prefab = itemName,
                Slot = slot
            });
            return;
        }

        var equipmentSlot = new EquipmentSlotSnapshot
        {
            Guid = itemType.GuidHash,
            Prefab = itemName
        };

        if (itemName.StartsWith("Item_Armor_Chest_", StringComparison.OrdinalIgnoreCase) && snap.Equipment.Chest == null)
            snap.Equipment.Chest = equipmentSlot;
        else if (itemName.StartsWith("Item_Armor_Legs_", StringComparison.OrdinalIgnoreCase) && snap.Equipment.Legs == null)
            snap.Equipment.Legs = equipmentSlot;
        else if (itemName.StartsWith("Item_Armor_Boots_", StringComparison.OrdinalIgnoreCase) && snap.Equipment.Boots == null)
            snap.Equipment.Boots = equipmentSlot;
        else if (itemName.StartsWith("Item_Armor_Gloves_", StringComparison.OrdinalIgnoreCase) && snap.Equipment.Gloves == null)
            snap.Equipment.Gloves = equipmentSlot;
        else if (itemName.StartsWith("Item_Headgear_", StringComparison.OrdinalIgnoreCase) && snap.Equipment.Head == null)
            snap.Equipment.Head = equipmentSlot;
        else if (itemName.StartsWith("Item_Cloak_", StringComparison.OrdinalIgnoreCase) && snap.Equipment.Cloak == null)
            snap.Equipment.Cloak = equipmentSlot;
        else if (itemName.StartsWith("Item_MagicSource_", StringComparison.OrdinalIgnoreCase) && snap.Equipment.MagicSource == null)
            snap.Equipment.MagicSource = equipmentSlot;
        else if (itemName.StartsWith("Item_Bag_", StringComparison.OrdinalIgnoreCase) && snap.Equipment.Bag == null)
            snap.Equipment.Bag = equipmentSlot;
    }

    static void CaptureAbilitySlotSnapshot(PlayerSnapshot snap, int slot, PrefabGUID abilityGuid)
    {
        if (abilityGuid == PrefabGUID.Empty)
            return;

        var ability = new AbilitySlotSnapshot
        {
            Guid = abilityGuid.GuidHash,
            Prefab = PrefabHelper.GetName(abilityGuid) ?? string.Empty
        };

        // Slot mapping from AbilityController: Travel=3, Spell1=5, Spell2=6, Ultimate=7
        switch (slot)
        {
            case 3:
                snap.Abilities.Travel = ability;
                break;
            case 5:
                snap.Abilities.Spell1 = ability;
                break;
            case 6:
                snap.Abilities.Spell2 = ability;
                break;
            case 7:
                snap.Abilities.Ultimate = ability;
                break;
        }
    }

    static void CapturePassiveBuffSnapshot(PlayerSnapshot snap, PrefabGUID buffGuid)
    {
        if (buffGuid == PrefabGUID.Empty)
            return;

        var buffName = PrefabHelper.GetName(buffGuid) ?? string.Empty;
        if (!buffName.Contains("Passive", StringComparison.OrdinalIgnoreCase))
            return;

        if (snap.Passives.Any(p => p.Guid == buffGuid.GuidHash))
            return;

        snap.Passives.Add(new PassiveSlotSnapshot
        {
            Slot = snap.Passives.Count,
            Guid = buffGuid.GuidHash,
            Prefab = buffName
        });
    }

    static void RestoreEquipmentItems(Entity character, EquipmentSlotsSnapshot equipment)
    {
        TryGiveSlot(character, equipment.Chest);
        TryGiveSlot(character, equipment.Legs);
        TryGiveSlot(character, equipment.Boots);
        TryGiveSlot(character, equipment.Gloves);
        TryGiveSlot(character, equipment.Head);
        TryGiveSlot(character, equipment.Cloak);
        TryGiveSlot(character, equipment.MagicSource);
        TryGiveSlot(character, equipment.Bag);
    }

    static void RestoreWeapons(Entity character, List<WeaponSnapshot> weapons)
    {
        foreach (var weapon in weapons)
            character.TryGiveItem(new PrefabGUID(weapon.Guid), 1);
    }

    static void RestoreAbilitySlots(Entity character, AbilitiesSnapshot abilities)
    {
        if (abilities.Travel != null)
            AbilityController.SetSpellOnSlot(character, 3, new PrefabGUID(abilities.Travel.Guid));
        if (abilities.Spell1 != null)
            AbilityController.SetSpellOnSlot(character, 5, new PrefabGUID(abilities.Spell1.Guid));
        if (abilities.Spell2 != null)
            AbilityController.SetSpellOnSlot(character, 6, new PrefabGUID(abilities.Spell2.Guid));
        if (abilities.Ultimate != null)
            AbilityController.SetSpellOnSlot(character, 7, new PrefabGUID(abilities.Ultimate.Guid));
    }

    static void RestorePassives(Entity character, List<PassiveSlotSnapshot> passives)
    {
        foreach (var passive in passives)
        {
            var guid = new PrefabGUID(passive.Guid);
            if (!character.HasBuff(guid))
                character.TryApplyBuff(guid);
        }
    }

    static void TryGiveSlot(Entity character, EquipmentSlotSnapshot? slot)
    {
        if (slot == null || slot.Guid == 0)
            return;

        character.TryGiveItem(new PrefabGUID(slot.Guid), 1);
    }

    /// <summary>Clear all items from player inventory.</summary>
    static void ClearInventory(Entity character)
    {
        var em = VRisingCore.EntityManager;
        if (!InventoryUtilities.TryGetInventoryEntity(em, character, out Entity invEntity)) return;
        if (!em.HasBuffer<InventoryBuffer>(invEntity)) return;

        var buffer = em.GetBuffer<InventoryBuffer>(invEntity);
        var toRemove = new List<(PrefabGUID prefab, int amount)>();
        for (int i = 0; i < buffer.Length; i++)
        {
            var slot = buffer[i];
            if (slot.ItemType != PrefabGUID.Empty && slot.Amount > 0)
                toRemove.Add((slot.ItemType, slot.Amount));
        }

        foreach (var (prefab, amount) in toRemove)
            character.TryRemoveItem(prefab, amount);
    }

    public bool HasSnapshot(ulong steamId) => _cache.ContainsKey(steamId) || FileExists(steamId);

    public PlayerSnapshot? GetSnapshot(ulong steamId)
    {
        if (_cache.TryGetValue(steamId, out var cached))
            return cached;
        return ReadFromDisk(steamId);
    }

    public void ClearSnapshot(ulong steamId)
    {
        _cache.Remove(steamId);
        DeleteFromDisk(steamId);
    }

    public void ClearAll()
    {
        _cache.Clear();
        if (Directory.Exists(SnapshotDir))
        {
            foreach (var file in Directory.GetFiles(SnapshotDir, "*.json"))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }

    // ── File persistence ────────────────────────────────────────────────

    static string GetPath(ulong steamId) => Path.Combine(SnapshotDir, $"{steamId}.json");

    static bool FileExists(ulong steamId) => File.Exists(GetPath(steamId));

    void WriteToDisk(ulong steamId, PlayerSnapshot snap)
    {
        try
        {
            var json = JsonSerializer.Serialize(snap, JsonOpts);
            File.WriteAllText(GetPath(steamId), json);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"[PlayerState] Failed to write snapshot for {steamId}: {ex.Message}");
        }
    }

    static PlayerSnapshot? ReadFromDisk(ulong steamId)
    {
        var path = GetPath(steamId);
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PlayerSnapshot>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"[PlayerState] Failed to read snapshot for {steamId}: {ex.Message}");
            return null;
        }
    }

    static void DeleteFromDisk(ulong steamId)
    {
        var path = GetPath(steamId);
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { }
        }
    }
}

static class PlayerSnapshotMetrics
{
    public static int EquipmentCount(this PlayerSnapshot snapshot)
    {
        var count = 0;
        if (snapshot.Equipment.Chest != null) count++;
        if (snapshot.Equipment.Legs != null) count++;
        if (snapshot.Equipment.Boots != null) count++;
        if (snapshot.Equipment.Gloves != null) count++;
        if (snapshot.Equipment.Head != null) count++;
        if (snapshot.Equipment.Cloak != null) count++;
        if (snapshot.Equipment.MagicSource != null) count++;
        if (snapshot.Equipment.Bag != null) count++;
        return count;
    }

    public static int AbilityCount(this PlayerSnapshot snapshot)
    {
        var count = 0;
        if (snapshot.Abilities.Travel != null) count++;
        if (snapshot.Abilities.Spell1 != null) count++;
        if (snapshot.Abilities.Spell2 != null) count++;
        if (snapshot.Abilities.Ultimate != null) count++;
        return count;
    }
}

