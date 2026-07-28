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
