using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tarinoi.Data;
using Tarinoi.Editor.Codegen;

namespace Tarinoi.Tests
{
    public class CodegenNamingTests
    {
        [TestCase("global", "Global")]
        [TestCase("player_state", "PlayerState")]
        [TestCase("my_col2", "MyCol2")]
        [TestCase("already Pascal", "AlreadyPascal")]
        [TestCase("kebab-case", "KebabCase")]
        [TestCase("dotted.name", "DottedName")]
        public void IdentifiersBecomePascalCase(string authored, string expected)
        {
            Assert.AreEqual(expected, CodeNames.ToPascal(authored));
        }

        [Test]
        public void NamesStartingWithADigitAreMadeLegal()
        {
            // C# identifiers cannot start with a digit, but authored ones can.
            StringAssert.StartsWith("_", CodeNames.ToPascal("2nd_chance"));
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("!!!")]
        [TestCase("___")]
        public void UnusableNamesFallBackRatherThanEmittingBrokenCode(string authored)
        {
            Assert.AreEqual("Unnamed", CodeNames.ToPascal(authored));
        }

        [Test]
        public void PascalCasedMembersNeverCollideWithKeywords()
        {
            // C# keywords are all lowercase, so PascalCasing already resolves the clash:
            // an authored "class" becomes the perfectly legal identifier "Class".
            Assert.AreEqual("Class", CodeNames.Member("class"));
            Assert.AreEqual("Event", CodeNames.Member("event"));
        }

        [Test]
        public void ParametersEscapeKeywords()
        {
            // Parameters are camelCased, so they do collide — and authored arguments
            // called "object" or "value" are entirely ordinary.
            Assert.AreEqual("@object", CodeNames.Parameter("object"));
            Assert.AreEqual("@string", CodeNames.Parameter("string"));
            Assert.AreEqual("@event", CodeNames.Parameter("event"));
            Assert.AreEqual("skill", CodeNames.Parameter("skill"));
            Assert.AreEqual("skillLevel", CodeNames.Parameter("skill_level"));

            // "value" is only contextually reserved, inside a property setter — it is a
            // legal parameter name and does not need escaping.
            Assert.AreEqual("value", CodeNames.Parameter("value"));
        }

        [Test]
        public void CollectionClassesAreNotPrefixed()
        {
            // Godot needs a "Tarinoi" prefix because its class-name table is flat.
            // C# namespaces make that unnecessary.
            Assert.AreEqual("GlobalFunctions", CodeNames.CollectionClass("global", "Functions"));
            Assert.AreEqual("PlayerStateVariables",
                CodeNames.CollectionClass("player_state", "Variables"));
        }

        [Test]
        public void StringLiteralsAreEscaped()
        {
            Assert.AreEqual("\"plain\"", CodeNames.StringLiteral("plain"));
            Assert.AreEqual("\"say \\\"hi\\\"\"", CodeNames.StringLiteral("say \"hi\""));
            Assert.AreEqual("\"back\\\\slash\"", CodeNames.StringLiteral("back\\slash"));
            Assert.AreEqual("\"line\\nbreak\"", CodeNames.StringLiteral("line\nbreak"));
        }
    }

    public class CodegenTypeTests
    {
        [TestCase("boolean", "bool")]
        [TestCase("number", "double")]
        [TestCase("string", "string")]
        [TestCase("BOOLEAN", "bool")]
        [TestCase("something_new", "object")]
        [TestCase("", "object")]
        public void DataTypesMapToCsharp(string authored, string expected)
        {
            Assert.AreEqual(expected, CodeTypes.ForData(authored));
        }

        [Test]
        public void NumbersBecomeDoubleNotFloat()
        {
            // Authored numbers arrive from JSON as doubles; narrowing would lose
            // precision silently.
            Assert.AreEqual("double", CodeTypes.ForData("number"));
        }

        [TestCase("void", "void")]
        [TestCase("", "void")]
        [TestCase("boolean", "bool")]
        [TestCase("string", "string")]
        public void ReturnTypesMapToCsharp(string authored, string expected)
        {
            Assert.AreEqual(expected, CodeTypes.ForReturn(authored));
        }

        [TestCase("void", "")]
        [TestCase("boolean", "false")]
        [TestCase("number", "0d")]
        [TestCase("string", "\"\"")]
        [TestCase("other", "null")]
        public void StubsReturnATypedDefault(string returns, string expected)
        {
            Assert.AreEqual(expected, CodeTypes.DefaultReturnLiteral(returns));
        }

        [Test]
        public void VariableDefaultsUseTheDeclaredValue()
        {
            Assert.AreEqual("true", CodeTypes.DefaultValueLiteral("boolean", true));
            Assert.AreEqual("false", CodeTypes.DefaultValueLiteral("boolean", false));
            Assert.AreEqual("3.5d", CodeTypes.DefaultValueLiteral("number", 3.5));
            Assert.AreEqual("\"hi\"", CodeTypes.DefaultValueLiteral("string", "hi"));
        }

        [Test]
        public void VariableDefaultsFallBackToTheTypesZero()
        {
            Assert.AreEqual("false", CodeTypes.DefaultValueLiteral("boolean", null));
            Assert.AreEqual("0d", CodeTypes.DefaultValueLiteral("number", null));
            Assert.AreEqual("\"\"", CodeTypes.DefaultValueLiteral("string", null));
            Assert.AreEqual("null", CodeTypes.DefaultValueLiteral("mystery", null));
        }

        [Test]
        public void ADefaultStringIsEscaped()
        {
            Assert.AreEqual("\"say \\\"hi\\\"\"", CodeTypes.DefaultValueLiteral("string", "say \"hi\""));
        }

        [Test]
        public void ANumericDefaultUsesInvariantFormatting()
        {
            // A comma decimal separator would emit code that does not compile.
            StringAssert.Contains(".", CodeTypes.DefaultValueLiteral("number", 1.5));
        }
    }

    public class CodeEmitterTests
    {
        static CodegenModel ModelWith(params FunctionDecl[] functions)
        {
            var model = new CodegenModel();
            model.Functions["global"] = functions.ToList();
            return model;
        }

        [Test]
        public void FunctionClassesAreGeneratedPerCollection()
        {
            var model = new CodegenModel();
            model.Functions["global"] = new List<FunctionDecl>
            {
                new FunctionDecl { Name = "IsReady", Returns = "boolean" },
            };
            model.Functions["combat"] = new List<FunctionDecl>
            {
                new FunctionDecl { Name = "Attack", Returns = "void" },
            };

            var code = CodeEmitter.Functions(model, "proj");

            StringAssert.Contains("public abstract partial class GlobalFunctions", code);
            StringAssert.Contains("public abstract partial class CombatFunctions", code);
            StringAssert.Contains("namespace Tarinoi.Generated", code);
        }

        [Test]
        public void FunctionStubsCarryTheirSignatureAndEffectInDocs()
        {
            var code = CodeEmitter.Functions(ModelWith(new FunctionDecl
            {
                Name = "Check",
                Args = new List<string> { "skill", "difficulty" },
                Returns = "boolean",
                Effect = "mutation",
            }), "proj");

            StringAssert.Contains("Check(skill, difficulty) -> boolean", code);
            StringAssert.Contains("Effect: mutation", code);
            StringAssert.Contains("public virtual bool Check(object skill, object difficulty)", code);
        }

        [Test]
        public void UnimplementedStubsLogRatherThanThrow()
        {
            var code = CodeEmitter.Functions(ModelWith(
                new FunctionDecl { Name = "Check", Returns = "boolean" }), "proj");

            StringAssert.Contains("TarinoiLog.Error", code);
            StringAssert.DoesNotContain("throw", code,
                "an unimplemented binding must not take down a running game");
            StringAssert.Contains("return false;", code);
        }

        [Test]
        public void VoidFunctionsReturnNothing()
        {
            var code = CodeEmitter.Functions(ModelWith(
                new FunctionDecl { Name = "Effect", Returns = "void" }), "proj");

            StringAssert.Contains("public virtual void Effect()", code);
            StringAssert.Contains("Effect();", code);
            StringAssert.DoesNotContain("result = Effect()", code);
        }

        [Test]
        public void DispatchIsASwitchNotReflection()
        {
            // Reflection-only call sites can be stripped by IL2CPP.
            var code = CodeEmitter.Functions(ModelWith(
                new FunctionDecl { Name = "Check", Returns = "boolean" }), "proj");

            StringAssert.Contains("public bool TryInvoke(string name, object[] args, out object result)", code);
            StringAssert.Contains("case \"Check\":", code);
            StringAssert.Contains("result = Check();", code);
            StringAssert.DoesNotContain("GetMethod", code);
        }

        [Test]
        public void DispatchChecksArgumentCount()
        {
            var code = CodeEmitter.Functions(ModelWith(new FunctionDecl
            {
                Name = "Check",
                Args = new List<string> { "a", "b" },
                Returns = "boolean",
            }), "proj");

            StringAssert.Contains("if (count != 2)", code);
            StringAssert.Contains("result = Check(args[0], args[1]);", code);
        }

        [Test]
        public void HasFunctionCoversEveryDeclaredName()
        {
            var code = CodeEmitter.Functions(ModelWith(
                new FunctionDecl { Name = "Alpha" },
                new FunctionDecl { Name = "Beta" }), "proj");

            StringAssert.Contains("public bool HasFunction(string name)", code);
            StringAssert.Contains("case \"Alpha\":", code);
            StringAssert.Contains("case \"Beta\":", code);
        }

        [Test]
        public void EmptyModelsStillProduceCompilableFiles()
        {
            var model = new CodegenModel();

            foreach (var code in new[]
                     {
                         CodeEmitter.Functions(model, "proj"),
                         CodeEmitter.Variables(model, "proj"),
                         CodeEmitter.Lists(model, "proj"),
                         CodeEmitter.Entities(model, "proj"),
                     })
            {
                StringAssert.Contains("namespace Tarinoi.Generated", code);
                Assert.AreEqual(CountOf(code, '{'), CountOf(code, '}'), "braces must balance");
            }
        }

        [Test]
        public void VariablesBecomeTypedFieldsWithAccessors()
        {
            var model = new CodegenModel();
            model.Variables["player"] = new List<VariableDecl>
            {
                new VariableDecl { Name = "health", DataType = "number", DefaultValue = 100.0 },
                new VariableDecl { Name = "has_key", DataType = "boolean" },
            };

            var code = CodeEmitter.Variables(model, "proj");

            StringAssert.Contains("public partial class PlayerVariables : ITarinoiVariables", code);
            StringAssert.Contains("public double Health = 100d;", code);
            StringAssert.Contains("public bool HasKey = false;", code);
            StringAssert.Contains("case \"health\": return Health;", code);
            StringAssert.Contains("Health = ValueConvert.ToDouble(value);", code);
        }

        [Test]
        public void VariableAccessorsKeyOnTheAuthoredName()
        {
            // Expressions say Var.player.has_key, not Var.player.HasKey.
            var model = new CodegenModel();
            model.Variables["player"] = new List<VariableDecl>
            {
                new VariableDecl { Name = "has_key", DataType = "boolean" },
            };

            var code = CodeEmitter.Variables(model, "proj");

            StringAssert.Contains("case \"has_key\":", code);
        }

        [Test]
        public void ListOptionKeysBecomeConstants()
        {
            var model = new CodegenModel();
            model.Lists["global"] = new List<ListDecl>
            {
                new ListDecl
                {
                    Identifier = "moods",
                    OptionKeys = new List<string> { "happy", "very_sad" },
                },
            };

            var code = CodeEmitter.Lists(model, "proj");

            StringAssert.Contains("public static class GlobalLists", code);
            StringAssert.Contains("public static class Moods", code);
            StringAssert.Contains("public const string Happy = \"happy\";", code);
            StringAssert.Contains("public const string VerySad = \"very_sad\";",
                code);
        }

        [Test]
        public void EntityIdentifiersBecomeConstants()
        {
            var model = new CodegenModel();
            model.Entities["cast"] = new List<EntityDecl>
            {
                new EntityDecl { Identifier = "narrator" },
            };

            var code = CodeEmitter.Entities(model, "proj");

            StringAssert.Contains("public static class CastEntities", code);
            StringAssert.Contains("public const string Narrator = \"narrator\";", code);
        }

        [Test]
        public void OutputIsDeterministic()
        {
            // These files are committed; a shuffling generator produces noisy diffs.
            var model = new CodegenModel();
            model.Functions["global"] = new List<FunctionDecl>
            {
                new FunctionDecl { Name = "Zulu" },
                new FunctionDecl { Name = "Alpha" },
            };

            var first = CodeEmitter.Functions(model, "proj");
            var second = CodeEmitter.Functions(model, "proj");

            Assert.AreEqual(first, second);
            Assert.Less(first.IndexOf("Alpha", System.StringComparison.Ordinal),
                first.IndexOf("Zulu", System.StringComparison.Ordinal),
                "members are emitted in sorted order");
        }

        [Test]
        public void TheHeaderCarriesNoTimestamp()
        {
            // A timestamp would make every regeneration a diff.
            var code = CodeEmitter.Functions(new CodegenModel(), "proj");

            StringAssert.Contains("auto-generated", code);
            StringAssert.DoesNotContain("Generated:", code);
        }

        [Test]
        public void KeywordNamesSurviveGeneration()
        {
            var code = CodeEmitter.Functions(ModelWith(new FunctionDecl
            {
                Name = "lock",
                Args = new List<string> { "object" },
            }), "proj");

            StringAssert.Contains("Lock(object @object)", code,
                "the parameter needs escaping; the PascalCased method name does not");
            StringAssert.Contains("case \"lock\":", code, "dispatch still keys on the authored name");
        }

        static int CountOf(string text, char c) => text.Count(ch => ch == c);
    }

    public class BindingCodegenLoadTests
    {
        TestDb _fixture;

        [SetUp]
        public void SetUp() => _fixture = new TestDb();

        [TearDown]
        public void TearDown() => _fixture.Dispose();

        void SeedManifest(string collectionId, string identifier)
        {
            _fixture.InsertDocument(collectionId, collectionId, documentType: "collection-manifest",
                identifier: identifier, payload: "{\"label\":\"Display Label\"}");
        }

        void SeedDecl(string documentType, string documentId, string identifier, string payload,
            string collectionId = "col1")
        {
            _fixture.InsertDocument(documentId, collectionId, documentType: documentType,
                identifier: identifier, payload: payload);
        }

        [Test]
        public void CollectionsAreKeyedOnTheMachineIdentifierNotTheLabel()
        {
            // Authored expressions say Fn.global.*, and bindings register under "global".
            // Keying on "Display Label" would produce classes nothing can bind to.
            SeedManifest("col1", "global");
            SeedDecl("function-declaration", "f1", "IsReady",
                "{\"function_returns\":\"boolean\",\"function_args\":[]}");

            var model = BindingCodegen.Load(_fixture.Db);

            CollectionAssert.AreEqual(new[] { "global" }, model.Functions.Keys.ToList());
        }

        [Test]
        public void FunctionArgumentsAndMetadataAreRead()
        {
            SeedManifest("col1", "global");
            SeedDecl("function-declaration", "f1", "Check",
                "{\"function_returns\":\"boolean\",\"effect\":\"mutation\","
                + "\"function_args\":[{\"arg_name\":\"skill\"},{\"arg_name\":\"dc\"}]}");

            var fn = BindingCodegen.Load(_fixture.Db).Functions["global"].Single();

            Assert.AreEqual("Check", fn.Name);
            Assert.AreEqual("boolean", fn.Returns);
            Assert.AreEqual("mutation", fn.Effect);
            CollectionAssert.AreEqual(new[] { "skill", "dc" }, fn.Args);
        }

        [Test]
        public void VariableDeclarationsAreRead()
        {
            SeedManifest("col1", "player");
            SeedDecl("variable-declaration", "v1", "health",
                "{\"data_type\":\"number\",\"default_value\":50}");

            var variable = BindingCodegen.Load(_fixture.Db).Variables["player"].Single();

            Assert.AreEqual("health", variable.Name);
            Assert.AreEqual("number", variable.DataType);
            Assert.AreEqual(50L, variable.DefaultValue);
        }

        [Test]
        public void ListSpecsUseTheKeyNotTheValue()
        {
            SeedManifest("col1", "global");
            SeedDecl("list-spec", "l1", "moods",
                "{\"list_options\":[{\"key\":\"happy\",\"option_value\":\"Cheerful\"}]}");

            var list = BindingCodegen.Load(_fixture.Db).Lists["global"].Single();

            CollectionAssert.AreEqual(new[] { "happy" }, list.OptionKeys,
                "constants name the key, since that is what expressions use");
        }

        [Test]
        public void ListSpecsFallBackToTheOlderOptionsField()
        {
            SeedManifest("col1", "global");
            SeedDecl("list-spec", "l1", "moods", "{\"options\":[{\"key\":\"legacy\"}]}");

            CollectionAssert.AreEqual(new[] { "legacy" },
                BindingCodegen.Load(_fixture.Db).Lists["global"].Single().OptionKeys);
        }

        [Test]
        public void OnlyDialogueCapableEntitiesAreGenerated()
        {
            SeedManifest("col1", "cast");
            SeedDecl("entity", "e1", "narrator", "{\"dialog_capable\":1}");
            SeedDecl("entity", "e2", "prop", "{\"dialog_capable\":0}");

            var entities = BindingCodegen.Load(_fixture.Db).Entities["cast"];

            CollectionAssert.AreEqual(new[] { "narrator" }, entities.Select(e => e.Identifier).ToList());
        }

        [Test]
        public void ArchivedDeclarationsAreExcluded()
        {
            SeedManifest("col1", "global");
            _fixture.InsertDocument("f1", "col1", documentType: "function-declaration",
                identifier: "Gone", archived: true, payload: "{}");

            Assert.IsFalse(BindingCodegen.Load(_fixture.Db).Functions.ContainsKey("global"));
        }

        [Test]
        public void DeclarationsWithoutAManifestAreSkipped()
        {
            // The join requires a manifest; without one there is no collection name to
            // bind against.
            SeedDecl("function-declaration", "f1", "Orphan", "{}");

            Assert.IsEmpty(BindingCodegen.Load(_fixture.Db).Functions);
        }

        [Test]
        public void AnUnreadablePayloadIsSkippedWithAWarning()
        {
            SeedManifest("col1", "global");
            SeedDecl("function-declaration", "f1", "Broken", "{not json");

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex("unreadable payload"));

            Assert.IsEmpty(BindingCodegen.Load(_fixture.Db).Functions);
        }

        [Test]
        public void AClosedDatabaseReportsRatherThanThrowing()
        {
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex("sync before generating"));

            Assert.IsTrue(BindingCodegen.Load(new TarinoiDb()).IsEmpty);
        }
    }
}
