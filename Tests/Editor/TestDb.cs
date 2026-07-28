using System;
using System.IO;
using Tarinoi.Data;

namespace Tarinoi.Tests
{
    /// <summary>
    /// A throwaway <see cref="TarinoiDb"/> on a unique project id, deleted on dispose.
    /// </summary>
    /// <remarks>
    /// Tests use real databases rather than mocks because the behaviour under test —
    /// the layer merge, the composite-key upsert, <c>json_extract</c> — lives in SQL.
    /// A mock would only assert that we wrote the SQL we wrote.
    /// </remarks>
    public sealed class TestDb : IDisposable
    {
        public TarinoiDb Db { get; }
        public string ProjectId { get; }

        public TestDb(bool committedOnly = false)
        {
            ProjectId = "__test__" + Guid.NewGuid().ToString("N");
            Db = new TarinoiDb { CommittedOnly = committedOnly };
            if (!Db.Open(ProjectId))
            {
                throw new InvalidOperationException($"could not open test database '{ProjectId}'");
            }
        }

        /// <summary>
        /// Inserts a document. Defaults describe an ordinary active card on the main
        /// layer, so each test only states the fields it actually cares about.
        /// </summary>
        public void InsertDocument(
            string documentId,
            string collectionId = "col1",
            string layerId = LayerFilter.MainLayer,
            string documentType = "card",
            string payload = null,
            string identifier = null,
            long updateKey = 1,
            bool tombstone = false,
            bool archived = false,
            bool moved = false)
        {
            Db.Execute(
                @"INSERT OR REPLACE INTO documents
                  (document_id, collection_id, document_type, layer_id, namespace,
                   identifier, update_key, is_tombstone, is_archived, is_moved, payload)
                  VALUES (?, ?, ?, ?, 'document', ?, ?, ?, ?, ?, ?)",
                documentId, collectionId, documentType, layerId,
                identifier, updateKey,
                tombstone ? 1 : 0, archived ? 1 : 0, moved ? 1 : 0,
                payload ?? $"{{\"id\":\"{documentId}\",\"layer\":\"{layerId}\"}}");
        }

        /// <summary>Returns the document ids currently visible through the active filter.</summary>
        public System.Collections.Generic.List<string> VisibleDocumentIds()
        {
            return Db.QueryScalars<string>(
                $"SELECT d.document_id FROM documents d WHERE {Db.ActiveFilter} ORDER BY d.document_id");
        }

        /// <summary>Returns the payloads currently visible through the active filter.</summary>
        public System.Collections.Generic.List<string> VisiblePayloads()
        {
            return Db.QueryScalars<string>(
                $"SELECT d.payload FROM documents d WHERE {Db.ActiveFilter} ORDER BY d.document_id");
        }

        public void Dispose()
        {
            var path = TarinoiDb.PathForProject(ProjectId);
            Db.Dispose();

            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var file = path + suffix;
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }
    }
}
