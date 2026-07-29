# Tarinoi Quickstart sample

Two files:

- `MyQuickstart.cs` — a `TarinoiQuickstart` subclass showing where your bindings go.
- `MyBindings.cs` — hand-written stand-ins for the classes Tarinoi generates.

## Using it

1. Add `MyQuickstart` to an empty GameObject in an empty scene and press Play.
   (Or run **Tools → Tarinoi → Create Quickstart Scene**, which does that for you.)
2. Once you have synced real content, run **Tools → Tarinoi → Regenerate Bindings**
   and delete `MyBindings.cs` — derive from the generated classes instead.

The interface is built in code so the sample runs with no setup at all. It is a
development tool, not a shipping UI: replace it once you know what your game needs.
