/// <summary>
/// Centralized logger wrapping BattleLuckPlugin.Log with severity levels.
/// </summary>
public static class BattleLuckLogger
{
    public static void Info(string message)
        => BattleLuckPlugin.LogInfo(message);

    public static void Warning(string message)
        => BattleLuckPlugin.LogWarning(message);

    public static void Critical(string message)
        => BattleLuckPlugin.LogError($"[CRITICAL] {message}");

    public static void Debug(string message)
    {
#if DEBUG
        BattleLuckPlugin.LogInfo($"[DEBUG] {message}");
#endif
    }
}
