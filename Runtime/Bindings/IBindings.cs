namespace Tarinoi.Bindings
{
    /// <summary>
    /// Implements the functions an author can call as <c>Fn.collection.Name(...)</c>.
    /// </summary>
    /// <remarks>
    /// The generated binding classes implement <see cref="TryInvoke"/> as a plain
    /// <c>switch</c>, which keeps dispatch AOT-safe: IL2CPP strips reflection targets,
    /// so a reflective call that works in the editor can fail in a built player.
    /// <para>
    /// Hand-written classes don't have to implement this interface — pass any object to
    /// <see cref="BindingRegistry.BindFunctions(string, object)"/> and it will be
    /// adapted reflectively, which is convenient during development.
    /// </para>
    /// </remarks>
    public interface ITarinoiFunctions
    {
        /// <summary>Whether a function of this name exists.</summary>
        bool HasFunction(string name);

        /// <summary>
        /// Calls a function. Returns false if it doesn't exist; the caller logs and
        /// carries on. Implementations should not throw.
        /// </summary>
        bool TryInvoke(string name, object[] args, out object result);
    }

    /// <summary>
    /// Supplies the game state an author reads and writes as <c>Var.collection.name</c>.
    /// </summary>
    public interface ITarinoiVariables
    {
        object GetVariable(string name);
        void SetVariable(string name, object value);
    }

    /// <summary>
    /// Supplies the game objects an author refers to as <c>Ent.collection.name</c>.
    /// </summary>
    /// <remarks>
    /// What an entity <i>is</i> stays deliberately open — the game decides. Tarinoi only
    /// passes the returned object back to your own functions.
    /// </remarks>
    public interface ITarinoiEntities
    {
        object GetEntity(string name);
    }
}
