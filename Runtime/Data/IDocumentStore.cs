using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Tarinoi.Data
{
    /// <summary>
    /// Supplies entity payloads that have already been cached in memory.
    /// </summary>
    /// <remarks>
    /// Entities are loaded once at configure time and after each sync, so looking them
    /// up doesn't need to touch the database. The runtime implements this; the document
    /// store depends on the interface rather than on the runtime itself, which keeps
    /// the data layer independently testable.
    /// </remarks>
    public interface IEntityCache
    {
        /// <summary>Returns the entity payload for an identifier, or null if unknown.</summary>
        JObject GetEntity(string identifier);
    }

    /// <summary>
    /// Reads dialogue content for the runtime. This is the seam where an integration
    /// can move queries off the main thread or substitute a different backend entirely.
    /// </summary>
    /// <remarks>
    /// Every method is asynchronous even though the built-in implementation answers
    /// synchronously. That asymmetry is deliberate: it means an integration can make
    /// these genuinely async — a worker thread, a network service, a test double that
    /// injects delays — without any caller changing. The runtime awaits all of them.
    /// <para>
    /// Implementations must not throw. Return null or an empty collection for missing
    /// content; the runtime treats absence as a recoverable authoring problem and
    /// keeps the game running.
    /// </para>
    /// </remarks>
    public interface IDocumentStore
    {
        /// <summary>
        /// Called once after the database is open, before any content is requested.
        /// </summary>
        void Setup(TarinoiDb db, IEntityCache entities);

        /// <summary>
        /// Returns a document's parsed payload, or null when it isn't found.
        /// Pass a <paramref name="collectionId"/> to disambiguate when the same
        /// document id appears in more than one collection.
        /// </summary>
        Task<JObject> GetDocumentAsync(string documentId, string collectionId = null);

        /// <summary>Returns a dialogue card's parsed payload, or null when it isn't found.</summary>
        Task<JObject> LoadCardAsync(string collectionId, string cardId);

        /// <summary>Returns an entity's payload by identifier, or null when unknown.</summary>
        Task<JObject> GetEntityAsync(string identifier);

        /// <summary>Returns every dialogue entry point currently visible.</summary>
        Task<List<StartCardRow>> QueryStartCardsAsync();
    }
}
