using System.Collections.Generic;
using BattleLuck.Models;
using Unity.Mathematics;

/// <summary>
/// Gauntlet — PvE wave survival mode.
/// Players fight through waves of enemies. Score by kills. Survive all waves to win.
/// </summary>
public sealed class GauntletMode : GameModeBase
{
    public override string ModeId => "gauntlet";
    public override string DisplayName => "Gauntlet";

    private readonly WaveController _waves = new();
    private readonly TimerController _timer = new();
    private float3 _spawnCenter;

    public override void OnStart(GameModeContext ctx)
    {
        var config = ctx.State.TryGetValue("config", out var cfg) && cfg is ModeConfig mc ? mc : ConfigLoader.Load(ModeId);
        var rules = config.Session.Rules;

        int waveCount = rules.MatchDurationMinutes > 0 ? Math.Max(3, rules.MatchDurationMinutes) : 5;
        int timeLimitSec = rules.MatchDurationMinutes * 60;
        if (timeLimitSec <= 0) timeLimitSec = 300;

        var zone = config.Zones.Zones.FirstOrDefault();
        _spawnCenter = zone?.TeleportSpawn?.ToFloat3() ?? new float3(0, 0, 0);

        var waves = new List<WaveDefinition>();
        for (int i = 1; i <= waveCount; i++)
        {
            var prefabs = SpawnController.GetEnemiesForWave(i);
            waves.Add(new WaveDefinition
            {
                WaveNumber = i,
                EnemyCount = 3 + i * 2,
                Prefabs = prefabs.Select(p => p.GuidHash.ToString()).ToList(),
                DelaySeconds = i == 1 ? 3 : 5
            });
        }
        _waves.Configure(waves);

        if (timeLimitSec > 0)
        {
            _timer.Start(timeLimitSec);
            ctx.TimeLimitSeconds = timeLimitSec;
        }

        ctx.Broadcast?.Invoke($"GAUNTLET — Survive {waveCount} waves! Time: {_timer.FormatRemaining()}");
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
            ctx.Broadcast?.Invoke("Time's up! Gauntlet failed.");
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

        // Actually spawn enemies using SpawnController
        if (ctx.State.TryGetValue("spawner", out var sp) && sp is SpawnController spawner)
        {
            var prefabs = SpawnController.GetEnemiesForWave(waveDef.WaveNumber);
            spawner.SpawnWave(prefabs, waveDef.EnemyCount, _spawnCenter, 8f);
        }
    }

    public override void OnPlayerDowned(GameModeContext ctx, ulong victimSteamId, ulong? killerSteamId)
    {
    }

    public override void OnEnd(GameModeContext ctx)
    {
        _timer.Stop();
        var result = _waves.IsComplete ? "victory" : (ctx.State.TryGetValue("result", out var r) ? r?.ToString() : "defeat");
        var leaderboard = ctx.Scores.GetLeaderboard();
        var topPlayer = leaderboard.Count > 0 ? leaderboard[0] : 0UL;

        ctx.Broadcast?.Invoke($"Gauntlet {result}! Waves: {_waves.CurrentWave}/{_waves.TotalWaves}");

        GameEvents.OnModeEnded?.Invoke(new ModeEndedEvent
        {
            SessionId = ctx.SessionId,
            ModeId = ModeId,
            WinnerSteamId = topPlayer != 0 ? topPlayer : null
        });
    }

    public override void OnReset(GameModeContext ctx)
    {
        _waves.Reset();
        _timer.Reset();
        ctx.Scores.Reset();
    }

    /// <summary>Exposed for commands to record an enemy killed.</summary>
    public bool RecordEnemyKill(GameModeContext ctx, ulong killerSteamId, int points = 10)
    {
        ScoreAction(ctx, killerSteamId, ActionType.WaveKill, points);
        bool waveCleared = _waves.RecordKill(ctx);
        if (waveCleared)
            ScoreAction(ctx, killerSteamId, ActionType.WaveClear, 0);
        return waveCleared;
    }

    public WaveController Waves => _waves;
    public TimerController Timer => _timer;
}
