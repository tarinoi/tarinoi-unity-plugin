# Tarinoi for Unity

Tarinoi dialogue for Unity. Sync authored dialogue content from the Tarinoi service
into a local SQLite database, evaluate authored conditions and functions against your
game's own code, and play dialogue back through a small event-based runtime.

Requires **Unity 6000.0** or newer.

> **Status: early development.** The package is being built out; the API is not yet
> stable and there is no tagged release. Watch `CHANGELOG.md`.

## Installation

Tarinoi is distributed through [OpenUPM](https://openupm.com). Install the OpenUPM
CLI once:

```bash
npm install -g openupm-cli
```

Then, from your Unity project folder:

```bash
openupm add com.tarinoi.unity
```

That pulls in the SQLite dependency and its native binaries automatically.

<details>
<summary>Manual installation without the CLI</summary>

Add the OpenUPM scoped registry to `Packages/manifest.json`:

```json
{
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": ["com.tarinoi", "com.gilzoide"]
    }
  ],
  "dependencies": {
    "com.tarinoi.unity": "0.1.0"
  }
}
```

</details>

## Getting started

**https://tarinoi.app/docs/plugins/unity** — the full guide: configuration, bindings,
events, trigger components, shipping a build, and troubleshooting.

Import the **Quickstart** sample from the Package Manager window for a scene that plays
dialogue with a place to register your own bindings.

Related:

- [What the plugins do and don't do](https://tarinoi.app/docs/plugins/)
- [Writing your own integration](https://tarinoi.app/docs/plugins/writing_your_own) — the engine-agnostic data contract
- [Adapting a plugin](https://tarinoi.app/docs/plugins/adapting) — forking, porting, and the traps we hit

## License

MIT — see [`LICENSE.md`](LICENSE.md). These plugins are reference implementations, meant to
be built on, modified, and incorporated into your own work.
