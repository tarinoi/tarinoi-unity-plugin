using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Tarinoi.Bindings
{
    /// <summary>
    /// Binds whatever the generated bindings can supply on their own: the generated
    /// variable classes, and the scaffolded core functions.
    /// </summary>
    /// <remarks>
    /// The generated variable classes are concrete — a typed field per variable — and the
    /// core functions scaffold derives from the generated base for the <c>tarinoi</c>
    /// collection, so synced content that only uses <c>Fn.tarinoi.*</c> can play with no
    /// bindings written at all. The quickstart component calls this after
    /// <c>SetupBindings</c>, for every collection the game left unbound.
    /// <para>
    /// A real game binds explicitly. This looks the classes up reflectively, which is a
    /// convenience for a development scene, not something to build a shipped player on:
    /// IL2CPP strips types nothing references, so a scaffold found here in the editor may
    /// be gone from a build unless it carries <c>[Preserve]</c> (the scaffold does).
    /// </para>
    /// </remarks>
    public static class GeneratedBindings
    {
        /// <summary>Namespace the generated code lives in. Mirrors the editor's emitter.</summary>
        public const string Namespace = "Tarinoi.Generated";

        /// <summary>The core function collection, and the generated base class for it.</summary>
        public const string CoreCollection = "tarinoi";

        const string CoreBaseClass = "TarinoiFunctions";
        const string CollectionConstant = "Collection";

        /// <summary>
        /// Binds every generated variable class whose collection is unbound, and the core
        /// functions scaffold if <c>tarinoi</c> is unbound. Returns what it bound.
        /// </summary>
        public static List<string> BindDefaults(BindingRegistry registry) =>
            BindDefaults(registry, AllTypes());

        /// <summary>The same, over a given set of types and namespace — for tests.</summary>
        public static List<string> BindDefaults(BindingRegistry registry, IEnumerable<Type> types,
            string ns = Namespace)
        {
            var bound = new List<string>();
            var all = types as IList<Type> ?? types.ToList();
            var generated = all.Where(t => t.Namespace == ns).ToList();

            foreach (var type in generated)
            {
                if (type.IsAbstract || !typeof(ITarinoiVariables).IsAssignableFrom(type))
                {
                    continue;
                }

                var collection = type.GetField(CollectionConstant)?.GetRawConstantValue() as string;
                if (string.IsNullOrEmpty(collection) || registry.GetVariables(collection) != null)
                {
                    continue;
                }

                if (Instantiate(type) is ITarinoiVariables variables)
                {
                    registry.BindVariables(collection, variables);
                    bound.Add($"Var.{collection} → {type.Name}");
                }
            }

            if (registry.GetFunctions(CoreCollection) == null)
            {
                var scaffold = FindCoreFunctionsScaffold(all, ns);
                if (scaffold != null && Instantiate(scaffold) is ITarinoiFunctions functions)
                {
                    registry.BindFunctions(CoreCollection, functions);
                    bound.Add($"Fn.{CoreCollection} → {scaffold.Name}");
                }
            }

            return bound;
        }

        /// <summary>
        /// The scaffolded core functions: a concrete class deriving from the generated base
        /// for the <c>tarinoi</c> collection, whatever it is called or wherever it was
        /// moved to.
        /// </summary>
        public static Type FindCoreFunctionsScaffold(IEnumerable<Type> types, string ns = Namespace)
        {
            var all = types as IList<Type> ?? types.ToList();
            var baseType = all.FirstOrDefault(t => t.Namespace == ns && t.Name == CoreBaseClass);
            if (baseType == null)
            {
                return null;
            }

            return all.FirstOrDefault(t => !t.IsAbstract && t != baseType && baseType.IsAssignableFrom(t));
        }

        static object Instantiate(Type type)
        {
            try
            {
                return Activator.CreateInstance(type);
            }
            catch (Exception e)
            {
                TarinoiLog.Warn($"Could not create a {type.FullName} to bind by default: {e.Message}");
                return null;
            }
        }

        static IEnumerable<Type> AllTypes()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var type in types)
                {
                    yield return type;
                }
            }
        }
    }
}
