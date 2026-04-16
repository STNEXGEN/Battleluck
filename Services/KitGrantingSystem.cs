using ProjectM.Network;
using Stunlock.Core;
using Unity.Entities;

/// <summary>
/// Applies kits when crafting jobs complete for configured item rules.
///
/// Intended hook point:
/// if (jobs == 0) {
///   craftingJobs.Remove(itemPrefabGuid);
///   KitGrantingSystem.OnCraftCompleted(steamId, user, itemPrefabGuid);
/// }
/// </summary>
public static class KitGrantingSystem
{
    public static void OnCraftCompleted(ulong steamId, User user, PrefabGUID craftedItem)
    {
        if (!KitRules.TryGetKitForItem(craftedItem, out var kitId))
            return;

        MainThreadDispatcher.Enqueue(() =>
        {
            try
            {
                var character = user.LocalCharacter._Entity;
                if (!character.Exists())
                {
                    BattleLuckPlugin.LogWarning($"[KitGranting] Character missing for {steamId}; skipping kit '{kitId}'.");
                    return;
                }

                var result = KitController.ApplyKit(character, kitId);
                if (!result.Success)
                {
                    BattleLuckPlugin.LogWarning($"[KitGranting] Failed to apply kit '{kitId}' to {steamId}: {result.Error}");
                    return;
                }

                var itemName = PrefabHelper.GetLivePrefabName(craftedItem) ?? craftedItem.GuidHash.ToString();
                NotificationHelper.NotifyPlayer(user, $"Kit '{kitId}' granted for crafting {itemName}.");
            }
            catch (Exception ex)
            {
                BattleLuckPlugin.LogWarning($"[KitGranting] Unexpected failure for {steamId}: {ex.Message}");
            }
        });
    }
}
