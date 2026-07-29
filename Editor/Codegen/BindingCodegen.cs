using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Tarinoi.Data;

namespace Tarinoi.Editor.Codegen
{
    /// <summary>
    /// Reads synced declarations from the local database and writes the C# classes a
    /// game derives its bindings from.
    /// </summary>
    public static class BindingCodegen
    {
        /// <summary>
        /// Loads every declaration codegen cares about.
        /// </summary>
        /// <remarks>
        /// Each query joins the collection's own manifest to recover its machine
        /// identifier. That identifier — not the display label — is what authored
        /// expressions use and what bindings must be registered under, and confusing the
        /// two is a mistake this codebase has made before.
        /// </remarks>
        public static CodegenModel Load(TarinoiDb db)
        {
            var model = new CodegenModel();
            if (db == null || !db.IsOpen)
            {
                TarinoiLog.Error("Codegen: no synced content — sync before generating bindings.");
                return model;
            }

            foreach (var row in Query(db, "function-declaration"))
            {
                Add(model.Functions, row.Collection, new FunctionDecl
                {
                    Name = row.Identifier,
                    Args = ArgNames(row.Payload["function_args"] as JArray),
                    Returns = Str(row.Payload["function_returns"]),
                    Effect = Str(row.Payload["effect"]),
                });
            }

            foreach (var row in Query(db, "variable-declaration"))
            {
                model.Variables.TryGetValue(row.Collection, out _);
                Add(model.Variables, row.Collection, new VariableDecl
                {
                    Name = row.Identifier,
                    DataType = Str(row.Payload["data_type"]),
                    DefaultValue = (row.Payload["default_value"] as JValue)?.Value,
                });
            }

            foreach (var row in Query(db, "list-spec"))
            {
                var options = (row.Payload["list_options"] ?? row.Payload["options"]) as JArray;
                var keys = new List<string>();
                if (options != null)
                {
                    foreach (var option in options)
                    {
                        var key = Str(option["key"]);
                        if (key.Length > 0)
                        {
                            keys.Add(key);
                        }
                    }
                }

                Add(model.Lists, row.Collection, new ListDecl
                {
                    Identifier = row.Identifier,
                    OptionKeys = keys,
                });
            }

            // Only dialogue-capable entities are worth a constant.
            foreach (var row in Query(db, "entity", "AND json_extract(d.payload, '$.dialog_capable') = 1"))
            {
                Add(model.Entities, row.Collection, new EntityDecl { Identifier = row.Identifier });
            }

            return model;
        }

        /// <summary>
        /// Writes the four generated files. Returns false if nothing could be written.
        /// </summary>
        /// <param name="withAsmdef">
        /// Whether to write an assembly definition beside the generated code. Needed by
        /// projects that organise their own code with asmdefs: without one the generated
        /// classes land in the predefined <c>Assembly-CSharp</c>, which no asmdef-based
        /// assembly is allowed to reference. Off by default, because turning it on moves
        /// the generated types out of <c>Assembly-CSharp</c> and would break a project
        /// that keeps its code there.
        /// </param>
        public static bool Write(CodegenModel model, string outputDirectory, string projectId,
            bool withAsmdef = false)
        {
            try
            {
                Directory.CreateDirectory(outputDirectory);

                File.WriteAllText(Path.Combine(outputDirectory, CodeEmitter.FunctionsFile),
                    CodeEmitter.Functions(model, projectId));
                File.WriteAllText(Path.Combine(outputDirectory, CodeEmitter.VariablesFile),
                    CodeEmitter.Variables(model, projectId));
                File.WriteAllText(Path.Combine(outputDirectory, CodeEmitter.ListsFile),
                    CodeEmitter.Lists(model, projectId));
                File.WriteAllText(Path.Combine(outputDirectory, CodeEmitter.EntitiesFile),
                    CodeEmitter.Entities(model, projectId));

                var asmdefPath = Path.Combine(outputDirectory, CodeEmitter.AsmdefFile);
                if (withAsmdef)
                {
                    File.WriteAllText(asmdefPath, CodeEmitter.Asmdef());
                }
                else if (File.Exists(asmdefPath))
                {
                    // Turning the setting back off has to remove it, or the assembly
                    // stays split and the reason is invisible.
                    File.Delete(asmdefPath);
                }

                return true;
            }
            catch (Exception e)
            {
                TarinoiLog.Error($"Codegen: could not write to '{outputDirectory}': {e.Message}");
                return false;
            }
        }

        // -------------------------------------------------------------------------

        struct DeclRow
        {
            public string Identifier;
            public string Collection;
            public JObject Payload;
        }

        class RawDeclRow
        {
            [SQLite.Column("identifier")] public string Identifier { get; set; }
            [SQLite.Column("col_name")] public string ColName { get; set; }
            [SQLite.Column("payload")] public string Payload { get; set; }
        }

        static IEnumerable<DeclRow> Query(TarinoiDb db, string documentType, string extraFilter = "")
        {
            var rows = db.Query<RawDeclRow>(
                $@"SELECT d.identifier, d.payload, cm.identifier AS col_name
                   FROM documents d
                   JOIN documents cm
                     ON cm.document_id = d.collection_id
                    AND cm.document_type = 'collection-manifest'
                   WHERE d.document_type = '{documentType}'
                     {extraFilter}
                     AND {db.ActiveFilter}
                   ORDER BY col_name, d.identifier");

            var results = new List<DeclRow>();
            foreach (var row in rows)
            {
                if (string.IsNullOrEmpty(row.ColName) || string.IsNullOrEmpty(row.Identifier))
                {
                    continue;
                }

                JObject payload;
                try
                {
                    payload = JObject.Parse(row.Payload ?? "{}");
                }
                catch (Exception)
                {
                    TarinoiLog.Warn($"Codegen: '{row.Identifier}' has an unreadable payload — skipping it.");
                    continue;
                }

                results.Add(new DeclRow
                {
                    Identifier = row.Identifier,
                    Collection = row.ColName,
                    Payload = payload,
                });
            }

            return results;
        }

        static void Add<T>(IDictionary<string, List<T>> target, string collection, T item)
        {
            if (!target.TryGetValue(collection, out var list))
            {
                list = new List<T>();
                target[collection] = list;
            }

            list.Add(item);
        }

        static List<string> ArgNames(JArray args)
        {
            var names = new List<string>();
            if (args == null)
            {
                return names;
            }

            foreach (var arg in args)
            {
                var name = Str(arg["arg_name"]);
                if (name.Length > 0)
                {
                    names.Add(name);
                }
            }

            return names;
        }

        static string Str(JToken token) =>
            token == null || token.Type == JTokenType.Null ? "" : token.ToString();
    }
}
