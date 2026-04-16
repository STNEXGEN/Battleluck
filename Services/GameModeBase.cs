using System;
using System.Collections.Generic;
using BattleLuck.Models;

/// <summary>
/// Abstract base class for all BattleLuck game modes.
/// Subclasses implement lifecycle hooks; the runtime drives them via GameModeRegistry.
/// All methods run synchronously on the main server thread (ECS is not thread-safe).
/// </summary>
public abstract class GameModeBase
{
    public abstract string ModeId { get; }
    public abstract string DisplayName { get; }

    /// <summary>Called once when the session transitions to Active.</summary>
    public virtual void OnStart(GameModeContext ctx) { }

    /// <summary>Called periodically while the session is Active (tick interval set by controller).</summary>
    public virtual void OnTick(GameModeContext ctx, float deltaSeconds) { }

    /// <summary>Called when a player joins the active session zone.</summary>
    public virtual void OnPlayerJoin(GameModeContext ctx, ulong steamId) { }

    /// <summary>Called when a player leaves the active session zone.</summary>
    public virtual void OnPlayerLeave(GameModeContext ctx, ulong steamId) { }

    /// <summary>Called when a player is downed/killed inside the session zone.</summary>
    public virtual void OnPlayerDowned(GameModeContext ctx, ulong victimSteamId, ulong? killerSteamId) { }

    /// <summary>Called when a round ends (for multi-round modes).</summary>
    public virtual void OnRoundEnd(GameModeContext ctx, int roundNumber) { }

    /// <summary>Called when the session transitions to Ending.</summary>
    public virtual void OnEnd(GameModeContext ctx) { }

    /// <summary>Called when the session resets back to Idle.</summary>
    public virtual void OnReset(GameModeContext ctx) { }

    /// <summary>
    /// Score a player action: adds points, fires PlayerScored + ActionPerformed events, queues VFX.
    /// </summary>
    protected void ScoreAction(GameModeContext ctx, ulong steamId, ActionType action, int? pointsOverride = null, Unity.Entities.Entity? playerEntity = null)
    {
        int points = pointsOverride ?? ActionRegistry.GetDefaultPoints(action);
        if (points != 0)
            ctx.Scores.AddPlayerScore(steamId, points);

        GameEvents.OnPlayerScored?.Invoke(new PlayerScoredEvent
        {
            SessionId = ctx.SessionId,
            SteamId = steamId,
            Points = points,
            TotalScore = ctx.Scores.GetPlayerScore(steamId),
            Reason = action.ToString().ToLowerInvariant(),
            Action = action
        });

        GameEvents.OnActionPerformed?.Invoke(new ActionPerformedEvent
        {
            SessionId = ctx.SessionId,
            SteamId = steamId,
            Action = action,
            ModeId = ModeId,
            Points = points,
            PlayerEntity = playerEntity
        });
    }
}
