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

Import the **Quickstart** sample from the Package Manager window, then follow
[`Documentation~/getting-started.md`](Documentation~/getting-started.md).

## License

MIT — see [`LICENSE.md`](LICENSE.md).
