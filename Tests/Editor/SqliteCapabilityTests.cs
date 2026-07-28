using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using SQLite;

namespace Tarinoi.Tests
{
    /// <summary>
    /// Verifies that the bundled SQLite build supports everything the Tarinoi data
    /// layer depends on.
    /// </summary>
    /// <remarks>
    /// These are platform-capability checks, not logic tests. They exist because the
    /// data layer's design assumes three things that are compile-time options in
    /// SQLite and therefore vary between builds and platforms: the JSON1 extension
    /// (<c>json_extract</c>, used by the codegen entity query), write-ahead logging,
    /// and multi-connection concurrency (sync writes on a worker thread while the
    /// runtime reads). If any of these fail on a target platform, that platform needs
    /// a different SQLite provider — so failures here are architectural, not cosmetic.
    /// </remarks>
    public class SqliteCapabilityTests
    {
        string _dir;
        string _dbPath;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "tarinoi-sqlite-spike-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _dbPath = Path.Combine(_dir, "spike.db");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }

        SQLiteConnection Open()
        {
            return new SQLiteConnection(
                _dbPath,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
        }

        [Test]
        public void NativeLibraryLoadsAndReportsAVersion()
        {
            using (var db = Open())
            {
                var version = db.ExecuteScalar<string>("SELECT sqlite_version()");
                Assert.IsNotEmpty(version, "the bundled native SQLite should report a version");
            }
        }

        [Test]
        public void JournalModeCanBeSetToWal()
        {
            using (var db = Open())
            {
                var mode = db.ExecuteScalar<string>("PRAGMA journal_mode=WAL");
                Assert.AreEqual("wal", mode.ToLowerInvariant(),
                    "the data layer opens every connection in WAL mode");
            }
        }

        [Test]
        public void JsonExtractIsAvailable()
        {
            // The codegen entity query filters on json_extract(payload, '$.dialog_capable'),
            // so the JSON1 extension must be compiled into the bundled build.
            using (var db = Open())
            {
                db.Execute("CREATE TABLE docs (document_id TEXT PRIMARY KEY, payload TEXT NOT NULL)");
                db.Execute("INSERT INTO docs VALUES (?, ?)", "e1",
                    "{\"dialog_capable\": 1, \"label\": \"Narrator\"}");
                db.Execute("INSERT INTO docs VALUES (?, ?)", "e2",
                    "{\"dialog_capable\": 0, \"label\": \"Prop\"}");

                var ids = db.QueryScalars<string>(
                    "SELECT document_id FROM docs WHERE json_extract(payload, '$.dialog_capable') = 1");

                CollectionAssert.AreEqual(new[] { "e1" }, ids);

                var label = db.ExecuteScalar<string>(
                    "SELECT json_extract(payload, '$.label') FROM docs WHERE document_id = ?", "e2");
                Assert.AreEqual("Prop", label);
            }
        }

        [Test]
        public void CompositePrimaryKeyAndInsertOrReplaceBehaveAsTheSchemaExpects()
        {
            // documents is keyed on (document_id, collection_id, layer_id) so both
            // layers of the same document coexist, and re-syncing a document replaces
            // only its own layer row.
            using (var db = Open())
            {
                db.Execute(@"CREATE TABLE documents (
                    document_id TEXT NOT NULL,
                    collection_id TEXT NOT NULL,
                    layer_id TEXT NOT NULL,
                    payload TEXT NOT NULL,
                    PRIMARY KEY (document_id, collection_id, layer_id))");

                db.Execute("INSERT OR REPLACE INTO documents VALUES (?, ?, ?, ?)",
                    "d1", "c1", "tarinoi:main-project-layer", "main-v1");
                db.Execute("INSERT OR REPLACE INTO documents VALUES (?, ?, ?, ?)",
                    "d1", "c1", "tarinoi:main-project-layer.buffer", "buffer-v1");
                Assert.AreEqual(2, db.ExecuteScalar<int>("SELECT COUNT(*) FROM documents"),
                    "both layers of one document must coexist");

                db.Execute("INSERT OR REPLACE INTO documents VALUES (?, ?, ?, ?)",
                    "d1", "c1", "tarinoi:main-project-layer", "main-v2");
                Assert.AreEqual(2, db.ExecuteScalar<int>("SELECT COUNT(*) FROM documents"));
                Assert.AreEqual("buffer-v1", db.ExecuteScalar<string>(
                    "SELECT payload FROM documents WHERE layer_id = 'tarinoi:main-project-layer.buffer'"),
                    "replacing the main layer must not disturb the buffer layer");
            }
        }

        [Test]
        public void ConcurrentWriterAndReaderConnectionsCoexistInWalMode()
        {
            // The sync importer writes on a worker thread using its own connection
            // while the runtime reads on the main thread using another. WAL is what
            // makes that safe; without it the reader blocks or errors.
            using (var setup = Open())
            {
                setup.ExecuteScalar<string>("PRAGMA journal_mode=WAL");
                setup.Execute("CREATE TABLE docs (document_id TEXT PRIMARY KEY, payload TEXT NOT NULL)");
                setup.Execute("INSERT INTO docs VALUES ('seed', 'seed-payload')");
            }

            const int writeCount = 200;

            var writer = Task.Run(() =>
            {
                using (var db = Open())
                {
                    db.ExecuteScalar<string>("PRAGMA journal_mode=WAL");
                    db.ExecuteScalar<int>("PRAGMA busy_timeout=5000");
                    for (var i = 0; i < writeCount; i++)
                    {
                        db.Execute("INSERT OR REPLACE INTO docs VALUES (?, ?)", "d" + i, "payload-" + i);
                    }
                }
            });

            var readErrors = new List<string>();
            using (var reader = Open())
            {
                reader.ExecuteScalar<int>("PRAGMA busy_timeout=5000");
                while (!writer.IsCompleted)
                {
                    try
                    {
                        var seed = reader.ExecuteScalar<string>(
                            "SELECT payload FROM docs WHERE document_id = 'seed'");
                        Assert.AreEqual("seed-payload", seed);
                    }
                    catch (Exception e)
                    {
                        readErrors.Add(e.Message);
                        break;
                    }
                }
            }

            Assert.DoesNotThrow(() => writer.GetAwaiter().GetResult(), "the writer connection should not error");
            CollectionAssert.IsEmpty(readErrors, "reads must not fail while a writer is active");

            using (var verify = Open())
            {
                Assert.AreEqual(writeCount + 1, verify.ExecuteScalar<int>("SELECT COUNT(*) FROM docs"));
            }
        }

        [Test]
        public void ParameterBindingRejectsSqlInjectionThroughValues()
        {
            using (var db = Open())
            {
                db.Execute("CREATE TABLE docs (document_id TEXT PRIMARY KEY, payload TEXT NOT NULL)");
                db.Execute("INSERT INTO docs VALUES (?, ?)", "d1'; DROP TABLE docs; --", "payload");

                Assert.AreEqual(1, db.ExecuteScalar<int>("SELECT COUNT(*) FROM docs"),
                    "a quote in a bound value must stay data, never SQL");
            }
        }
    }
}
