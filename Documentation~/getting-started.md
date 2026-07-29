# Getting started

From an empty Unity project to dialogue playing on screen. Around ten minutes.

You will need a Tarinoi project with some dialogue in it, and permission to create an
API token for it.

---

## 1. Install the package

Tarinoi is published on [OpenUPM](https://openupm.com). From your project folder:

```bash
openupm add com.tarinoi.unity
```

That pulls in the SQLite dependency and its native libraries for every platform.

<details>
<summary>Without the OpenUPM CLI</summary>

Add the registry and the package to `Packages/manifest.json` by hand:

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

The `com.gilzoide` scope is required — that is where the SQLite dependency comes from.
</details>

## 2. Point Unity at your Tarinoi project

Open **Project Settings → Tarinoi**.

1. **API path** — paste the documents endpoint from your Tarinoi project. It ends in
   `/documents`. The **Project** field underneath fills in automatically; if it stays
   empty, the path is wrong.
2. **API token** — click **Set…** and paste a token from your Tarinoi project's
   Integrations page. A **Read** token is enough.

The token is stored outside your Unity project, so it is never committed and never
ends up in a build.

## 3. Sync

**Tools → Tarinoi → Sync**.

The Console reports what arrived, for example
`sync complete — 281 upserted, 737 removed, 22 collections`. Syncing again is
incremental; only what changed comes down.

## 4. Generate your bindings

**Tools → Tarinoi → Regenerate Bindings**.

This writes C# into `Assets/Tarinoi/Generated` describing what your authors declared:
one class per function collection, one per variable collection, and constants for list
options and entities.

They are stubs. Deriving from them and overriding the methods is how your game gives
those authored names meaning:

```csharp
public class MyFunctions : Tarinoi.Generated.GlobalFunctions
{
    public override bool CheckFlag(object flag) =>
        ValueConvert.ToBool(VarRef.Resolve(flag));
}
```

Re-run this whenever authors add or rename something. **Tools → Tarinoi → Check
Bindings** reports what has drifted without writing anything.

## 5. Play it

**Tools → Tarinoi → Create Quickstart Scene**, then press Play.

You get a list of every entry point in your content. Pick one and the dialogue plays.
That confirms the whole chain works: sync, bindings, and playback.

To register your own bindings, import the **Quickstart** sample from the Package
Manager and use `MyQuickstart` instead — it shows where they go.

---

## Wiring it into your own game

The quickstart scene is a development tool. A real game does three things:

```csharp
// 1. Configure once, at startup.
await TarinoiRuntime.Instance.ConfigureAsync();

// 2. Register your bindings, before any dialogue runs.
TarinoiRuntime.Instance.Registry.BindVariables("global", myVariables);
TarinoiRuntime.Instance.Registry.BindFunctions("global", myFunctions);

// 3. Handle the events, and drive it from your own UI.
TarinoiRuntime.Instance.LineReady += line => ShowLine(line.EntityLabel, line.Line);
TarinoiRuntime.Instance.ChoicesReady += ShowChoices;
TarinoiRuntime.Instance.DialogueEnded += CloseDialogueUi;

await TarinoiRuntime.Instance.StartDialogueAsync(collectionId, cardId);
```

Then `AdvanceAsync()` past a line, and `SelectChoiceAsync(index)` to take an option.

**Bind against the collection's machine identifier, not its display label.** A
collection shown as "Global State" might be `global` in expressions — and `global` is
what you register. Getting this wrong fails when the dialogue runs, not when you bind.

To start dialogue from the world, put a `DialogueTrigger` (or the collider-based
`DialogueTriggerVolume`) on an object and handle its `InteractionTriggered` event.

## Shipping a build

**This step is required, not optional.** A build cannot see the content you synced in
the editor: Unity gives players their own storage location, separate from the editor's.
Ship without a snapshot and your game starts with no dialogue at all.

1. Sync everything you want to ship.
2. **Tools → Tarinoi → Snapshot for Export** — copies the content into
   `StreamingAssets`, stripping the API path and sync cursor.
3. Tick **Offline mode** in Project Settings → Tarinoi.

Builds then copy that snapshot into place on first run and never contact the network.
Re-export whenever you want shipped content to change.

The same separation applies to your API token, deliberately: it lives outside the
project and does not travel into a build.

From a build script:

```bash
Unity -batchmode -quit -projectPath . \
  -executeMethod Tarinoi.Editor.TarinoiCli.SyncAndGenerate
Unity -batchmode -quit -projectPath . \
  -executeMethod Tarinoi.Editor.TarinoiCli.ExportSnapshot
```

## Troubleshooting

| What you see | What it means |
|---|---|
| "Set your project's API path…" | Project Settings → Tarinoi, paste the documents URL. |
| "credentials rejected" | The token is wrong or expired. Set it again. |
| "project not found" | The API path points at a project that is not there. Check it. |
| The entry-point list is empty | Nothing synced yet, or your content has no start cards. Sync and re-check the Console. |
| "no bindings registered for function collection 'x'" | Register a binding under `x` — and check you used the machine identifier, not the label. |
| "'Col.Name' is not implemented" | The generated stub is still in place. Derive from the class and override it. |
| "…is not bound. Regenerate your bindings." | An author added a function since you last generated. Run Regenerate Bindings. |
| Dialogue stops with "has no pin 'x'" | A card's output selector returned a pin the card does not have. Fix the selector or add the pin. |
| A build has no dialogue, though the editor does | No snapshot was exported. See "Shipping a build" — a player cannot read the editor's content. |
| A build logs "No API token saved" | It is trying to sync. Turn on Offline mode; players should play a snapshot, not call the API. |

## Reference

- **Project Settings → Tarinoi** — connection, generated bindings, behaviour.
- **Tools → Tarinoi** — Sync, Regenerate Bindings, Check Bindings, Set API token,
  Snapshot for Export, Clear Local Content, Create Quickstart Scene.
- **Command line** — `-executeMethod Tarinoi.Editor.TarinoiCli.SyncAndGenerate`
  syncs and regenerates from a build script.
