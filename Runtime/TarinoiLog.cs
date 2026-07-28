namespace Tarinoi
{
    /// <summary>
    /// Verbosity levels for <see cref="TarinoiLog"/>, ordered least to most severe.
    /// A message is emitted when its level is at least the configured level.
    /// </summary>
    public enum TarinoiLogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3,
        Off = 4,
    }

    /// <summary>
    /// Level-gated logging for everything Tarinoi writes to the Unity console.
    /// Every message carries a <c>[Tarinoi]</c> prefix so it can be filtered.
    /// </summary>
    /// <remarks>
    /// <see cref="Level"/> is set from the project's Tarinoi settings during
    /// runtime configuration; until then it defaults to <see cref="TarinoiLogLevel.Info"/>.
    /// Warnings and errors go through Unity's warning/error channels so they surface
    /// in the console filter and the Debugger, matching how the Godot plugin behaves.
    /// </remarks>
    public static class TarinoiLog
    {
        const string DebugTag = "<color=#888888>[DEBUG]</color>";
        const string InfoTag = "<color=#4d9de0>[INFO] </color>";

        public static TarinoiLogLevel Level { get; set; } = TarinoiLogLevel.Info;

        public static void Debug(string message)
        {
            if (Level <= TarinoiLogLevel.Debug)
            {
                UnityEngine.Debug.Log($"{DebugTag} [Tarinoi] {message}");
            }
        }

        public static void Info(string message)
        {
            if (Level <= TarinoiLogLevel.Info)
            {
                UnityEngine.Debug.Log($"{InfoTag} [Tarinoi] {message}");
            }
        }

        public static void Warn(string message)
        {
            if (Level <= TarinoiLogLevel.Warn)
            {
                UnityEngine.Debug.LogWarning($"[WARN]  [Tarinoi] {message}");
            }
        }

        public static void Error(string message)
        {
            if (Level <= TarinoiLogLevel.Error)
            {
                UnityEngine.Debug.LogError($"[ERROR] [Tarinoi] {message}");
            }
        }
    }
}
