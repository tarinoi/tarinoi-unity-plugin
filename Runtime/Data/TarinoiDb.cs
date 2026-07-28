using System;
using System.Collections.Generic;
using System.IO;
using SQLite;

namespace Tarinoi.Data
{
    /// <summary>
    /// The local SQLite store for synced Tarinoi content: connection lifecycle, schema
    /// creation, metadata, and query helpers that log rather than throw.
    /// </summary>
    /// <remarks>
    /// Each project gets its own database file under <see cref="BaseDirectory"/>.
    /// Connections are opened in WAL mode so the sync importer can write from a worker
    /// thread while the runtime reads on the main thread — each side owns a separate
    /// <see cref="TarinoiDb"/> instance and therefore a separate connection.
    /// </remarks>
    public sealed class TarinoiDb : IDisposable
    {
        /// <summary>
        /// Bumped when the schema changes incompatibly. On an older stored version the
        /// content tables are dropped and recreated; the next sync repopulates them.
        /// Migration is deliberately destructive because the database is a cache of
        /// server state, never a source of truth.
        /// </summary>
        public const int SchemaVersion = 3;

        public const string SchemaVersionKey = "schema_version";
        public const string ProjectIdKey = "project_id";
        public const string ApiPathKey = "api_path";
        public const string ApiSyncCursorKey = "api_sync_cursor";

        SQLiteConnection _connection;

        /// <summary>The directory holding every project's database file.</summary>
        public static string BaseDirectory =>
            Path.Combine(UnityEngine.Application.persistentDataPath, "tarinoi");

        public static string PathForProject(string projectId) =>
            Path.Combine(BaseDirectory, projectId + ".db");

        public string ProjectId { get; private set; }

        public bool IsOpen => _connection != null;

        /// <summary>
        /// When true, buffer-layer (uncommitted) content is hidden — the project sees
        /// only what a player would. Read from settings when the runtime opens the
        /// database; settable directly in tests.
        /// </summary>
        public bool CommittedOnly { get; set; }

        /// <summary>
        /// The <c>WHERE</c> fragment selecting currently visible documents, honouring
        /// <see cref="CommittedOnly"/>. Queries using it must alias documents as <c>d</c>.
        /// </summary>
        public string ActiveFilter => LayerFilter.ActiveFilterSql(CommittedOnly);

        // -------------------------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------------------------

        /// <summary>
        /// Opens (creating if needed) the database for a project and brings its schema
        /// up to date. Returns false and logs on failure rather than throwing, so a
        /// broken database degrades to "no content" instead of taking down the game.
        /// </summary>
        public bool Open(string projectId)
        {
            if (string.IsNullOrEmpty(projectId))
            {
                TarinoiLog.Error("TarinoiDb: cannot open a database without a project id");
                return false;
            }

            Close();

            try
            {
                Directory.CreateDirectory(BaseDirectory);
                _connection = new SQLiteConnection(
                    PathForProject(projectId),
                    SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);

                // These PRAGMAs return a row, so they must go through ExecuteScalar.
                // Execute() would raise a rather unhelpful "not an error" exception.
                _connection.ExecuteScalar<string>("PRAGMA journal_mode=WAL");
                _connection.ExecuteScalar<int>("PRAGMA busy_timeout=5000");

                ProjectId = projectId;
                InitSchema();
                return true;
            }
            catch (Exception e)
            {
                TarinoiLog.Error($"TarinoiDb: failed to open '{PathForProject(projectId)}': {e.Message}");
                _connection = null;
                ProjectId = null;
                return false;
            }
        }

        public void Close()
        {
            if (_connection == null)
            {
                return;
            }

            try
            {
                _connection.Close();
            }
            catch (Exception e)
            {
                TarinoiLog.Warn($"TarinoiDb: error closing database: {e.Message}");
            }
            finally
            {
                _connection = null;
                ProjectId = null;
            }
        }

        public void Dispose() => Close();

        // -------------------------------------------------------------------------
        // Query helpers
        // -------------------------------------------------------------------------

        /// <summary>Runs a query and maps rows onto <typeparamref name="T"/>. Returns empty on error.</summary>
        public List<T> Query<T>(string sql, params object[] args) where T : new()
        {
            if (_connection == null)
            {
                TarinoiLog.Error("TarinoiDb: query attempted on a closed database");
                return new List<T>();
            }

            try
            {
                return _connection.Query<T>(sql, args);
            }
            catch (Exception e)
            {
                TarinoiLog.Error($"TarinoiDb: {e.Message}\n→ {sql}");
                return new List<T>();
            }
        }

        /// <summary>Runs a single-column query. Returns empty on error.</summary>
        public List<T> QueryScalars<T>(string sql, params object[] args)
        {
            if (_connection == null)
            {
                TarinoiLog.Error("TarinoiDb: query attempted on a closed database");
                return new List<T>();
            }

            try
            {
                return _connection.QueryScalars<T>(sql, args);
            }
            catch (Exception e)
            {
                TarinoiLog.Error($"TarinoiDb: {e.Message}\n→ {sql}");
                return new List<T>();
            }
        }

        /// <summary>Runs a statement. Returns the affected row count, or -1 on error.</summary>
        public int Execute(string sql, params object[] args)
        {
            if (_connection == null)
            {
                TarinoiLog.Error("TarinoiDb: execute attempted on a closed database");
                return -1;
            }

            try
            {
                return _connection.Execute(sql, args);
            }
            catch (Exception e)
            {
                TarinoiLog.Error($"TarinoiDb: {e.Message}\n→ {sql}");
                return -1;
            }
        }

        /// <summary>
        /// Runs <paramref name="action"/> inside a transaction, rolling back on error.
        /// The importer wraps each page of synced documents in one of these — without
        /// it, a few hundred individual inserts each pay their own fsync.
        /// </summary>
        public bool RunInTransaction(Action action)
        {
            if (_connection == null)
            {
                TarinoiLog.Error("TarinoiDb: transaction attempted on a closed database");
                return false;
            }

            try
            {
                _connection.RunInTransaction(action);
                return true;
            }
            catch (Exception e)
            {
                TarinoiLog.Error($"TarinoiDb: transaction rolled back: {e.Message}");
                return false;
            }
        }

        // -------------------------------------------------------------------------
        // Metadata
        // -------------------------------------------------------------------------

        /// <summary>Reads a metadata value, or "" when the key is absent.</summary>
        public string ReadMeta(string key)
        {
            var rows = QueryScalars<string>("SELECT value FROM metadata WHERE key = ?", key);
            return rows.Count > 0 ? rows[0] : "";
        }

        public void WriteMeta(string key, string value)
        {
            Execute("INSERT OR REPLACE INTO metadata (key, value) VALUES (?, ?)", key, value);
        }

        public void DeleteMeta(params string[] keys)
        {
            foreach (var key in keys)
            {
                Execute("DELETE FROM metadata WHERE key = ?", key);
            }
        }

        // -------------------------------------------------------------------------
        // Schema
        // -------------------------------------------------------------------------

        void InitSchema()
        {
            // metadata comes first: it stores the schema version that decides whether
            // the content tables below need wiping.
            Execute(@"CREATE TABLE IF NOT EXISTS metadata (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            )");

            var stored = 0;
            int.TryParse(ReadMeta(SchemaVersionKey), out stored);
            if (stored < SchemaVersion)
            {
                if (stored > 0)
                {
                    TarinoiLog.Info($"TarinoiDb: schema {stored} → {SchemaVersion}, "
                                    + "clearing local content (the next sync will refetch it)");
                }

                Execute("DROP TABLE IF EXISTS documents");
                Execute("DROP TABLE IF EXISTS collections");
                WriteMeta(SchemaVersionKey, SchemaVersion.ToString());
            }

            Execute(@"CREATE TABLE IF NOT EXISTS documents (
                document_id   TEXT NOT NULL,
                collection_id TEXT NOT NULL,
                document_type TEXT NOT NULL,
                layer_id      TEXT NOT NULL,
                namespace     TEXT NOT NULL DEFAULT 'document',
                identifier    TEXT,
                update_key    INTEGER NOT NULL,
                is_tombstone  INTEGER NOT NULL DEFAULT 0,
                is_archived   INTEGER NOT NULL DEFAULT 0,
                is_moved      INTEGER NOT NULL DEFAULT 0,
                payload       TEXT NOT NULL,
                PRIMARY KEY (document_id, collection_id, layer_id)
            )");

            Execute("CREATE INDEX IF NOT EXISTS idx_documents_collection ON documents (collection_id, document_type)");
            Execute("CREATE INDEX IF NOT EXISTS idx_documents_update_key ON documents (update_key)");
            Execute("CREATE INDEX IF NOT EXISTS idx_documents_identifier ON documents (identifier)");

            Execute(@"CREATE TABLE IF NOT EXISTS collections (
                collection_id   TEXT PRIMARY KEY,
                collection_name TEXT,
                collection_type TEXT NOT NULL,
                payload         TEXT NOT NULL
            )");
        }
    }
}
