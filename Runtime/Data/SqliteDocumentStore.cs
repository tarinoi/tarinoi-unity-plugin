using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Tarinoi.Data
{
    /// <summary>
    /// The default <see cref="IDocumentStore"/>: reads straight from the local SQLite
    /// database on the calling thread.
    /// </summary>
    /// <remarks>
    /// Queries answer from a local file in well under a frame, so going asynchronous
    /// would cost more than it saves. The returned tasks are already completed.
    /// To move reads off the main thread, subclass and override <see cref="QueryAsync{T}"/>
    /// — every accessor routes through it, so that one override changes all of them.
    /// </remarks>
    public class SqliteDocumentStore : IDocumentStore
    {
        protected TarinoiDb Db { get; private set; }
        protected IEntityCache Entities { get; private set; }

        public virtual void Setup(TarinoiDb db, IEntityCache entities)
        {
            Db = db;
            Entities = entities;
        }

        /// <summary>
        /// The async seam. Override to dispatch queries elsewhere; the base
        /// implementation runs them inline and returns a completed task.
        /// </summary>
        protected virtual Task<List<T>> QueryAsync<T>(string sql, params object[] args) where T : new()
        {
            if (Db == null || !Db.IsOpen)
            {
                return Task.FromResult(new List<T>());
            }

            return Task.FromResult(Db.Query<T>(sql, args));
        }

        public virtual async Task<JObject> GetDocumentAsync(string documentId, string collectionId = null)
        {
            if (Db == null || !Db.IsOpen || string.IsNullOrEmpty(documentId))
            {
                return null;
            }

            List<DocumentRow> rows;
            if (string.IsNullOrEmpty(collectionId))
            {
                rows = await QueryAsync<DocumentRow>(
                    $"SELECT d.payload FROM documents d WHERE d.document_id = ? AND {Db.ActiveFilter}",
                    documentId);
            }
            else
            {
                rows = await QueryAsync<DocumentRow>(
                    "SELECT d.payload FROM documents d "
                    + $"WHERE d.document_id = ? AND d.collection_id = ? AND {Db.ActiveFilter}",
                    documentId, collectionId);
            }

            return rows.Count > 0 ? ParsePayload(rows[0].Payload, documentId) : null;
        }

        public virtual async Task<JObject> LoadCardAsync(string collectionId, string cardId)
        {
            if (Db == null || !Db.IsOpen || string.IsNullOrEmpty(cardId))
            {
                return null;
            }

            var rows = await QueryAsync<DocumentRow>(
                "SELECT d.payload FROM documents d "
                + $"WHERE d.document_id = ? AND d.collection_id = ? AND {Db.ActiveFilter}",
                cardId, collectionId);

            return rows.Count > 0 ? ParsePayload(rows[0].Payload, cardId) : null;
        }

        public virtual Task<JObject> GetEntityAsync(string identifier)
        {
            return Task.FromResult(Entities?.GetEntity(identifier));
        }

        public virtual async Task<List<StartCardRow>> QueryStartCardsAsync()
        {
            if (Db == null || !Db.IsOpen)
            {
                return new List<StartCardRow>();
            }

            return await QueryAsync<StartCardRow>(
                @"SELECT d.document_id, d.collection_id,
                         json_extract(d.payload, '$.data.label') AS label
                  FROM documents d
                  WHERE json_extract(d.payload, '$.base_ref') = 'start'
                    AND " + Db.ActiveFilter);
        }

        /// <summary>
        /// Parses a stored payload. Malformed JSON is logged and treated as missing
        /// content rather than thrown, so one corrupt document can't stop a dialogue.
        /// </summary>
        protected static JObject ParsePayload(string payload, string documentId)
        {
            if (string.IsNullOrEmpty(payload))
            {
                return null;
            }

            try
            {
                return JObject.Parse(payload);
            }
            catch (Exception e)
            {
                TarinoiLog.Error($"SqliteDocumentStore: document '{documentId}' has an unreadable payload: {e.Message}");
                return null;
            }
        }
    }
}
