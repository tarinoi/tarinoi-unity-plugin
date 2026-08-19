using System.Collections.Generic;

namespace Tarinoi
{
    /// <summary>
    /// Remembers which cards a player has already seen, so previously seen options can
    /// be shown differently — or, with the <c>shown_once</c> card flag, not at all.
    /// </summary>
    /// <remarks>
    /// Implement this over your save system to persist across sessions. Leave
    /// <see cref="TarinoiRuntime.HistoryStore"/> null to skip tracking entirely, in
    /// which case every choice reports <see cref="DialogueChoice.Visited"/> as false.
    /// <para>
    /// The runtime keeps this state only while a dialogue is running: it asks for a
    /// dialogue's seen cards on start and hands the updated set back when the dialogue
    /// ends, so nothing is retained inside the plugin.
    /// </para>
    /// </remarks>
    public interface IHistoryStore
    {
        /// <summary>
        /// Card ids already seen in the dialogue starting at <paramref name="startCardId"/>,
        /// across all previous visits. Return an empty collection when nothing is recorded.
        /// </summary>
        IEnumerable<string> GetVisited(string startCardId);

        /// <summary>
        /// Persists the cumulative set of seen card ids for an entry point: every NPC
        /// line displayed and every PC line the player chose. Called when a dialogue
        /// ends or is aborted.
        /// </summary>
        void SaveVisited(string startCardId, IEnumerable<string> visitedIds);
    }

    /// <summary>
    /// Keeps seen cards for the lifetime of the process only.
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
