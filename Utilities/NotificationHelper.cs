using ProjectM.Network;
using Unity.Collections;

/// <summary>
/// Notification utilities for sending messages to players, admins, or all online players.
/// Uses V Rising's ServerChatUtils for message delivery.
/// </summary>
public static class NotificationHelper
{
    /// <summary>Send a system message to a specific player.</summary>
    public static void NotifyPlayer(User user, string message)
    {
        try
        {
            var em = VRisingCore.EntityManager;
            var msg = (FixedString512Bytes)message;
            ProjectM.ServerChatUtils.SendSystemMessageToClient(em, user, ref msg);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[Notify] Failed to notify player: {ex.Message}");
        }
    }

    /// <summary>Broadcast a message to all connected players.</summary>
    public static void NotifyAll(string message)
    {
        try
        {
            var em = VRisingCore.EntityManager;
            var query = em.CreateEntityQuery(
                Unity.Entities.ComponentType.ReadOnly<User>()
            );
            var users = query.ToEntityArray(Unity.Collections.Allocator.Temp);

            for (int i = 0; i < users.Length; i++)
            {
                try
                {
                    var user = em.GetComponentData<User>(users[i]);
                    if (user.IsConnected)
                    {
                        var msg = (FixedString512Bytes)message;
                        ProjectM.ServerChatUtils.SendSystemMessageToClient(em, user, ref msg);
                    }
                }
                catch { /* skip disconnected */ }
            }
            users.Dispose();
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[Notify] Broadcast failed: {ex.Message}");
        }
    }

    /// <summary>Send a message to admin users only.</summary>
    public static void NotifyAdmins(string message)
    {
        try
        {
            var em = VRisingCore.EntityManager;
            var query = em.CreateEntityQuery(
                Unity.Entities.ComponentType.ReadOnly<User>()
            );
            var users = query.ToEntityArray(Unity.Collections.Allocator.Temp);

            for (int i = 0; i < users.Length; i++)
            {
                try
                {
                    var user = em.GetComponentData<User>(users[i]);
                    if (user.IsConnected && user.IsAdmin)
                    {
                        var msg = (FixedString512Bytes)message;
                        ProjectM.ServerChatUtils.SendSystemMessageToClient(em, user, ref msg);
                    }
                }
                catch { /* skip */ }
            }
            users.Dispose();
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[Notify] Admin notify failed: {ex.Message}");
        }
    }
}
