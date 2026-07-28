using System.Collections.Generic;

namespace Tarinoi.Bindings
{
    /// <summary>
    /// Maps authored collection names onto the game code that implements them.
    /// </summary>
    /// <remarks>
    /// <b>Keys are the collection's machine identifier, never its display label.</b>
    /// A collection labelled "Global State" in the Tarinoi editor might have the
    /// identifier <c>global</c>, and authored expressions say <c>Fn.global.…</c>.
    /// Binding against the label is a mistake the Godot plugin has made before, and it
    /// fails at dialogue time rather than at bind time — so the tests here assert it
    /// explicitly.
    /// <para>
    /// The runtime holds a reference to this registry, so bindings registered after
    /// configuration still take effect.
    /// </para>
    /// </remarks>
    public sealed class BindingRegistry
    {
        readonly Dictionary<string, ITarinoiFunctions> _functions =
            new Dictionary<string, ITarinoiFunctions>();

        readonly Dictionary<string, ITarinoiVariables> _variables =
            new Dictionary<string, ITarinoiVariables>();

        readonly Dictionary<string, ITarinoiEntities> _entities =
            new Dictionary<string, ITarinoiEntities>();

        /// <summary>Binds a generated or hand-implemented function collection.</summary>
        /// <param name="collectionIdentifier">The collection's machine identifier.</param>
        public void BindFunctions(string collectionIdentifier, ITarinoiFunctions impl)
        {
            if (!Validate(collectionIdentifier, impl, "function"))
            {
                return;
            }

            _functions[collectionIdentifier] = impl;
        }

        /// <summary>
        /// Binds a plain object as a function collection, dispatching to its methods
        /// reflectively. Convenient while prototyping; prefer generated bindings for a
        /// shipped build (see <see cref="ReflectionFunctions"/>).
        /// </summary>
        public void BindFunctions(string collectionIdentifier, object impl)
        {
            if (!Validate(collectionIdentifier, impl, "function"))
            {
                return;
            }

            _functions[collectionIdentifier] =
                impl as ITarinoiFunctions ?? new ReflectionFunctions(impl);
        }

        public void BindVariables(string collectionIdentifier, ITarinoiVariables impl)
        {
            if (!Validate(collectionIdentifier, impl, "variable"))
            {
                return;
            }

            _variables[collectionIdentifier] = impl;
        }

        public void BindEntities(string collectionIdentifier, ITarinoiEntities impl)
        {
            if (!Validate(collectionIdentifier, impl, "entity"))
            {
                return;
            }

            _entities[collectionIdentifier] = impl;
        }

        /// <summary>Returns the bound function collection, or null.</summary>
        public ITarinoiFunctions GetFunctions(string collectionIdentifier) =>
            Lookup(_functions, collectionIdentifier);

        /// <summary>Returns the bound variable collection, or null.</summary>
        public ITarinoiVariables GetVariables(string collectionIdentifier) =>
            Lookup(_variables, collectionIdentifier);

        /// <summary>Returns the bound entity collection, or null.</summary>
        public ITarinoiEntities GetEntities(string collectionIdentifier) =>
            Lookup(_entities, collectionIdentifier);

        /// <summary>Every bound collection identifier, for editor diagnostics.</summary>
        public IEnumerable<string> BoundFunctionCollections => _functions.Keys;
        public IEnumerable<string> BoundVariableCollections => _variables.Keys;
        public IEnumerable<string> BoundEntityCollections => _entities.Keys;

        /// <summary>Drops every binding. Mainly useful between tests.</summary>
        public void Clear()
        {
            _functions.Clear();
            _variables.Clear();
            _entities.Clear();
        }

        static T Lookup<T>(Dictionary<string, T> map, string key) where T : class =>
            key != null && map.TryGetValue(key, out var value) ? value : null;

        static bool Validate(string collectionIdentifier, object impl, string kind)
        {
            if (string.IsNullOrEmpty(collectionIdentifier))
            {
                TarinoiLog.Error($"Bindings: cannot bind a {kind} collection without an identifier.");
                return false;
            }

            if (impl == null)
            {
                TarinoiLog.Error($"Bindings: cannot bind null as the '{collectionIdentifier}' {kind} collection.");
                return false;
            }

            return true;
        }
    }
}
