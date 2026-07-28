# Changelog

All notable changes to this package are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
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
- `IHistoryStore` and `InMemoryHistoryStore` — optional visited-choice tracking.
- Optional re-syncing on a timer while playing, so authored changes appear without
  restarting play mode.
- **Project Settings → Tarinoi** for connection, codegen and behaviour settings,
  creating the settings asset on demand.
- **Tools → Tarinoi** menu: Sync, Regenerate Bindings, Check Bindings, Set API
  token…, Snapshot for Export, and Clear Local Content.
- Binding codegen — generates typed C# classes from your synced content, with
  dispatch emitted as a `switch` so it survives IL2CPP code stripping.
- `TarinoiCli` — `-executeMethod` entry points for syncing and generating from a
  build script.

### Fixed
- A bare `Var.collection.flag` used as a condition now reads the variable. In the
  Godot plugin the unresolved reference is itself truthy, so such conditions are
  always true regardless of the variable's value.
- Syncing no longer deadlocks when a caller waits on it from Unity's main thread.
  Async work inside the package now uses `ConfigureAwait(false)` so continuations
  never need the main thread to resume.
