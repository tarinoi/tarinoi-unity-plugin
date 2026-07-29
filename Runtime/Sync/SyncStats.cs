using System.Collections.Generic;

namespace Tarinoi
{
    // SyncStats and SyncProgress live in Tarinoi rather than Tarinoi.Sync because they
    // are what TarinoiRuntime's SyncCompleted and SyncProgress events hand to game code.
    // Under Tarinoi.Sync, subscribing to an event on a Tarinoi type needed a second using
    // and failed with an error that pointed at the payload rather than the event.

    /// <summary>What a sync changed. Reported on completion and useful in the console.</summary>
    public sealed class SyncStats
    {
        public int DocumentsUpserted;

        /// <summary>
        /// Documents removed from view. Counts both server-side deletions (tombstones)
        /// and documents stored as archived or moved, since all three disappear from
        /// the player's perspective.
        /// </summary>
        public int DocumentsDeleted;

        public int CollectionsUpdated;

        public readonly List<string> Warnings = new List<string>();

        public override string ToString() =>
            $"{DocumentsUpserted} upserted, {DocumentsDeleted} removed, "
            + $"{CollectionsUpdated} collections"
            + (Warnings.Count > 0 ? $", {Warnings.Count} warnings" : "");
    }

    /// <summary>A progress report from a running sync.</summary>
    public readonly struct SyncProgress
    {
        public readonly string Message;

        /// <summary>Rough completion between 0 and 1. Pagination length is unknown up front, so this is an estimate.</summary>
        public readonly float Fraction;

        public SyncProgress(string message, float fraction)
        {
            Message = message;
            Fraction = fraction;
        }
    }
}

namespace Tarinoi.Sync
{
    /// <summary>The outcome of a sync: either stats, or a human-actionable error.</summary>
    /// <remarks>
    /// Stays in <c>Tarinoi.Sync</c>: this is what the importer returns, not what the
    /// runtime's events carry.
    /// </remarks>
    public sealed class SyncResult
    {
        public bool Success => Error == null;

        /// <summary>Null on success. Otherwise a message written to be acted on, not just read.</summary>
        public string Error { get; private set; }

        public SyncStats Stats { get; private set; }

        public static SyncResult Ok(SyncStats stats) => new SyncResult { Stats = stats };

        public static SyncResult Fail(string error) =>
            new SyncResult { Error = error, Stats = new SyncStats() };
    }
}
