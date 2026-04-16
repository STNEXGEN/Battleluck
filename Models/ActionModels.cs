using ProjectM;

namespace BattleLuck.Models;

/// <summary>
/// All trackable player actions across BattleLuck game modes.
/// Used by quest/objective systems and AI analytics.
/// </summary>
public enum ActionType
{
    // ── Universal ────────────────────────────────────────────────────
    Kill,
    Death,
    Assist,
    Survive,

    // ── Bloodbath ────────────────────────────────────────────────────
    LootCrate,
    BossKill,
    EliteKill,
    Elimination,

    // ── Colosseum ────────────────────────────────────────────────────
    DuelWin,
    DuelLoss,
    EloGain,

    // ── Gauntlet ─────────────────────────────────────────────────────
    WaveKill,
    WaveClear,
    WaveSurvive,

    // ── Siege ────────────────────────────────────────────────────────
    ObjectiveCapture,
    ObjectiveDefend,
    TeamWipeRound,

    // ── Trials ───────────────────────────────────────────────────────
    TrialKill,
    ObjectiveComplete,
    TimeBonus,
    SpeedBonus
}

/// <summary>
/// Curated SequenceGUID constants from V Rising for BattleLuck event VFX.
/// </summary>
public static class ActionSequences
{
    // ── Combat ───────────────────────────────────────────────────────
    public static readonly SequenceGUID Kill_Impact           = new(-476819435);   // SEQ_Shared_Object_Hit
    public static readonly SequenceGUID Death_Dissolve        = new(1498540414);   // SEQ_Shared_AdvancedDeath
    public static readonly SequenceGUID Assist_Glow           = new(893118035);    // SEQ_Shared_Buff
    public static readonly SequenceGUID Elimination_Burst     = new(-906728229);   // SEQ_Shared_Object_Destroy

    // ── Level / Score ────────────────────────────────────────────────
    public static readonly SequenceGUID LevelUp               = new(-1046001899);  // SEQ_Vampire_LevelUp
    public static readonly SequenceGUID EloGain_Sparkle       = new(1845986301);   // SEQ_Shared_Buff_1
    public static readonly SequenceGUID ScoreFlash            = new(785779005);    // SEQ_Shared_Buff_2

    // ── Boss / Elite ─────────────────────────────────────────────────
    public static readonly SequenceGUID BossKill_Explosion    = new(-1790763425);  // SEQ_Shared_AdvancedDeath_2
    public static readonly SequenceGUID BossWounded           = new(-868779850);   // SEQ_Shared_Boss_Wounded
    public static readonly SequenceGUID EliteKill_Shatter     = new(-1654901741);  // SEQ_Shared_AdvancedDeath_3
    public static readonly SequenceGUID BossSpawn_Aura        = new(1127550179);   // SEQ_Vampire_FeedBoss_Trigger_Complete

    // ── Loot / Pickup ────────────────────────────────────────────────
    public static readonly SequenceGUID LootCrate_Open        = new(1268037080);   // SEQ_PickupItem_01_7
    public static readonly SequenceGUID ItemPickup            = new(-1430997544);   // SEQ_PickupItem_01_15

    // ── Objectives ───────────────────────────────────────────────────
    public static readonly SequenceGUID ObjectiveCapture_Flag = new(744374235);    // SEQ_ContestArena_ActiveFlag_Team01
    public static readonly SequenceGUID ObjectiveDefend_Shield= new(1427675323);   // SEQ_ContestArena_ActiveFlag_Team02
    public static readonly SequenceGUID ObjectiveComplete_Win = new(922857755);    // SEQ_Contest_Start
    public static readonly SequenceGUID ContestCountdown      = new(-1142510568);  // SEQ_ContestArenaCountdown
    public static readonly SequenceGUID ContestBossCountdown  = new(-679471019);   // SEQ_ContestBossCountdown

    // ── Wave / Gauntlet ──────────────────────────────────────────────
    public static readonly SequenceGUID WaveKill_Hit          = new(-173616274);   // SEQ_Shared_Object_Hit_1
    public static readonly SequenceGUID WaveClear_Pulse       = new(2049283058);   // SEQ_Shared_Buff_3
    public static readonly SequenceGUID WaveSurvive_Shield    = new(-723783303);   // SEQ_Shared_Buff_4

    // ── Duel / Colosseum ─────────────────────────────────────────────
    public static readonly SequenceGUID DuelWin_Triumph       = new(-192572431);   // SEQ_Contest_Immaterial
    public static readonly SequenceGUID DuelLoss_Fade         = new(-262774549);   // SEQ_Contest_Rematerialize
    public static readonly SequenceGUID DuelStart_Flash       = new(1382804845);   // SEQ_Blink_Standard_White_1

    // ── Siege ────────────────────────────────────────────────────────
    public static readonly SequenceGUID TeamFlag_01           = new(2136921210);   // SEQ_ArenaFlag_OccupiedSlotActive_Team01
    public static readonly SequenceGUID TeamFlag_02           = new(617961739);    // SEQ_ArenaFlag_OccupiedSlotActive_Team02
    public static readonly SequenceGUID TeamFlag_03           = new(-146303596);   // SEQ_ArenaFlag_OccupiedSlotActive_Team03
    public static readonly SequenceGUID TeamFlag_04           = new(478520190);    // SEQ_ArenaFlag_OccupiedSlotActive_Team04
    public static readonly SequenceGUID TeamWipe_Shake        = new(-648034606);   // SEQ_Shared_Shake_Feedback_Small_6

    // ── Trials / Speed / Time ────────────────────────────────────────
    public static readonly SequenceGUID TimeBonus_Glow        = new(-217996174);   // SEQ_Shared_Buff_5
    public static readonly SequenceGUID SpeedBonus_Dash       = new(-207886408);   // SEQ_Shared_Dash_Phase_1
    public static readonly SequenceGUID TrialKill_Slash       = new(-1765901933);  // SEQ_Fireball_Cast_01

    // ── Environment / Utility ────────────────────────────────────────
    public static readonly SequenceGUID Teleport_Arrive       = new(-1780773164);  // SEQ_GeneralTeleport_TravelEnd
    public static readonly SequenceGUID Waypoint_Active       = new(-1096288616);  // SEQ_Waypoint_Active
    public static readonly SequenceGUID Dawn_Warning          = new(392579464);    // SEQ_Dawn
    public static readonly SequenceGUID Dusk_Signal           = new(-1603359499);  // SEQ_Dusk
    public static readonly SequenceGUID Coffin_Respawn        = new(1043723895);   // SEQ_Workstation_Coffin_Respawn
    public static readonly SequenceGUID ShrinkZone_Effect     = new(-850896030);   // SEQ_Shared_OverTime_Effect
    public static readonly SequenceGUID Stun_VFX              = new(1972314495);   // SEQ_Shared_Buff_Stun
    public static readonly SequenceGUID Poison_Debuff         = new(-174380957);   // SEQ_Poison_Debuff
    public static readonly SequenceGUID BellRing              = new(1968734759);   // SEQ_BellRing

    /// <summary>
    /// Maps each ActionType to its primary VFX SequenceGUID.
    /// </summary>
    public static readonly Dictionary<ActionType, SequenceGUID> ActionVFX = new()
    {
        { ActionType.Kill,             Kill_Impact },
        { ActionType.Death,            Death_Dissolve },
        { ActionType.Assist,           Assist_Glow },
        { ActionType.Survive,          WaveSurvive_Shield },
        { ActionType.LootCrate,        LootCrate_Open },
        { ActionType.BossKill,         BossKill_Explosion },
        { ActionType.EliteKill,        EliteKill_Shatter },
        { ActionType.Elimination,      Elimination_Burst },
        { ActionType.DuelWin,          DuelWin_Triumph },
        { ActionType.DuelLoss,         DuelLoss_Fade },
        { ActionType.EloGain,          EloGain_Sparkle },
        { ActionType.WaveKill,         WaveKill_Hit },
        { ActionType.WaveClear,        WaveClear_Pulse },
        { ActionType.WaveSurvive,      WaveSurvive_Shield },
        { ActionType.ObjectiveCapture, ObjectiveCapture_Flag },
        { ActionType.ObjectiveDefend,  ObjectiveDefend_Shield },
        { ActionType.TeamWipeRound,    TeamWipe_Shake },
        { ActionType.TrialKill,        TrialKill_Slash },
        { ActionType.ObjectiveComplete,ObjectiveComplete_Win },
        { ActionType.TimeBonus,        TimeBonus_Glow },
        { ActionType.SpeedBonus,       SpeedBonus_Dash },
    };

    public static SequenceGUID GetVFX(ActionType type) =>
        ActionVFX.TryGetValue(type, out var seq) ? seq : Kill_Impact;
}

/// <summary>
/// Category grouping for display filtering.
/// </summary>
public enum ActionCategory
{
    Combat,
    Objective,
    Survival,
    Bonus
}

/// <summary>
/// Defines display metadata and point values for each action type per mode.
/// </summary>
public static class ActionRegistry
{
    public static readonly Dictionary<ActionType, ActionInfo> Actions = new()
    {
        // Universal
        { ActionType.Kill,             new("Kill",              ActionCategory.Combat,    "<color=#FF4444>Kill</color>",              1) },
        { ActionType.Death,            new("Death",             ActionCategory.Combat,    "<color=#888888>Death</color>",             0) },
        { ActionType.Assist,           new("Assist",            ActionCategory.Combat,    "<color=#FFAA00>Assist</color>",            0) },
        { ActionType.Survive,          new("Survive",           ActionCategory.Survival,  "<color=#44FF44>Survive</color>",           0) },

        // Bloodbath
        { ActionType.LootCrate,        new("Loot Crate",        ActionCategory.Objective, "<color=#FFD700>Loot Crate</color>",        5) },
        { ActionType.BossKill,         new("Boss Kill",         ActionCategory.Combat,    "<color=#FF00FF>Boss Kill</color>",         10) },
        { ActionType.EliteKill,        new("Elite Kill",        ActionCategory.Combat,    "<color=#FF6600>Elite Kill</color>",        3) },
        { ActionType.Elimination,      new("Elimination",       ActionCategory.Combat,    "<color=#FF0000>Elimination</color>",       0) },

        // Colosseum
        { ActionType.DuelWin,          new("Duel Win",          ActionCategory.Combat,    "<color=#00FFFF>Duel Win</color>",          1) },
        { ActionType.DuelLoss,         new("Duel Loss",         ActionCategory.Combat,    "<color=#666666>Duel Loss</color>",         0) },
        { ActionType.EloGain,          new("Elo Gain",          ActionCategory.Bonus,     "<color=#00FF88>Elo Gain</color>",          0) },

        // Gauntlet
        { ActionType.WaveKill,         new("Wave Kill",         ActionCategory.Combat,    "<color=#FF8844>Wave Kill</color>",         10) },
        { ActionType.WaveClear,        new("Wave Clear",        ActionCategory.Objective, "<color=#44FFFF>Wave Clear</color>",        0) },
        { ActionType.WaveSurvive,      new("Wave Survive",      ActionCategory.Survival,  "<color=#44FF44>Wave Survive</color>",      0) },

        // Siege
        { ActionType.ObjectiveCapture, new("Objective Capture", ActionCategory.Objective, "<color=#00CCFF>Objective Capture</color>", 0) },
        { ActionType.ObjectiveDefend,  new("Objective Defend",  ActionCategory.Objective, "<color=#00AAFF>Objective Defend</color>",  0) },
        { ActionType.TeamWipeRound,    new("Team Wipe Round",   ActionCategory.Combat,    "<color=#FF2222>Team Wipe</color>",         0) },

        // Trials
        { ActionType.TrialKill,        new("Trial Kill",        ActionCategory.Combat,    "<color=#FFAA44>Trial Kill</color>",        5) },
        { ActionType.ObjectiveComplete,new("Objective Complete", ActionCategory.Objective, "<color=#FFD700>Objective Complete</color>",50) },
        { ActionType.TimeBonus,        new("Time Bonus",        ActionCategory.Bonus,     "<color=#AAFFAA>Time Bonus</color>",        0) },
        { ActionType.SpeedBonus,       new("Speed Bonus",       ActionCategory.Bonus,     "<color=#AAFFEE>Speed Bonus</color>",       0) },
    };

    /// <summary>
    /// Which actions are valid for each game mode.
    /// </summary>
    public static readonly Dictionary<string, HashSet<ActionType>> ModeActions = new(StringComparer.OrdinalIgnoreCase)
    {
        { "bloodbath", new() { ActionType.Kill, ActionType.Death, ActionType.Assist, ActionType.Survive, ActionType.LootCrate, ActionType.BossKill, ActionType.EliteKill, ActionType.Elimination } },
        { "colosseum", new() { ActionType.Kill, ActionType.Death, ActionType.DuelWin, ActionType.DuelLoss, ActionType.EloGain } },
        { "gauntlet",  new() { ActionType.Kill, ActionType.Death, ActionType.Survive, ActionType.WaveKill, ActionType.WaveClear, ActionType.WaveSurvive } },
        { "siege",     new() { ActionType.Kill, ActionType.Death, ActionType.Assist, ActionType.ObjectiveCapture, ActionType.ObjectiveDefend, ActionType.TeamWipeRound } },
        { "trials",    new() { ActionType.Kill, ActionType.Death, ActionType.Survive, ActionType.TrialKill, ActionType.ObjectiveComplete, ActionType.TimeBonus, ActionType.SpeedBonus } },
    };

    public static string GetColoredName(ActionType type) =>
        Actions.TryGetValue(type, out var info) ? info.ColoredLabel : type.ToString();

    public static int GetDefaultPoints(ActionType type) =>
        Actions.TryGetValue(type, out var info) ? info.DefaultPoints : 0;

    public static bool IsValidForMode(string modeId, ActionType type) =>
        ModeActions.TryGetValue(modeId, out var set) && set.Contains(type);

    public static IEnumerable<ActionType> GetActionsForMode(string modeId) =>
        ModeActions.TryGetValue(modeId, out var set) ? set : Enumerable.Empty<ActionType>();
}

public sealed record ActionInfo(string Name, ActionCategory Category, string ColoredLabel, int DefaultPoints);
