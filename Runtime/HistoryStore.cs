using System.Collections.Generic;

namespace Tarinoi
{
    /// <summary>
    /// Remembers which choices a player has already taken, so previously seen options
    /// can be shown differently.
    /// </summary>
    /// <remarks>
    /// Implement this over your save system to persist across sessions. Leave
    /// <see cref="TarinoiRuntime.HistoryStore"/> null to skip tracking entirely, in
    /// which case every choice reports <see cref="DialogueChoice.Visited"/> as false.
    /// </remarks>
    public interface IHistoryStore
    {
        /// <summary>
        /// Card ids already chosen in the dialogue starting at <paramref name="startCardId"/>,
        /// across all previous visits. Return an empty collection when nothing is recorded.
        /// </summary>
        IEnumerable<string> GetVisited(string startCardId);

        /// <summary>
        /// Persists the cumulative set of chosen card ids for an entry point. Called
        /// when a dialogue ends or is aborted.
        /// </summary>
        void SaveVisited(string startCardId, IEnumerable<string> visitedIds);
    }

    /// <summary>
    /// Keeps visited choices for the lifetime of the process only.
    /// </summary>
    /// <remarks>
    /// Enough to stop a player re-reading the same option within a play session.
    /// Implement <see cref="IHistoryStore"/> yourself to survive a restart.
    /// </remarks>
    public sealed class InMemoryHistoryStore : IHistoryStore
    {
        readonly Dictionary<string, HashSet<string>> _store =
            new Dictionary<string, HashSet<string>>();

        public IEnumerable<string> GetVisited(string startCardId)
        {
            return startCardId != null && _store.TryGetValue(startCardId, out var visited)
                ? (IEnumerable<string>)visited
                : new string[0];
        }

        public void SaveVisited(string startCardId, IEnumerable<string> visitedIds)
        {
            if (startCardId == null)
            {
                return;
            }

            _store[startCardId] = new HashSet<string>(visitedIds ?? new string[0]);
        }
    }
}
