using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Tarinoi.Data;
using Tarinoi.Ui;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Tarinoi.Tests
{
    /// <summary>
    /// Drives the sample interface in play mode.
    /// </summary>
    /// <remarks>
    /// Everything else in this suite runs in edit mode, which cannot exercise what only
    /// exists while playing: canvases, layout, coroutines, and the event wiring between
    /// the runtime and the widgets. These tests build the real UI, feed it dialogue from a
    /// fake store, and click the actual buttons.
    /// </remarks>
    public class DialogueUiPlayModeTests
    {
        /// <summary>Serves cards from memory, so no database or network is involved.</summary>
        sealed class FakeStore : IDocumentStore
        {
            public readonly Dictionary<string, JObject> Cards = new Dictionary<string, JObject>();
            public readonly List<StartCardRow> StartCards = new List<StartCardRow>();

            public void Setup(TarinoiDb db, IEntityCache entities) { }

            public Task<JObject> LoadCardAsync(string collectionId, string cardId)
            {
                Cards.TryGetValue(cardId, out var card);
                return Task.FromResult(card);
            }

            public Task<JObject> GetDocumentAsync(string documentId, string collectionId = null) =>
                LoadCardAsync(collectionId, documentId);

            public Task<JObject> GetEntityAsync(string identifier) => Task.FromResult<JObject>(null);

            public Task<List<StartCardRow>> QueryStartCardsAsync() =>
                Task.FromResult(new List<StartCardRow>(StartCards));
        }

        TarinoiRuntime _runtime;
        FakeStore _store;
        GameObject _host;
        DialogueStrip _strip;
        TarinoiSettings _settings;
        string _projectId;

        [SetUp]
        public void SetUp()
        {
            _runtime = new TarinoiRuntime();
            _store = new FakeStore();

            // Assigned before configuring, so the runtime keeps it instead of building a
            // database-backed store. The database itself still has to exist: the runtime
            // refuses to start a dialogue until it is configured.
            _runtime.DocumentStore = _store;

            _projectId = "__test__ui_" + System.Guid.NewGuid().ToString("N");
            _settings = ScriptableObject.CreateInstance<TarinoiSettings>();
            _settings.apiPath = $"https://example.invalid/api/{_projectId}/documents";
            Assert.IsTrue(_runtime.ConfigureAsync(_settings).GetAwaiter().GetResult());

            _host = new GameObject("UiHost");
            var canvas = QuickstartUi.CreateCanvas("Canvas", _host.transform);

            _strip = new GameObject("Strip", typeof(RectTransform)).AddComponent<DialogueStrip>();
            _strip.Build(_runtime, canvas.transform);
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                Object.Destroy(_host);
            }

            _runtime.Shutdown();

            if (_settings != null)
            {
                Object.DestroyImmediate(_settings);
            }

            var path = TarinoiDb.PathForProject(_projectId);
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                if (System.IO.File.Exists(path + suffix))
                {
                    System.IO.File.Delete(path + suffix);
                }
            }
        }

        static JObject Line(string text, string mode = "npc", params string[] connections)
        {
            return new JObject
            {
                ["base_ref"] = "line",
                ["line_mode"] = mode,
                ["data"] = new JObject { ["line"] = text },
                ["connections"] = new JArray(connections.Length > 0
                    ? connections
                    : new object[] { "default>>flow:end" }),
            };
        }

        IEnumerable<Text> Texts() => _host.GetComponentsInChildren<Text>(true);

        IEnumerable<Button> LiveButtons() =>
            _host.GetComponentsInChildren<Button>(true).Where(b => b.interactable);

        [UnityTest]
        public IEnumerator AnEventSystemIsCreatedSoButtonsCanBeClicked()
        {
            yield return null;

            Assert.IsNotNull(UnityEngine.EventSystems.EventSystem.current,
                "without one, nothing in the sample would respond to a click");
        }

        [UnityTest]
        public IEnumerator ALineAppearsInTheTranscript()
        {
            _store.Cards["c1"] = Line("Hello from play mode");
            yield return _runtime.StartDialogueAsync("col1", "c1").AsCoroutine();
            yield return null;

            Assert.IsTrue(Texts().Any(t => t.text == "Hello from play mode"));
        }

        [UnityTest]
        public IEnumerator ClickingContinueAdvancesTheDialogue()
        {
            _store.Cards["c1"] = Line("First", "npc", "default>>c2");
            _store.Cards["c2"] = Line("Second");

            yield return _runtime.StartDialogueAsync("col1", "c1").AsCoroutine();
            yield return null;

            var advance = LiveButtons().First();
            advance.onClick.Invoke();
            yield return null;

            Assert.IsTrue(Texts().Any(t => t.text == "Second"),
                "the click should have driven the runtime forward");
        }

        [UnityTest]
        public IEnumerator EarlierEntriesAreFrozenOnceTheDialogueMovesOn()
        {
            _store.Cards["c1"] = Line("First", "npc", "default>>c2");
            _store.Cards["c2"] = Line("Second");

            yield return _runtime.StartDialogueAsync("col1", "c1").AsCoroutine();
            yield return null;
            LiveButtons().First().onClick.Invoke();
            yield return null;

            Assert.AreEqual(1, LiveButtons().Count(),
                "history must not stay clickable");
        }

        [UnityTest]
        public IEnumerator ChoicesRenderAsNumberedButtonsInOrder()
        {
            _store.Cards["c1"] = new JObject
            {
                ["base_ref"] = "blank",
                ["connections"] = new JArray("default>>top", "default>>bottom"),
            };
            _store.Cards["top"] = Line("Top choice", "pc");
            _store.Cards["top"]["geo"] = new JObject { ["y"] = 0 };
            _store.Cards["bottom"] = Line("Bottom choice", "pc");
            _store.Cards["bottom"]["geo"] = new JObject { ["y"] = 100 };

            yield return _runtime.StartDialogueAsync("col1", "c1").AsCoroutine();
            yield return null;

            var labels = Texts().Select(t => t.text).ToList();
            Assert.IsTrue(labels.Any(l => l == "1. Top choice"), "choices are numbered from 1");
            Assert.IsTrue(labels.Any(l => l == "2. Bottom choice"));
        }

        [UnityTest]
        public IEnumerator ASystemLineIsStyledDifferentlyFromDialogue()
        {
            _store.Cards["c1"] = Line("Ordinary line");
            yield return _runtime.StartDialogueAsync("col1", "c1").AsCoroutine();
            yield return null;

            var ordinary = Texts().First(t => t.text == "Ordinary line");
            Assert.AreEqual(QuickstartUi.Body, ordinary.color);
        }

        [UnityTest]
        public IEnumerator ClearEmptiesTheTranscript()
        {
            _store.Cards["c1"] = Line("Something");
            yield return _runtime.StartDialogueAsync("col1", "c1").AsCoroutine();
            yield return null;

            _strip.Clear();
            yield return null;

            Assert.IsFalse(Texts().Any(t => t.text == "Something"));
        }
    }

    static class TaskCoroutineExtensions
    {
        /// <summary>
        /// Waits for a task from a coroutine, so a play-mode test can await runtime work
        /// without blocking the main thread — which would deadlock.
        /// </summary>
        public static IEnumerator AsCoroutine(this Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                throw task.Exception;
            }
        }
    }
}
