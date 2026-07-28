using System.Text.RegularExpressions;
using NUnit.Framework;
using Tarinoi.Expressions;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tarinoi.Tests
{
    public class ExpressionParserTests
    {
        static readonly Regex ParseError = new Regex("ExpressionParser:");

        // -------------------------------------------------------------------
        // Empty and trivial
        // -------------------------------------------------------------------

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("\t\n")]
        [TestCase(null)]
        public void EmptyConditionsParseToNothing(string expr)
        {
            Assert.IsNull(ExpressionParser.ParseCondition(expr),
                "an absent condition is represented as null, which callers treat as passing");
        }

        [Test]
        public void BooleanLiterals()
        {
            Assert.IsTrue(((BoolLiteral)ExpressionParser.ParseCondition("true")).Value);
            Assert.IsFalse(((BoolLiteral)ExpressionParser.ParseCondition("false")).Value);
        }

        // -------------------------------------------------------------------
        // Operators and precedence
        // -------------------------------------------------------------------

        [Test]
        public void Negation()
        {
            var node = (NotNode)ExpressionParser.ParseCondition("!true");
            Assert.IsInstanceOf<BoolLiteral>(node.Operand);
        }

        [Test]
        public void DoubleNegation()
        {
            var node = (NotNode)ExpressionParser.ParseCondition("!!false");
            Assert.IsInstanceOf<NotNode>(node.Operand);
        }

        [Test]
        public void Conjunction()
        {
            Assert.IsInstanceOf<AndNode>(ExpressionParser.ParseCondition("true && false"));
        }

        [Test]
        public void Disjunction()
        {
            Assert.IsInstanceOf<OrNode>(ExpressionParser.ParseCondition("true || false"));
        }

        [Test]
        public void AndBindsTighterThanOr()
        {
            // a || (b && c)
            var root = (OrNode)ExpressionParser.ParseCondition("true || false && true");
            Assert.IsInstanceOf<BoolLiteral>(root.Left);
            Assert.IsInstanceOf<AndNode>(root.Right);
        }

        [Test]
        public void ParenthesesOverridePrecedence()
        {
            // (a || b) && c
            var root = (AndNode)ExpressionParser.ParseCondition("(true || false) && true");
            Assert.IsInstanceOf<OrNode>(root.Left);
            Assert.IsInstanceOf<BoolLiteral>(root.Right);
        }

        [Test]
        public void NotBindsTighterThanAnd()
        {
            var root = (AndNode)ExpressionParser.ParseCondition("!true && false");
            Assert.IsInstanceOf<NotNode>(root.Left);
        }

        [Test]
        public void OperatorsAreLeftAssociative()
        {
            var root = (AndNode)ExpressionParser.ParseCondition("true && false && true");
            Assert.IsInstanceOf<AndNode>(root.Left);
            Assert.IsInstanceOf<BoolLiteral>(root.Right);
        }

        // -------------------------------------------------------------------
        // References and calls
        // -------------------------------------------------------------------

        [Test]
        public void FunctionCallWithNoArguments()
        {
            var call = (CallNode)ExpressionParser.ParseCondition("Fn.global.IsReady()");
            Assert.AreEqual("global", call.Collection);
            Assert.AreEqual("IsReady", call.Name);
            Assert.IsEmpty(call.Args);
        }

        [Test]
        public void FunctionCallWithArgumentsOfEveryLiteralType()
        {
            var call = (CallNode)ExpressionParser.ParseCondition(
                "Fn.global.Check(true, 42, 3.5, \"hello\")");

            Assert.AreEqual(4, call.Args.Count);
            Assert.IsTrue(((BoolLiteral)call.Args[0]).Value);
            Assert.AreEqual(42L, ((IntLiteral)call.Args[1]).Value);
            Assert.AreEqual(3.5, ((FloatLiteral)call.Args[2]).Value, 1e-9);
            Assert.AreEqual("hello", ((StringLiteral)call.Args[3]).Value);
        }

        [Test]
        public void VariableReference()
        {
            var node = (RefNode)ExpressionParser.ParseCondition("Var.player.health");
            Assert.AreEqual(RefKind.Variable, node.Kind);
            Assert.AreEqual("player", node.Collection);
            Assert.AreEqual("health", node.Name);
        }

        [Test]
        public void EntityReference()
        {
            var node = (RefNode)ExpressionParser.ParseCondition("Ent.cast.narrator");
            Assert.AreEqual(RefKind.Entity, node.Kind);
            Assert.AreEqual("narrator", node.Name);
        }

        [Test]
        public void ListReferenceCarriesItsKey()
        {
            var node = (RefNode)ExpressionParser.ParseCondition("Ls.global.moods.happy");
            Assert.AreEqual(RefKind.List, node.Kind);
            Assert.AreEqual("global", node.Collection);
            Assert.AreEqual("moods", node.Name);
            Assert.AreEqual("happy", node.Key);
        }

        [Test]
        public void CardReference()
        {
            var node = (CardRefNode)ExpressionParser.ParseCondition("Card.CurrentContextCard");
            Assert.AreEqual("CurrentContextCard", node.Name);
        }

        [Test]
        public void ReferencesCanBeArguments()
        {
            var call = (CallNode)ExpressionParser.ParseCondition(
                "Fn.global.Check(Var.player.hp, Ent.cast.npc, Ls.global.moods.sad)");

            Assert.AreEqual(RefKind.Variable, ((RefNode)call.Args[0]).Kind);
            Assert.AreEqual(RefKind.Entity, ((RefNode)call.Args[1]).Kind);
            Assert.AreEqual(RefKind.List, ((RefNode)call.Args[2]).Kind);
        }

        [Test]
        public void CallsCanNest()
        {
            var outer = (CallNode)ExpressionParser.ParseCondition(
                "Fn.global.Outer(Fn.global.Inner(1))");

            var inner = (CallNode)outer.Args[0];
            Assert.AreEqual("Inner", inner.Name);
            Assert.AreEqual(1L, ((IntLiteral)inner.Args[0]).Value);
        }

        [Test]
        public void CallsCombineWithOperators()
        {
            var root = (AndNode)ExpressionParser.ParseCondition(
                "Fn.global.A() && !Fn.global.B()");

            Assert.IsInstanceOf<CallNode>(root.Left);
            Assert.IsInstanceOf<NotNode>(root.Right);
        }

        [Test]
        public void WhitespaceIsInsignificant()
        {
            var tight = ExpressionParser.ParseCondition("Fn.global.Check(1,2)").ToString();
            var loose = ExpressionParser.ParseCondition("  Fn . global . Check ( 1 , 2 )  ").ToString();
            Assert.AreEqual(tight, loose);
        }

        [Test]
        public void IdentifiersMayContainUnderscoresAndDigits()
        {
            var node = (RefNode)ExpressionParser.ParseCondition("Var.my_col2.some_var_3");
            Assert.AreEqual("my_col2", node.Collection);
            Assert.AreEqual("some_var_3", node.Name);
        }

        [Test]
        public void AListKeyMayBeNumeric()
        {
            // The '.' before a digit is member access here, not a decimal point.
            var node = (RefNode)ExpressionParser.ParseCondition("Ls.global.moods.happy");
            Assert.AreEqual("happy", node.Key);
        }

        [Test]
        public void FloatsAreDistinguishedFromMemberAccess()
        {
            var call = (CallNode)ExpressionParser.ParseCondition("Fn.g.F(1.5)");
            Assert.IsInstanceOf<FloatLiteral>(call.Args[0]);

            var intCall = (CallNode)ExpressionParser.ParseCondition("Fn.g.F(1)");
            Assert.IsInstanceOf<IntLiteral>(intCall.Args[0]);
        }

        // -------------------------------------------------------------------
        // Failures
        // -------------------------------------------------------------------

        [TestCase("Fn.global.Check(", "unclosed argument list")]
        [TestCase("true &&", "dangling operator")]
        [TestCase("Bogus.thing.here", "unknown prefix")]
        [TestCase("Fn.global.Check() extra", "trailing tokens")]
        [TestCase("@invalid", "illegal character")]
        [TestCase("\"unterminated", "unterminated string")]
        [TestCase("()", "empty parentheses")]
        public void MalformedExpressionsAreLoggedAndReturnNull(string expr, string why)
        {
            LogAssert.Expect(LogType.Error, ParseError);
            Assert.IsNull(ExpressionParser.ParseCondition(expr), why);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void OnlyTheFirstErrorIsReported()
        {
            // Cascading messages from a derailed cursor hide the real cause.
            LogAssert.Expect(LogType.Error, ParseError);
            Assert.IsNull(ExpressionParser.ParseCondition("Fn.a.B(,,,"));
            LogAssert.NoUnexpectedReceived();
        }

        // -------------------------------------------------------------------
        // ParseCall
        // -------------------------------------------------------------------

        [Test]
        public void ParseCallAcceptsACall()
        {
            var call = ExpressionParser.ParseCall("Fn.global.PickPin(Var.player.mood)");
            Assert.AreEqual("PickPin", call.Name);
            Assert.AreEqual(1, call.Args.Count);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void ParseCallRejectsEmptyInput(string expr)
        {
            Assert.IsNull(ExpressionParser.ParseCall(expr));
        }

        [Test]
        public void ParseCallRejectsANonCall()
        {
            LogAssert.Expect(LogType.Error, ParseError);
            Assert.IsNull(ExpressionParser.ParseCall("Var.player.health"),
                "an output selector must be a function call, not a bare reference");
        }
    }
}
