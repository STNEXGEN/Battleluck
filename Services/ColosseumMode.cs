using System.Linq;
using BattleLuck.Models;

/// <summary>
/// Colosseum — Ranked 1v1 duel mode with Elo ratings.
/// Best-of-N rounds. Winner gains Elo, loser loses Elo.
/// </summary>
public sealed class ColosseumMode : GameModeBase
{
    public override string ModeId => "colosseum";
    public override string DisplayName => "Colosseum";

    private readonly EloController _elo = new();
    private readonly RoundManager _rounds = new();
    private readonly TimerController _timer = new();

    private ulong _player1;
    private ulong _player2;

    public override void OnStart(GameModeContext ctx)
    {
        var config = ctx.State.TryGetValue("config", out var cfg) && cfg is ModeConfig mc ? mc : ConfigLoader.Load(ModeId);
        var rules = config.Session.Rules;

        int totalRounds = rules.LivesPerPlayer > 0 ? rules.LivesPerPlayer : 3;
        int roundTimeSec = rules.MatchDurationMinutes > 0 ? (rules.MatchDurationMinutes * 60 / totalRounds) : 120;

        _rounds.Initialize(totalRounds);
        ctx.Rounds.Initialize(totalRounds);

        var players = ctx.Players.ToArray();
        _player1 = players.Length > 0 ? players[0] : 0;
        _player2 = players.Length > 1 ? players[1] : 0;

        _timer.Start(roundTimeSec);

        int p1Elo = _elo.GetRating(_player1);
        int p2Elo = _elo.GetRating(_player2);

        ctx.Broadcast?.Invoke($"COLOSSEUM — Best of {totalRounds}! [{_player1} ({p1Elo})] vs [{_player2} ({p2Elo})]");
        GameEvents.OnModeStarted?.Invoke(new ModeStartedEvent
        {
            SessionId = ctx.SessionId,
            ModeId = ModeId,
        });
    }

    public override void OnTick(GameModeContext ctx, float deltaSeconds)
    {
        if (_timer.IsExpired)
        {
            ctx.Broadcast?.Invoke("Round timeout — draw!");
            AdvanceRound(ctx, 0);
        }
    }

    public override void OnPlayerDowned(GameModeContext ctx, ulong victimSteamId, ulong? killerSteamId)
    {
        if (!killerSteamId.HasValue) return;

        ulong winner = killerSteamId.Value;
        int winnerId = winner == _player1 ? 1 : 2;
        ulong loser = winner == _player1 ? _player2 : _player1;

        ScoreAction(ctx, winner, ActionType.DuelWin, 1);
        ScoreAction(ctx, loser, ActionType.DuelLoss, 0);
        ctx.Broadcast?.Invoke($"Round {_rounds.CurrentRound} — Player {winnerId} wins!");

        AdvanceRound(ctx, winnerId);
    }

    private void AdvanceRound(GameModeContext ctx, int winnerId)
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

        if (_rounds.HasMajority(1) || _rounds.HasMajority(2) || !moreRounds)
        {
            int p1Wins = _rounds.GetWinCount(1);
            int p2Wins = _rounds.GetWinCount(2);
            ulong matchWinner = p1Wins >= p2Wins ? _player1 : _player2;
            ulong matchLoser = matchWinner == _player1 ? _player2 : _player1;

            var (winElo, loseElo) = _elo.RecordMatch(matchWinner, matchLoser, ctx);

            ScoreAction(ctx, matchWinner, ActionType.EloGain, 0);

            ctx.State["result"] = "complete";
            ctx.State["winner"] = matchWinner;
            ctx.State["winner_elo"] = winElo;
            ctx.State["loser_elo"] = loseElo;
        }
        else
        {
            int roundTimeSec = ctx.State.TryGetValue("round_time", out var rt) && rt is int r ? r : 120;
            _timer.Start(roundTimeSec);
            ctx.Broadcast?.Invoke($"🔄 Round {_rounds.CurrentRound} — Fight!");
        }
    }

    public override void OnEnd(GameModeContext ctx)
    {
        _timer.Stop();

        ulong winner = ctx.State.TryGetValue("winner", out var w) && w is ulong ws ? ws : 0UL;
        int winnerElo = ctx.State.TryGetValue("winner_elo", out var we) && we is int we2 ? we2 : 0;

        ctx.Broadcast?.Invoke($"Colosseum complete! Winner: {winner} (Elo: {winnerElo})");
        GameEvents.OnModeEnded?.Invoke(new ModeEndedEvent
        {
            SessionId = ctx.SessionId,
            ModeId = ModeId,
            WinnerSteamId = winner != 0 ? winner : null
        });
    }

    public override void OnReset(GameModeContext ctx)
    {
        _timer.Reset();
        _rounds.Reset();
        ctx.Scores.Reset();
        _player1 = 0;
        _player2 = 0;
    }

    public EloController Elo => _elo;
    public RoundManager Rounds => _rounds;
    public TimerController Timer => _timer;
}
