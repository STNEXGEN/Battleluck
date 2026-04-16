using System.Collections.Generic;
using BattleLuck.Models;
using Unity.Mathematics;

/// <summary>
/// Trials — Timed PvE challenge mode.
/// Players race to complete objectives (kills, captures) within a time limit.
/// Time bonuses for speed. Score = objectives + time remaining.
/// </summary>
public sealed class TrialsMode : GameModeBase
{
    public override string ModeId => "trials";
    public override string DisplayName => "Trials";

    private readonly TimerController _timer = new();
    private readonly WaveController _waves = new();
    private int _objectiveTarget;
    private int _objectivesCompleted;
    private float3 _spawnCenter;

    public override void OnStart(GameModeContext ctx)
    {
        var config = ctx.State.TryGetValue("config", out var cfg) && cfg is ModeConfig mc ? mc : ConfigLoader.Load(ModeId);
        var rules = config.Session.Rules;

        int timeLimitSec = rules.MatchDurationMinutes * 60;
        if (timeLimitSec <= 0) timeLimitSec = 180;

        int waveCount = Math.Max(3, rules.MatchDurationMinutes);
        _objectiveTarget = waveCount;
        _objectivesCompleted = 0;

        var zone = config.Zones.Zones.FirstOrDefault();
        _spawnCenter = zone?.TeleportSpawn?.ToFloat3() ?? new float3(0, 0, 0);

        _timer.Start(timeLimitSec);
        ctx.TimeLimitSeconds = timeLimitSec;

        var waves = new List<WaveDefinition>();
        for (int i = 1; i <= waveCount; i++)
        {
            waves.Add(new WaveDefinition
            {
                WaveNumber = i,
                EnemyCount = 2 + i,
                DelaySeconds = 2
            });
        }
        _waves.Configure(waves);

        ctx.Broadcast?.Invoke($"TRIALS — Complete {_objectiveTarget} objectives! Time: {_timer.FormatRemaining()}");
        GameEvents.OnModeStarted?.Invoke(new ModeStartedEvent
        {
            SessionId = ctx.SessionId,
            ModeId = ModeId,
        });

        DoSpawnWave(ctx);
    }

    public override void OnTick(GameModeContext ctx, float deltaSeconds)
    {
        if (_timer.IsExpired)
        {
            ctx.State["result"] = "timeout";
            return;
        }

        if (!_waves.IsWaveActive && !_waves.IsComplete)
        {
            DoSpawnWave(ctx);
        }
    }

    private void DoSpawnWave(GameModeContext ctx)
    {
        var waveDef = _waves.StartNextWave(ctx);
        if (waveDef == null) return;

        if (ctx.State.TryGetValue("spawner", out var sp) && sp is SpawnController spawner)
        {
            var prefabs = SpawnController.GetEnemiesForWave(waveDef.WaveNumber);
            spawner.SpawnWave(prefabs, waveDef.EnemyCount, _spawnCenter, 6f);
        }
    }

    /// <summary>Record an objective completion (wave cleared, boss killed, etc).</summary>
    public void CompleteObjective(GameModeContext ctx, ulong? completedBySteamId = null)
    {
        _objectivesCompleted++;

        int bonusSec = (int)(_timer.RemainingSeconds * 0.1);
        if (bonusSec > 0)
        {
            _timer.AddBonus(bonusSec, ctx);
        }

        if (completedBySteamId.HasValue)
        {
            int points = 50 + bonusSec;
            ScoreAction(ctx, completedBySteamId.Value, ActionType.ObjectiveComplete, points);
            if (bonusSec > 0)
                ScoreAction(ctx, completedBySteamId.Value, ActionType.TimeBonus, 0);
        }

        ctx.Broadcast?.Invoke($"✅ Objective {_objectivesCompleted}/{_objectiveTarget} complete! +{bonusSec}s bonus");

        if (_objectivesCompleted >= _objectiveTarget)
        {
            ctx.State["result"] = "victory";
        }
    }

    /// <summary>Record an enemy kill from a wave.</summary>
    public bool RecordEnemyKill(GameModeContext ctx, ulong killerSteamId, int points = 5)
    {
        ScoreAction(ctx, killerSteamId, ActionType.TrialKill, points);
        bool waveCleared = _waves.RecordKill(ctx);
        if (waveCleared)
        {
            CompleteObjective(ctx, killerSteamId);
        }
        return waveCleared;
    }

    public override void OnEnd(GameModeContext ctx)
    {
        _timer.Stop();
        var result = _objectivesCompleted >= _objectiveTarget ? "victory" : "defeat";

        if (result == "victory")
        {
            foreach (var steamId in ctx.Players)
            {
                int timeBonus = (int)_timer.RemainingSeconds;
                ctx.Scores.AddPlayerScore(steamId, timeBonus);
            }
        }

        var leaderboard = ctx.Scores.GetLeaderboard();
        var topPlayer = leaderboard.Count > 0 ? leaderboard[0] : 0UL;

        ctx.Broadcast?.Invoke($"Trials {result}! Objectives: {_objectivesCompleted}/{_objectiveTarget}");
        GameEvents.OnModeEnded?.Invoke(new ModeEndedEvent
        {
            SessionId = ctx.SessionId,
            ModeId = ModeId,
            WinnerSteamId = topPlayer != 0 ? topPlayer : null
        });
    }

    public override void OnReset(GameModeContext ctx)
    {
        _timer.Reset();
        _waves.Reset();
        _objectivesCompleted = 0;
        ctx.Scores.Reset();
    }
}
