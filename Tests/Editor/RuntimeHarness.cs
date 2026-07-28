using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Tarinoi;
using Tarinoi.Data;
using UnityEngine;

namespace Tarinoi.Tests
{
    /// <summary>Serves cards from memory, and records what was asked for.</summary>
    public sealed class FakeDocumentStore : IDocumentStore
    {
        readonly Dictionary<string, JObject> _cards = new Dictionary<string, JObject>();

        public readonly List<string> LoadedCardIds = new List<string>();
        public readonly List<StartCardRow> StartCards = new List<StartCardRow>();

        IEntityCache _entities;

        public void Setup(TarinoiDb db, IEntityCache entities) => _entities = entities;

        public void Add(string cardId, JObject card, string collectionId = "col1")
        {
            _cards[collectionId + "/" + cardId] = card;
        }

        public Task<JObject> LoadCardAsync(string collectionId, string cardId)
        {
            LoadedCardIds.Add(cardId);
            _cards.TryGetValue(collectionId + "/" + cardId, out var card);
            return Task.FromResult(card);
        }

        public Task<JObject> GetDocumentAsync(string documentId, string collectionId = null) =>
            LoadCardAsync(collectionId ?? "col1", documentId);

        public Task<JObject> GetEntityAsync(string identifier) =>
            Task.FromResult(_entities?.GetEntity(identifier));

        public Task<List<StartCardRow>> QueryStartCardsAsync() =>
            Task.FromResult(new List<StartCardRow>(StartCards));
    }

    /// <summary>Builds card payloads without a wall of JSON in every test.</summary>
    public sealed class CardBuilder
    {
        readonly JObject _card = new JObject();

        public static CardBuilder Of(string baseRef) => new CardBuilder().Base(baseRef);
        public static CardBuilder Line(string line = "") => Of("line").Data("line", line);
        public static CardBuilder Start() => Of("start");
        public static CardBuilder Blank() => Of("blank");

        public CardBuilder Base(string baseRef)
        {
            _card["base_ref"] = baseRef;
            return this;
        }

        public CardBuilder Mode(string lineMode)
        {
            _card["line_mode"] = lineMode;
            return this;
        }

        public CardBuilder Entity(string entityRef)
        {
            _card["entity_ref"] = entityRef;
            return this;
        }

        public CardBuilder Template(string templateRef)
        {
            _card["template_ref"] = templateRef;
            return this;
        }

        /// <summary>Sets the entry condition. Pass null to store an explicit JSON null.</summary>
        public CardBuilder Condition(string condition)
        {
            _card["input_pin"] = condition == null
                ? (JToken)JValue.CreateNull()
                : new JObject { ["condition"] = condition };
            return this;
        }

        public CardBuilder Connect(params string[] connections)
        {
            _card["connections"] = new JArray(connections);
            return this;
        }

        /// <summary>Adds a plain "default" connection to each target.</summary>
        public CardBuilder To(params string[] targets)
        {
            var connections = new JArray();
            foreach (var target in targets)
            {
                connections.Add("default>>" + target);
            }

            _card["connections"] = connections;
            return this;
        }

        public CardBuilder Selector(string expression)
        {
            _card["output_selector"] = expression;
            return this;
        }

        public CardBuilder Geo(double y)
        {
            _card["geo"] = new JObject { ["x"] = 0, ["y"] = y };
            return this;
        }

        public CardBuilder Data(string key, object value)
        {
            var data = _card["data"] as JObject;
            if (data == null)
            {
                data = new JObject();
                _card["data"] = data;
            }

            data[key] = value == null ? JValue.CreateNull() : JToken.FromObject(value);
            return this;
        }

        /// <summary>Declares property order, as the authoring tool does.</summary>
        public CardBuilder Props(params string[] names)
        {
            var props = new JArray();
            foreach (var name in names)
            {
                props.Add(new JObject { ["name"] = name });
            }

            _card["props"] = props;
            return this;
        }

        public CardBuilder Jump(string collectionId, string cardId)
        {
            _card["base_ref"] = "jump";
            return Data("target_collection_id", collectionId).Data("target_card_id", cardId);
        }

        public JObject Build() => _card;

        public static implicit operator JObject(CardBuilder builder) => builder.Build();
    }

    /// <summary>
    /// A configured <see cref="TarinoiRuntime"/> backed by a fake store and a throwaway
    /// database, with every event recorded.
    /// </summary>
    /// <remarks>
    /// A real database is used rather than a mock because the runtime's caches are
    /// populated by real queries with the layer merge applied — that path is worth
    /// exercising. Cards themselves come from the fake store, which keeps the tests about
    /// traversal rather than SQL.
    /// </remarks>
    public sealed class RuntimeHarness : IDisposable
    {
        public readonly TarinoiRuntime Runtime = new TarinoiRuntime();
        public readonly FakeDocumentStore Store = new FakeDocumentStore();

        public readonly List<DialogueLine> Lines = new List<DialogueLine>();
        public readonly List<IReadOnlyList<DialogueChoice>> ChoiceSets =
            new List<IReadOnlyList<DialogueChoice>>();
        public readonly List<DialogueLine> ChoicesMade = new List<DialogueLine>();
        public readonly List<string> Errors = new List<string>();
        public readonly List<IReadOnlyList<string>> PinRequests = new List<IReadOnlyList<string>>();
        public int EndedCount;

        readonly string _projectId;
        readonly TarinoiSettings _settings;

        public RuntimeHarness()
        {
            _projectId = "__test__rt_" + Guid.NewGuid().ToString("N");
            _settings = ScriptableObject.CreateInstance<TarinoiSettings>();
            _settings.apiPath = $"https://example.com/api/v1/group/{_projectId}/documents";

            Runtime.DocumentStore = Store;
            Runtime.LineReady += Lines.Add;
            Runtime.ChoicesReady += ChoiceSets.Add;
            Runtime.ChoiceMade += ChoicesMade.Add;
            Runtime.DialogueError += Errors.Add;
            Runtime.PinChoiceNeeded += PinRequests.Add;
            Runtime.DialogueEnded += () => EndedCount++;
        }

        public TarinoiSettings Settings => _settings;

        /// <summary>Most recent choice set offered.</summary>
        public IReadOnlyList<DialogueChoice> Choices =>
            ChoiceSets.Count > 0 ? ChoiceSets[ChoiceSets.Count - 1] : new DialogueChoice[0];

        public DialogueLine LastLine => Lines.Count > 0 ? Lines[Lines.Count - 1] : null;

        /// <summary>
        /// Writes an entity into the database. Call before <see cref="Configure"/> — the
        /// caches are loaded during configuration.
        /// </summary>
        public RuntimeHarness SeedEntity(string identifier, bool isPlayerCharacter, string label = null)
        {
            var payload = new JObject
            {
                ["is_player_character"] = isPlayerCharacter,
                ["label"] = label ?? identifier,
            };

            WriteDocument("entity", identifier, payload.ToString(Newtonsoft.Json.Formatting.None));
            return this;
        }

        /// <summary>Writes a collection manifest into the database.</summary>
        public RuntimeHarness SeedCollection(string collectionId, string label,
            string collectionType = "card-collection", string collectionName = null)
        {
            var payload = new JObject
            {
                ["label"] = label,
                ["collection_type"] = collectionType,
                ["collection_name"] = collectionName ?? collectionId,
            };

            WriteDocument("collection-manifest", collectionId,
                payload.ToString(Newtonsoft.Json.Formatting.None), collectionId);
            return this;
        }

        void WriteDocument(string documentType, string identifier, string payload,
            string documentId = null)
        {
            using (var db = new TarinoiDb())
            {
                if (!db.Open(_projectId))
                {
                    throw new InvalidOperationException("could not open the harness database");
                }

                db.Execute(
                    @"INSERT OR REPLACE INTO documents
                      (document_id, collection_id, document_type, layer_id, namespace, identifier,
                       update_key, is_tombstone, is_archived, is_moved, payload)
                      VALUES (?, ?, ?, ?, 'document', ?, 1, 0, 0, 0, ?)",
                    documentId ?? identifier, "col1", documentType, LayerFilter.MainLayer,
                    identifier, payload);
            }
        }

        /// <summary>Configures the runtime. Seed any entities or collections first.</summary>
        public RuntimeHarness Configure()
        {
            var ok = Runtime.ConfigureAsync(_settings).GetAwaiter().GetResult();
            if (!ok)
            {
                throw new InvalidOperationException("harness runtime failed to configure");
            }

            return this;
        }

        // Every await in the runtime completes synchronously against the fake store, so
        // blocking here cannot deadlock. See the threading note on TarinoiRuntime.
        public void Start(string cardId, string collectionId = "col1") =>
            Runtime.StartDialogueAsync(collectionId, cardId).GetAwaiter().GetResult();

        public void Advance() => Runtime.AdvanceAsync().GetAwaiter().GetResult();

        public void Select(int index) => Runtime.SelectChoiceAsync(index).GetAwaiter().GetResult();

        public void SelectPin(string pin) => Runtime.SelectPinAsync(pin).GetAwaiter().GetResult();

        public IReadOnlyList<StartCard> GetStartCards() =>
            Runtime.GetStartCardsAsync().GetAwaiter().GetResult();

        public void Dispose()
        {
            Runtime.Shutdown();

            if (_settings != null)
            {
                UnityEngine.Object.DestroyImmediate(_settings);
            }

            var path = TarinoiDb.PathForProject(_projectId);
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
