using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Tarinoi.Data;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tarinoi.Tests
{
    public class TarinoiDbTests
    {
        [Test]
        public void OpenCreatesTheFileAndSchema()
        {
            using (var fixture = new TestDb())
            {
                Assert.IsTrue(fixture.Db.IsOpen);
                Assert.IsTrue(File.Exists(TarinoiDb.PathForProject(fixture.ProjectId)));

                var tables = fixture.Db.QueryScalars<string>(
                    "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name");
                CollectionAssert.Contains(tables, "documents");
                CollectionAssert.Contains(tables, "collections");
                CollectionAssert.Contains(tables, "metadata");
            }
        }

        [Test]
        public void OpenCreatesTheExpectedIndexes()
        {
            using (var fixture = new TestDb())
            {
                var indexes = fixture.Db.QueryScalars<string>(
                    "SELECT name FROM sqlite_master WHERE type = 'index' AND name LIKE 'idx_%'");
                CollectionAssert.Contains(indexes, "idx_documents_collection");
                CollectionAssert.Contains(indexes, "idx_documents_update_key");
                CollectionAssert.Contains(indexes, "idx_documents_identifier");
            }
        }

        [Test]
        public void OpenRecordsTheCurrentSchemaVersion()
        {
            using (var fixture = new TestDb())
            {
                Assert.AreEqual(TarinoiDb.SchemaVersion.ToString(),
                    fixture.Db.ReadMeta(TarinoiDb.SchemaVersionKey));
            }
        }

        [Test]
        public void OpenWithoutAProjectIdFails()
        {
            LogAssert.Expect(LogType.Error, new Regex("without a project id"));
            using (var db = new TarinoiDb())
            {
                Assert.IsFalse(db.Open(""));
                Assert.IsFalse(db.IsOpen);
            }
        }

        [Test]
        public void MetadataRoundTrips()
        {
            using (var fixture = new TestDb())
            {
                Assert.AreEqual("", fixture.Db.ReadMeta("absent"), "a missing key reads as empty, not null");

                fixture.Db.WriteMeta(TarinoiDb.ApiPathKey, "https://example.com/p/proj1/documents");
                Assert.AreEqual("https://example.com/p/proj1/documents",
                    fixture.Db.ReadMeta(TarinoiDb.ApiPathKey));

                fixture.Db.WriteMeta(TarinoiDb.ApiPathKey, "changed");
                Assert.AreEqual("changed", fixture.Db.ReadMeta(TarinoiDb.ApiPathKey),
                    "writing an existing key must replace it, not duplicate it");

                fixture.Db.DeleteMeta(TarinoiDb.ApiPathKey);
                Assert.AreEqual("", fixture.Db.ReadMeta(TarinoiDb.ApiPathKey));
            }
        }

        [Test]
        public void DeleteMetaRemovesOnlyTheNamedKeys()
        {
            using (var fixture = new TestDb())
            {
                fixture.Db.WriteMeta(TarinoiDb.ApiPathKey, "path");
                fixture.Db.WriteMeta(TarinoiDb.ApiSyncCursorKey, "42");

                // This pair is exactly what snapshot export strips.
                fixture.Db.DeleteMeta(TarinoiDb.ApiPathKey, TarinoiDb.ApiSyncCursorKey);

                Assert.AreEqual("", fixture.Db.ReadMeta(TarinoiDb.ApiPathKey));
                Assert.AreEqual("", fixture.Db.ReadMeta(TarinoiDb.ApiSyncCursorKey));
                Assert.AreEqual(TarinoiDb.SchemaVersion.ToString(),
                    fixture.Db.ReadMeta(TarinoiDb.SchemaVersionKey), "unrelated keys survive");
            }
        }

        [Test]
        public void ReopeningPreservesContent()
        {
            var projectId = "__test__reopen_" + System.Guid.NewGuid().ToString("N");
            try
            {
                using (var db = new TarinoiDb())
                {
                    Assert.IsTrue(db.Open(projectId));
                    db.WriteMeta("keep", "me");
                    db.Execute(
                        @"INSERT INTO documents (document_id, collection_id, document_type, layer_id,
                          namespace, identifier, update_key, is_tombstone, is_archived, is_moved, payload)
                          VALUES ('d1', 'c1', 'card', ?, 'document', NULL, 1, 0, 0, 0, '{}')",
                        LayerFilter.MainLayer);
                }

                using (var db = new TarinoiDb())
                {
                    Assert.IsTrue(db.Open(projectId));
                    Assert.AreEqual("me", db.ReadMeta("keep"));
                    Assert.AreEqual(1, db.QueryScalars<int>("SELECT COUNT(*) FROM documents")[0],
                        "reopening at the same schema version must not wipe content");
                }
            }
            finally
            {
                DeleteProject(projectId);
            }
        }

        [Test]
        public void StaleSchemaVersionDropsContentButKeepsMetadata()
        {
            var projectId = "__test__migrate_" + System.Guid.NewGuid().ToString("N");
            try
            {
                using (var db = new TarinoiDb())
                {
                    Assert.IsTrue(db.Open(projectId));
                    db.Execute(
                        @"INSERT INTO documents (document_id, collection_id, document_type, layer_id,
                          namespace, identifier, update_key, is_tombstone, is_archived, is_moved, payload)
                          VALUES ('d1', 'c1', 'card', ?, 'document', NULL, 1, 0, 0, 0, '{}')",
                        LayerFilter.MainLayer);
                    db.WriteMeta(TarinoiDb.ApiPathKey, "https://example.com/p/proj1/documents");

                    // Simulate a database written by an older version of the package.
                    db.WriteMeta(TarinoiDb.SchemaVersionKey, "1");
                }

                using (var db = new TarinoiDb())
                {
                    Assert.IsTrue(db.Open(projectId));
                    Assert.AreEqual(0, db.QueryScalars<int>("SELECT COUNT(*) FROM documents")[0],
                        "stale content is dropped so the next sync refetches it");
                    Assert.AreEqual(TarinoiDb.SchemaVersion.ToString(),
                        db.ReadMeta(TarinoiDb.SchemaVersionKey));
                    Assert.AreEqual("https://example.com/p/proj1/documents",
                        db.ReadMeta(TarinoiDb.ApiPathKey),
                        "configuration must survive a schema migration");
                }
            }
            finally
            {
                DeleteProject(projectId);
            }
        }

        [Test]
        public void QueriesOnAClosedDatabaseLogAndReturnEmpty()
        {
            using (var db = new TarinoiDb())
            {
                LogAssert.Expect(LogType.Error, new Regex("closed database"));
                Assert.IsEmpty(db.QueryScalars<string>("SELECT 1"));

                LogAssert.Expect(LogType.Error, new Regex("closed database"));
                Assert.AreEqual(-1, db.Execute("SELECT 1"));
            }
        }

        [Test]
        public void MalformedSqlLogsAndReturnsEmptyRatherThanThrowing()
        {
            using (var fixture = new TestDb())
            {
                LogAssert.Expect(LogType.Error, new Regex("TarinoiDb:"));
                Assert.IsEmpty(fixture.Db.QueryScalars<string>("SELECT nope FROM nowhere"));
            }
        }

        [Test]
        public void RunInTransactionRollsBackOnError()
        {
            using (var fixture = new TestDb())
            {
                fixture.InsertDocument("survivor");

                LogAssert.Expect(LogType.Error, new Regex("rolled back"));
                var ok = fixture.Db.RunInTransaction(() =>
                {
                    fixture.InsertDocument("doomed");
                    throw new System.InvalidOperationException("boom");
                });

                Assert.IsFalse(ok);
                CollectionAssert.AreEqual(new[] { "survivor" }, fixture.VisibleDocumentIds(),
                    "a failed transaction must leave no partial writes");
            }
        }

        [Test]
        public void RunInTransactionCommitsOnSuccess()
        {
            using (var fixture = new TestDb())
            {
                var ok = fixture.Db.RunInTransaction(() =>
                {
                    fixture.InsertDocument("a");
                    fixture.InsertDocument("b");
                });

                Assert.IsTrue(ok);
                CollectionAssert.AreEqual(new[] { "a", "b" }, fixture.VisibleDocumentIds());
            }
        }

        [Test]
        public void ActiveFilterFollowsCommittedOnly()
        {
            using (var fixture = new TestDb())
            {
                StringAssert.Contains(LayerFilter.BufferLayer, fixture.Db.ActiveFilter);

                fixture.Db.CommittedOnly = true;
                StringAssert.DoesNotContain(LayerFilter.BufferLayer, fixture.Db.ActiveFilter);
                StringAssert.Contains(LayerFilter.MainLayer, fixture.Db.ActiveFilter);
            }
        }

        static void DeleteProject(string projectId)
        {
            var path = TarinoiDb.PathForProject(projectId);
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                if (File.Exists(path + suffix))
                {
                    File.Delete(path + suffix);
                }
            }
        }
    }
}
