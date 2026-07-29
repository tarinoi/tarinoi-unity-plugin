using Tarinoi.Ui;
using UnityEngine;

namespace TarinoiSample
{
    /// <summary>
    /// A playable Tarinoi setup with this game's bindings registered.
    /// </summary>
    /// <remarks>
    /// Put this on an empty GameObject in an empty scene and press Play. Everything else —
    /// the canvas, the entry-point list, the dialogue view — is built at runtime.
    /// </remarks>
    [AddComponentMenu("Tarinoi/Sample/My Quickstart")]
    public class MyQuickstart : TarinoiQuickstart
    {
        /// <summary>
        /// Registers this game's bindings. Called once, after the content database is open
        /// and before any dialogue runs.
        /// </summary>
        /// <remarks>
        /// The collection name — <c>"global"</c> here — is the collection's <b>machine
        /// identifier</b> in Tarinoi, not its display label. Authored expressions say
        /// <c>Fn.global.…</c>, so that is what the binding has to be registered under; a
        /// mismatch shows up as an unbound-collection error when the dialogue runs.
        /// </remarks>
        protected override void SetupBindings()
        {
            var variables = new MyVariables();

            Runtime.Registry.BindVariables("global", variables);
            Runtime.Registry.BindFunctions("global", (object)new MyFunctions(variables));
        }
    }
}
