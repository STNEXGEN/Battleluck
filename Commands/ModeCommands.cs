using System.Linq;
using VampireCommandFramework;

public static class ModeCommands
{
    [Command("modelist", description: "List all registered game modes", adminOnly: true)]
    public static void ListModes(ChatCommandContext ctx)
    {
        var registry = BattleLuckPlugin.GameModes;
        if (registry == null)
        {
            ctx.Reply("Game mode registry not initialized.");
            return;
        }

        var modes = registry.GetRegisteredModes();
        if (modes.Count == 0)
        {
            ctx.Reply("No game modes registered.");
            return;
        }

        ctx.Reply($"Registered modes ({modes.Count}):");
        foreach (var modeId in modes)
        {
            var mode = registry.Resolve(modeId);
            ctx.Reply($"  {modeId} — {mode?.DisplayName ?? "?"}");
        }

        var session = BattleLuckPlugin.Session;
        if (session != null && session.ActiveSessions.Count > 0)
        {
            ctx.Reply($"Active sessions ({session.ActiveSessions.Count}):");
            foreach (var kv in session.ActiveSessions)
            {
                ctx.Reply($"  Zone {kv.Key} — {kv.Value.Context.ModeId} ({kv.Value.Context.Players.Count} players)");
            }
        }
    }

    [Command("modestart", description: "Start a game mode manually", adminOnly: true)]
    public static void StartMode(ChatCommandContext ctx, string modeId)
    {
        var registry = BattleLuckPlugin.GameModes;
        if (registry == null)
        {
            ctx.Reply("Game mode registry not initialized.");
            return;
        }

        var mode = registry.Resolve(modeId);
        if (mode == null)
        {
            ctx.Reply($"Unknown mode: {modeId}");
            return;
        }

        ctx.Reply($"Mode '{mode.DisplayName}' is registered. Walk into the zone to start a session, or use bl.force {modeId}.");
    }

    [Command("modeend", description: "Force-end all sessions for a mode", adminOnly: true)]
    public static void EndMode(ChatCommandContext ctx, string modeId)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null)
        {
            ctx.Reply("Session controller not initialized.");
            return;
        }

        session.ForceEndByModeId(modeId);
        ctx.Reply($"Force-ended all sessions for '{modeId}'.");
    }

    [Command("modeinfo", description: "Show mode configuration details", adminOnly: true)]
    public static void ModeInfo(ChatCommandContext ctx, string modeId)
    {
        try
        {
            var config = ConfigLoader.Load(modeId);

            ctx.Reply($"Mode: {modeId}");
            ctx.Reply($"  Display: {config.Session.DisplayName}");
            ctx.Reply($"  Description: {config.Session.Description}");
            ctx.Reply($"  MinPlayers: {config.Session.Rules.MinPlayers}");
            ctx.Reply($"  MaxPlayers: {config.Session.Rules.MaxPlayers}");
            ctx.Reply($"  MatchDuration: {config.Session.Rules.MatchDurationMinutes} min");
            ctx.Reply($"  EnablePvP: {config.Session.Rules.EnablePvP}");
            ctx.Reply($"  EnableVBloods: {config.Session.Rules.EnableVBloods}");

            if (config.Zones.Zones.Count > 0)
            {
                ctx.Reply($"  Zones ({config.Zones.Zones.Count}):");
                foreach (var zone in config.Zones.Zones)
                {
                    ctx.Reply($"    - {zone.Name} (hash={zone.Hash}, radius={zone.Radius})");
                }
            }
        }
        catch (System.Exception ex)
        {
            ctx.Reply($"Error loading config: {ex.Message}");
        }
    }

    [Command("force", description: "Teleport to mode's zone and auto-start session", adminOnly: true)]
    public static void ForceStart(ChatCommandContext ctx, string modeId)
    {
        var registry = BattleLuckPlugin.GameModes;
        if (registry == null)
        {
            ctx.Reply("Game mode registry not initialized.");
            return;
        }

        var mode = registry.Resolve(modeId);
        if (mode == null)
        {
            ctx.Reply($"Unknown mode: {modeId}. Use bl.mode.list to see available modes.");
            return;
        }

        var session = BattleLuckPlugin.Session;
        if (session == null)
        {
            ctx.Reply("Session controller not initialized.");
            return;
        }

        var entity = ctx.Event.SenderCharacterEntity;
        session.ForceStart(modeId, entity);
        ctx.Reply($"Teleporting to {mode.DisplayName} zone and starting session...");
    }
}