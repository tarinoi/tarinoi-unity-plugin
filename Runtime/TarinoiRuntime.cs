using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Tarinoi.Bindings;
using Tarinoi.Data;
using Tarinoi.Sync;

namespace Tarinoi
{
    /// <summary>
    /// Plays Tarinoi dialogue: walks the authored card graph, evaluates conditions
    /// against your bindings, and raises events for the lines and choices to show.
    /// </summary>
    /// <remarks>
    /// Typical use:
    /// <code>
    /// await TarinoiRuntime.Instance.ConfigureAsync();
    /// TarinoiRuntime.Instance.Registry.BindFunctions("global", new MyFunctions());
    /// TarinoiRuntime.Instance.LineReady += ShowLine;
    /// TarinoiRuntime.Instance.ChoicesReady += ShowChoices;
    /// await TarinoiRuntime.Instance.SyncAsync();
    /// await TarinoiRuntime.Instance.StartDialogueAsync(collectionId, cardId);
    /// </code>
    /// <para>
    /// <b>Threading.</b> Unlike the sync layer, this class deliberately does <i>not</i>
    /// use <c>ConfigureAwait(false)</c>: events must reach game code on Unity's main
    /// thread. With the built-in document store every await completes synchronously, so
    /// nothing ever leaves the calling thread. If you supply a genuinely asynchronous
    /// <see cref="IDocumentStore"/>, do not block on the returned tasks from the main
    /// thread — await them.
    /// </para>
    /// <para>
    /// Nothing here throws for authoring mistakes. A missing card, a broken condition or
    /// an unbound function raises <see cref="DialogueError"/> and stops that traversal;
    /// the game keeps running.
    /// </para>
    /// </remarks>
    public sealed class TarinoiRuntime : IEntityCache
    {
        // -------------------------------------------------------------------------
        // Events
        // -------------------------------------------------------------------------

        /// <summary>A line is ready to display. The player should then call <see cref="AdvanceAsync"/>.</summary>
        public event Action<DialogueLine> LineReady;

        /// <summary>Choices are ready. The player picks one via <see cref="SelectChoiceAsync"/>.</summary>
        public event Action<IReadOnlyList<DialogueChoice>> ChoicesReady;

        /// <summary>The dialogue reached an end, was aborted, or could not continue.</summary>
        public event Action DialogueEnded;

        /// <summary>An authoring or binding problem stopped the current traversal.</summary>
        public event Action<string> DialogueError;

        /// <summary>The player chose an option. Raised before the traversal continues.</summary>
        public event Action<DialogueLine> ChoiceMade;

        /// <summary>
        /// A card has named pins but no usable output selector, so the pin must be picked
        /// by hand via <see cref="SelectPinAsync"/>. This is developer tooling, not a
        /// player-facing state.
        /// </summary>
        public event Action<IReadOnlyList<string>> PinChoiceNeeded;

        public event Action SyncStarted;
        public event Action<SyncStats> SyncCompleted;
        public event Action<string> SyncFailed;
        public event Action<SyncProgress> SyncProgress;

        // -------------------------------------------------------------------------
        // Configuration surface
        // -------------------------------------------------------------------------

        /// <summary>
        /// Where game code registers the implementations behind <c>Fn.*</c>, <c>Var.*</c>
        /// and <c>Ent.*</c>. Bindings may be registered before or after
        /// <see cref="ConfigureAsync"/>, as long as they are in place before dialogue starts.
        /// </summary>
        public BindingRegistry Registry { get; } = new BindingRegistry();

        /// <summary>
        /// Reads dialogue content. Assign your own before <see cref="ConfigureAsync"/> to
        /// change the backend; otherwise a <see cref="SqliteDocumentStore"/> is used.
        /// </summary>
        public IDocumentStore DocumentStore { get; set; }

        /// <summary>
        /// Optional visited-choice tracking. Leave null to skip it entirely.
        /// </summary>
        public IHistoryStore HistoryStore { get; set; }

        public DialogueState State { get; private set; } = DialogueState.Idle;

        public bool IsConfigured => _db != null && _db.IsOpen;

        public string ProjectId { get; private set; } = "";

        // -------------------------------------------------------------------------
        // Singleton
        // -------------------------------------------------------------------------

        static TarinoiRuntime _instance;

        /// <summary>
        /// The shared runtime. Construct your own instead when you need more than one,
        /// or in tests.
        /// </summary>
        public static TarinoiRuntime Instance => _instance ?? (_instance = new TarinoiRuntime());

        /// <summary>Disposes the shared runtime so the next access builds a fresh one.</summary>
        public static void ResetInstance()
        {
            _instance?.Shutdown();
            _instance = null;
        }

        // -------------------------------------------------------------------------
        // State
        // -------------------------------------------------------------------------

        TarinoiSettings _settings;
        TarinoiDb _db;
        Dispatcher _dispatcher;
        SynchronizationContext _mainThread;

        string _currentCollectionId = "";
        JObject _currentCard;
        string _currentCardId = "";
        List<DialogueChoice> _choices = new List<DialogueChoice>();

        /// <summary>Cards seen during the current uninterrupted traversal; loop guard.</summary>
        readonly HashSet<string> _visited = new HashSet<string>();

        string _sessionStartCardId = "";
        readonly HashSet<string> _sessionVisitedChoices = new HashSet<string>();

        readonly List<string> _pendingSystemLines = new List<string>();
        string _pendingNavTarget = "";

        // Layer-merged caches of everything that isn't a card, rebuilt after each sync.
        readonly Dictionary<string, JObject> _collections = new Dictionary<string, JObject>();
        readonly Dictionary<string, string> _collectionLabels = new Dictionary<string, string>();
        readonly Dictionary<string, JObject> _entities = new Dictionary<string, JObject>();
        readonly Dictionary<string, List<JObject>> _lists = new Dictionary<string, List<JObject>>();

        bool _syncInProgress;
        TarinoiPoller _poller;

        // -------------------------------------------------------------------------
        // Configuration
        // -------------------------------------------------------------------------

        /// <summary>
        /// Opens the local content database and prepares the runtime. Safe to call again
        /// to reconfigure.
        /// </summary>
        /// <param name="settings">Defaults to the project's settings asset.</param>
        public async Task<bool> ConfigureAsync(TarinoiSettings settings = null)
        {
            _settings = settings ?? TarinoiSettings.Instance;
            _mainThread = SynchronizationContext.Current;
            TarinoiLog.Level = _settings.logLevel;

            ProjectId = _settings.ProjectId;
            if (string.IsNullOrEmpty(ProjectId))
            {
                TarinoiLog.Error(
                    "TarinoiRuntime: cannot work out which project to load. Set the API path in "
                    + "Project Settings > Tarinoi.");
                return false;
            }

            // Offline mode plays a snapshot bundled at build time, which has to be copied
            // somewhere writable before SQLite can open it.
            if (_settings.offlineMode && !await SnapshotSeeder.SeedAsync(ProjectId))
            {
                return false;
            }

            _db?.Dispose();
            _db = new TarinoiDb { CommittedOnly = _settings.committedOnly };
            if (!_db.Open(ProjectId))
            {
                _db = null;
                return false;
            }

            // The dispatcher holds the registry by reference, so bindings registered
            // after this point still take effect.
            _dispatcher = new Dispatcher(Registry);

            LoadGlobalCache();

            if (DocumentStore == null)
            {
                DocumentStore = CreateConfiguredStore();
            }

            DocumentStore.Setup(_db, this);
            return true;
        }

        IDocumentStore CreateConfiguredStore()
        {
            if (string.IsNullOrEmpty(_settings.dataProvider))
            {
                return new SqliteDocumentStore();
            }

            try
            {
                var type = Type.GetType(_settings.dataProvider);
                if (type == null)
                {
                    TarinoiLog.Error($"TarinoiRuntime: could not find the data provider type "
                                     + $"'{_settings.dataProvider}'. Using the built-in store instead.");
                    return new SqliteDocumentStore();
                }

                return (IDocumentStore)Activator.CreateInstance(type);
            }
            catch (Exception e)
            {
                TarinoiLog.Error($"TarinoiRuntime: could not create the data provider "
                                 + $"'{_settings.dataProvider}': {e.Message}. Using the built-in store instead.");
                return new SqliteDocumentStore();
            }
        }

        /// <summary>Closes the database and clears cached content.</summary>
        public void Shutdown()
        {
            _poller?.Stop();
            _poller = null;
            _db?.Dispose();
            _db = null;
            _dispatcher = null;
            State = DialogueState.Idle;
            _choices = new List<DialogueChoice>();
            _visited.Clear();
        }

        // -------------------------------------------------------------------------
        // Sync
        // -------------------------------------------------------------------------

        /// <summary>
        /// Fetches new content from the Tarinoi API and refreshes the caches. In offline
        /// mode this completes immediately without contacting anything.
        /// </summary>
        public async Task SyncAsync()
        {
            if (_settings == null)
            {
                TarinoiLog.Error("TarinoiRuntime: call ConfigureAsync() before syncing.");
                return;
            }

            if (_settings.offlineMode)
            {
                Raise(() => SyncCompleted?.Invoke(new SyncStats()));
                return;
            }

            if (_syncInProgress)
            {
                TarinoiLog.Debug("TarinoiRuntime: a sync is already running.");
                return;
            }

            _syncInProgress = true;
            Raise(() => SyncStarted?.Invoke());

            try
            {
                // A separate connection: the importer may run its writes on another
                // thread, and a SQLite connection must not be shared across threads.
                using (var syncDb = new TarinoiDb { CommittedOnly = _settings.committedOnly })
                {
                    if (!syncDb.Open(ProjectId))
                    {
                        Raise(() => SyncFailed?.Invoke($"Could not open the local database for '{ProjectId}'."));
                        return;
                    }

                    var progress = new Progress<SyncProgress>(p => Raise(() => SyncProgress?.Invoke(p)));
                    var result = await new ApiImporter().SyncAsync(
                        _settings.apiPath,
                        Credentials.Read(Credentials.ApiKeyName),
                        syncDb,
                        progress,
                        _settings.skipTlsVerify);

                    if (!result.Success)
                    {
                        TarinoiLog.Error("TarinoiRuntime: sync failed — " + result.Error);
                        Raise(() => SyncFailed?.Invoke(result.Error));
                        return;
                    }

                    TarinoiLog.Info($"TarinoiRuntime: sync complete — {result.Stats}");
                    LoadGlobalCache();
                    Raise(() => SyncCompleted?.Invoke(result.Stats));
                }
            }
            finally
            {
                _syncInProgress = false;
                Raise(EnsurePolling);
            }
        }

        /// <summary>
        /// Starts the re-sync timer if the project asks for it. Deliberately play-mode
        /// only: polling exists to shorten the author's edit-and-see loop, and spawning
        /// a GameObject during an editor test or an asset import would be intrusive.
        /// </summary>
        void EnsurePolling()
        {
            if (!_settings.pollEnabled || !UnityEngine.Application.isPlaying)
            {
                return;
            }

            if (_poller == null)
            {
                _poller = TarinoiPoller.Create(this, _settings.pollInterval);
                TarinoiLog.Info($"TarinoiRuntime: re-syncing every {_settings.pollInterval}s.");
                return;
            }

            _poller.SetInterval(_settings.pollInterval);
        }

        /// <summary>
        /// Runs an event on the thread that configured the runtime. The sync path
        /// deliberately resumes off the main thread, so its events must be marshalled
        /// back explicitly rather than relying on await's context capture.
        /// </summary>
        void Raise(Action action)
        {
            if (_mainThread == null || SynchronizationContext.Current == _mainThread)
            {
                action();
                return;
            }

            _mainThread.Post(_ => action(), null);
        }

        // -------------------------------------------------------------------------
        // Dialogue control
        // -------------------------------------------------------------------------

        /// <summary>Starts a dialogue at the given card.</summary>
        public async Task StartDialogueAsync(string collectionId, string cardId)
        {
            if (!EnsureConfigured())
            {
                return;
            }

            State = DialogueState.Idle;
            _currentCollectionId = collectionId;
            _visited.Clear();
            _sessionStartCardId = cardId;

            _sessionVisitedChoices.Clear();
            if (HistoryStore != null)
            {
                foreach (var visited in HistoryStore.GetVisited(cardId))
                {
                    _sessionVisitedChoices.Add(visited);
                }
            }

            await LoadAndProcessCardAsync(collectionId, cardId);
        }

        /// <summary>Advances past the line currently on screen.</summary>
        public async Task AdvanceAsync()
        {
            if (State != DialogueState.NpcLine)
            {
                TarinoiLog.Warn($"TarinoiRuntime: Advance() ignored — nothing to advance past (state {State}).");
                return;
            }

            // A system line has no card of its own; it stashed where to go next.
            if (_currentCardId == DialogueLine.SystemCardId)
            {
                var target = _pendingNavTarget;
                _pendingNavTarget = "";
                State = DialogueState.Idle;
                await LoadAndProcessCardAsync(_currentCollectionId, target);
                return;
            }

            var card = _currentCard;
            var cardId = _currentCardId;
            var collectionId = _currentCollectionId;
            State = DialogueState.Idle;
            await FollowConnectionsAsync(card, cardId, collectionId);
        }

        /// <summary>Selects one of the choices most recently offered.</summary>
        public async Task SelectChoiceAsync(int index)
        {
            if (State != DialogueState.PcChoice)
            {
                TarinoiLog.Warn($"TarinoiRuntime: SelectChoice() ignored — no choices are open (state {State}).");
                return;
            }

            if (index < 0 || index >= _choices.Count)
            {
                TarinoiLog.Warn($"TarinoiRuntime: choice {index} is out of range "
                                + $"(0..{_choices.Count - 1}).");
                return;
            }

            var chosen = _choices[index];
            State = DialogueState.Idle;
            _choices = new List<DialogueChoice>();
            _sessionVisitedChoices.Add(chosen.CardId);

            // Functions run only for the option actually taken — evaluating the others
            // would fire their side effects for lines the player never saw.
            EvalCardFunctions(chosen.Card, chosen.CardId);

            var line = MakeLineData(chosen.Card, chosen.CardId, chosen.CollectionId);
            ChoiceMade?.Invoke(line);

            await FollowConnectionsAsync(chosen.Card, chosen.CardId, chosen.CollectionId);
        }

        /// <summary>Picks a named pin by hand. See <see cref="PinChoiceNeeded"/>.</summary>
        public async Task SelectPinAsync(string pinName)
        {
            if (State != DialogueState.AwaitingPin)
            {
                TarinoiLog.Warn($"TarinoiRuntime: SelectPin() ignored — not waiting for a pin (state {State}).");
                return;
            }

            var target = ConnectionTargetForPin(_currentCard, pinName);
            if (string.IsNullOrEmpty(target))
            {
                var message = $"Card '{_currentCardId}' has no pin named '{pinName}'.";
                TarinoiLog.Error("TarinoiRuntime: " + message);
                DialogueError?.Invoke(message);
                return;
            }

            State = DialogueState.Idle;
            await LoadAndProcessCardAsync(_currentCollectionId, target);
        }

        /// <summary>Ends the dialogue immediately, saving visited choices.</summary>
        public void AbortDialogue()
        {
            _choices = new List<DialogueChoice>();
            _visited.Clear();
            FinishDialogue();
        }

        /// <summary>
        /// Every dialogue entry point currently available, grouped by collection label.
        /// </summary>
        public async Task<IReadOnlyList<StartCard>> GetStartCardsAsync()
        {
            if (!EnsureConfigured())
            {
                return new StartCard[0];
            }

            var rows = await DocumentStore.QueryStartCardsAsync();
            var cards = rows.Select(row => new StartCard
            {
                CardId = row.DocumentId,
                CollectionId = row.CollectionId,
                CollectionLabel = CollectionLabel(row.CollectionId),
                Label = $"{(string.IsNullOrEmpty(row.Label) ? "Start" : row.Label)} <{row.DocumentId}>",
            });

            // OrderBy is stable, so entry points within a collection keep query order.
            return cards.OrderBy(c => c.CollectionLabel, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Evaluates any Tarinoi expression and returns its raw value. Useful from inside
        /// a binding, for example to read a list option.
        /// </summary>
        public object EvalExpression(string expr) => _dispatcher?.EvalValue(expr);

        /// <summary>
        /// Queues a line to show before the dialogue moves on.
        /// </summary>
        /// <remarks>
        /// Intended for bindings called from an output selector — a skill check that wants
        /// to tell the player what happened before routing them. The line appears as an
        /// ordinary NPC line that the player advances past.
        /// <para>
        /// Only the first queued line is shown; any others are discarded.
        /// </para>
        /// </remarks>
        public void PostSystemLine(string text) => _pendingSystemLines.Add(text);

        // -------------------------------------------------------------------------
        // Card traversal
        // -------------------------------------------------------------------------

        async Task LoadAndProcessCardAsync(string collectionId, string cardId)
        {
            if (CheckLoop(cardId))
            {
                return;
            }

            var card = await DocumentStore.LoadCardAsync(collectionId, cardId);
            if (card == null)
            {
                var message = $"Card '{cardId}' was not found in collection '{collectionId}'.";
                TarinoiLog.Error("TarinoiRuntime: " + message);
                DialogueError?.Invoke(message);
                return;
            }

            await ProcessCardAsync(card, cardId, collectionId);
        }

        async Task ProcessCardAsync(JObject card, string cardId, string collectionId)
        {
            var condition = Str(Obj(card, "input_pin")?["condition"]);
            if (condition.Length > 0 && !EvalGuarded(condition, $"input_pin on card '{cardId}'"))
            {
                // The card refused entry; keep walking from it rather than stopping.
                await FollowConnectionsAsync(card, cardId, collectionId);
                return;
            }

            var baseRef = Str(card["base_ref"]);

            // Line cards defer their functions: an NPC line fires them when shown, a PC
            // line when chosen. Firing them here would trigger side effects for options
            // the player never picks.
            if (baseRef != "line")
            {
                EvalCardFunctions(card, cardId);
            }

            switch (baseRef)
            {
                case "line":
                    ProcessLine(card, cardId, collectionId);
                    break;
                case "jump":
                    await FollowJumpAsync(card, cardId);
                    break;
                case "start":
                case "blank":
                    await FollowConnectionsAsync(card, cardId, collectionId);
                    break;
                default:
                    TarinoiLog.Warn($"TarinoiRuntime: card '{cardId}' has an unrecognised type "
                                    + $"'{baseRef}' — passing through it.");
                    await FollowConnectionsAsync(card, cardId, collectionId);
                    break;
            }
        }

        void ProcessLine(JObject card, string cardId, string collectionId)
        {
            if (IsPcCard(card))
            {
                // A player line reached on its own is still a choice — of one.
                _choices = new List<DialogueChoice> { MakeChoice(card, cardId, collectionId, 0) };
                State = DialogueState.PcChoice;
                _visited.Clear();
                ChoicesReady?.Invoke(_choices);
                return;
            }

            EvalCardFunctions(card, cardId);
            State = DialogueState.NpcLine;
            _currentCard = card;
            _currentCardId = cardId;
            _currentCollectionId = collectionId;
            _visited.Clear();
            LineReady?.Invoke(MakeLineData(card, cardId, collectionId));
        }

        /// <summary>
        /// Works out where to go after a card, and how.
        /// </summary>
        /// <remarks>
        /// Named pins with a working output selector route automatically; named pins
        /// without one fall back to manual selection. A single default target is followed
        /// silently; several become a choice.
        /// </remarks>
        async Task FollowConnectionsAsync(JObject card, string cardId, string collectionId)
        {
            _dispatcher?.SetContextCard(card);

            var connections = card["connections"] as JArray;
            if (connections == null || connections.Count == 0)
            {
                TarinoiLog.Error($"TarinoiRuntime: card '{cardId}' in '{collectionId}' leads nowhere "
                                 + "— ending the dialogue. Connect it to another card or to flow:end.");
                FinishDialogue();
                return;
            }

            var defaultTargets = new List<string>();
            var seenDefaults = new HashSet<string>();
            var namedPins = new Dictionary<string, string>();

            foreach (var connection in connections)
            {
                var parts = Str(connection).Split(new[] { ">>" }, StringSplitOptions.None);
                if (parts.Length < 2)
                {
                    continue;
                }

                var pin = parts[0];
                var target = parts[1];

                if (target == "flow:end")
                {
                    FinishDialogue();
                    return;
                }

                if (pin == "default")
                {
                    if (seenDefaults.Add(target))
                    {
                        defaultTargets.Add(target);
                    }
                }
                else if (!namedPins.ContainsKey(pin))
                {
                    // First wins: a duplicate pin name is an authoring mistake, and
                    // picking the first keeps behaviour predictable.
                    namedPins[pin] = target;
                }
            }

            if (namedPins.Count > 0)
            {
                await FollowNamedPinsAsync(card, cardId, collectionId, namedPins);
                return;
            }

            if (defaultTargets.Count == 0)
            {
                TarinoiLog.Error($"TarinoiRuntime: card '{cardId}' in '{collectionId}' has connections "
                                 + "but none of them name a target — ending the dialogue.");
                FinishDialogue();
                return;
            }

            if (defaultTargets.Count == 1)
            {
                await LoadAndProcessCardAsync(collectionId, defaultTargets[0]);
                return;
            }

            await BuildChoicesFromTargetsAsync(defaultTargets, collectionId, cardId);
        }

        async Task FollowNamedPinsAsync(JObject card, string cardId, string collectionId,
            Dictionary<string, string> namedPins)
        {
            var selector = Str(card["output_selector"]);

            if (selector.Length > 0 && selector.Contains("$"))
            {
                TarinoiLog.Warn($"TarinoiRuntime: card '{cardId}' has an unfilled template in its "
                                + $"output selector '{selector}' — asking for the pin instead.");
            }
            else if (selector.Length > 0 && _dispatcher != null && _dispatcher.HasCall(selector))
            {
                var pinName = Str2(_dispatcher.EvalCall(selector));
                TarinoiLog.Debug($"output_selector '{selector}' [card:{cardId}] → '{pinName}'");

                if (!namedPins.TryGetValue(pinName, out var target) || string.IsNullOrEmpty(target))
                {
                    // Deliberately stalls rather than guessing: the selector and the
                    // authored pins disagree, and picking one would hide the mistake.
                    _pendingSystemLines.Clear();
                    var message = $"Card '{cardId}' has no pin '{pinName}', which its output "
                                  + $"selector returned. Pins available: {string.Join(", ", namedPins.Keys)}.";
                    TarinoiLog.Error("TarinoiRuntime: " + message);
                    DialogueError?.Invoke(message);
                    return;
                }

                if (_pendingSystemLines.Count > 0)
                {
                    ShowSystemLine(collectionId, target);
                    return;
                }

                await LoadAndProcessCardAsync(collectionId, target);
                return;
            }
            else if (selector.Length > 0)
            {
                TarinoiLog.Error($"TarinoiRuntime: card '{cardId}' uses the output selector "
                                 + $"'{selector}', which is not bound — asking for the pin instead.");
            }

            _currentCard = card;
            _currentCardId = cardId;
            _currentCollectionId = collectionId;
            State = DialogueState.AwaitingPin;
            PinChoiceNeeded?.Invoke(namedPins.Keys.ToList());
        }

        /// <summary>
        /// Shows a line queued by <see cref="PostSystemLine"/> as an interstitial the
        /// player advances past before reaching <paramref name="navTarget"/>.
        /// </summary>
        void ShowSystemLine(string collectionId, string navTarget)
        {
            var message = _pendingSystemLines[0];
            _pendingSystemLines.Clear();
            _pendingNavTarget = navTarget;

            _currentCard = new JObject();
            _currentCardId = DialogueLine.SystemCardId;
            _currentCollectionId = collectionId;
            State = DialogueState.NpcLine;

            LineReady?.Invoke(new DialogueLine
            {
                CardId = DialogueLine.SystemCardId,
                CollectionId = collectionId,
                EntityRef = "system",
                EntityLabel = "",
                LineMode = "system",
                Line = message,
                BaseRef = "",
                TemplateRef = "",
                Data = new JObject(),
            });
        }

        async Task BuildChoicesFromTargetsAsync(List<string> targetIds, string collectionId,
            string sourceCardId)
        {
            var choices = new List<DialogueChoice>();
            var lineCandidates = 0;

            foreach (var targetId in targetIds)
            {
                var card = await DocumentStore.LoadCardAsync(collectionId, targetId);
                if (card == null)
                {
                    continue;
                }

                if (Str(card["base_ref"]) != "line")
                {
                    TarinoiLog.Warn($"TarinoiRuntime: card '{targetId}' is not a line, so it cannot be "
                                    + "offered as a choice — skipping it.");
                    continue;
                }

                lineCandidates++;

                var condition = Str(Obj(card, "input_pin")?["condition"]);
                if (condition.Length > 0 && !EvalGuarded(condition, $"input_pin on card '{targetId}'"))
                {
                    continue;
                }

                choices.Add(MakeChoice(card, targetId, collectionId, choices.Count));
            }

            if (choices.Count == 0)
            {
                if (lineCandidates > 0)
                {
                    TarinoiLog.Error($"TarinoiRuntime: nothing follows card '{sourceCardId}' — all "
                                     + $"{lineCandidates} possible continuation(s) were ruled out by their "
                                     + "conditions. Ending the dialogue.");
                }

                FinishDialogue();
                return;
            }

            _choices = SortByGeometry(choices);

            WarnAboutMixedChoiceSets(_choices, sourceCardId);

            if (_choices.Count == 1)
            {
                // Only one option survived its condition, so there is nothing to choose.
                var only = _choices[0];
                _choices = new List<DialogueChoice>();
                await LoadAndProcessCardAsync(collectionId, only.CardId);
                return;
            }

            State = DialogueState.PcChoice;
            _visited.Clear();
            ChoicesReady?.Invoke(_choices);
        }

        /// <summary>
        /// Orders choices by their vertical position in the authoring graph.
        /// </summary>
        /// <remarks>
        /// Authors express the intended reading order by laying cards out top to bottom,
        /// so connection order is an implementation detail. Cards without usable geometry
        /// sort last rather than to zero — authored positions are routinely negative, so
        /// zero would drop them into the middle. <c>OrderBy</c> is stable, so ties keep
        /// their original order.
        /// </remarks>
        static List<DialogueChoice> SortByGeometry(List<DialogueChoice> choices)
        {
            var sorted = choices.OrderBy(c => GeometryY(c.Card)).ToList();
            for (var i = 0; i < sorted.Count; i++)
            {
                sorted[i].Index = i;
            }

            return sorted;
        }

        static double GeometryY(JObject card)
        {
            var y = (card?["geo"] as JObject)?["y"];
            if (y == null)
            {
                return double.PositiveInfinity;
            }

            return y.Type == JTokenType.Integer || y.Type == JTokenType.Float
                ? (double)y
                : double.PositiveInfinity;
        }

        void WarnAboutMixedChoiceSets(List<DialogueChoice> choices, string sourceCardId)
        {
            var npcCount = choices.Count(c => !IsPcCard(c.Card));
            if (npcCount > 0 && npcCount < choices.Count)
            {
                TarinoiLog.Warn($"TarinoiRuntime: card '{sourceCardId}' leads to both player and "
                                + $"non-player lines; the {npcCount} non-player line(s) cannot be reached.");
                return;
            }

            // An all-NPC set is chosen between by condition, so duplicate or missing
            // conditions mean the later lines are unreachable.
            if (npcCount != choices.Count || choices.Count <= 1)
            {
                return;
            }

            var seen = new HashSet<string>();
            foreach (var choice in choices)
            {
                var condition = Str(Obj(choice.Card, "input_pin")?["condition"]);
                if (!seen.Add(condition))
                {
                    TarinoiLog.Warn($"TarinoiRuntime: card '{sourceCardId}' leads to several lines "
                                    + "sharing the same condition — only the first can be reached.");
                    return;
                }
            }
        }

        async Task FollowJumpAsync(JObject card, string cardId)
        {
            var data = card["data"] as JObject;
            var targetCollection = Str(data?["target_collection_id"]);
            var targetCard = Str(data?["target_card_id"]);

            if (targetCollection.Length == 0 || targetCard.Length == 0)
            {
                var message = $"Jump card '{cardId}' does not say where to jump to.";
                TarinoiLog.Error("TarinoiRuntime: " + message);
                DialogueError?.Invoke(message);
                return;
            }

            _currentCollectionId = targetCollection;
            await LoadAndProcessCardAsync(targetCollection, targetCard);
        }

        // -------------------------------------------------------------------------
        // Card functions
        // -------------------------------------------------------------------------

        /// <summary>
        /// Runs every <c>Fn.*</c> expression stored in a card's data.
        /// </summary>
        /// <remarks>
        /// Order follows the card's <c>props</c> declaration, since these calls have side
        /// effects and authors sequence them deliberately. Expressions not listed in
        /// <c>props</c> run last — <c>props</c> can lag behind a template change, so it is
        /// an ordering hint rather than an inventory.
        /// </remarks>
        void EvalCardFunctions(JObject card, string cardId)
        {
            if (_dispatcher == null)
            {
                return;
            }

            var data = card?["data"] as JObject;
            if (data == null)
            {
                return;
            }

            var order = new Dictionary<string, int>();
            if (card["props"] is JArray props)
            {
                for (var i = 0; i < props.Count; i++)
                {
                    order[Str((props[i] as JObject)?["name"])] = i;
                }
            }

            var calls = data.Properties()
                .Where(p => p.Value.Type == JTokenType.String
                            && ((string)p.Value).StartsWith("Fn.", StringComparison.Ordinal))
                .Select(p => new
                {
                    Name = p.Name,
                    Expr = (string)p.Value,
                    Order = order.TryGetValue(p.Name, out var i) ? i : int.MaxValue,
                })
                .OrderBy(c => c.Order)
                .ToList();

            foreach (var call in calls)
            {
                if (call.Expr.Contains("$"))
                {
                    TarinoiLog.Warn($"TarinoiRuntime: card '{cardId}' has an unfilled template in "
                                    + $"'{call.Name}' ({call.Expr}) — skipping it.");
                }
                else if (_dispatcher.HasCall(call.Expr))
                {
                    _dispatcher.EvalCall(call.Expr);
                }
                else
                {
                    TarinoiLog.Error($"TarinoiRuntime: card '{cardId}' calls '{call.Expr}' in "
                                     + $"'{call.Name}', which is not bound. Regenerate your bindings.");
                }
            }
        }

        /// <summary>
        /// Evaluates a condition, treating an unfilled template as satisfied.
        /// </summary>
        /// <remarks>
        /// A <c>$</c> means the Tarinoi template was never filled in. Failing such a
        /// condition would silently hide content while the author is still working, so it
        /// passes with a warning instead.
        /// </remarks>
        bool EvalGuarded(string condition, string where)
        {
            if (condition.Contains("$"))
            {
                TarinoiLog.Warn($"TarinoiRuntime: unfilled template in the condition on {where} "
                                + $"({condition}) — treating it as met.");
                return true;
            }

            return _dispatcher != null && _dispatcher.EvalCondition(condition);
        }

        // -------------------------------------------------------------------------
        // Content caches
        // -------------------------------------------------------------------------

        /// <summary>
        /// Loads collections, entities and lists into memory with the layer merge applied.
        /// Rebuilt after every sync.
        /// </summary>
        void LoadGlobalCache()
        {
            if (_db == null || !_db.IsOpen)
            {
                return;
            }

            _collections.Clear();
            _collectionLabels.Clear();
            _entities.Clear();
            _lists.Clear();

            LoadCollections();
            LoadEntities();
            LoadLists();

            _dispatcher?.SetLists(_lists);
        }

        void LoadCollections()
        {
            var rows = _db.Query<DocumentRow>(
                "SELECT * FROM documents WHERE document_type = 'collection-manifest'");

            foreach (var row in LayerFilter.Merge(rows, _db.CommittedOnly))
            {
                var payload = Parse(row.Payload);
                if (payload == null)
                {
                    continue;
                }

                _collections[row.DocumentId] = payload;
                _collectionLabels[row.DocumentId] = Str(payload["label"]);
            }

            // Fall back to the collections table, which the importer rebuilds and which
            // survives even when the manifest documents themselves are absent.
            if (_collections.Count > 0)
            {
                return;
            }

            foreach (var row in _db.Query<CollectionRow>("SELECT * FROM collections"))
            {
                var payload = Parse(row.Payload);
                if (payload == null)
                {
                    continue;
                }

                _collections[row.CollectionId] = payload;
                _collectionLabels[row.CollectionId] =
                    string.IsNullOrEmpty(row.CollectionName) ? Str(payload["label"]) : row.CollectionName;
            }
        }

        void LoadEntities()
        {
            var rows = _db.Query<DocumentRow>("SELECT * FROM documents WHERE document_type = 'entity'");

            foreach (var row in LayerFilter.Merge(rows, _db.CommittedOnly))
            {
                var payload = Parse(row.Payload);
                if (payload != null && !string.IsNullOrEmpty(row.Identifier))
                {
                    _entities[row.Identifier] = payload;
                }
            }
        }

        /// <summary>
        /// Loads authored option lists, keyed as <c>collectionName/listIdentifier</c> to
        /// match how <c>Ls.*</c> expressions name them.
        /// </summary>
        void LoadLists()
        {
            // The Ls.* key is the collection's own name, not its document id.
            var listCollections = new Dictionary<string, string>();
            foreach (var entry in _collections)
            {
                if (Str(entry.Value["collection_type"]) != "list-collection")
                {
                    continue;
                }

                var name = Str(entry.Value["collection_name"]);
                if (name.Length == 0)
                {
                    name = Str(entry.Value["identifier"]);
                }

                if (name.Length > 0)
                {
                    listCollections[entry.Key] = name;
                }
            }

            if (listCollections.Count == 0)
            {
                return;
            }

            var placeholders = string.Join(",", listCollections.Keys.Select(_ => "?"));
            var rows = _db.Query<DocumentRow>(
                $"SELECT * FROM documents WHERE collection_id IN ({placeholders})",
                listCollections.Keys.Cast<object>().ToArray());

            foreach (var row in LayerFilter.Merge(rows, _db.CommittedOnly))
            {
                var payload = Parse(row.Payload);
                if (payload == null || string.IsNullOrEmpty(row.Identifier)
                                    || !listCollections.TryGetValue(row.CollectionId, out var collectionName))
                {
                    continue;
                }

                // list_options is current; options is the older name.
                var options = (payload["list_options"] ?? payload["options"]) as JArray;
                if (options == null)
                {
                    continue;
                }

                _lists[collectionName + "/" + row.Identifier] =
                    options.OfType<JObject>().ToList();
            }
        }

        /// <summary>Entity payload lookup, used by the document store.</summary>
        public JObject GetEntity(string identifier)
        {
            return identifier != null && _entities.TryGetValue(identifier, out var entity) ? entity : null;
        }

        string CollectionLabel(string collectionId)
        {
            return collectionId != null && _collectionLabels.TryGetValue(collectionId, out var label)
                   && !string.IsNullOrEmpty(label)
                ? label
                : collectionId;
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------

        bool IsPcCard(JObject card)
        {
            var mode = Str(card?["line_mode"]);
            if (mode == "pc")
            {
                return true;
            }

            if (mode == "npc")
            {
                return false;
            }

            // "inherit", or unset: the speaking entity decides.
            var entity = GetEntity(Str(card?["entity_ref"]));
            var isPlayer = entity?["is_player_character"];
            return isPlayer != null && isPlayer.Type == JTokenType.Boolean && (bool)isPlayer;
        }

        DialogueChoice MakeChoice(JObject card, string cardId, string collectionId, int index)
        {
            var data = card["data"] as JObject;
            return new DialogueChoice
            {
                Index = index,
                CardId = cardId,
                CollectionId = collectionId,
                EntityRef = Str(card["entity_ref"]),
                LineMode = Str(card["line_mode"]),
                Line = Str(data?["line"]),
                Data = data ?? new JObject(),
                Card = card,
                Visited = _sessionVisitedChoices.Contains(cardId),
            };
        }

        DialogueLine MakeLineData(JObject card, string cardId, string collectionId)
        {
            var entityRef = Str(card["entity_ref"]);
            var entity = GetEntity(entityRef);
            var data = card["data"] as JObject;

            return new DialogueLine
            {
                CardId = cardId,
                CollectionId = collectionId,
                EntityRef = entityRef,
                EntityLabel = entity != null && !string.IsNullOrEmpty(Str(entity["label"]))
                    ? Str(entity["label"])
                    : entityRef,
                LineMode = string.IsNullOrEmpty(Str(card["line_mode"])) ? "inherit" : Str(card["line_mode"]),
                Line = Str(data?["line"]),
                BaseRef = Str(card["base_ref"]),
                TemplateRef = Str(card["template_ref"]),
                Data = data ?? new JObject(),
            };
        }

        static string ConnectionTargetForPin(JObject card, string pinName)
        {
            if (!(card?["connections"] is JArray connections))
            {
                return "";
            }

            foreach (var connection in connections)
            {
                var parts = Str(connection).Split(new[] { ">>" }, StringSplitOptions.None);
                if (parts.Length >= 2 && parts[0] == pinName)
                {
                    return parts[1];
                }
            }

            return "";
        }

        /// <summary>
        /// Guards against a cycle in the authored graph. Cleared whenever the dialogue
        /// stops for input, so revisiting a card in a later turn is fine — only looping
        /// without ever reaching the player is a problem.
        /// </summary>
        bool CheckLoop(string cardId)
        {
            if (_visited.Add(cardId))
            {
                return false;
            }

            var message = $"The dialogue loops back to card '{cardId}' without ever stopping for the "
                          + "player. Ending it here.";
            TarinoiLog.Error("TarinoiRuntime: " + message);
            DialogueError?.Invoke(message);
            return true;
        }

        void FinishDialogue()
        {
            State = DialogueState.Idle;

            if (HistoryStore != null && !string.IsNullOrEmpty(_sessionStartCardId))
            {
                HistoryStore.SaveVisited(_sessionStartCardId, _sessionVisitedChoices.ToList());
            }

            DialogueEnded?.Invoke();
        }

        bool EnsureConfigured()
        {
            if (IsConfigured)
            {
                return true;
            }

            TarinoiLog.Error("TarinoiRuntime: not configured — call ConfigureAsync() first.");
            return false;
        }

        static JObject Parse(string json)
        {
            try
            {
                return JObject.Parse(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Reads a token as text, treating JSON null as absent.</summary>
        static string Str(JToken token) =>
            token == null || token.Type == JTokenType.Null ? "" : token.ToString();

        static string Str2(object value) => value?.ToString() ?? "";

        /// <summary>
        /// Reads a nested object, treating an explicit JSON null like a missing key —
        /// card fields such as <c>input_pin</c> are stored as null when unused.
        /// </summary>
        static JObject Obj(JObject parent, string key) => parent?[key] as JObject;
    }
}
