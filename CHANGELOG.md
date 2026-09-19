# Changelog

All notable changes to this package are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Core functions.** Regenerate Bindings now scaffolds `TarinoiCoreFunctions.cs` —
  the reference implementation of Tarinoi's built-in `Fn.tarinoi.*` set (flags,
  counters, text, comparisons) with the same semantics as in-app playback — into
  the implementations folder (`codegenImplPath`, default `Assets/Tarinoi`). It is
  written once and never overwritten: the file is yours to adapt to your own
  variable storage. Check Bindings reports a scaffold that is out of date or
  missing a function.
- Generated variables classes carry a `Collection` constant naming the
  collection they are bound under.
- `GeneratedBindings.BindDefaults` binds the generated variables classes and the
  scaffolded core functions for any collection left unbound; `TarinoiQuickstart`
  calls it after `SetupBindings`, so synced content that only uses core
  functions plays with no code.
- Initial package scaffolding: UPM manifest, assembly definitions, and test harness.
- `TarinoiLog` — log-level-gated logging.
- `DataVersion` — semantic version compatibility gate for synced documents.
- `TarinoiDb` — local SQLite store: connection lifecycle, schema creation and
  versioned migration, metadata, transactions, and query helpers that log rather
  than throw.
- `LayerFilter` — the two-layer (committed/uncommitted) document merge, as both a
  SQL fragment and an in-memory merge that are held to the same semantics by test.
- `IDocumentStore` and `SqliteDocumentStore` — content reads for the runtime, with
  an overridable seam for custom or off-thread backends.
- `TarinoiSettings` — project configuration, loaded from `Resources` at runtime.
- `ApiImporter` — incremental sync from the Tarinoi documents API: NDJSON pages,
  cursor pagination that resumes after an interruption, layer-aware upserts, and
  failures reported as messages you can act on.
- `NdjsonReader` — streaming newline-delimited JSON parsing.
- `Credentials` — API token storage outside the project directory, so a token
  cannot be committed or shipped in a build.
- `SnapshotSeeder` — offline mode: copies a snapshot bundled in `StreamingAssets`
  into a writable location before opening it.

- `ExpressionParser` — parses authored conditions and function calls into a typed
  syntax tree. Malformed expressions are reported once and degrade gracefully
  rather than throwing.
- `BindingRegistry`, `ITarinoiFunctions`, `ITarinoiVariables`, `ITarinoiEntities` —
  registration of the game code behind `Fn.*`, `Var.*` and `Ent.*`. Plain classes
  can be bound directly and are adapted reflectively.
- `VarRef` — a located-but-unread variable reference, so functions can write back.
- `Dispatcher` — evaluates expressions against the bindings, with short-circuiting
  boolean logic and a parse cache.
- `TarinoiRuntime` — dialogue playback: walks the authored card graph and raises
  events for the lines and choices to show. Typed `DialogueLine`, `DialogueChoice`
  and `StartCard` results.
- `IHistoryStore` and `InMemoryHistoryStore` — optional seen-card tracking, which
  drives the `Visited` flag on choices and the `shown_once` card flag.
- `shown_once` card flag — a card an author marks show-once stops being a valid
  continuation once the player has seen it: dropped from a choice set, or, when it
  is the only way forward, ending the dialogue as an ordinary dead end.
- Running out of continuations because every remaining candidate was a spent
  `shown_once` card is reported like any other dead end: the dialogue ends and an
  error names the card and the cause.
- Optional re-syncing on a timer while playing, so authored changes appear without
  restarting play mode.
- **Project Settings → Tarinoi** for connection, codegen and behaviour settings,
  creating the settings asset on demand.
- **Tools → Tarinoi** menu: Sync, Regenerate Bindings, Check Bindings, Set API
  token…, Snapshot for Export, and Clear Local Content.
- Binding codegen — generates typed C# classes from your synced content, with
  dispatch emitted as a `switch` so it survives IL2CPP code stripping.
- `TarinoiCli` — `-executeMethod` entry points for syncing, generating and exporting
  a snapshot from a build script.
- A ready-to-play interface: an entry-point picker and a scrolling dialogue view,
  built at runtime so a scene needs no setup. **Tools → Tarinoi → Create Quickstart
  Scene** assembles one.
- `DialogueTrigger`, and collider-based `DialogueTriggerVolume` / `…Volume2D`, for
  starting dialogue from the world.
- Quickstart sample showing where a game registers its own bindings.

### Changed
- Supported data format is now `2.0.0`. Function-declaration arguments moved from
  `sub_type`/`allow_literal` to a `value_selectors` list; the package never read
  those fields, so this only lifts the version gate that refused to sync `2.0.0`
  documents.

### Fixed
- Jump cards are followed again. A jump's destination is its `data.target`
  card-link — the target card's bare document id, possibly on another board —
  not the `target_collection_id` / `target_card_id` pair the runtime expected
  (a documentation error the app corrected on 2026-08-27). Reaching a jump used
  to stop the dialogue with "does not say where to jump to".
  `IDocumentStore` gains `LocateCardAsync(cardId)`, with a default that finds
  nothing so existing custom stores still compile; implement it for jumps.
- Generated function stubs now carry their `Effect:` remark. Codegen read the
  payload's `effect` key, which does not exist; the field is `function_effect`.
- A bare `Var.collection.flag` used as a condition now reads the variable. In the
  Godot plugin the unresolved reference is itself truthy, so such conditions are
  always true regardless of the variable's value.
- Syncing no longer deadlocks when a caller waits on it from Unity's main thread.
  Async work inside the package now uses `ConfigureAwait(false)` so continuations
  never need the main thread to resume.
