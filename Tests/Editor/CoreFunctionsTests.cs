using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Tarinoi.Bindings;
using Tarinoi.Editor.Codegen;
using Tarinoi.Tests.Fixtures;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tarinoi.Tests
{
    /// <summary>
    /// The core functions scaffold. <c>Fixtures/TarinoiCoreFunctions.cs</c> is the emitter's
    /// output, checked in so it compiles into this assembly: one test holds the emitter to
    /// it byte for byte, and the rest call it. The behavioural cases follow
    /// CoreFunctions.spec.js in the Tarinoi app.
    /// </summary>
    public class CoreFunctionsEmitterTests
    {
        const string FixturePath = "Packages/com.tarinoi.unity/Tests/Editor/Fixtures/TarinoiCoreFunctions.cs";
        const string FixtureNamespace = "Tarinoi.Tests.Fixtures";

        /// <summary>The thirteen declarations, as codegen loads them from a synced project.</summary>
        internal static List<FunctionDecl> CoreDecls()
        {
            var decls = new List<FunctionDecl>();
            foreach (var name in CoreFunctionsEmitter.Order)
            {
                var predicate = name == "FlagIsSet" || name.StartsWith("Number") || name.StartsWith("String");
                var args = name switch
                {
                    "SetCounter" => new List<string> { "counterRef", "value" },
                    "IncrementCounter" => new List<string> { "counterRef", "delta" },
                    "SetText" => new List<string> { "textRef", "value" },
                    _ when predicate && name != "FlagIsSet" => new List<string> { "a", "b" },
                    _ => new List<string> { "flagRef" },
                };
                decls.Add(new FunctionDecl
                {
                    Name = name, Args = args, Returns = predicate ? "boolean" : "void",
                    Effect = predicate ? "pure" : "mutation",
                });
            }

            // Codegen hands them over sorted by identifier, not in author order.
            return decls.OrderBy(d => d.Name, StringComparer.Ordinal).ToList();
        }

        static string RenderFixture(out List<string> unknown) =>
            CoreFunctionsEmitter.Render(CoreDecls(), "fixture", out unknown,
                baseNamespace: FixtureNamespace, baseClass: "TarinoiFunctions", ns: FixtureNamespace);

        [Test]
        public void EmitterReproducesTheCompiledFixtureByteForByte()
        {
            var expected = File.ReadAllText(Path.GetFullPath(FixturePath)).Replace("\r\n", "\n");
            var actual = RenderFixture(out _);

            if (expected != actual)
            {
                // Make the fix a copy rather than a transcription.
                var dump = Path.GetFullPath("Temp/TarinoiCoreFunctions.actual.cs");
                File.WriteAllText(dump, actual);
                Assert.Fail($"The emitter's output differs from the fixture. Actual output written to {dump}; "
                            + "copy it over the fixture if the change is intended.");
            }
        }

        [Test]
        public void DefaultsTargetTheGeneratedBaseInTheGlobalNamespace()
        {
            var code = CoreFunctionsEmitter.Render(CoreDecls(), "proj", out var unknown);

            Assert.IsEmpty(unknown);
            StringAssert.StartsWith(CoreFunctionsEmitter.StampPrefix + CoreFunctionsEmitter.Version, code);
            StringAssert.Contains("using Tarinoi.Generated;", code);
            StringAssert.Contains("\n[Preserve]\npublic class TarinoiCoreFunctions : TarinoiFunctions\n", code);
            StringAssert.DoesNotContain("namespace ", code);
            StringAssert.Contains("public const string ScaffoldVersion = \"" + CoreFunctionsEmitter.Version + "\";", code);
        }

        [Test]
        public void FunctionsAreEmittedInAuthorOrder()
        {
            var code = CoreFunctionsEmitter.Render(CoreDecls(), "proj", out _);

            var last = -1;
            foreach (var name in CoreFunctionsEmitter.Order)
            {
                var at = code.IndexOf($" {name}(", StringComparison.Ordinal);
                Assert.Greater(at, last, $"{name} follows its predecessor");
                last = at;
            }
        }

        [Test]
        public void AFunctionThePackageDoesNotKnowGetsAStub()
        {
            var decls = CoreDecls();
            decls.Add(new FunctionDecl { Name = "ResetEverything", Returns = "void" });

            var code = CoreFunctionsEmitter.Render(decls, "proj", out var unknown);

            CollectionAssert.AreEqual(new[] { "ResetEverything" }, unknown);
            StringAssert.Contains("public override void ResetEverything()", code);
            StringAssert.Contains("TarinoiLog.Error(\"TarinoiCoreFunctions.ResetEverything is not implemented.\");", code);
        }

        [Test]
        public void AKnownNameWithTheWrongArityGetsAStub()
        {
            var decls = CoreDecls().Where(d => d.Name != "SetFlag").ToList();
            decls.Add(new FunctionDecl { Name = "SetFlag", Args = { "flagRef", "value" }, Returns = "void" });

            var code = CoreFunctionsEmitter.Render(decls, "proj", out var unknown);

            CollectionAssert.AreEqual(new[] { "SetFlag" }, unknown);
            StringAssert.Contains("public override void SetFlag(object flagRef, object value)", code);
            StringAssert.DoesNotContain("Write(flagRef, true)", code);
        }

        [Test]
        public void ScaffoldIsWrittenOnceAndNeverOverwritten()
        {
            var model = new CodegenModel();
            model.Functions["tarinoi"] = CoreDecls();
            var dir = Path.Combine(Path.GetTempPath(), "tarinoi-core-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(dir, CoreFunctionsEmitter.FileName);

            try
            {
                Assert.IsTrue(BindingCodegen.ScaffoldCoreFunctions(model, dir, "proj"));
                Assert.IsTrue(File.Exists(path));

                File.WriteAllText(path, "// my edits\n");
                Assert.IsFalse(BindingCodegen.ScaffoldCoreFunctions(model, dir, "proj"));
                Assert.AreEqual("// my edits\n", File.ReadAllText(path), "the game's file is left alone");
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }

        [Test]
        public void NoScaffoldWithoutTheCoreCollection()
        {
            var model = new CodegenModel();
            model.Functions["global"] = new List<FunctionDecl> { new FunctionDecl { Name = "Mine" } };
            var dir = Path.Combine(Path.GetTempPath(), "tarinoi-core-" + Guid.NewGuid().ToString("N"));

            Assert.IsFalse(BindingCodegen.ScaffoldCoreFunctions(model, dir, "proj"));
            Assert.IsFalse(Directory.Exists(dir));
        }

        [Test]
        public void CompiledVersionAndOverridesAreReadFromTheType()
        {
            var type = typeof(TarinoiCoreFunctions);

            Assert.AreEqual(CoreFunctionsEmitter.Version, CoreFunctionsEmitter.CompiledVersion(type));
            Assert.IsTrue(CoreFunctionsEmitter.Overrides(type, "SetFlag"));
            Assert.IsFalse(CoreFunctionsEmitter.Overrides(type, "Nope"));
            Assert.AreEqual("", CoreFunctionsEmitter.CompiledVersion(typeof(object)));
        }
    }

    /// <summary>
    /// Runs the compiled scaffold. Every case here is one from CoreFunctions.spec.js, plus
    /// the package's own rule that a mismatch logs and degrades rather than throws.
    /// </summary>
    public class CoreFunctionsBehaviourTests
    {
        /// <summary>A store where an unset variable reads as null — what the defaults are about.</summary>
        class FakeVariables : ITarinoiVariables
        {
            public readonly Dictionary<string, object> Values = new Dictionary<string, object>();
            public object GetVariable(string name) => Values.TryGetValue(name, out var v) ? v : null;
            public void SetVariable(string name, object value) => Values[name] = value;
        }

        FakeVariables _vars;
        TarinoiCoreFunctions _core;

        [SetUp]
        public void SetUp()
        {
            _vars = new FakeVariables();
            _core = new TarinoiCoreFunctions();
        }

        VarRef Ref(string name) => new VarRef(_vars, "global", name);

        static void ExpectError(string fragment) =>
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(fragment)));

        // -- Flags: "sets, clears and toggles, treating an unset flag as clear" ------------

        [Test]
        public void FlagIsSetReadsUnsetAsFalse()
        {
            Assert.IsFalse(_core.FlagIsSet(Ref("met")));
        }

        [Test]
        public void SetAndClearFlag()
        {
            _core.SetFlag(Ref("met"));
            Assert.AreEqual(true, _vars.Values["met"]);
            Assert.IsTrue(_core.FlagIsSet(Ref("met")));

            _core.ClearFlag(Ref("met"));
            Assert.AreEqual(false, _vars.Values["met"]);
            Assert.IsFalse(_core.FlagIsSet(Ref("met")));
        }

        [Test]
        public void ToggleFlagSetsAnUnsetFlagThenClearsIt()
        {
            _core.ToggleFlag(Ref("met"));
            Assert.AreEqual(true, _vars.Values["met"]);
            _core.ToggleFlag(Ref("met"));
            Assert.AreEqual(false, _vars.Values["met"]);
        }

        [Test]
        public void AMutatorWithoutAReferenceLogsAndDoesNothing()
        {
            ExpectError("expected a Var.* reference");
            _core.SetFlag(true);
            Assert.IsEmpty(_vars.Values);
        }

        // -- Counters and text: "increments from 0 when unset, and by a negative delta";
        //    "takes a value from a literal, a variable, or a list item" -------------------

        [Test]
        public void IncrementCounterStartsFromZeroAndTakesNegativeDeltas()
        {
            _core.IncrementCounter(Ref("morale"), 1L);
            Assert.AreEqual(1d, _vars.Values["morale"]);
            _core.IncrementCounter(Ref("morale"), -3d);
            Assert.AreEqual(-2d, _vars.Values["morale"]);
        }

        [Test]
        public void SetCounterFromALiteralAVariableAndAListItem()
        {
            _core.SetCounter(Ref("morale"), 5L);
            Assert.AreEqual(5d, _vars.Values["morale"]);

            _vars.Values["starting"] = 7d;
            _core.SetCounter(Ref("morale"), Ref("starting"));
            Assert.AreEqual(7d, _vars.Values["morale"]);

            // A list item reaches the function already resolved to its option value.
            _core.SetCounter(Ref("morale"), 2.5d);
            Assert.AreEqual(2.5d, _vars.Values["morale"]);
        }

        [Test]
        public void SetTextFromALiteralAndAVariable()
        {
            _core.SetText(Ref("title"), "Ferry Captain");
            Assert.AreEqual("Ferry Captain", _vars.Values["title"]);

            _vars.Values["rank"] = "Admiral";
            _core.SetText(Ref("title"), Ref("rank"));
            Assert.AreEqual("Admiral", _vars.Values["title"]);
        }

        [Test]
        public void AValueOfTheWrongTypeLogsAndUsesTheDefault()
        {
            // Playback rejects these outright; the game logs and carries on with the
            // type's default rather than crash mid-dialogue.
            ExpectError("expected a number");
            _core.SetCounter(Ref("morale"), "lots");
            Assert.AreEqual(0d, _vars.Values["morale"]);

            ExpectError("expected a string");
            _core.SetText(Ref("title"), 42L);
            Assert.AreEqual("", _vars.Values["title"]);

            ExpectError("expected a boolean");
            _vars.Values["flag"] = "yes";
            Assert.IsFalse(_core.FlagIsSet(Ref("flag")));
        }

        // -- Comparisons: "treats unset counters as 0 and unset text as empty";
        //    "compares any mix of literals, variables and list items" -------------------

        [Test]
        public void AnUnsetCounterComparesAsZero()
        {
            Assert.IsTrue(_core.NumberEquals(Ref("nothing"), 0L));
            Assert.IsTrue(_core.NumberAtMost(Ref("nothing"), 0d));
            Assert.IsFalse(_core.NumberGreaterThan(Ref("nothing"), 0L));
        }

        [Test]
        public void UnsetTextComparesAsEmpty()
        {
            Assert.IsTrue(_core.StringEquals(Ref("nothing"), ""));
            Assert.IsFalse(_core.StringEquals(Ref("nothing"), "x"));
        }

        [Test]
        public void NumericComparatorsOverAMixOfSources()
        {
            _vars.Values["strength"] = 12d;
            _vars.Values["hard"] = 12d;

            Assert.IsTrue(_core.NumberAtLeast(Ref("strength"), Ref("hard")));
            Assert.IsTrue(_core.NumberAtLeast(Ref("strength"), 12L));
            Assert.IsFalse(_core.NumberGreaterThan(Ref("strength"), 12d));
            Assert.IsTrue(_core.NumberGreaterThan(13L, Ref("strength")));
            Assert.IsTrue(_core.NumberAtMost(Ref("strength"), 12L));
            Assert.IsTrue(_core.NumberLessThan(11.5d, Ref("strength")));
            Assert.IsFalse(_core.NumberLessThan(Ref("strength"), Ref("hard")));
            Assert.IsTrue(_core.NumberEquals(1L, 1d), "long and double literals compare as numbers");
        }

        [Test]
        public void StringEqualsOverAMixOfSources()
        {
            _vars.Values["faction"] = "rebels";

            Assert.IsTrue(_core.StringEquals(Ref("faction"), "rebels"));
            Assert.IsTrue(_core.StringEquals("rebels", Ref("faction")));
            Assert.IsFalse(_core.StringEquals(Ref("faction"), "empire"));
        }

        [Test]
        public void MismatchedOperandTypesLogRatherThanCoerce()
        {
            // "rejects mismatched operand types rather than coercing": "1" is not 1.
            ExpectError("expected a number");
            Assert.IsFalse(_core.NumberEquals("1", 1L));
        }

        [Test]
        public void DispatchesThroughTheGeneratedSwitch()
        {
            // The scaffold is reached the way the runtime reaches every binding.
            ITarinoiFunctions functions = _core;

            Assert.IsTrue(functions.TryInvoke("SetFlag", new object[] { Ref("met") }, out _));
            Assert.IsTrue(functions.TryInvoke("FlagIsSet", new object[] { Ref("met") }, out var result));
            Assert.AreEqual(true, result);
            Assert.IsFalse(functions.TryInvoke("Nope", new object[0], out _));
        }
    }

    public class GeneratedBindingsTests
    {
        const string FixtureNamespace = "Tarinoi.Tests.Fixtures";

        static readonly Type[] FixtureTypes =
        {
            typeof(TarinoiFunctions), typeof(TarinoiCoreFunctions), typeof(GlobalVariables), typeof(object),
        };

        [Test]
        public void BindsTheGeneratedVariablesAndTheScaffoldWhenUnbound()
        {
            var registry = new BindingRegistry();

            var bound = GeneratedBindings.BindDefaults(registry, FixtureTypes, FixtureNamespace);

            CollectionAssert.AreEquivalent(
                new[] { "Var.global → GlobalVariables", "Fn.tarinoi → TarinoiCoreFunctions" }, bound);
            Assert.IsInstanceOf<GlobalVariables>(registry.GetVariables("global"));
            Assert.IsInstanceOf<TarinoiCoreFunctions>(registry.GetFunctions("tarinoi"));
        }

        [Test]
        public void LeavesExplicitBindingsAlone()
        {
            var registry = new BindingRegistry();
            var mine = new GlobalVariables();
            var myCore = new TarinoiCoreFunctions();
            registry.BindVariables("global", mine);
            registry.BindFunctions("tarinoi", myCore);

            var bound = GeneratedBindings.BindDefaults(registry, FixtureTypes, FixtureNamespace);

            Assert.IsEmpty(bound);
            Assert.AreSame(mine, registry.GetVariables("global"));
            Assert.AreSame(myCore, registry.GetFunctions("tarinoi"));
        }

        [Test]
        public void FindsTheScaffoldByItsBaseNotItsName()
        {
            Assert.AreEqual(typeof(TarinoiCoreFunctions),
                GeneratedBindings.FindCoreFunctionsScaffold(FixtureTypes, FixtureNamespace));
            Assert.IsNull(GeneratedBindings.FindCoreFunctionsScaffold(new[] { typeof(object) }, FixtureNamespace));
        }
    }
}
