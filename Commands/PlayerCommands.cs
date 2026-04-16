using System.Linq;
using BattleLuck.Models;
using VampireCommandFramework;

public static class PlayerCommands
{
    [Command("toggleenter", description: "Enter a zone session. Use: .toggleenter [modeName]")]
    public static void ToggleEnter(ChatCommandContext ctx, string modeId = "")
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        var entity = ctx.Event.SenderCharacterEntity;
        var result = session.ToggleEnter(entity.GetSteamId(), entity, string.IsNullOrEmpty(modeId) ? null : modeId);
        ctx.Reply(result.Success ? "Entered zone — kit applied, 2-minute timer started." : result.Error);
    }

    [Command("toggleleave", description: "Properly leave the current zone session")]
    public static void ToggleLeave(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        var entity = ctx.Event.SenderCharacterEntity;
        var result = session.ToggleLeave(entity.GetSteamId(), entity);
        ctx.Reply(result.Success ? "Left zone — gear and position restored." : result.Error);
    }

    [Command("exit", description: "Force exit current zone session")]
    public static void ForceExit(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        var entity = ctx.Event.SenderCharacterEntity;
        ulong steamId = entity.GetSteamId();

        if (!session.ForceExitPlayer(steamId, entity))
        {
            ctx.Reply("You are not in any active zone session.");
            return;
        }

        ctx.Reply("Exited zone — gear and position restored.");
    }

    [Command("score", description: "Show current scoreboard")]
    public static void ShowScore(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null || session.ActiveSessions.Count == 0)
        {
            ctx.Reply("No active sessions.");
            return;
        }

        foreach (var kv in session.ActiveSessions)
        {
            var s = kv.Value;
            var leaderboard = s.Context.Scores.GetLeaderboard();
            ctx.Reply($"{s.Context.ModeId} (zone {kv.Key}):");
            int rank = 1;
            foreach (var steamId in leaderboard.Take(5))
            {
                ctx.Reply($"  #{rank++}. {steamId} — {s.Context.Scores.GetPlayerScore(steamId)} pts");
            }
        }
    }

    [Command("elo", description: "Show Elo ratings for Colosseum mode")]
    public static void ShowElo(ChatCommandContext ctx)
    {
        var registry = BattleLuckPlugin.GameModes;
        var colosseum = registry?.Resolve("colosseum") as ColosseumMode;
        if (colosseum == null)
        {
            ctx.Reply("Colosseum mode not available.");
            return;
        }

        var leaderboard = colosseum.Elo.GetLeaderboard();
        if (leaderboard.Count == 0)
        {
            ctx.Reply("No Elo ratings recorded yet.");
            return;
        }

        ctx.Reply("Elo Leaderboard:");
        int rank = 1;
        foreach (var (steamId, elo) in leaderboard.Take(10))
        {
            ctx.Reply($"  #{rank++}. {steamId} — {elo}");
        }
    }

    [Command("help", description: "Show available BattleLuck commands")]
    public static void ShowHelp(ChatCommandContext ctx)
    {
        ctx.Reply("BattleLuck Commands:");
        ctx.Reply("  bl.help — This help");
        ctx.Reply("  bl.toggleenter — Enter zone session");
        ctx.Reply("  bl.toggleleave — Leave zone session");
        ctx.Reply("  bl.exit — Force exit (admin bypass)");
        ctx.Reply("  bl.score — View scoreboard");
        ctx.Reply("  bl.elo — View Elo leaderboard");
        ctx.Reply("Admin commands (admin only):");
        ctx.Reply("  bl.mode.list — List modes");
        ctx.Reply("  bl.mode.info <id> — Mode details");
        ctx.Reply("  bl.mode.end <id> — End mode");
        ctx.Reply("  bl.force <mode> — Start session");
        ctx.Reply("  bl.admin.reload — Reload configs");
        ctx.Reply("  bl.admin.pause/resume — Pause sessions");
        ctx.Reply("  bl.admin.zoneinfo — Zone stats");
    }

    [Command("kit", description: "Apply full end-game kit to yourself", adminOnly: true)]
    public static void ApplyKit(ChatCommandContext ctx)
    {
        var entity = ctx.Event.SenderCharacterEntity;
        KitController.ApplyFullKit(entity);
        KitController.SetMaxLevel(entity);
        AbilityController.UnlockAllAbilities(entity);
        ctx.Reply("Full end-game kit applied.");
    }

    [Command("ai", description: "Chat with the AI assistant. Usage: .ai <your question>")]
    public static async void AskAI(ChatCommandContext ctx, params string[] words)
    {
        var query = string.Join(" ", words);
        var aiAssistant = BattleLuckPlugin.AIAssistant;
        if (aiAssistant == null || !aiAssistant.IsEnabled)
        {
            ctx.Reply("AI Assistant is currently unavailable.");
            return;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            ctx.Reply("Please provide a question. Example: .ai How do I improve in Colosseum mode?");
            return;
        }

        var steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        
        try
        {
            var response = await aiAssistant.HandleDirectQuery(steamId, query);
            ctx.Reply($"🤖 AI Assistant: {response}");
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"AI command error: {ex.Message}");
            ctx.Reply("Sorry, I encountered an error processing your request.");
        }
    }

    [Command("aistatus", description: "Show AI assistant status and settings")]
    public static void AIStatus(ChatCommandContext ctx)
    {
        var aiAssistant = BattleLuckPlugin.AIAssistant;
        if (aiAssistant == null)
        {
            ctx.Reply("AI Assistant is not initialized.");
            return;
        }

        var status = aiAssistant.IsEnabled ? "Enabled" : "Disabled";
        ctx.Reply($"🤖 AI Assistant Status: {status}");
        ctx.Reply("Available commands:");
        ctx.Reply("  .ai <question> — Ask the AI assistant");
        ctx.Reply("  .aistatus — Show this status");
        
        if (aiAssistant.IsEnabled)
        {
            var providerSummary = aiAssistant.IsSidecarConfigured
                ? $"Google AI Studio with local battle sidecar enrichment ({aiAssistant.SidecarBaseUrl})"
                : "Google AI Studio";

            ctx.Reply($"The AI is powered by {providerSummary} and provides tips during gameplay:");
            ctx.Reply("• Game mode strategies • Commands help • Performance advice • ELO improvement");
        }
    }

    [Command("actions", description: "Show valid actions for the current mode")]
    public static void ShowActions(ChatCommandContext ctx, string modeId = "")
    {
        if (string.IsNullOrEmpty(modeId))
        {
            var session = BattleLuckPlugin.Session;
            var steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
            if (session != null)
            {
                foreach (var kv in session.ActiveSessions)
                {
                    if (kv.Value.Context.Players.Contains(steamId))
                    {
                        modeId = kv.Value.Context.ModeId;
                        break;
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(modeId))
        {
            ctx.Reply("Specify a mode: .actions <modeId>");
            return;
        }

        var actions = ActionRegistry.GetActionsForMode(modeId).ToList();
        if (actions.Count == 0)
        {
            ctx.Reply($"No actions defined for mode '{modeId}'.");
            return;
        }

        ctx.Reply($"Actions for {modeId}:");
        foreach (var action in actions)
        {
            var info = ActionRegistry.Actions[action];
            var pts = info.DefaultPoints > 0 ? $" (+{info.DefaultPoints} pts)" : "";
            ctx.Reply($"  {info.ColoredLabel}{pts}");
        }
    }
}