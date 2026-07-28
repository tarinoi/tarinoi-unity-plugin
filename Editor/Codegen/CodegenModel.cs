using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Tarinoi.Editor.Codegen
{
    /// <summary>One authored function an author can call as <c>Fn.collection.Name(...)</c>.</summary>
    public sealed class FunctionDecl
    {
        public string Name;
        public List<string> Args = new List<string>();
        public string Returns = "";
        public string Effect = "";
    }

    /// <summary>One authored variable, readable as <c>Var.collection.name</c>.</summary>
    public sealed class VariableDecl
    {
        public string Name;
        public string DataType = "";
        public object DefaultValue;
    }

    /// <summary>One authored option list, readable as <c>Ls.collection.list.key</c>.</summary>
    public sealed class ListDecl
    {
        public string Identifier;
        public List<string> OptionKeys = new List<string>();
    }

    /// <summary>One entity that can take part in dialogue.</summary>
    public sealed class EntityDecl
    {
        public string Identifier;
    }

    /// <summary>Everything codegen reads from the database, grouped by collection.</summary>
    public sealed class CodegenModel
    {
        public readonly SortedDictionary<string, List<FunctionDecl>> Functions =
            new SortedDictionary<string, List<FunctionDecl>>(StringComparer.Ordinal);

        public readonly SortedDictionary<string, List<VariableDecl>> Variables =
            new SortedDictionary<string, List<VariableDecl>>(StringComparer.Ordinal);

        public readonly SortedDictionary<string, List<ListDecl>> Lists =
            new SortedDictionary<string, List<ListDecl>>(StringComparer.Ordinal);

        public readonly SortedDictionary<string, List<EntityDecl>> Entities =
            new SortedDictionary<string, List<EntityDecl>>(StringComparer.Ordinal);

        public bool IsEmpty =>
            Functions.Count == 0 && Variables.Count == 0
                                 && Lists.Count == 0 && Entities.Count == 0;
    }

    /// <summary>
    /// Turns authored names and types into C#.
    /// </summary>
    /// <remarks>
    /// Authored identifiers are snake_case and unconstrained; C# identifiers are not.
    /// Everything here is deliberately deterministic, because a generated file that
    /// shuffles between runs produces noisy diffs and spurious merge conflicts.
    /// </remarks>
    public static class CodeNames
    {
        /// <summary>
        /// C# keywords that would be illegal as identifiers. Prefixing with <c>@</c>
        /// keeps a collection genuinely called "class" or "event" working.
        /// </summary>
        static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
            "checked", "class", "const", "continue", "decimal", "default", "delegate",
            "do", "double", "else", "enum", "event", "explicit", "extern", "false",
            "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
            "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
            "new", "null", "object", "operator", "out", "override", "params", "private",
            "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
            "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
            "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
            "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
        };

        /// <summary>
        /// Converts an authored identifier to PascalCase, dropping characters C# will
        /// not accept. Returns "Unnamed" for input with nothing usable in it, so
        /// generation still produces compilable code rather than failing outright.
        /// </summary>
        public static string ToPascal(string authored)
        {
            if (string.IsNullOrEmpty(authored))
            {
                return "Unnamed";
            }

            var builder = new StringBuilder();
            var capitalise = true;

            foreach (var c in authored)
            {
                if (c == '_' || c == '-' || c == ' ' || c == '.')
                {
                    capitalise = true;
                    continue;
                }

                if (!char.IsLetterOrDigit(c))
                {
                    continue;
                }

                builder.Append(capitalise ? char.ToUpperInvariant(c) : c);
                capitalise = false;
            }

            if (builder.Length == 0)
            {
                return "Unnamed";
            }

            // C# identifiers cannot start with a digit.
            if (char.IsDigit(builder[0]))
            {
                builder.Insert(0, '_');
            }

            return builder.ToString();
        }

        /// <summary>Whether a name is a C# keyword and so needs escaping.</summary>
        /// <remarks>
        /// Keywords are all lowercase, so a PascalCased member name can never be one.
        /// This matters for parameters, which are camelCased and routinely collide —
        /// an authored argument called <c>object</c> or <c>value</c> is entirely normal.
        /// </remarks>
        public static bool IsKeyword(string name) => Keywords.Contains(name ?? "");

        /// <summary>A member name safe to emit.</summary>
        public static string Member(string authored)
        {
            var name = ToPascal(authored);
            return IsKeyword(name) ? "@" + name : name;
        }

        /// <summary>A parameter name safe to emit, escaping keywords.</summary>
        public static string Parameter(string authored)
        {
            var pascal = ToPascal(authored);
            var camel = char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
            return IsKeyword(camel) ? "@" + camel : camel;
        }

        /// <summary>
        /// The generated class for a collection, e.g. <c>global</c> + <c>Functions</c>
        /// becomes <c>GlobalFunctions</c>.
        /// </summary>
        /// <remarks>
        /// The Godot generator prefixes every class with "Tarinoi" because Godot's
        /// global class-name table is flat and even nested names can collide project-wide.
        /// C# namespaces solve that, so the prefix is dropped here.
        /// </remarks>
        public static string CollectionClass(string collection, string kind) =>
            ToPascal(collection) + kind;

        /// <summary>Escapes a string for a C# literal.</summary>
        public static string StringLiteral(string value)
        {
            if (value == null)
            {
                return "null";
            }

            return "\"" + value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t") + "\"";
        }
    }

    /// <summary>Maps Tarinoi's authored types onto C# ones.</summary>
    public static class CodeTypes
    {
        /// <summary>The C# type for a declared variable or return value.</summary>
        /// <remarks>
        /// <c>number</c> becomes <c>double</c> rather than <c>float</c>: authored numbers
        /// arrive from JSON as doubles, and narrowing at the boundary would lose precision
        /// silently. An unrecognised type becomes <c>object</c> so new authored types keep
        /// compiling.
        /// </remarks>
        public static string ForData(string dataType)
        {
            switch ((dataType ?? "").ToLowerInvariant())
            {
                case "boolean": return "bool";
                case "number": return "double";
                case "string": return "string";
                default: return "object";
            }
        }

        /// <summary>The C# return type for a declared function.</summary>
        public static string ForReturn(string returns)
        {
            var declared = (returns ?? "").ToLowerInvariant();
            return declared == "void" || declared.Length == 0 ? "void" : ForData(declared);
        }

        /// <summary>Whether a function returns nothing.</summary>
        public static bool IsVoid(string returns) => ForReturn(returns) == "void";

        /// <summary>The literal a stub returns when the game has not implemented it.</summary>
        public static string DefaultReturnLiteral(string returns)
        {
            switch (ForReturn(returns))
            {
                case "void": return "";
                case "bool": return "false";
                case "double": return "0d";
                case "string": return "\"\"";
                default: return "null";
            }
        }

        /// <summary>
        /// The literal for a variable field's initial value, falling back to the type's
        /// zero when the author declared no default.
        /// </summary>
        public static string DefaultValueLiteral(string dataType, object defaultValue)
        {
            switch (ForData(dataType))
            {
                case "bool":
                    return ToBool(defaultValue) ? "true" : "false";

                case "double":
                    return ToDouble(defaultValue).ToString("R", CultureInfo.InvariantCulture) + "d";

                case "string":
                    return CodeNames.StringLiteral(defaultValue?.ToString() ?? "");

                default:
                    return "null";
            }
        }

        static bool ToBool(object value)
        {
            if (value == null)
            {
                return false;
            }

            if (value is bool b)
            {
                return b;
            }

            var text = value.ToString().Trim().ToLowerInvariant();
            return text == "true" || text == "1";
        }

        static double ToDouble(object value)
        {
            if (value == null)
            {
                return 0d;
            }

            if (value is IConvertible)
            {
                try
                {
                    return Convert.ToDouble(value, CultureInfo.InvariantCulture);
                }
                catch (Exception)
                {
                    return 0d;
                }
            }

            return 0d;
        }
    }
}
