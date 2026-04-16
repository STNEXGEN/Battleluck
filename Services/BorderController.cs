using Stunlock.Core;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Enforces zone boundary via DOT buffs with proper lifetime control (VAMP pattern).
/// Distance bands from zone center:
///   - Inside warningRadius: safe (no buff)
///   - Warning to danger: light DOT (Ignite) with timed duration
///   - Danger to exit: heavy DOT (Ignite + Slow) with timed duration
///   - Beyond exit: teleport back to center (if enabled)
///
/// Also applies a persistent zone indicator buff (Buff_InCombat) to all players
/// inside the zone, removed when they leave.
/// </summary>
public sealed class BorderController
{
    // Track which band each player is in to avoid re-applying buffs every tick
    readonly Dictionary<ulong, BandLevel> _playerBands = new();

    // Last safe position per player — used to respawn after boundary DOT death
    readonly Dictionary<ulong, float3> _lastSafePositions = new();

    // Track zone indicator buff per player
    readonly HashSet<ulong> _hasZoneIndicator = new();

    // Default buff durations (seconds)
    const float WARNING_BUFF_DURATION = 5f;
    const float DANGER_BUFF_DURATION = 8f;
    const float OUTSIDE_BUFF_DURATION = 0f; // infinite while outside

    enum BandLevel { Safe, Warning, Danger, Outside }

    /// <summary>
    /// Tick boundary enforcement for all players in a zone.
    /// Call once per session tick when DOT policy is active.
    /// If Buff_General_Ignite is invalid (PrefabGUID.Empty), uses HP drain instead.
    /// </summary>
    public void Tick(GameModeContext ctx, IEnumerable<Entity> players, float3 center, float radius, float exitRadius, DotBoundaryConfig dot)
    {
        float warningDist = radius * dot.WarningRadiusPercent;
        float dangerDist = radius * dot.DangerRadiusPercent;
        bool hasIgniteBuff = Prefabs.Buff_General_Ignite != PrefabGUID.Empty;

        foreach (var player in players)
        {
            var pos = player.GetPosition();
            float dist = math.distance(new float2(pos.x, pos.z), new float2(center.x, center.z));
            ulong steamId = player.GetSteamId();

            BandLevel newBand;
            if (dist <= warningDist)
                newBand = BandLevel.Safe;
            else if (dist <= dangerDist)
                newBand = BandLevel.Warning;
            else if (dist <= exitRadius)
                newBand = BandLevel.Danger;
            else
                newBand = BandLevel.Outside;

            _playerBands.TryGetValue(steamId, out var oldBand);

            if (newBand == oldBand) continue;
            _playerBands[steamId] = newBand;

            switch (newBand)
            {
                case BandLevel.Safe:
                    if (hasIgniteBuff) player.TryRemoveBuff(Prefabs.Buff_General_Ignite);
                    player.TryRemoveBuff(Prefabs.Buff_General_Slow);
                    _lastSafePositions[steamId] = pos;
                    if (!_hasZoneIndicator.Contains(steamId))
                    {
                        player.BuffEntity(Prefabs.Buff_InCombat, out _, duration: 0f, persistThroughDeath: true);
                        _hasZoneIndicator.Add(steamId);
                    }
                    break;

                case BandLevel.Warning:
                    if (hasIgniteBuff)
                        player.RemoveAndAddBuff(Prefabs.Buff_General_Ignite, duration: WARNING_BUFF_DURATION);
                    else
                        player.DealDamagePercent(0.03f); // Fallback: 3% HP per band transition
                    player.TryRemoveBuff(Prefabs.Buff_General_Slow);
                    if (_hasZoneIndicator.Remove(steamId))
                        player.TryRemoveBuff(Prefabs.Buff_InCombat);
                    break;

                case BandLevel.Danger:
                    if (hasIgniteBuff)
                        player.RemoveAndAddBuff(Prefabs.Buff_General_Ignite, duration: DANGER_BUFF_DURATION);
                    else
                        player.DealDamagePercent(0.06f); // Fallback: 6% HP per band transition
                    player.RemoveAndAddBuff(Prefabs.Buff_General_Slow, duration: DANGER_BUFF_DURATION);
                    if (_hasZoneIndicator.Remove(steamId))
                        player.TryRemoveBuff(Prefabs.Buff_InCombat);
                    break;

                case BandLevel.Outside:
                    if (dot.TeleportOnExit)
                    {
                        player.SetPosition(center);
                        if (hasIgniteBuff) player.TryRemoveBuff(Prefabs.Buff_General_Ignite);
                        player.TryRemoveBuff(Prefabs.Buff_General_Slow);
                        _playerBands[steamId] = BandLevel.Safe;
                        player.BuffEntity(Prefabs.Buff_InCombat, out _, duration: 0f, persistThroughDeath: true);
                        _hasZoneIndicator.Add(steamId);
                        BattleLuckPlugin.LogInfo($"[BorderController] Player {steamId} teleported back — exceeded exit radius.");
                    }
                    else
                    {
                        if (hasIgniteBuff)
                            player.BuffEntity(Prefabs.Buff_General_Ignite, out _, duration: OUTSIDE_BUFF_DURATION);
                        else
                            player.DealDamagePercent(0.10f); // Fallback: 10% HP per tick
                        player.BuffEntity(Prefabs.Buff_General_Slow, out _, duration: OUTSIDE_BUFF_DURATION);
                        if (_hasZoneIndicator.Remove(steamId))
                            player.TryRemoveBuff(Prefabs.Buff_InCombat);
                    }
                    break;
            }
        }
    }

    /// <summary>Remove all DOT buffs from tracked players and clear state.</summary>
    public void CleanupAll(IEnumerable<Entity> players)
    {
        bool hasIgniteBuff = Prefabs.Buff_General_Ignite != PrefabGUID.Empty;
        foreach (var player in players)
        {
            if (hasIgniteBuff) player.TryRemoveBuff(Prefabs.Buff_General_Ignite);
            player.TryRemoveBuff(Prefabs.Buff_General_Slow);
            player.TryRemoveBuff(Prefabs.Buff_InCombat);
        }
        _playerBands.Clear();
        _lastSafePositions.Clear();
        _hasZoneIndicator.Clear();
    }

    /// <summary>
    /// Check if a dead player was in a DOT boundary band.
    /// If so, teleport them to their last safe position, remove buffs, and reset band.
    /// Returns true if handled (was a boundary death).
    /// </summary>
    public bool HandleBoundaryDeath(Entity player, ulong steamId)
    {
        if (!_playerBands.TryGetValue(steamId, out var band)) return false;
        if (band == BandLevel.Safe) return false;

        // Remove DOT buffs
        if (Prefabs.Buff_General_Ignite != PrefabGUID.Empty)
            player.TryRemoveBuff(Prefabs.Buff_General_Ignite);
        player.TryRemoveBuff(Prefabs.Buff_General_Slow);

        // Teleport to last safe position
        if (_lastSafePositions.TryGetValue(steamId, out var safePos))
        {
            player.SetPosition(safePos);
            BattleLuckPlugin.LogInfo($"[BorderController] Player {steamId} died in boundary — returned to last safe position.");
        }

        _playerBands[steamId] = BandLevel.Safe;
        return true;
    }

    /// <summary>Returns true if the player is currently in a DOT band (Warning/Danger/Outside).</summary>
    public bool IsInBoundary(ulong steamId)
    {
        return _playerBands.TryGetValue(steamId, out var band) && band != BandLevel.Safe;
    }

    /// <summary>Remove tracking for a single player (on zone exit).</summary>
    public void RemovePlayer(ulong steamId)
    {
        _playerBands.Remove(steamId);
    }

    /// <summary>Reset all state.</summary>
    public void Reset()
    {
        _playerBands.Clear();
    }
}
