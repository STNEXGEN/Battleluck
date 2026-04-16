using System.Linq;

/// <summary>
/// AI Event mode - deterministic test mode that emits a full sequence of AI-relevant gameplay events.
/// Use this to validate assistant prompts, sidecar enrichment, logger forwarding, and end-of-match flow.
/// </summary>
public sealed class AiEventMode : GameModeBase
{
    public override string ModeId => "aievent";
    public override string DisplayName => "AI Event Test";

    private float _elapsedSeconds;
    private bool _sequencePublished;

    public override void OnStart(GameModeContext ctx)
    {
        _elapsedSeconds = 0f;
        _sequencePublished = false;

        ctx.Broadcast?.Invoke("AIEVENT - Running AI flow test sequence...");
        GameEvents.OnModeStarted?.Invoke(new ModeStartedEvent
        {
            SessionId = ctx.SessionId,
            ModeId = ModeId,
        });
    }

    public override void OnTick(GameModeContext ctx, float deltaSeconds)
    {
        _elapsedSeconds += deltaSeconds;

        if (!_sequencePublished && _elapsedSeconds >= 2f)
        {
            EmitCoreAiTestSequence(ctx);
            _sequencePublished = true;
        }

        if (_sequencePublished && _elapsedSeconds >= 6f)
        {
            ctx.State["result"] = "aievent_complete";
        }
    }

    public override void OnEnd(GameModeContext ctx)
    {
        var leaderboard = ctx.Scores.GetLeaderboard();
        var topPlayer = leaderboard.Count > 0 ? leaderboard[0] : 0UL;

        ctx.Broadcast?.Invoke("AIEVENT complete - emitted mode/scoring/elimination/end events.");
        GameEvents.OnModeEnded?.Invoke(new ModeEndedEvent
        {
            SessionId = ctx.SessionId,
            ModeId = ModeId,
            WinnerSteamId = topPlayer != 0 ? topPlayer : null,
        });
    }

    public override void OnReset(GameModeContext ctx)
    {
        _elapsedSeconds = 0f;
        _sequencePublished = false;
        ctx.Scores.Reset();
    }

    public static void EmitCoreAiTestSequence(GameModeContext ctx)
    {
        var players = ctx.Players.ToList();
        if (players.Count == 0)
        {
            ctx.Broadcast?.Invoke("AIEVENT: no players found in session; waiting for players.");
            return;
        }

        var primary = players[0];
        var secondary = players.Count > 1 ? players[1] : primary;

        ctx.Scores.AddPlayerScore(primary, 120);
        GameEvents.OnPlayerScored?.Invoke(new PlayerScoredEvent
        {
            SessionId = ctx.SessionId,
            SteamId = primary,
            Points = 120,
            TotalScore = ctx.Scores.GetPlayerScore(primary),
            Reason = "aievent_opening_score"
        });

        if (secondary != primary)
        {
            ctx.Scores.AddPlayerScore(secondary, 150);
            GameEvents.OnPlayerScored?.Invoke(new PlayerScoredEvent
            {
                SessionId = ctx.SessionId,
                SteamId = secondary,
                Points = 150,
                TotalScore = ctx.Scores.GetPlayerScore(secondary),
                Reason = "aievent_counter_score"
            });
        }

        GameEvents.OnPlayerEliminated?.Invoke(new PlayerEliminatedEvent
        {
            SessionId = ctx.SessionId,
            SteamId = primary,
            EliminatedBy = secondary != primary ? secondary : null,
        });

        ctx.Broadcast?.Invoke($"AIEVENT: emitted score/elimination events for {players.Count} player(s).");
    }
}
