using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Tarinoi.Data;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tarinoi.Tests
{
    public class SqliteDocumentStoreTests
    {
        class FakeEntityCache : IEntityCache
        {
            public readonly Dictionary<string, JObject> Entities = new Dictionary<string, JObject>();

            public JObject GetEntity(string identifier)
            {
                return Entities.TryGetValue(identifier ?? "", out var e) ? e : null;
            }
        }

        TestDb _fixture;
        SqliteDocumentStore _store;
        FakeEntityCache _entities;

        [SetUp]
        public void SetUp()
        {
            _fixture = new TestDb();
            _entities = new FakeEntityCache();
            _store = new SqliteDocumentStore();
            _store.Setup(_fixture.Db, _entities);
        }

        [TearDown]
        public void TearDown()
        {
            _fixture.Dispose();
        }

        static T Await<T>(Task<T> task) => task.GetAwaiter().GetResult();

        [Test]
        public void LoadCardReturnsTheParsedPayload()
        {
            _fixture.InsertDocument("card1", "col1",
                payload: "{\"base_ref\":\"line\",\"data\":{\"label\":\"Hello\"}}");

            var card = Await(_store.LoadCardAsync("col1", "card1"));

            Assert.IsNotNull(card);
            Assert.AreEqual("line", (string)card["base_ref"]);
            Assert.AreEqual("Hello", (string)card["data"]["label"]);
        }

        [Test]
        public void LoadCardReturnsNullForAMissingCard()
        {
            Assert.IsNull(Await(_store.LoadCardAsync("col1", "nope")));
        }

        [Test]
        public void LoadCardRespectsTheActiveFilter()
        {
            _fixture.InsertDocument("card1", "col1", archived: true);
            Assert.IsNull(Await(_store.LoadCardAsync("col1", "card1")),
                "archived content must not reach the runtime");
        }

        [Test]
        public void LoadCardPrefersTheBufferLayer()
        {
            _fixture.InsertDocument("card1", "col1", LayerFilter.MainLayer,
                payload: "{\"data\":{\"label\":\"committed\"}}");
            _fixture.InsertDocument("card1", "col1", LayerFilter.BufferLayer,
                payload: "{\"data\":{\"label\":\"edited\"}}");

            var card = Await(_store.LoadCardAsync("col1", "card1"));
            Assert.AreEqual("edited", (string)card["data"]["label"]);
        }

        [Test]
        public void GetDocumentWithoutACollectionFindsTheDocument()
        {
            _fixture.InsertDocument("doc1", "col1", payload: "{\"kind\":\"anything\"}");

            var doc = Await(_store.GetDocumentAsync("doc1"));
            Assert.AreEqual("anything", (string)doc["kind"]);
        }

        [Test]
        public void GetDocumentWithACollectionDisambiguates()
        {
            _fixture.InsertDocument("shared", "col1", payload: "{\"from\":\"col1\"}");
            _fixture.InsertDocument("shared", "col2", payload: "{\"from\":\"col2\"}");

            Assert.AreEqual("col2", (string)Await(_store.GetDocumentAsync("shared", "col2"))["from"]);
        }

        [Test]
        public void GetDocumentReturnsNullForBlankInput()
        {
            Assert.IsNull(Await(_store.GetDocumentAsync("")));
            Assert.IsNull(Await(_store.GetDocumentAsync(null)));
        }

        [Test]
        public void MalformedPayloadIsLoggedAndTreatedAsMissing()
        {
            _fixture.InsertDocument("broken", "col1", payload: "{not json");

            LogAssert.Expect(LogType.Error, new Regex("unreadable payload"));
            Assert.IsNull(Await(_store.LoadCardAsync("col1", "broken")),
                "one corrupt document must not throw and stop a dialogue");
        }

        [Test]
        public void GetEntityReadsFromTheCacheNotTheDatabase()
        {
            _entities.Entities["narrator"] = JObject.Parse("{\"is_player_character\":false}");

            var entity = Await(_store.GetEntityAsync("narrator"));
            Assert.IsNotNull(entity);
            Assert.IsFalse((bool)entity["is_player_character"]);

            Assert.IsNull(Await(_store.GetEntityAsync("unknown")));
        }

        [Test]
        public void QueryStartCardsReturnsOnlyStartCardsWithTheirLabels()
        {
            _fixture.InsertDocument("start1", "col1",
                payload: "{\"base_ref\":\"start\",\"data\":{\"label\":\"Opening\"}}");
            _fixture.InsertDocument("start2", "col2",
                payload: "{\"base_ref\":\"start\",\"data\":{\"label\":\"Side quest\"}}");
            _fixture.InsertDocument("line1", "col1",
                payload: "{\"base_ref\":\"line\",\"data\":{\"label\":\"Not a start\"}}");

            var cards = Await(_store.QueryStartCardsAsync());

            CollectionAssert.AreEquivalent(new[] { "start1", "start2" },
                cards.Select(c => c.DocumentId).ToList());
            Assert.AreEqual("Opening", cards.First(c => c.DocumentId == "start1").Label);
            Assert.AreEqual("col2", cards.First(c => c.DocumentId == "start2").CollectionId);
        }

        [Test]
        public void QueryStartCardsRespectsTheActiveFilter()
        {
            _fixture.InsertDocument("visible", "col1",
                payload: "{\"base_ref\":\"start\",\"data\":{\"label\":\"Yes\"}}");
            _fixture.InsertDocument("hidden", "col1", moved: true,
                payload: "{\"base_ref\":\"start\",\"data\":{\"label\":\"No\"}}");

            var cards = Await(_store.QueryStartCardsAsync());
            CollectionAssert.AreEqual(new[] { "visible" }, cards.Select(c => c.DocumentId).ToList());
        }

        [Test]
        public void AClosedDatabaseYieldsEmptyResultsRatherThanErrors()
        {
            var store = new SqliteDocumentStore();
            store.Setup(new TarinoiDb(), _entities);

            Assert.IsNull(Await(store.LoadCardAsync("col1", "card1")));
            Assert.IsNull(Await(store.GetDocumentAsync("doc1")));
            Assert.IsEmpty(Await(store.QueryStartCardsAsync()));
        }

        [Test]
        public void TheQuerySeamCanBeOverridden()
        {
            // Proves the documented extension point works: an integration overriding
            // QueryAsync redirects every accessor at once.
            var store = new RecordingStore();
            store.Setup(_fixture.Db, _entities);
            _fixture.InsertDocument("card1", "col1", payload: "{\"base_ref\":\"line\"}");

            Await(store.LoadCardAsync("col1", "card1"));
            Await(store.QueryStartCardsAsync());

            Assert.AreEqual(2, store.QueryCount, "both accessors must route through the seam");
        }

        class RecordingStore : SqliteDocumentStore
        {
            public int QueryCount;

            protected override Task<List<T>> QueryAsync<T>(string sql, params object[] args)
            {
                QueryCount++;
                return base.QueryAsync<T>(sql, args);
            }
        }
    }
}
