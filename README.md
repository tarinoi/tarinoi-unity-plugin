# Tarinoi for Unity

Tarinoi dialogue for Unity. Sync authored dialogue content from the Tarinoi service
into a local SQLite database, evaluate authored conditions and functions against your
game's own code, and play dialogue back through a small event-based runtime.

Requires **Unity 6000.0** or newer.

> **Status: early development.** The package is being built out; the API is not yet
> stable and there is no tagged release. Watch `CHANGELOG.md`.

## Installation

The package installs from this repository's Git URL. Its SQLite dependency lives on
[OpenUPM](https://openupm.com), so that registry goes in alongside it — add both to
`Packages/manifest.json`:

```json
{
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": ["com.gilzoide"]
    }
  ],
  "dependencies": {
    "com.tarinoi.unity": "https://github.com/tarinoi/tarinoi-unity-plugin.git"
  }
}
```

Unity resolves the rest: `com.gilzoide.sqlite-net` from the registry above, and
`com.unity.nuget.newtonsoft-json` and `com.unity.ugui` from Unity's own.

**The scoped registry is not optional.** `com.gilzoide.sqlite-net` supplies SQLite and
its native libraries for every platform. Installing from a Git URL does not change how
dependencies are resolved — without that entry, Unity cannot find it and the install
fails.

<details>
<summary>Installing through the Package Manager window instead</summary>

Add the scoped registry under **Edit → Project Settings → Package Manager** first, then
use **Window → Package Manager → + → Install package from git URL**:

```
https://github.com/tarinoi/tarinoi-unity-plugin.git
```

</details>

<details>
<summary>Pinning to a specific version</summary>

A bare Git URL tracks the default branch, so a later **Update** can pull changes you have
not reviewed. Append a tag or commit hash to pin it:

```json
"com.tarinoi.unity": "https://github.com/tarinoi/tarinoi-unity-plugin.git#<tag-or-commit>"
```

Worth doing while the package is pre-1.0 and the API is still moving.

</details>

## Getting started

**https://tarinoi.app/docs/plugins/unity.html** — the full guide: configuration, bindings,
events, trigger components, shipping a build, and troubleshooting.

Import the **Quickstart** sample from the Package Manager window for a scene that plays
dialogue with a place to register your own bindings.

Related:

- [What the plugins do and don't do](https://tarinoi.app/docs/plugins/)
- [Writing your own integration](https://tarinoi.app/docs/plugins/writing_your_own.html) — the engine-agnostic data contract
- [Adapting a plugin](https://tarinoi.app/docs/plugins/adapting.html) — forking, porting, and the traps we hit

## License

MIT — see [`LICENSE.md`](LICENSE.md). These plugins are reference implementations, meant to
be built on, modified, and incorporated into your own work.
