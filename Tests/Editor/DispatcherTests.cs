using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Tarinoi.Bindings;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tarinoi.Tests
{
    public class DispatcherTests
    {
        /// <summary>
        /// A hand-written function collection. Bound reflectively, which also exercises
        /// <see cref="ReflectionFunctions"/> — the path most users start on.
        /// </summary>
        class FakeFunctions
        {
            public readonly List<string> Calls = new List<string>();
            public readonly List<object[]> LastArgs = new List<object[]>();

            public bool AlwaysTrue()
            {
                Calls.Add(nameof(AlwaysTrue));
                return true;
            }

            public bool AlwaysFalse()
            {
                Calls.Add(nameof(AlwaysFalse));
                return false;
            }

            public string ReturnString()
            {
                Calls.Add(nameof(ReturnString));
                return "pin_a";
            }

            public object ReturnNull()
            {
                Calls.Add(nameof(ReturnNull));
                return null;
            }

            public object RecordArgs(object a, object b)
            {
                Calls.Add(nameof(RecordArgs));
                LastArgs.Add(new[] { a, b });
                return true;
            }

            public object Echo(object value)
            {
                Calls.Add(nameof(Echo));
                LastArgs.Add(new[] { value });
                return value;
            }

            public bool ReadVar(object reference)
            {
                Calls.Add(nameof(ReadVar));
                return (bool)VarRef.Resolve(reference);
            }

            public void WriteVar(object reference)
            {
                Calls.Add(nameof(WriteVar));
                ((VarRef)reference).Value = true;
            }

            public void Throws()
            {
                Calls.Add(nameof(Throws));
                throw new System.InvalidOperationException("game code blew up");
            }
        }

        class FakeVariables : ITarinoiVariables
        {
            public readonly Dictionary<string, object> Values = new Dictionary<string, object>();

            public object GetVariable(string name) =>
                Values.TryGetValue(name, out var v) ? v : null;

            public void SetVariable(string name, object value) => Values[name] = value;
        }

        class FakeEntities : ITarinoiEntities
        {
            public readonly Dictionary<string, object> Entities = new Dictionary<string, object>();

            public object GetEntity(string name) =>
                Entities.TryGetValue(name, out var e) ? e : null;
        }

        BindingRegistry _registry;
        Dispatcher _dispatcher;
        FakeFunctions _functions;
        FakeVariables _variables;
        FakeEntities _entities;

        [SetUp]
        public void SetUp()
        {
            _registry = new BindingRegistry();
            _functions = new FakeFunctions();
            _variables = new FakeVariables();
            _entities = new FakeEntities();

            _registry.BindFunctions("global", (object)_functions);
            _registry.BindVariables("global", _variables);
            _registry.BindEntities("cast", _entities);

            _dispatcher = new Dispatcher(_registry);
        }

        // -------------------------------------------------------------------
        // Boolean logic
        // -------------------------------------------------------------------

        [Test]
        public void AnEmptyConditionPasses()
        {
            Assert.IsTrue(_dispatcher.EvalCondition(""));
            Assert.IsTrue(_dispatcher.EvalCondition(null));
        }

        [TestCase("true", true)]
        [TestCase("false", false)]
        [TestCase("!true", false)]
        [TestCase("!false", true)]
        [TestCase("true && true", true)]
        [TestCase("true && false", false)]
        [TestCase("false || true", true)]
        [TestCase("false || false", false)]
        [TestCase("true || false && false", true)]
        [TestCase("(true || false) && false", false)]
        public void BooleanAlgebra(string expr, bool expected)
        {
            Assert.AreEqual(expected, _dispatcher.EvalCondition(expr));
        }

        [Test]
        public void AndShortCircuits()
        {
            // Authored functions have side effects, so skipping the right side matters.
            Assert.IsFalse(_dispatcher.EvalCondition("Fn.global.AlwaysFalse() && Fn.global.AlwaysTrue()"));
            CollectionAssert.AreEqual(new[] { "AlwaysFalse" }, _functions.Calls);
        }

        [Test]
        public void OrShortCircuits()
        {
            Assert.IsTrue(_dispatcher.EvalCondition("Fn.global.AlwaysTrue() || Fn.global.AlwaysFalse()"));
            CollectionAssert.AreEqual(new[] { "AlwaysTrue" }, _functions.Calls);
        }

        [Test]
        public void AnUnparseableConditionPasses()
        {
            LogAssert.Expect(LogType.Error, new Regex("ExpressionParser:"));
            Assert.IsTrue(_dispatcher.EvalCondition("Fn.global.Broken("),
                "a broken condition must not silently hide content");
        }

        // -------------------------------------------------------------------
        // Argument marshalling
        // -------------------------------------------------------------------

        [Test]
        public void LiteralArgumentsArriveWithTheirTypes()
        {
            _dispatcher.EvalCondition("Fn.global.RecordArgs(42, \"text\")");

            Assert.AreEqual(42L, _functions.LastArgs[0][0]);
            Assert.AreEqual("text", _functions.LastArgs[0][1]);
        }

        [Test]
        public void FloatArgumentsArriveAsDoubles()
        {
            _dispatcher.EvalCondition("Fn.global.RecordArgs(1.5, false)");

            Assert.AreEqual(1.5, (double)_functions.LastArgs[0][0], 1e-9);
            Assert.AreEqual(false, _functions.LastArgs[0][1]);
        }

        [Test]
        public void NestedCallResultsArePassedAsArguments()
        {
            _dispatcher.EvalCondition("Fn.global.Echo(Fn.global.ReturnString())");
            Assert.AreEqual("pin_a", _functions.LastArgs[0][0]);
        }

        // -------------------------------------------------------------------
        // Variables
        // -------------------------------------------------------------------

        [Test]
        public void VariableArgumentsArriveAsUnresolvedReferences()
        {
            // The function decides whether to read or write, so it must receive the
            // reference rather than the value.
            _variables.Values["hp"] = 10L;
            _dispatcher.EvalCondition("Fn.global.Echo(Var.global.hp)");

            var reference = (VarRef)_functions.LastArgs[0][0];
            Assert.AreEqual("global", reference.Collection);
            Assert.AreEqual("hp", reference.Name);
            Assert.AreEqual(10L, reference.Value);
        }

        [Test]
        public void FunctionsCanReadThroughAReference()
        {
            _variables.Values["flag"] = true;
            Assert.IsTrue(_dispatcher.EvalCondition("Fn.global.ReadVar(Var.global.flag)"));
        }

        [Test]
        public void FunctionsCanWriteThroughAReference()
        {
            _dispatcher.EvalCondition("Fn.global.WriteVar(Var.global.flag)");
            Assert.AreEqual(true, _variables.Values["flag"]);
        }

        [Test]
        public void ResolvePassesPlainValuesThrough()
        {
            Assert.AreEqual(7, VarRef.Resolve(7));
            Assert.IsNull(VarRef.Resolve(null));
        }

        [Test]
        public void ABareVariableConditionReadsTheVariable()
        {
            // A deliberate departure from the Godot plugin, where the unresolved
            // reference object is itself truthy and such conditions are always true.
            _variables.Values["flag"] = false;
            Assert.IsFalse(_dispatcher.EvalCondition("Var.global.flag"),
                "a bare variable condition must read the variable, not test the reference");

            _variables.Values["flag"] = true;
            Assert.IsTrue(_dispatcher.EvalCondition("Var.global.flag"));
        }

        // -------------------------------------------------------------------
        // Entities, lists, context card
        // -------------------------------------------------------------------

        [Test]
        public void EntityReferencesResolveThroughTheBinding()
        {
            _entities.Entities["narrator"] = "the-narrator";
            _dispatcher.EvalCondition("Fn.global.Echo(Ent.cast.narrator)");
            Assert.AreEqual("the-narrator", _functions.LastArgs[0][0]);
        }

        [Test]
        public void ListOptionsResolveByKey()
        {
            SetLists("global/moods", "{'key':'happy','option_value':'Cheerful'}",
                "{'key':'sad','option_value':'Glum'}");

            Assert.AreEqual("Cheerful", _dispatcher.EvalValue("Ls.global.moods.happy"));
            Assert.AreEqual("Glum", _dispatcher.EvalValue("Ls.global.moods.sad"));
        }

        [Test]
        public void ListOptionsFallBackToTheOlderValueField()
        {
            SetLists("global/moods", "{'key':'happy','value':'legacy'}");
            Assert.AreEqual("legacy", _dispatcher.EvalValue("Ls.global.moods.happy"));
        }

        [Test]
        public void OptionValueWinsOverValue()
        {
            SetLists("global/moods", "{'key':'happy','option_value':'new','value':'old'}");
            Assert.AreEqual("new", _dispatcher.EvalValue("Ls.global.moods.happy"));
        }

        [Test]
        public void AnUnknownListWarnsAndResolvesToNull()
        {
            LogAssert.Expect(LogType.Warning, new Regex("no list 'global.nope'"));
            Assert.IsNull(_dispatcher.EvalValue("Ls.global.nope.key"));
        }

        [Test]
        public void AnUnknownListKeyWarnsAndResolvesToNull()
        {
            SetLists("global/moods", "{'key':'happy','option_value':'Cheerful'}");
            LogAssert.Expect(LogType.Warning, new Regex("has no option 'furious'"));
            Assert.IsNull(_dispatcher.EvalValue("Ls.global.moods.furious"));
        }

        [Test]
        public void CardReferencesResolveToTheContextCard()
        {
            var card = JObject.Parse("{\"document_id\":\"card1\"}");
            _dispatcher.SetContextCard(card);

            Assert.AreSame(card, _dispatcher.EvalValue("Card.CurrentContextCard"));
        }

        [Test]
        public void CardReferencesResolveToAnEmptyObjectByDefault()
        {
            var value = (JObject)_dispatcher.EvalValue("Card.CurrentContextCard");
            Assert.IsNotNull(value);
            Assert.IsEmpty(value);
        }

        // -------------------------------------------------------------------
        // Unbound and missing
        // -------------------------------------------------------------------

        [Test]
        public void AnUnboundFunctionCollectionLogsAndEvaluatesFalse()
        {
            LogAssert.Expect(LogType.Error, new Regex("no bindings registered for function collection 'nope'"));
            Assert.IsFalse(_dispatcher.EvalCondition("Fn.nope.Anything()"));
        }

        [Test]
        public void AMissingFunctionLogsAndEvaluatesFalse()
        {
            LogAssert.Expect(LogType.Error, new Regex("'global.NotThere' is not implemented"));
            Assert.IsFalse(_dispatcher.EvalCondition("Fn.global.NotThere()"));
        }

        [Test]
        public void AnUnboundVariableCollectionLogsAndResolvesToNull()
        {
            LogAssert.Expect(LogType.Error, new Regex("variable collection 'nope'"));
            Assert.IsNull(_dispatcher.EvalValue("Var.nope.thing"));
        }

        [Test]
        public void AnUnboundEntityCollectionLogsAndResolvesToNull()
        {
            LogAssert.Expect(LogType.Error, new Regex("entity collection 'nope'"));
            Assert.IsNull(_dispatcher.EvalValue("Ent.nope.thing"));
        }

        [Test]
        public void AnExceptionInGameCodeIsContainedAndLogged()
        {
            LogAssert.Expect(LogType.Error, new Regex("game code blew up"));
            Assert.IsFalse(_dispatcher.EvalCondition("Fn.global.Throws()"),
                "a throwing binding must not propagate out of the dialogue system");
        }

        [Test]
        public void AWrongArgumentCountIsReportedActionably()
        {
            LogAssert.Expect(LogType.Error, new Regex("takes 2 argument"));
            _dispatcher.EvalCondition("Fn.global.RecordArgs(1)");
        }

        // -------------------------------------------------------------------
        // EvalCall / EvalValue / HasCall
        // -------------------------------------------------------------------

        [Test]
        public void EvalCallReturnsTheFunctionResult()
        {
            Assert.AreEqual("pin_a", _dispatcher.EvalCall("Fn.global.ReturnString()"));
        }

        [Test]
        public void EvalCallOnAnEmptyExpressionReturnsEmptyString()
        {
            Assert.AreEqual("", _dispatcher.EvalCall(""));
        }

        [Test]
        public void EvalValueDoesNotCoerceToBool()
        {
            Assert.AreEqual("pin_a", _dispatcher.EvalValue("Fn.global.ReturnString()"));
            Assert.IsNull(_dispatcher.EvalValue(""));
        }

        [Test]
        public void HasCallDistinguishesBoundFromUnbound()
        {
            Assert.IsTrue(_dispatcher.HasCall("Fn.global.AlwaysTrue()"));
            Assert.IsFalse(_dispatcher.HasCall("Fn.global.NotThere()"));
            Assert.IsFalse(_dispatcher.HasCall("Fn.nope.Anything()"));
            Assert.IsFalse(_dispatcher.HasCall(""));
        }

        [Test]
        public void HasCallDoesNotInvokeTheFunction()
        {
            _dispatcher.HasCall("Fn.global.AlwaysTrue()");
            CollectionAssert.IsEmpty(_functions.Calls, "checking must have no side effects");
        }

        [Test]
        public void HasCallOnAMalformedExpressionIsFalse()
        {
            LogAssert.Expect(LogType.Error, new Regex("ExpressionParser:"));
            Assert.IsFalse(_dispatcher.HasCall("not a call"));
        }

        // -------------------------------------------------------------------
        // Caching
        // -------------------------------------------------------------------

        [Test]
        public void RepeatedEvaluationIsStable()
        {
            for (var i = 0; i < 3; i++)
            {
                Assert.IsTrue(_dispatcher.EvalCondition("Fn.global.AlwaysTrue()"));
            }

            Assert.AreEqual(3, _functions.Calls.Count, "caching the parse must not cache the result");
        }

        [Test]
        public void AParseFailureIsReportedOnlyOnce()
        {
            LogAssert.Expect(LogType.Error, new Regex("ExpressionParser:"));

            _dispatcher.EvalCondition("Fn.global.Broken(");
            _dispatcher.EvalCondition("Fn.global.Broken(");
            _dispatcher.EvalCondition("Fn.global.Broken(");

            LogAssert.NoUnexpectedReceived();
        }

        // -------------------------------------------------------------------
        // Late binding
        // -------------------------------------------------------------------

        [Test]
        public void BindingsRegisteredAfterConstructionStillApply()
        {
            // The runtime creates the dispatcher during configuration, before game code
            // has had a chance to bind anything.
            var registry = new BindingRegistry();
            var dispatcher = new Dispatcher(registry);
            var functions = new FakeFunctions();

            registry.BindFunctions("late", (object)functions);

            Assert.IsTrue(dispatcher.EvalCondition("Fn.late.AlwaysTrue()"));
        }

        void SetLists(string key, params string[] optionsJson)
        {
            var options = new List<JObject>();
            foreach (var json in optionsJson)
            {
                options.Add(JObject.Parse(json.Replace('\'', '"')));
            }

            _dispatcher.SetLists(new Dictionary<string, List<JObject>> { [key] = options });
        }
    }
}
