using NUnit.Framework;
using Tarinoi.Sync;

namespace Tarinoi.Tests
{
    /// <summary>
    /// Exercises a real sync against a real Tarinoi project.
    /// </summary>
    /// <remarks>
    /// These are ignored unless both an API token and an API path are configured, so a
    /// clean checkout still runs green. To enable them, set the API path in
    /// <b>Project Settings → Tarinoi</b> and save a token via
    /// <b>Tools → Tarinoi → Set API token…</b>.
    /// <para>
    /// The rest of the importer's behaviour is covered offline in
    /// <see cref="ApiImporterTests"/> against a scripted transport. What only a live run
    /// can tell us is whether our understanding of the real feed is right: that the
    /// endpoint shape, auth scheme, NDJSON framing and cursor semantics are what we
    /// think they are.
    /// </para>
    /// </remarks>
    [Category("Live")]
    public class ApiImporterLiveTests
    {
        string _apiPath;
        string _apiKey;

        [SetUp]
        public void SetUp()
        {
            _apiKey = Credentials.Read(Credentials.ApiKeyName);
            _apiPath = TarinoiSettings.Instance.apiPath;

            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiPath))
            {
                Assert.Ignore("No API token and path configured — skipping live sync tests.");
            }
        }

        [Test]
        public void AFullSyncFetchesContent()
        {
            using (var fixture = new TestDb())
            {
                var result = new ApiImporter()
                    .SyncAsync(_apiPath, _apiKey, fixture.Db,
                        skipTlsVerify: TarinoiSettings.Instance.skipTlsVerify)
                    .GetAwaiter().GetResult();

                Assert.IsTrue(result.Success, result.Error);
                Assert.Greater(result.Stats.DocumentsUpserted, 0,
                    "a live project should return at least one document");

                TarinoiLog.Info($"Live sync: {result.Stats}");
            }
        }

        [Test]
        public void ASecondSyncIsIncrementalAndChangesNothing()
        {
            using (var fixture = new TestDb())
            {
                var importer = new ApiImporter();

                var first = importer
                    .SyncAsync(_apiPath, _apiKey, fixture.Db,
                        skipTlsVerify: TarinoiSettings.Instance.skipTlsVerify)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(first.Success, first.Error);

                var cursor = fixture.Db.ReadMeta(Data.TarinoiDb.ApiSyncCursorKey);
                Assert.IsNotEmpty(cursor, "the first sync must leave a cursor behind");

                var second = importer
                    .SyncAsync(_apiPath, _apiKey, fixture.Db,
                        skipTlsVerify: TarinoiSettings.Instance.skipTlsVerify)
                    .GetAwaiter().GetResult();

                Assert.IsTrue(second.Success, second.Error);
                Assert.AreEqual(0, second.Stats.DocumentsUpserted,
                    "nothing changed server-side, so an incremental sync should be a no-op");
            }
        }

        [Test]
        public void ABadTokenIsRejectedWithAnActionableMessage()
        {
            using (var fixture = new TestDb())
            {
                var result = new ApiImporter()
                    .SyncAsync(_apiPath, "definitely-not-a-valid-token", fixture.Db,
                        skipTlsVerify: TarinoiSettings.Instance.skipTlsVerify)
                    .GetAwaiter().GetResult();

                Assert.IsFalse(result.Success);
                StringAssert.Contains("credentials rejected", result.Error);
            }
        }
    }
}
