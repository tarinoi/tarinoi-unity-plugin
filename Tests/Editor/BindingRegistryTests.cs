using System.Text.RegularExpressions;
using NUnit.Framework;
using Tarinoi.Bindings;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tarinoi.Tests
{
    public class BindingRegistryTests
    {
        class Functions : ITarinoiFunctions
        {
            public bool HasFunction(string name) => name == "Known";

            public bool TryInvoke(string name, object[] args, out object result)
            {
                result = "called";
                return name == "Known";
            }
        }

        class Variables : ITarinoiVariables
        {
            public object GetVariable(string name) => null;
            public void SetVariable(string name, object value) { }
        }

        class Entities : ITarinoiEntities
        {
            public object GetEntity(string name) => null;
        }

        class PlainFunctions
        {
            public bool Ready() => true;
            public int Add(int a, int b) => a + b;
        }

        BindingRegistry _registry;

        [SetUp]
        public void SetUp() => _registry = new BindingRegistry();

        [Test]
        public void BoundCollectionsAreRetrievable()
        {
            var functions = new Functions();
            _registry.BindFunctions("global", functions);
            _registry.BindVariables("global", new Variables());
            _registry.BindEntities("cast", new Entities());

            Assert.AreSame(functions, _registry.GetFunctions("global"));
            Assert.IsNotNull(_registry.GetVariables("global"));
            Assert.IsNotNull(_registry.GetEntities("cast"));
        }

        [Test]
        public void UnboundCollectionsReturnNull()
        {
            Assert.IsNull(_registry.GetFunctions("nope"));
            Assert.IsNull(_registry.GetVariables("nope"));
            Assert.IsNull(_registry.GetEntities("nope"));
            Assert.IsNull(_registry.GetFunctions(null));
        }

        [Test]
        public void TheThreeKindsHaveSeparateNamespaces()
        {
            // The same identifier commonly names a function and a variable collection.
            _registry.BindFunctions("global", new Functions());

            Assert.IsNotNull(_registry.GetFunctions("global"));
            Assert.IsNull(_registry.GetVariables("global"),
                "binding functions must not imply a variable binding");
        }

        [Test]
        public void KeysAreCaseSensitiveMachineIdentifiers()
        {
            // Authored expressions use the machine identifier verbatim; matching loosely
            // would hide the label-vs-identifier mistake this is meant to surface.
            _registry.BindFunctions("global", new Functions());

            Assert.IsNull(_registry.GetFunctions("Global"));
            Assert.IsNull(_registry.GetFunctions("Global State"));
        }

        [Test]
        public void RebindingReplaces()
        {
            var second = new Functions();
            _registry.BindFunctions("global", new Functions());
            _registry.BindFunctions("global", second);

            Assert.AreSame(second, _registry.GetFunctions("global"));
        }

        [Test]
        public void BindingWithoutAnIdentifierIsRejected()
        {
            LogAssert.Expect(LogType.Error, new Regex("without an identifier"));
            _registry.BindFunctions("", new Functions());
            Assert.IsNull(_registry.GetFunctions(""));
        }

        [Test]
        public void BindingNullIsRejected()
        {
            LogAssert.Expect(LogType.Error, new Regex("cannot bind null"));
            _registry.BindVariables("global", (ITarinoiVariables)null);
            Assert.IsNull(_registry.GetVariables("global"));
        }

        [Test]
        public void APlainObjectIsAdaptedReflectively()
        {
            _registry.BindFunctions("global", (object)new PlainFunctions());

            var impl = _registry.GetFunctions("global");
            Assert.IsInstanceOf<ReflectionFunctions>(impl);
            Assert.IsTrue(impl.HasFunction("Ready"));
            Assert.IsFalse(impl.HasFunction("Missing"));

            Assert.IsTrue(impl.TryInvoke("Add", new object[] { 2, 3 }, out var result));
            Assert.AreEqual(5, result);
        }

        [Test]
        public void AnObjectAlreadyImplementingTheInterfaceIsNotWrapped()
        {
            var functions = new Functions();
            _registry.BindFunctions("global", (object)functions);

            Assert.AreSame(functions, _registry.GetFunctions("global"),
                "a generated binding must dispatch directly, not through reflection");
        }

        [Test]
        public void TryInvokeReportsAnUnknownFunction()
        {
            _registry.BindFunctions("global", new Functions());
            Assert.IsFalse(_registry.GetFunctions("global")
                .TryInvoke("Unknown", new object[0], out _));
        }

        [Test]
        public void ClearRemovesEverything()
        {
            _registry.BindFunctions("global", new Functions());
            _registry.BindVariables("global", new Variables());
            _registry.BindEntities("cast", new Entities());

            _registry.Clear();

            Assert.IsNull(_registry.GetFunctions("global"));
            Assert.IsNull(_registry.GetVariables("global"));
            Assert.IsNull(_registry.GetEntities("cast"));
        }

        [Test]
        public void BoundCollectionsCanBeEnumeratedForDiagnostics()
        {
            _registry.BindFunctions("global", new Functions());
            _registry.BindFunctions("player", new Functions());

            CollectionAssert.AreEquivalent(new[] { "global", "player" },
                _registry.BoundFunctionCollections);
        }
    }
}
