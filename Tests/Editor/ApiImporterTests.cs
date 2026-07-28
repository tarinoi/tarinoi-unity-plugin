using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Tarinoi.Data;
using Tarinoi.Sync;

namespace Tarinoi.Tests
{
    public class ApiImporterTests
    {
        const string ApiPath = "https://app.tarinoi.com/api/v1/group1/proj1/documents";
        const string ApiKey = "test-token";

        /// <summary>
        /// A scripted HTTP transport. Lets the whole pagination loop, error handling and
        /// upsert semantics be exercised with no network and no API key.
        /// </summary>
        sealed class FakeHandler : HttpMessageHandler
        {
            readonly Queue<(HttpStatusCode Status, string Body)> _responses =
                new Queue<(HttpStatusCode, string)>();

            public readonly List<Uri> RequestedUris = new List<Uri>();
            public readonly List<string> AuthHeaders = new List<string>();
            public readonly List<string> AcceptHeaders = new List<string>();

            public FakeHandler Respond(string body, HttpStatusCode status = HttpStatusCode.OK)
            {
                _responses.Enqueue((status, body));
                return this;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestedUris.Add(request.RequestUri);
                AuthHeaders.Add(request.Headers.Authorization?.ToString() ?? "");
                AcceptHeaders.Add(string.Join(",", request.Headers.Accept.Select(a => a.MediaType)));

                var (status, body) = _responses.Count > 0
                    ? _responses.Dequeue()
                    : (HttpStatusCode.OK, "");

                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(body ?? ""),
                });
            }
        }

        TestDb _fixture;

        [SetUp]
        public void SetUp() => _fixture = new TestDb();

        [TearDown]
        public void TearDown() => _fixture.Dispose();

        static SyncResult Sync(FakeHandler handler, TarinoiDb db,
            string apiPath = ApiPath, string apiKey = ApiKey,
            IProgress<SyncProgress> progress = null)
        {
            return new ApiImporter(handler)
                .SyncAsync(apiPath, apiKey, db, progress)
                .GetAwaiter().GetResult();
        }

        // The collection id matches TestDb.InsertDocument's default, so tests can mix
        // pre-seeded rows with synced ones and have them refer to the same documents.
        static string Doc(string documentId, string collectionId = "col1",
            string layerId = LayerFilter.MainLayer, string documentType = "card",
            long updateKey = 1, bool tombstone = false, bool archived = false, bool moved = false,
            string dataVersion = "1.0.0", string identifier = null, string payload = "{\"a\":1}")
        {
            var o = new JObject
            {
                ["document_id"] = documentId,
                ["collection_id"] = collectionId,
                ["layer_id"] = layerId,
                ["document_type"] = documentType,
                ["update_key"] = updateKey,
                ["is_tombstone"] = tombstone,
                ["is_archived"] = archived,
                ["is_moved"] = moved,
                ["data_version"] = dataVersion,
                ["identifier"] = identifier,
                ["payload"] = JToken.Parse(payload),
            };
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        // ---------------------------------------------------------------------
        // Preconditions
        // ---------------------------------------------------------------------

        [Test]
        public void MissingApiKeyFailsWithAnActionableMessage()
        {
            var result = Sync(new FakeHandler(), _fixture.Db, apiKey: "");
            Assert.IsFalse(result.Success);
            StringAssert.Contains("Set API token", result.Error);
        }

        [Test]
        public void MissingApiPathFails()
        {
            var result = Sync(new FakeHandler(), _fixture.Db, apiPath: "");
            Assert.IsFalse(result.Success);
            StringAssert.Contains("No API path", result.Error);
        }

        [Test]
        public void UnparseableApiPathFails()
        {
            var result = Sync(new FakeHandler(), _fixture.Db, apiPath: "not a url");
            Assert.IsFalse(result.Success);
            StringAssert.Contains("Cannot parse", result.Error);
        }

        [Test]
        public void ClosedDatabaseFails()
        {
            var result = Sync(new FakeHandler(), new TarinoiDb());
            Assert.IsFalse(result.Success);
            StringAssert.Contains("open database", result.Error);
        }

        // ---------------------------------------------------------------------
        // Happy path and pagination
        // ---------------------------------------------------------------------

        [Test]
        public void SinglePageSyncStoresDocumentsAndRecordsConfiguration()
        {
            var handler = new FakeHandler().Respond(Doc("d1") + "\n" + Doc("d2"));

            var result = Sync(handler, _fixture.Db);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(2, result.Stats.DocumentsUpserted);
            CollectionAssert.AreEqual(new[] { "d1", "d2" }, _fixture.VisibleDocumentIds());
            Assert.AreEqual("proj1", _fixture.Db.ReadMeta(TarinoiDb.ProjectIdKey));
            Assert.AreEqual(ApiPath, _fixture.Db.ReadMeta(TarinoiDb.ApiPathKey));
        }

        [Test]
        public void RequestCarriesBearerTokenAndNdjsonAccept()
        {
            var handler = new FakeHandler().Respond(Doc("d1"));
            Sync(handler, _fixture.Db);

            Assert.AreEqual("Bearer " + ApiKey, handler.AuthHeaders[0]);
            StringAssert.Contains("application/x-ndjson", handler.AcceptHeaders[0]);
        }

        [Test]
        public void PaginationFollowsTheCursorUntilItStops()
        {
            var handler = new FakeHandler()
                .Respond(Doc("d1") + "\n{\"cursor\":\"10\"}")
                .Respond(Doc("d2") + "\n{\"cursor\":\"20\"}")
                .Respond(Doc("d3"));

            var result = Sync(handler, _fixture.Db);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(3, handler.RequestedUris.Count);
            Assert.IsFalse(handler.RequestedUris[0].Query.Contains("cursor"),
                "the first request has no cursor");
            StringAssert.Contains("cursor=10", handler.RequestedUris[1].Query);
            StringAssert.Contains("cursor=20", handler.RequestedUris[2].Query);
            CollectionAssert.AreEqual(new[] { "d1", "d2", "d3" }, _fixture.VisibleDocumentIds());
        }

        [Test]
        public void CursorIsPersistedAfterEachPageSoAnInterruptedSyncResumes()
        {
            var handler = new FakeHandler()
                .Respond(Doc("d1") + "\n{\"cursor\":\"10\"}")
                .Respond(Doc("d2"));

            Sync(handler, _fixture.Db);

            Assert.AreEqual("10", _fixture.Db.ReadMeta(TarinoiDb.ApiSyncCursorKey));
        }

        [Test]
        public void AStoredCursorMakesTheNextSyncIncremental()
        {
            _fixture.Db.WriteMeta(TarinoiDb.ApiSyncCursorKey, "500");
            var handler = new FakeHandler().Respond(Doc("d1", updateKey: 501));

            Sync(handler, _fixture.Db);

            StringAssert.Contains("cursor=500", handler.RequestedUris[0].Query,
                "an existing cursor must be sent on the very first request");
        }

        [Test]
        public void WithNoServerCursorTheHighestUpdateKeyBecomesTheCursor()
        {
            // This is what makes the *next* sync incremental when the server doesn't
            // paginate explicitly.
            var handler = new FakeHandler()
                .Respond(Doc("d1", updateKey: 7) + "\n" + Doc("d2", updateKey: 42));

            Sync(handler, _fixture.Db);

            Assert.AreEqual("42", _fixture.Db.ReadMeta(TarinoiDb.ApiSyncCursorKey));
        }

        [Test]
        public void AnEmptyIncrementalResponseLeavesContentAndCursorAlone()
        {
            _fixture.InsertDocument("existing", updateKey: 5);
            _fixture.Db.WriteMeta(TarinoiDb.ApiSyncCursorKey, "5");
            var handler = new FakeHandler().Respond("");

            var result = Sync(handler, _fixture.Db);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(0, result.Stats.DocumentsUpserted);
            CollectionAssert.AreEqual(new[] { "existing" }, _fixture.VisibleDocumentIds());
            Assert.AreEqual("5", _fixture.Db.ReadMeta(TarinoiDb.ApiSyncCursorKey));
        }

        [Test]
        public void ProgressIsReported()
        {
            var reports = new List<SyncProgress>();
            var progress = new SimpleProgress(reports.Add);

            Sync(new FakeHandler().Respond(Doc("d1")), _fixture.Db, progress: progress);

            Assert.IsNotEmpty(reports);
            Assert.AreEqual(1f, reports.Last().Fraction, "the final report completes the bar");
        }

        sealed class SimpleProgress : IProgress<SyncProgress>
        {
            readonly Action<SyncProgress> _onReport;
            public SimpleProgress(Action<SyncProgress> onReport) => _onReport = onReport;
            public void Report(SyncProgress value) => _onReport(value);
        }

        // ---------------------------------------------------------------------
        // HTTP failures
        // ---------------------------------------------------------------------

        [TestCase(HttpStatusCode.Unauthorized, "credentials rejected")]
        [TestCase(HttpStatusCode.Forbidden, "credentials rejected")]
        [TestCase(HttpStatusCode.NotFound, "project not found")]
        [TestCase(HttpStatusCode.InternalServerError, "server error")]
        [TestCase(HttpStatusCode.BadRequest, "unexpected response")]
        public void HttpFailuresBecomeActionableMessages(HttpStatusCode status, string expected)
        {
            var result = Sync(new FakeHandler().Respond("", status), _fixture.Db);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(expected, result.Error);
            StringAssert.Contains(((int)status).ToString(), result.Error);
        }

        [Test]
        public void AFailedPageLeavesTheCursorUnchanged()
        {
            _fixture.Db.WriteMeta(TarinoiDb.ApiSyncCursorKey, "100");
            Sync(new FakeHandler().Respond("", HttpStatusCode.InternalServerError), _fixture.Db);

            Assert.AreEqual("100", _fixture.Db.ReadMeta(TarinoiDb.ApiSyncCursorKey),
                "a failed sync must be safely retryable");
        }

        // ---------------------------------------------------------------------
        // Layer-aware upsert
        // ---------------------------------------------------------------------

        [Test]
        public void ActiveDocumentsOnBothLayersCoexist()
        {
            var handler = new FakeHandler().Respond(
                Doc("d1", layerId: LayerFilter.MainLayer) + "\n" +
                Doc("d1", layerId: LayerFilter.BufferLayer));

            Sync(handler, _fixture.Db);

            Assert.AreEqual(2, _fixture.Db.QueryScalars<int>("SELECT COUNT(*) FROM documents")[0],
                "both layer rows are stored");
            Assert.AreEqual(1, _fixture.VisibleDocumentIds().Count,
                "but the merge shows one document");
        }

        [Test]
        public void TombstonedDocumentsAreDeleted()
        {
            _fixture.InsertDocument("d1");
            var handler = new FakeHandler().Respond(Doc("d1", tombstone: true));

            var result = Sync(handler, _fixture.Db);

            Assert.AreEqual(1, result.Stats.DocumentsDeleted);
            Assert.IsEmpty(_fixture.VisibleDocumentIds());
        }

        [Test]
        public void TombstoningAManifestAlsoEvictsItFromCollections()
        {
            _fixture.Db.Execute(
                "INSERT INTO collections VALUES ('col-doc', 'Name', 'card-collection', '{}')");
            var handler = new FakeHandler().Respond(
                Doc("col-doc", documentType: "collection-manifest", tombstone: true));

            Sync(handler, _fixture.Db);

            Assert.AreEqual(0, _fixture.Db.QueryScalars<int>("SELECT COUNT(*) FROM collections")[0]);
        }

        [Test]
        public void TombstoneOnlyRemovesTheMatchingLayer()
        {
            _fixture.InsertDocument("d1", layerId: LayerFilter.MainLayer);
            _fixture.InsertDocument("d1", layerId: LayerFilter.BufferLayer);

            var handler = new FakeHandler().Respond(Doc("d1", layerId: LayerFilter.BufferLayer, tombstone: true));
            Sync(handler, _fixture.Db);

            var layers = _fixture.Db.QueryScalars<string>("SELECT layer_id FROM documents");
            CollectionAssert.AreEqual(new[] { LayerFilter.MainLayer }, layers);
        }

        [Test]
        public void ArchivedBufferRowsAreKeptAsSuppressionMarkers()
        {
            // Deleting these would wrongly resurrect the committed version — this is the
            // subtlest rule in the whole importer.
            var handler = new FakeHandler().Respond(
                Doc("d1", layerId: LayerFilter.MainLayer) + "\n" +
                Doc("d1", layerId: LayerFilter.BufferLayer, archived: true));

            var result = Sync(handler, _fixture.Db);

            Assert.AreEqual(2, _fixture.Db.QueryScalars<int>("SELECT COUNT(*) FROM documents")[0],
                "the archived buffer row must be stored, not dropped");
            Assert.IsEmpty(_fixture.VisibleDocumentIds(),
                "and it must hide the committed version");
            Assert.AreEqual(1, result.Stats.DocumentsUpserted);
            Assert.AreEqual(1, result.Stats.DocumentsDeleted);
        }

        [Test]
        public void MovedDocumentsAreStoredWithTheirFlag()
        {
            Sync(new FakeHandler().Respond(Doc("d1", moved: true)), _fixture.Db);

            Assert.AreEqual(1, _fixture.Db.QueryScalars<int>("SELECT is_moved FROM documents")[0]);
            Assert.IsEmpty(_fixture.VisibleDocumentIds());
        }

        [Test]
        public void ResyncingADocumentReplacesItInPlace()
        {
            var handler = new FakeHandler()
                .Respond(Doc("d1", updateKey: 1, payload: "{\"v\":1}"))
                .Respond(Doc("d1", updateKey: 2, payload: "{\"v\":2}"));

            Sync(handler, _fixture.Db);
            Sync(handler, _fixture.Db);

            Assert.AreEqual(1, _fixture.Db.QueryScalars<int>("SELECT COUNT(*) FROM documents")[0]);
            StringAssert.Contains("\"v\":2", _fixture.VisiblePayloads()[0]);
        }

        [Test]
        public void DocumentFieldsAreStored()
        {
            Sync(new FakeHandler().Respond(
                Doc("d1", documentType: "entity", identifier: "narrator", updateKey: 77)), _fixture.Db);

            var row = _fixture.Db.Query<DocumentRow>("SELECT * FROM documents")[0];
            Assert.AreEqual("entity", row.DocumentType);
            Assert.AreEqual("narrator", row.Identifier);
            Assert.AreEqual(77, row.UpdateKey);
            Assert.AreEqual("document", row.Namespace);
        }

        [Test]
        public void DocumentsMissingIdentityAreSkippedWithAWarning()
        {
            var handler = new FakeHandler().Respond("{\"document_id\":\"\",\"collection_id\":\"c1\"}");

            var result = Sync(handler, _fixture.Db);

            Assert.IsTrue(result.Success);
            Assert.IsEmpty(_fixture.VisibleDocumentIds());
            Assert.IsNotEmpty(result.Stats.Warnings);
        }

        [Test]
        public void MissingPayloadBecomesAnEmptyObject()
        {
            var handler = new FakeHandler().Respond(
                "{\"document_id\":\"d1\",\"collection_id\":\"c1\",\"layer_id\":\""
                + LayerFilter.MainLayer + "\",\"update_key\":1}");

            Sync(handler, _fixture.Db);

            Assert.AreEqual("{}", _fixture.VisiblePayloads()[0]);
        }

        [Test]
        public void FlagsSentAsNumbersOrStringsAreUnderstood()
        {
            // The feed is not strict about how it encodes booleans.
            var handler = new FakeHandler().Respond(
                "{\"document_id\":\"d1\",\"collection_id\":\"c1\",\"layer_id\":\""
                + LayerFilter.MainLayer + "\",\"update_key\":1,\"is_archived\":1}");

            Sync(handler, _fixture.Db);

            Assert.AreEqual(1, _fixture.Db.QueryScalars<int>("SELECT is_archived FROM documents")[0]);
        }

        // ---------------------------------------------------------------------
        // Collections rebuild and version gate
        // ---------------------------------------------------------------------

        [Test]
        public void CollectionManifestsRebuildTheCollectionsTable()
        {
            var handler = new FakeHandler().Respond(Doc("col1",
                documentType: "collection-manifest",
                payload: "{\"label\":\"Global\",\"collection_type\":\"list-collection\"}"));

            var result = Sync(handler, _fixture.Db);

            Assert.AreEqual(1, result.Stats.CollectionsUpdated);
            var row = _fixture.Db.Query<CollectionRow>("SELECT * FROM collections")[0];
            Assert.AreEqual("col1", row.CollectionId);
            Assert.AreEqual("Global", row.CollectionName);
            Assert.AreEqual("list-collection", row.CollectionType);
        }

        [Test]
        public void CollectionNameFallsBackWhenThereIsNoLabel()
        {
            var handler = new FakeHandler().Respond(Doc("col1",
                documentType: "collection-manifest",
                payload: "{\"collection_name\":\"fallback\",\"collection_type\":\"card-collection\"}"));

            Sync(handler, _fixture.Db);

            Assert.AreEqual("fallback",
                _fixture.Db.Query<CollectionRow>("SELECT * FROM collections")[0].CollectionName);
        }

        [Test]
        public void ArchivedManifestsAreNotRebuiltIntoCollections()
        {
            var handler = new FakeHandler().Respond(Doc("col1",
                documentType: "collection-manifest", archived: true,
                payload: "{\"label\":\"Gone\"}"));

            var result = Sync(handler, _fixture.Db);

            Assert.AreEqual(0, result.Stats.CollectionsUpdated);
        }

        [Test]
        public void AMajorDataVersionMismatchAbortsTheSync()
        {
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex("MAJOR data format mismatch"));

            var handler = new FakeHandler().Respond(Doc("d1", dataVersion: "2.0.0"));
            var result = Sync(handler, _fixture.Db);

            Assert.IsFalse(result.Success);
            StringAssert.Contains("MAJOR", result.Error);
        }

        [Test]
        public void AnAbortedPageWritesNothingAndLeavesTheCursorAlone()
        {
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex("MAJOR data format mismatch"));

            _fixture.Db.WriteMeta(TarinoiDb.ApiSyncCursorKey, "100");
            var handler = new FakeHandler().Respond(
                Doc("good") + "\n" + Doc("bad", dataVersion: "9.0.0"));

            var result = Sync(handler, _fixture.Db);

            Assert.IsFalse(result.Success);
            Assert.IsEmpty(_fixture.VisibleDocumentIds(),
                "the page is applied atomically, so a fatal document rolls back its neighbours");
            Assert.AreEqual("100", _fixture.Db.ReadMeta(TarinoiDb.ApiSyncCursorKey),
                "an upgraded client must resume from the same point");
        }

        [Test]
        public void ACompatibleMinorVersionSyncsWithAWarning()
        {
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex("minor data format mismatch"));

            var result = Sync(new FakeHandler().Respond(Doc("d1", dataVersion: "1.1.0")), _fixture.Db);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(1, result.Stats.DocumentsUpserted);
        }

        // ---------------------------------------------------------------------
        // URL helper
        // ---------------------------------------------------------------------

        [Test]
        public void AppendCursorAddsAQueryParameter()
        {
            var uri = ApiImporter.AppendCursor(new Uri("https://h/x/documents"), "42");
            Assert.AreEqual("?cursor=42", uri.Query);
        }

        [Test]
        public void AppendCursorPreservesExistingQueryParameters()
        {
            var uri = ApiImporter.AppendCursor(new Uri("https://h/x/documents?foo=bar"), "42");
            StringAssert.Contains("foo=bar", uri.Query);
            StringAssert.Contains("cursor=42", uri.Query);
        }

        [Test]
        public void AppendCursorEscapesTheValue()
        {
            var uri = ApiImporter.AppendCursor(new Uri("https://h/x"), "a b&c");
            StringAssert.DoesNotContain("a b&c", uri.Query);
            StringAssert.Contains("cursor=a%20b%26c", uri.Query);
        }
    }
}
