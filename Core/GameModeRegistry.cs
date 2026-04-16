using System;
using System.Collections.Generic;

/// <summary>
/// Central registry for all game modes. Maps mode IDs to mode instances.
/// </summary>
public sealed class GameModeRegistry
{
    private readonly Dictionary<string, GameModeBase> _modes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Register a game mode. Replaces any existing mode with the same ID.</summary>
    public void Register(GameModeBase mode)
    {
        _modes[mode.ModeId] = mode;
        BattleLuckPlugin.LogInfo($"[GameModeRegistry] Registered mode: {mode.ModeId} ({mode.DisplayName})");
    }

    /// <summary>Resolve a mode by its ID.</summary>
    public GameModeBase? Resolve(string modeId)
    {
        return _modes.TryGetValue(modeId, out var mode) ? mode : null;
    }

    /// <summary>Check if a mode is registered.</summary>
    public bool IsRegistered(string modeId) => _modes.ContainsKey(modeId);

    /// <summary>Get all registered mode IDs.</summary>
    public IReadOnlyCollection<string> GetRegisteredModes() => _modes.Keys;

    /// <summary>Get all registered modes.</summary>
    public IReadOnlyDictionary<string, GameModeBase> All => _modes;
}
