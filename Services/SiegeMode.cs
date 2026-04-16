using System.Linq;
using BattleLuck.Models;

/// <summary>
/// Siege — Team PvP with capture-point objectives.
/// Two teams compete to capture and hold objectives. First to hold all, or most at time-out, wins.
/// </summary>
public sealed class SiegeMode : GameModeBase
{
    public override string ModeId => "siege";
    public override string DisplayName => "Siege";

    private readonly ObjectiveController _objectives = new();
    private readonly TimerController _timer = new();
    private readonly RoundManager _rounds = new();

    public override void OnStart(GameModeContext ctx)
    {
        var config = ctx.State.TryGetValue("config", out var cfg) && cfg is ModeConfig mc ? mc : ConfigLoader.Load(ModeId);
        var rules = config.Session.Rules;

        int timeLimitSec = rules.MatchDurationMinutes * 60;
        if (timeLimitSec <= 0) timeLimitSec = 600;
        int totalRounds = 1;

        _timer.Start(timeLimitSec);
        ctx.TimeLimitSeconds = timeLimitSec;
        _rounds.Initialize(totalRounds);
        ctx.Rounds.Initialize(totalRounds);

        _objectives.Clear();
        _objectives.AddObjective("north", 0, 50);
        _objectives.AddObjective("center", 0, 0);
        _objectives.AddObjective("south", 0, -50);

        ctx.Broadcast?.Invoke($"SIEGE — Capture all objectives! Teams: 2 | Rounds: {totalRounds} | Time: {_timer.FormatRemaining()}");
        GameEvents.OnModeStarted?.Invoke(new ModeStartedEvent
        {
            SessionId = ctx.SessionId,
            ModeId = ModeId,
        });
    }

    public override void OnTick(GameModeContext ctx, float deltaSeconds)
    {
        for (int teamId = 1; teamId <= 2; teamId++)
        {
            if (_objectives.TeamControlsAll(teamId))
            {
                ctx.Scores.AddTeamScore(teamId, 1);
                ctx.Broadcast?.Invoke($"Team {teamId} controls all objectives! Round won!");
                EndCurrentRound(ctx, teamId);
                return;
            }
        }

        if (_timer.IsExpired)
        {
            int team1Count = _objectives.GetTeamObjectiveCount(1);
            int team2Count = _objectives.GetTeamObjectiveCount(2);
            int roundWinner = team1Count >= team2Count ? 1 : 2;
            ctx.Scores.AddTeamScore(roundWinner, 1);
            ctx.Broadcast?.Invoke($"Time's up! Team {roundWinner} wins the round ({_objectives.GetTeamObjectiveCount(roundWinner)}/{_objectives.TotalObjectives} objectives)");
            EndCurrentRound(ctx, roundWinner);
        }
    }

    private void EndCurrentRound(GameModeContext ctx, int winnerId)
    {
        GameEvents.OnRoundEnded?.Invoke(new RoundEndedEvent
        {
            SessionId = ctx.SessionId,
            ModeId = ModeId,
            RoundNumber = _rounds.CurrentRound,
            WinnerId = winnerId
        });

        bool moreRounds = _rounds.CompleteRound(winnerId);
        ctx.Rounds.CompleteRound(winnerId);

        if (!moreRounds || _rounds.HasMajority(winnerId))
        {
            ctx.State["result"] = "complete";
            ctx.State["winner_team"] = winnerId;
        }
        else
        {
            _objectives.Reset();
            _timer.Start(ctx.TimeLimitSeconds);
            ctx.Broadcast?.Invoke($"🔄 Round {_rounds.CurrentRound} starting!");
        }
    }

    public override void OnPlayerDowned(GameModeContext ctx, ulong victimSteamId, ulong? killerSteamId)
    {
        if (killerSteamId.HasValue && ctx.Teams.TryGetValue(killerSteamId.Value, out var killerTeam))
        {
            ScoreAction(ctx, killerSteamId.Value, ActionType.Kill, 1);
        }
        ScoreAction(ctx, victimSteamId, ActionType.Death, 0);
    }

    public override void OnEnd(GameModeContext ctx)
    {
        _timer.Stop();
        int winnerTeam = ctx.Scores.GetLeadingTeam();

        ctx.Broadcast?.Invoke($"Siege complete! Team {winnerTeam} wins!");
        GameEvents.OnModeEnded?.Invoke(new ModeEndedEvent
        {
            SessionId = ctx.SessionId,
            ModeId = ModeId,
            WinnerTeamId = winnerTeam
        });
    }

    public override void OnReset(GameModeContext ctx)
    {
        _timer.Reset();
        _objectives.Clear();
        _rounds.Reset();
        ctx.Scores.Reset();
    }

    public ObjectiveController Objectives => _objectives;
    public TimerController Timer => _timer;
    public RoundManager Rounds => _rounds;
}
