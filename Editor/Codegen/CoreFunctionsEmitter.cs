using System;
using System.Collections.Generic;
using System.Linq;

namespace Tarinoi.Editor.Codegen
{
    /// <summary>
    /// Renders the reference implementation of Tarinoi's core functions — the built-in
    /// <c>Fn.tarinoi.*</c> set that in-app playback evaluates for real — as a C# class
    /// the game owns.
    /// </summary>
    /// <remarks>
    /// Codegen writes the result beside the generated bindings, once: the file is the
    /// game's to edit (a different variable store, savegame hooks, …) and is never
    /// overwritten. Deleting it gets a fresh copy on the next run.
    /// <para>
    /// The set is versioned. <see cref="Version"/> follows the public core functions
    /// document; the scaffold carries it as a constant so <see cref="BindingValidator"/>
    /// can report one rendered from an older set.
    /// </para>
    /// </remarks>
    public static class CoreFunctionsEmitter
    {
        /// <summary>
        /// Version of the core set this package scaffolds. Matches the version line in
        /// the public core functions document.
        /// </summary>
        public const string Version = "0.0.1";

        /// <summary>Identifier of the function collection holding the set: <c>Fn.tarinoi.*</c>.</summary>
        public const string Collection = "tarinoi";

        /// <summary>Name of the scaffolded class.</summary>
        public const string ClassName = "TarinoiCoreFunctions";

        /// <summary>File the scaffold is written to, inside the impl directory.</summary>
        public const string FileName = ClassName + ".cs";

        /// <summary>Name of the constant the scaffold carries its version in.</summary>
        public const string VersionConstant = "ScaffoldVersion";

        /// <summary>First line of every scaffold.</summary>
        public const string StampPrefix = "// Tarinoi core functions ";

        /// <summary>
        /// Author-facing order of the set: setters by type, then comparators by type.
        /// Functions outside the set follow, by name.
        /// </summary>
        public static readonly IReadOnlyList<string> Order = new[]
        {
            "SetFlag", "ClearFlag", "ToggleFlag", "SetCounter", "IncrementCounter", "SetText",
            "FlagIsSet", "NumberEquals", "NumberAtLeast", "NumberGreaterThan", "NumberAtMost",
            "NumberLessThan", "StringEquals",
        };

        sealed class Body
        {
            public string[] Args;
            public string Returns;
            public string Doc;
            public string Expression;
        }

        /// <summary>
        /// The reference bodies, keyed by function name. A declaration that does not match
        /// its entry by name and arity is not a core function this package knows, and gets
        /// an ordinary not-implemented stub.
        /// </summary>
        static readonly Dictionary<string, Body> Bodies = new Dictionary<string, Body>(StringComparer.Ordinal)
        {
            ["SetFlag"] = Mutator(new[] { "flagRef" }, "Set a flag.",
                "Write(flagRef, true)"),
            ["ClearFlag"] = Mutator(new[] { "flagRef" }, "Clear a flag.",
                "Write(flagRef, false)"),
            ["ToggleFlag"] = Mutator(new[] { "flagRef" }, "Toggle a flag: clear if set, set if unset.",
                "Write(flagRef, !Flag(flagRef))"),
            ["SetCounter"] = Mutator(new[] { "counterRef", "value" },
                "Set a counter to a value, which must be a number.",
                "Write(counterRef, Number(value))"),
            ["IncrementCounter"] = Mutator(new[] { "counterRef", "delta" },
                "Increment a counter by delta, which must be a number. Use negative numbers to "
                + "decrement. An unset counter is treated as a 0.",
                "Write(counterRef, Number(counterRef) + Number(delta))"),
            ["SetText"] = Mutator(new[] { "textRef", "value" },
                "Set a text variable to a value, which must be a string.",
                "Write(textRef, Text(value))"),
            ["FlagIsSet"] = Predicate(new[] { "flagRef" },
                "Returns true if the flag is set. An unset flag is treated as clear. Use the NOT "
                + "operator in conditions to negate.",
                "Flag(flagRef)"),
            ["NumberEquals"] = Predicate(new[] { "a", "b" },
                "Returns true if a and b equal each other. Both must be numbers. An unset counter "
                + "is treated as a 0. Use the NOT operator in conditions to negate.",
                "Number(a) == Number(b)"),
            ["NumberAtLeast"] = Predicate(new[] { "a", "b" },
                "Returns true if a >= b. Both must be numbers. An unset counter is treated as a 0.",
                "Number(a) >= Number(b)"),
            ["NumberGreaterThan"] = Predicate(new[] { "a", "b" },
                "Returns true if a > b. Both must be numbers. An unset counter is treated as a 0.",
                "Number(a) > Number(b)"),
            ["NumberAtMost"] = Predicate(new[] { "a", "b" },
                "Returns true if a <= b. Both must be numbers. An unset counter is treated as a 0.",
                "Number(a) <= Number(b)"),
            ["NumberLessThan"] = Predicate(new[] { "a", "b" },
                "Returns true if a < b. Both must be numbers. An unset counter is treated as a 0.",
                "Number(a) < Number(b)"),
            ["StringEquals"] = Predicate(new[] { "a", "b" },
                "Returns true if a and b equal each other. Both must be strings. An unset text "
                + "variable is treated as empty. Use the NOT operator in conditions to negate.",
                "Text(a) == Text(b)"),
        };

        static Body Mutator(string[] args, string doc, string expression) =>
            new Body { Args = args, Returns = "void", Doc = doc, Expression = expression };

        static Body Predicate(string[] args, string doc, string expression) =>
            new Body { Args = args, Returns = "bool", Doc = doc, Expression = expression };

        /// <summary>
        /// Renders the scaffold for the declarations synced in the <c>tarinoi</c> collection.
        /// </summary>
        /// <param name="unknown">
        /// Receives the declarations that got a stub instead of a body: not in the set, or
        /// with a different arity.
        /// </param>
        /// <param name="baseNamespace">
        /// Where the generated base class lives. Tests point this at a fixture; codegen
        /// leaves the default.
        /// </param>
        /// <param name="baseClass">The generated base class to derive from.</param>
        /// <param name="ns">
        /// A namespace to wrap the class in, or null for none: the scaffold is the game's
        /// code, and most Unity projects keep theirs in the global namespace.
        /// </param>
        public static string Render(IEnumerable<FunctionDecl> decls, string projectId, out List<string> unknown,
            string baseNamespace = CodeEmitter.Namespace, string baseClass = "TarinoiFunctions",
            string ns = null)
        {
            unknown = new List<string>();
            var w = new CodeEmitter.Writer();

            w.Line(StampPrefix + Version + " — reference implementation.");
            w.Line($"// Scaffolded by Tarinoi from the synced content of project '{projectId}'.");
            w.Line("//");
            w.Line("// This file is yours: edit it freely, regenerating never overwrites it. Delete");
            w.Line("// it to get a fresh copy the next time you regenerate bindings.");
            w.Line("//");
            w.Line($"// These are the Fn.{Collection}.* functions in-app playback evaluates for real.");
            w.Line("// Keep the same semantics — in particular the defaults for unset variables — so");
            w.Line("// that what an author verified in playback holds in the game.");
            w.Line();
            w.Line("using System.Globalization;");
            w.Line("using Tarinoi;");
            w.Line("using Tarinoi.Bindings;");
            w.Line($"using {baseNamespace};");
            w.Line("using UnityEngine.Scripting;");
            w.Line();

            if (ns != null)
            {
                w.Line($"namespace {ns}");
                w.Open();
            }

            w.Doc($"Tarinoi's core functions, callable as <c>Fn.{Collection}.*</c>.");
            w.Doc("Bind an instance under <c>\"" + Collection + "\"</c>: "
                  + $"<c>Registry.BindFunctions(\"{Collection}\", new {ClassName}())</c>. "
                  + "The quickstart component does this on its own.", "remarks");
            // Preserve: the quickstart finds this class reflectively, and nothing else
            // may reference it, so IL2CPP would otherwise strip it from a player build.
            w.Line("[Preserve]");
            w.Line($"public class {ClassName} : {baseClass}");
            w.Open();

            w.Doc("The version of the core set this file was scaffolded from.");
            w.Line($"public const string {VersionConstant} = {CodeNames.StringLiteral(Version)};");
            w.Line();

            foreach (var fn in Ordered(decls))
            {
                var member = CodeNames.Member(fn.Name);
                var parameters = fn.Args.Select(CodeNames.Parameter).ToList();

                if (!Bodies.TryGetValue(fn.Name, out var body) || body.Args.Length != fn.Args.Count)
                {
                    unknown.Add(fn.Name);
                    EmitStub(w, fn, member, parameters);
                    continue;
                }

                w.Doc(body.Doc);
                w.Line($"public override {body.Returns} {member}("
                       + string.Join(", ", parameters.Select(p => "object " + p)) + ") => "
                       + body.Expression + ";");
                w.Line();
            }

            EmitHelpers(w);

            w.Close();
            if (ns != null)
            {
                w.Close();
            }

            return w.ToString();
        }

        /// <summary>
        /// Reads the version a compiled scaffold carries, or "" when the type has none.
        /// </summary>
        public static string CompiledVersion(Type scaffold)
        {
            var field = scaffold?.GetField(VersionConstant);
            return field != null && field.IsLiteral ? field.GetRawConstantValue() as string ?? "" : "";
        }

        /// <summary>Whether the scaffold defines a function of this name itself.</summary>
        public static bool Overrides(Type scaffold, string member) =>
            scaffold.GetMethod(member,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly) != null;

        // -------------------------------------------------------------------------

        /// <summary>
        /// A declaration this package has no body for: the same not-implemented stub the
        /// generated base class carries, so the mismatch is loud at runtime too.
        /// </summary>
        static void EmitStub(CodeEmitter.Writer w, FunctionDecl fn, string member, List<string> parameters)
        {
            var signature = $"{fn.Name}({string.Join(", ", fn.Args)}) -> "
                            + (string.IsNullOrEmpty(fn.Returns) ? "void" : fn.Returns);
            w.Doc($"{signature} — not a core function this package knows; implement it.");

            var returnType = CodeTypes.ForReturn(fn.Returns);
            w.Line($"public override {returnType} {member}("
                   + string.Join(", ", parameters.Select(p => "object " + p)) + ")");
            w.Open();
            w.Line($"TarinoiLog.Error(\"{ClassName}.{member} is not implemented.\");");
            var defaultLiteral = CodeTypes.DefaultReturnLiteral(fn.Returns);
            if (defaultLiteral.Length > 0)
            {
                w.Line($"return {defaultLiteral};");
            }

            w.Close();
            w.Line();
        }

        /// <summary>
        /// The helpers every reference body calls. They turn the loosely typed values the
        /// dispatcher hands over into what the function needs, honouring the defaults for
        /// unset variables, and log rather than throw on a mismatch.
        /// </summary>
        static void EmitHelpers(CodeEmitter.Writer w)
        {
            w.Line("// -------------------------------------------------------------------------");
            w.Line("// Helpers. The dispatcher passes a Var.* argument as a VarRef so the function");
            w.Line("// can write through it, and anything else as a plain value. Unset variables");
            w.Line("// read as false / 0 / \"\", matching in-app playback. A value of the wrong type");
            w.Line("// is an authoring error: it is logged, and the default is used instead.");
            w.Line("// -------------------------------------------------------------------------");
            w.Line();

            w.Doc("Writes through a Var.* reference.");
            w.Line("static void Write(object reference, object value)");
            w.Open();
            w.Line("if (reference is VarRef variable)");
            w.Open();
            w.Line("variable.Value = value;");
            w.Line("return;");
            w.Close();
            w.Line();
            w.Line("TarinoiLog.Error($\"Core functions: expected a Var.* reference, got {Describe(reference)}.\");");
            w.Close();
            w.Line();

            w.Doc("Reads a boolean: a flag, or a literal.");
            w.Line("static bool Flag(object value)");
            w.Open();
            w.Line("switch (ValueConvert.Unwrap(value))");
            w.Open();
            w.Line("case null: return false;");
            w.Line("case bool flag: return flag;");
            w.Close();
            w.Line();
            w.Line("TarinoiLog.Error($\"Core functions: expected a boolean, got {Describe(value)}.\");");
            w.Line("return false;");
            w.Close();
            w.Line();

            w.Doc("Reads a number: a counter, a list item, or a literal.");
            w.Line("static double Number(object value)");
            w.Open();
            w.Line("switch (ValueConvert.Unwrap(value))");
            w.Open();
            w.Line("case null: return 0d;");
            w.Line("case string _:");
            w.Line("case bool _:");
            w.Line("    break;");
            w.Line("case System.IConvertible number:");
            w.Line("    return number.ToDouble(CultureInfo.InvariantCulture);");
            w.Close();
            w.Line();
            w.Line("TarinoiLog.Error($\"Core functions: expected a number, got {Describe(value)}.\");");
            w.Line("return 0d;");
            w.Close();
            w.Line();

            w.Doc("Reads a string: a text variable, a list item, or a literal.");
            w.Line("static string Text(object value)");
            w.Open();
            w.Line("switch (ValueConvert.Unwrap(value))");
            w.Open();
            w.Line("case null: return \"\";");
            w.Line("case string text: return text;");
            w.Close();
            w.Line();
            w.Line("TarinoiLog.Error($\"Core functions: expected a string, got {Describe(value)}.\");");
            w.Line("return \"\";");
            w.Close();
            w.Line();

            w.Line("static string Describe(object value) =>");
            w.Line("    value is VarRef variable");
            w.Line("        ? $\"{variable} = {variable.Value ?? \"unset\"}\"");
            w.Line("        : $\"{value ?? \"null\"} ({value?.GetType().Name ?? \"null\"})\";");
        }

        static IEnumerable<FunctionDecl> Ordered(IEnumerable<FunctionDecl> decls)
        {
            int Rank(string name)
            {
                var index = Order.ToList().IndexOf(name);
                return index == -1 ? Order.Count : index;
            }

            return decls.OrderBy(d => Rank(d.Name)).ThenBy(d => d.Name, StringComparer.Ordinal);
        }
    }
}
