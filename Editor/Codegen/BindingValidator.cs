using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Tarinoi.Editor.Codegen
{
    /// <summary>A single difference between the synced content and the generated code.</summary>
    public sealed class BindingIssue
    {
        /// <summary>
        /// True when regenerating would break code that currently compiles — a removed or
        /// changed member. Additions are not breaking.
        /// </summary>
        public bool IsBreaking;

        public string Message;

        public override string ToString() => Message;
    }

    /// <summary>
    /// Compares the synced declarations against the generated classes currently compiled
    /// into the project, and reports what regenerating would change.
    /// </summary>
    /// <remarks>
    /// The Godot plugin does this by scanning the generated file's text with regular
    /// expressions and then <i>blocking</i> regeneration on a mismatch. Reflecting over
    /// the compiled types is both more accurate and simpler, and regeneration is not
    /// blocked here: in C# the compiler already reports anything that breaks, so refusing
    /// to write would only leave the developer stuck with stale bindings.
    /// </remarks>
    public static class BindingValidator
    {
        public static List<BindingIssue> Validate(CodegenModel model)
        {
            var issues = new List<BindingIssue>();
            var generated = GeneratedTypes();

            ValidateFunctions(model, generated, issues);
            ValidateVariables(model, generated, issues);
            ValidateCoreFunctions(model, generated, issues);

            return issues;
        }

        /// <summary>
        /// Checks the scaffolded core functions against the synced collection and the
        /// package's own version of the set. Nothing here is breaking: the scaffold is the
        /// game's code, and a function it does not override still falls through to the
        /// generated base class stub.
        /// </summary>
        static void ValidateCoreFunctions(CodegenModel model, IReadOnlyDictionary<string, Type> generated,
            List<BindingIssue> issues)
        {
            var hasCollection = model.Functions.TryGetValue(CoreFunctionsEmitter.Collection, out var decls);
            var scaffold = FindCoreFunctionsScaffold(generated);
            var name = CoreFunctionsEmitter.ClassName;

            if (scaffold == null)
            {
                if (hasCollection)
                {
                    issues.Add(Addition($"'{CoreFunctionsEmitter.Collection}' is the core function set "
                                        + $"but no {name} is compiled yet. Regenerate Bindings "
                                        + "scaffolds the reference implementation."));
                }

                return;
            }

            if (!hasCollection)
            {
                issues.Add(Addition($"{scaffold.FullName} exists but the project has no "
                                    + $"'{CoreFunctionsEmitter.Collection}' collection. Delete it, or "
                                    + "recreate the core functions in Tarinoi."));
                return;
            }

            var version = CoreFunctionsEmitter.CompiledVersion(scaffold);
            if (version != CoreFunctionsEmitter.Version)
            {
                issues.Add(Addition($"{scaffold.FullName} was scaffolded from core functions "
                                    + $"'{version}'; this package scaffolds {CoreFunctionsEmitter.Version}. "
                                    + "Delete it to re-scaffold, or merge the changes by hand."));
            }

            foreach (var fn in decls)
            {
                var member = CodeNames.Member(fn.Name).TrimStart('@');
                if (!CoreFunctionsEmitter.Overrides(scaffold, member))
                {
                    issues.Add(Addition($"{scaffold.FullName} does not override {member}, so "
                                        + $"Fn.{CoreFunctionsEmitter.Collection}.{fn.Name} hits the "
                                        + "not-implemented stub. Add it, or delete the file to re-scaffold."));
                }
            }
        }

        /// <summary>
        /// The compiled scaffold: a concrete class deriving from the generated base for
        /// the core collection, whatever it is called or wherever it was moved to.
        /// </summary>
        public static Type FindCoreFunctionsScaffold(IReadOnlyDictionary<string, Type> generated)
        {
            var baseName = CodeNames.CollectionClass(CoreFunctionsEmitter.Collection, "Functions");
            if (!generated.TryGetValue(baseName, out var baseType))
            {
                return null;
            }

            return AllTypes()
                .FirstOrDefault(t => !t.IsAbstract && t != baseType && baseType.IsAssignableFrom(t));
        }

        static void ValidateFunctions(CodegenModel model, IReadOnlyDictionary<string, Type> generated,
            List<BindingIssue> issues)
        {
            foreach (var collection in model.Functions)
            {
                var className = CodeNames.CollectionClass(collection.Key, "Functions");
                if (!generated.TryGetValue(className, out var type))
                {
                    issues.Add(Addition($"'{collection.Key}' has functions but no generated "
                                        + $"{className} exists yet."));
                    continue;
                }

                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .GroupBy(m => m.Name)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

                foreach (var fn in collection.Value)
                {
                    var member = CodeNames.Member(fn.Name).TrimStart('@');
                    if (!methods.TryGetValue(member, out var method))
                    {
                        issues.Add(Addition($"{className}.{member} is declared in Tarinoi but "
                                            + "not generated yet."));
                        continue;
                    }

                    var expected = fn.Args.Count;
                    var actual = method.GetParameters().Length;
                    if (expected != actual)
                    {
                        issues.Add(Breaking($"{className}.{member} now takes {expected} argument(s), "
                                            + $"but the generated version takes {actual}. Code calling "
                                            + "it will need updating."));
                    }

                    var expectedReturn = CodeTypes.ForReturn(fn.Returns);
                    var actualReturn = FriendlyTypeName(method.ReturnType);
                    if (expectedReturn != actualReturn)
                    {
                        issues.Add(Breaking($"{className}.{member} now returns {expectedReturn}, "
                                            + $"but the generated version returns {actualReturn}."));
                    }
                }

                foreach (var name in DeclaredMemberNames(type))
                {
                    if (collection.Value.All(f => CodeNames.Member(f.Name).TrimStart('@') != name))
                    {
                        issues.Add(Breaking($"{className}.{name} no longer exists in Tarinoi. "
                                            + "Any override of it will stop compiling."));
                    }
                }
            }
        }

        static void ValidateVariables(CodegenModel model, IReadOnlyDictionary<string, Type> generated,
            List<BindingIssue> issues)
        {
            foreach (var collection in model.Variables)
            {
                var className = CodeNames.CollectionClass(collection.Key, "Variables");
                if (!generated.TryGetValue(className, out var type))
                {
                    issues.Add(Addition($"'{collection.Key}' has variables but no generated "
                                        + $"{className} exists yet."));
                    continue;
                }

                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .ToDictionary(f => f.Name, f => f, StringComparer.Ordinal);

                foreach (var variable in collection.Value)
                {
                    var member = CodeNames.Member(variable.Name).TrimStart('@');
                    if (!fields.TryGetValue(member, out var field))
                    {
                        issues.Add(Addition($"{className}.{member} is declared in Tarinoi but "
                                            + "not generated yet."));
                        continue;
                    }

                    var expected = CodeTypes.ForData(variable.DataType);
                    var actual = FriendlyTypeName(field.FieldType);
                    if (expected != actual)
                    {
                        issues.Add(Breaking($"{className}.{member} is now {expected}, but the "
                                            + $"generated version is {actual}."));
                    }
                }

                foreach (var field in fields.Values)
                {
                    if (collection.Value.All(v => CodeNames.Member(v.Name).TrimStart('@') != field.Name))
                    {
                        issues.Add(Breaking($"{className}.{field.Name} no longer exists in Tarinoi."));
                    }
                }
            }
        }

        /// <summary>Generated classes currently compiled into the project, by short name.</summary>
        public static Dictionary<string, Type> GeneratedTypes()
        {
            var types = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var type in AllTypes())
            {
                if (type.Namespace == CodeEmitter.Namespace)
                {
                    types[type.Name] = type;
                }
            }

            return types;
        }

        static IEnumerable<Type> AllTypes()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] assemblyTypes;
                try
                {
                    assemblyTypes = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    // A partially loadable assembly is still worth reading.
                    assemblyTypes = e.Types.Where(t => t != null).ToArray();
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var type in assemblyTypes)
                {
                    yield return type;
                }
            }
        }

        /// <summary>
        /// Method names declared by the generated class itself, excluding the dispatch
        /// plumbing and anything inherited from object.
        /// </summary>
        static IEnumerable<string> DeclaredMemberNames(Type type)
        {
            return type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Select(m => m.Name)
                .Where(n => n != nameof(Bindings.ITarinoiFunctions.HasFunction)
                            && n != nameof(Bindings.ITarinoiFunctions.TryInvoke))
                .Distinct();
        }

        static string FriendlyTypeName(Type type)
        {
            if (type == typeof(void)) return "void";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(double)) return "double";
            if (type == typeof(string)) return "string";
            if (type == typeof(object)) return "object";
            return type.Name;
        }

        static BindingIssue Breaking(string message) =>
            new BindingIssue { IsBreaking = true, Message = message };

        static BindingIssue Addition(string message) =>
            new BindingIssue { IsBreaking = false, Message = message };
    }
}
