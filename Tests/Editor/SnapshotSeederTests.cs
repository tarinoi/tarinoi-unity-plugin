using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Tarinoi.Data;
using Tarinoi.Sync;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tarinoi.Tests
{
    /// <summary>
    /// Covers the desktop/editor path, where StreamingAssets is a real directory.
    /// The web-request path used on Android and WebGL can only be exercised in a player
    /// build on those platforms — see the outstanding verification note in the port docs.
    /// </summary>
    public class SnapshotSeederTests
    {
        string _projectId;
        string _snapshotDir;
        bool _createdSnapshotDir;
        bool _createdStreamingAssets;

        [SetUp]
        public void SetUp()
        {
            Assume.That(SnapshotSeeder.StreamingAssetsIsFilePath,
                "this test needs StreamingAssets to be a filesystem path");

            _projectId = "__test__snap_" + Guid.NewGuid().ToString("N");
            _snapshotDir = Path.Combine(Application.streamingAssetsPath, SnapshotSeeder.SnapshotFolder);
            _createdSnapshotDir = !Directory.Exists(_snapshotDir);

            // Creating the snapshot folder also creates StreamingAssets itself, so track
            // that separately or the tests leave an empty folder in the user's project.
            _createdStreamingAssets = !Directory.Exists(Application.streamingAssetsPath);
        }

        [TearDown]
        public void TearDown()
        {
            var source = SnapshotSeeder.SourcePath(_projectId);
            if (File.Exists(source))
            {
                File.Delete(source);
            }

            // Only remove directories this test created, so a project that genuinely
            // ships snapshots keeps them.
            DeleteIfCreatedAndEmpty(_createdSnapshotDir, _snapshotDir);
            DeleteIfCreatedAndEmpty(_createdStreamingAssets, Application.streamingAssetsPath);

            var target = TarinoiDb.PathForProject(_projectId);
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                if (File.Exists(target + suffix))
                {
                    File.Delete(target + suffix);
                }
            }
        }

        /// <summary>Removes a directory, and its Unity .meta sidecar, if empty.</summary>
        static void DeleteIfCreatedAndEmpty(bool created, string path)
        {
            if (!created || !Directory.Exists(path)
                         || Directory.GetFileSystemEntries(path).Length != 0)
            {
                return;
            }

            Directory.Delete(path);
            if (File.Exists(path + ".meta"))
            {
                File.Delete(path + ".meta");
            }
        }

        void WriteSnapshot(byte[] contents)
        {
            Directory.CreateDirectory(_snapshotDir);
            File.WriteAllBytes(SnapshotSeeder.SourcePath(_projectId), contents);
        }

        static bool Seed(string projectId, bool overwrite = true) =>
            SnapshotSeeder.SeedAsync(projectId, overwrite).GetAwaiter().GetResult();

        [Test]
        public void SeedingCopiesTheSnapshotIntoTheWritableLocation()
        {
            var contents = new byte[] { 1, 2, 3, 4 };
            WriteSnapshot(contents);

            Assert.IsTrue(Seed(_projectId));

            var target = TarinoiDb.PathForProject(_projectId);
            Assert.IsTrue(File.Exists(target), "SQLite needs a real writable file");
            CollectionAssert.AreEqual(contents, File.ReadAllBytes(target));
        }

        [Test]
        public void AMissingSnapshotIsAnActionableError()
        {
            LogAssert.Expect(LogType.Error, new Regex("Snapshot for Export"));
            Assert.IsFalse(Seed(_projectId));
        }

        [Test]
        public void SeedingWithoutAProjectIdFails()
        {
            LogAssert.Expect(LogType.Error, new Regex("without a project id"));
            Assert.IsFalse(Seed(""));
        }

        [Test]
        public void SeedingOverwritesAnExistingDatabaseByDefault()
        {
            // The snapshot is the source of truth in offline mode; the local file is
            // only ever a cache of it.
            Directory.CreateDirectory(TarinoiDb.BaseDirectory);
            File.WriteAllBytes(TarinoiDb.PathForProject(_projectId), new byte[] { 9, 9, 9 });
            WriteSnapshot(new byte[] { 1, 2 });

            Assert.IsTrue(Seed(_projectId));

            CollectionAssert.AreEqual(new byte[] { 1, 2 },
                File.ReadAllBytes(TarinoiDb.PathForProject(_projectId)));
        }

        [Test]
        public void SeedingCanBeSkippedWhenADatabaseAlreadyExists()
        {
            Directory.CreateDirectory(TarinoiDb.BaseDirectory);
            File.WriteAllBytes(TarinoiDb.PathForProject(_projectId), new byte[] { 9, 9, 9 });

            Assert.IsTrue(Seed(_projectId, overwrite: false));

            CollectionAssert.AreEqual(new byte[] { 9, 9, 9 },
                File.ReadAllBytes(TarinoiDb.PathForProject(_projectId)));
        }

        [Test]
        public void StaleJournalFilesAreRemovedSoTheyCannotBeAppliedToTheNewDatabase()
        {
            Directory.CreateDirectory(TarinoiDb.BaseDirectory);
            var target = TarinoiDb.PathForProject(_projectId);
            File.WriteAllBytes(target, new byte[] { 9 });
            File.WriteAllBytes(target + "-wal", new byte[] { 7 });
            File.WriteAllBytes(target + "-shm", new byte[] { 7 });
            WriteSnapshot(new byte[] { 1, 2 });

            Assert.IsTrue(Seed(_projectId));

            Assert.IsFalse(File.Exists(target + "-wal"));
            Assert.IsFalse(File.Exists(target + "-shm"));
        }

        [Test]
        public void ASeededSnapshotOpensAsAWorkingDatabase()
        {
            // End to end: build a real database, ship it as a snapshot, seed it, reopen it.
            using (var source = new TestDb())
            {
                source.InsertDocument("bundled-card", payload: "{\"base_ref\":\"start\"}");
                source.Db.Close();
                WriteSnapshot(File.ReadAllBytes(TarinoiDb.PathForProject(source.ProjectId)));
            }

            Assert.IsTrue(Seed(_projectId));

            using (var db = new TarinoiDb())
            {
                Assert.IsTrue(db.Open(_projectId));
                CollectionAssert.AreEqual(new[] { "bundled-card" },
                    db.QueryScalars<string>("SELECT document_id FROM documents"));
            }
        }
    }
}
