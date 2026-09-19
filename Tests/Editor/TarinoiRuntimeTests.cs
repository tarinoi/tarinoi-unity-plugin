using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Tarinoi.Bindings;
using Tarinoi.Data;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tarinoi.Tests
{
    /// <summary>
    /// The dialogue state machine, rule by rule.
    /// </summary>
    /// <remarks>
    /// The Godot plugin has no tests for its equivalent at all, so these are written
    /// against the behaviour its source specifies rather than ported from an existing
    /// suite. Each test names the rule it pins down.
    /// </remarks>
    public class TarinoiRuntimeTests
    {
        RuntimeHarness _h;

        [SetUp]
        public void SetUp() => _h = new RuntimeHarness();

        [TearDown]
        public void TearDown() => _h.Dispose();

        /// <summary>Records which functions ran, in order.</summary>
        class SpyFunctions
        {
            public readonly List<string> Calls = new List<string>();
            public string PinToReturn = "a";

            public bool True() { Calls.Add(nameof(True)); return true; }
            public bool False() { Calls.Add(nameof(False)); return false; }
            public void Effect() => Calls.Add(nameof(Effect));
            public void First() => Calls.Add(nameof(First));
            public void Second() => Calls.Add(nameof(Second));
            public string PickPin() { Calls.Add(nameof(PickPin)); return PinToReturn; }
        }

        SpyFunctions BindSpy()
        {
            var spy = new SpyFunctions();
            _h.Runtime.Registry.BindFunctions("g", (object)spy);
            return spy;
        }

        // =====================================================================
        // Basic playback
        // =====================================================================

        [Test]
        public void AnNpcLineIsRaisedWithItsSpeakerAndText()
        {
            _h.SeedEntity("narrator", false, "The Narrator").Configure();
            _h.Store.Add("c1", CardBuilder.Line("Hello there").Entity("narrator").To("flow:end"));

            _h.Start("c1");

            Assert.AreEqual(1, _h.Lines.Count);
            Assert.AreEqual("Hello there", _h.LastLine.Line);
            Assert.AreEqual("The Narrator", _h.LastLine.EntityLabel);
            Assert.AreEqual("c1", _h.LastLine.CardId);
            Assert.AreEqual(DialogueState.NpcLine, _h.Runtime.State);
        }

        [Test]
        public void TheSpeakerLabelFallsBackToTheEntityIdentifier()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Line("Hi").Entity("unknown_entity").Mode("npc").To("flow:end"));

            _h.Start("c1");

            Assert.AreEqual("unknown_entity", _h.LastLine.EntityLabel);
        }

        [Test]
        public void AMissingCardReportsAnErrorRatherThanThrowing()
        {
            _h.Configure();

            LogAssert.Expect(LogType.Error, new Regex("was not found"));
            _h.Start("nope");

            Assert.AreEqual(1, _h.Errors.Count);
            StringAssert.Contains("nope", _h.Errors[0]);
        }

        [Test]
        public void StartingBeforeConfiguringIsReportedNotThrown()
        {
            LogAssert.Expect(LogType.Error, new Regex("not configured"));
            _h.Start("c1");
            Assert.IsEmpty(_h.Lines);
        }

        // =====================================================================
        // Transparent cards
        // =====================================================================

        [TestCase("start")]
        [TestCase("blank")]
        public void TransparentCardsAreTraversedWithoutBeingShown(string baseRef)
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Of(baseRef).To("c2"));
            _h.Store.Add("c2", CardBuilder.Line("Arrived").Mode("npc").To("flow:end"));

            _h.Start("c1");

            Assert.AreEqual(1, _h.Lines.Count, "only the line card should surface");
            Assert.AreEqual("Arrived", _h.LastLine.Line);
        }

        [Test]
        public void AnUnrecognisedCardTypeIsTraversedWithAWarning()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Of("some_future_type").To("c2"));
            _h.Store.Add("c2", CardBuilder.Line("Arrived").Mode("npc").To("flow:end"));

            LogAssert.Expect(LogType.Warning, new Regex("unrecognised type"));
            _h.Start("c1");

            Assert.AreEqual("Arrived", _h.LastLine.Line);
        }

        [Test]
        public void AJumpCardFollowsItsTargetLinkToAnotherCollection()
        {
            // A jump's destination is data.target — a bare document id, which may live on
            // another board. Not a (collection, card) pair, as the package once assumed.
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Of("jump").Jump("far"));
            _h.Store.Add("far", CardBuilder.Line("Elsewhere").Mode("npc").To("flow:end"), "col2");

            _h.Start("c1");

            Assert.AreEqual("Elsewhere", _h.LastLine.Line);
            Assert.AreEqual("col2", _h.LastLine.CollectionId);
        }

        [Test]
        public void AJumpCardWithADanglingTargetReportsAnError()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Of("jump").Jump("nowhere"));

            LogAssert.Expect(LogType.Error, new Regex("does not exist: 'nowhere'"));
            _h.Start("c1");

            Assert.AreEqual(1, _h.Errors.Count);
        }

        [Test]
        public void AJumpCardWithNoTargetReportsAnError()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Of("jump"));

            LogAssert.Expect(LogType.Error, new Regex("does not say where to jump"));
            _h.Start("c1");

            Assert.AreEqual(1, _h.Errors.Count);
        }

        // =====================================================================
        // Ending
        // =====================================================================

        [Test]
        public void FlowEndFinishesTheDialogue()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Line("Bye").Mode("npc").To("flow:end"));

            _h.Start("c1");
            _h.Advance();

            Assert.AreEqual(1, _h.EndedCount);
            Assert.AreEqual(DialogueState.Idle, _h.Runtime.State);
        }

        [Test]
        public void ACardWithNoConnectionsEndsTheDialogueWithAnError()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Line("Dead end").Mode("npc"));

            _h.Start("c1");
            LogAssert.Expect(LogType.Error, new Regex("leads nowhere"));
            _h.Advance();

            Assert.AreEqual(1, _h.EndedCount);
        }

        [Test]
        public void AbortEndsTheDialogueImmediately()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Line("Hi").Mode("npc").To("flow:end"));

            _h.Start("c1");
            _h.Runtime.AbortDialogue();

            Assert.AreEqual(1, _h.EndedCount);
            Assert.AreEqual(DialogueState.Idle, _h.Runtime.State);
        }

        // =====================================================================
        // Default connections
        // =====================================================================

        [Test]
        public void ASingleDefaultTargetIsFollowedSilently()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Line("One").Mode("npc").To("c2"));
            _h.Store.Add("c2", CardBuilder.Line("Two").Mode("npc").To("flow:end"));

            _h.Start("c1");
            _h.Advance();

            Assert.AreEqual("Two", _h.LastLine.Line);
            Assert.IsEmpty(_h.ChoiceSets, "a single continuation is not a choice");
        }

        [Test]
        public void DuplicateDefaultTargetsAreCollapsed()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Line("One").Mode("npc")
                .Connect("default>>c2", "default>>c2"));
            _h.Store.Add("c2", CardBuilder.Line("Two").Mode("npc").To("flow:end"));

            _h.Start("c1");
            _h.Advance();

            Assert.AreEqual("Two", _h.LastLine.Line, "the duplicate must not become a choice");
            Assert.IsEmpty(_h.ChoiceSets);
        }

        [Test]
        public void SeveralDefaultTargetsBecomeChoices()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Blank().To("a", "b"));
            _h.Store.Add("a", CardBuilder.Line("Option A").Mode("pc").Geo(10));
            _h.Store.Add("b", CardBuilder.Line("Option B").Mode("pc").Geo(20));

            _h.Start("c1");

            Assert.AreEqual(1, _h.ChoiceSets.Count);
            Assert.AreEqual(2, _h.Choices.Count);
            Assert.AreEqual(DialogueState.PcChoice, _h.Runtime.State);
        }

        [Test]
        public void ANonLineCardCannotBeOfferedAsAChoice()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Blank().To("a", "b"));
            _h.Store.Add("a", CardBuilder.Line("Real option").Mode("npc").Geo(10).To("flow:end"));
            _h.Store.Add("b", CardBuilder.Blank().Geo(20));

            LogAssert.Expect(LogType.Warning, new Regex("is not a line"));
            _h.Start("c1");

            // Only one option survived, so it is followed rather than offered.
            Assert.IsEmpty(_h.ChoiceSets);
            Assert.AreEqual("Real option", _h.LastLine.Line);
        }

        // =====================================================================
        // Choice ordering — the geo.y rule
        // =====================================================================

        [Test]
        public void ChoicesArePresentedInAscendingVerticalOrder()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Blank().To("bottom", "top", "middle"));
            _h.Store.Add("top", CardBuilder.Line("Top").Mode("pc").Geo(-50));
            _h.Store.Add("middle", CardBuilder.Line("Middle").Mode("pc").Geo(0));
            _h.Store.Add("bottom", CardBuilder.Line("Bottom").Mode("pc").Geo(120.5));

            _h.Start("c1");

            CollectionAssert.AreEqual(new[] { "Top", "Middle", "Bottom" },
                _h.Choices.Select(c => c.Line).ToList(),
                "authors express reading order by vertical position, not connection order");
        }

        [Test]
        public void NegativePositionsSortBeforeZero()
        {
            // A naive "missing means 0" default would break this ordering.
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Blank().To("zero", "negative"));
            _h.Store.Add("zero", CardBuilder.Line("Zero").Mode("pc").Geo(0));
            _h.Store.Add("negative", CardBuilder.Line("Negative").Mode("pc").Geo(-1000));

            _h.Start("c1");

            CollectionAssert.AreEqual(new[] { "Negative", "Zero" },
                _h.Choices.Select(c => c.Line).ToList());
        }

        [Test]
        public void CardsWithoutGeometrySortLast()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Blank().To("nogeo", "positioned"));
            _h.Store.Add("nogeo", CardBuilder.Line("No geometry").Mode("pc"));
            _h.Store.Add("positioned", CardBuilder.Line("Positioned").Mode("pc").Geo(500));

            _h.Start("c1");

            CollectionAssert.AreEqual(new[] { "Positioned", "No geometry" },
                _h.Choices.Select(c => c.Line).ToList());
        }

        [Test]
        public void EqualPositionsKeepTheirOriginalOrder()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Blank().To("first", "second"));
            _h.Store.Add("first", CardBuilder.Line("First").Mode("pc").Geo(10));
            _h.Store.Add("second", CardBuilder.Line("Second").Mode("pc").Geo(10));

            _h.Start("c1");

            CollectionAssert.AreEqual(new[] { "First", "Second" },
                _h.Choices.Select(c => c.Line).ToList(), "ordering must be deterministic");
        }

        [Test]
        public void ChoiceIndicesMatchTheirPositionAfterSorting()
        {
            // SelectChoice indexes positionally, so stale indices would select the
            // wrong line.
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Blank().To("bottom", "top"));
            _h.Store.Add("top", CardBuilder.Line("Top").Mode("pc").Geo(0).To("flow:end"));
            _h.Store.Add("bottom", CardBuilder.Line("Bottom").Mode("pc").Geo(100).To("flow:end"));

            _h.Start("c1");
            CollectionAssert.AreEqual(new[] { 0, 1 }, _h.Choices.Select(c => c.Index).ToList());

            _h.Select(0);
            Assert.AreEqual("Top", _h.ChoicesMade[0].Line, "index 0 must be the topmost choice");
        }

        // =====================================================================
        // Conditions
        // =====================================================================

        [Test]
        public void AChoiceWhoseConditionFailsIsNotOffered()
        {
            _h.Configure();
            BindSpy();
            _h.Store.Add("c1", CardBuilder.Blank().To("a", "b", "c"));
            _h.Store.Add("a", CardBuilder.Line("Allowed").Mode("pc").Geo(0).Condition("Fn.g.True()"));
            _h.Store.Add("b", CardBuilder.Line("Blocked").Mode("pc").Geo(10).Condition("Fn.g.False()"));
            _h.Store.Add("c", CardBuilder.Line("Also allowed").Mode("pc").Geo(20));

            _h.Start("c1");

            CollectionAssert.AreEqual(new[] { "Allowed", "Also allowed" },
                _h.Choices.Select(c => c.Line).ToList());
        }

        [Test]
        public void WhenOnlyOneChoiceSurvivesItIsFollowedWithoutAsking()
        {
            _h.Configure();
            BindSpy();
            _h.Store.Add("c1", CardBuilder.Blank().To("a", "b"));
            _h.Store.Add("a", CardBuilder.Line("Survivor").Mode("npc").Geo(0).To("flow:end"));
            _h.Store.Add("b", CardBuilder.Line("Blocked").Mode("npc").Geo(10).Condition("Fn.g.False()"));

            _h.Start("c1");

            Assert.IsEmpty(_h.ChoiceSets, "one option is not a choice");
            Assert.AreEqual("Survivor", _h.LastLine.Line);
        }

        [Test]
        public void WhenEveryChoiceIsBlockedTheDialogueEndsWithAnError()
        {
            _h.Configure();
            BindSpy();
            _h.Store.Add("c1", CardBuilder.Blank().To("a", "b"));
            _h.Store.Add("a", CardBuilder.Line("A").Mode("pc").Geo(0).Condition("Fn.g.False()"));
            _h.Store.Add("b", CardBuilder.Line("B").Mode("pc").Geo(10).Condition("Fn.g.False()"));

            LogAssert.Expect(LogType.Error, new Regex("all 2 possible continuation"));
            _h.Start("c1");

            Assert.AreEqual(1, _h.EndedCount);
        }

        [Test]
        public void ACardWhoseEntryConditionFailsIsSteppedOverNotStoppedAt()
        {
            // The card refuses entry, so traversal continues through its connections.
            _h.Configure();
            BindSpy();
            _h.Store.Add("c1", CardBuilder.Line("Skipped").Mode("npc")
                .Condition("Fn.g.False()").To("c2"));
            _h.Store.Add("c2", CardBuilder.Line("Reached").Mode("npc").To("flow:end"));

            _h.Start("c1");

            Assert.AreEqual(1, _h.Lines.Count);
            Assert.AreEqual("Reached", _h.LastLine.Line);
        }

        [Test]
        public void AnUnfilledTemplateInAConditionIsTreatedAsMet()
        {
            // Hiding content while the author is still filling in a template would be
            // worse than showing it.
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Line("Shown").Mode("npc")
                .Condition("Fn.g.Check($placeholder)").To("flow:end"));

            LogAssert.Expect(LogType.Warning, new Regex("unfilled template"));
            _h.Start("c1");

            Assert.AreEqual("Shown", _h.LastLine.Line);
        }

        [Test]
        public void AnExplicitJsonNullInputPinIsTreatedAsNoCondition()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Line("Shown").Mode("npc").Condition(null).To("flow:end"));

            _h.Start("c1");

            Assert.AreEqual("Shown", _h.LastLine.Line);
        }

        // =====================================================================
        // Player vs non-player lines
        // =====================================================================

        [Test]
        public void APlayerLineReachedOnItsOwnBecomesASingleChoice()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Line("I speak").Mode("pc").To("flow:end"));

            _h.Start("c1");

            Assert.IsEmpty(_h.Lines);
            Assert.AreEqual(1, _h.Choices.Count);
            Assert.AreEqual("I speak", _h.Choices[0].Line);
            Assert.AreEqual(DialogueState.PcChoice, _h.Runtime.State);
        }

        [Test]
        public void LineModeOverridesTheSpeakingEntity()
        {
            _h.SeedEntity("hero", true).Configure();
            _h.Store.Add("c1", CardBuilder.Line("Spoken at me").Mode("npc").Entity("hero").To("flow:end"));

            _h.Start("c1");

            Assert.AreEqual(1, _h.Lines.Count, "an explicit npc mode wins over a player entity");
        }

        [Test]
        public void InheritedModeUsesTheEntityToDecide()
        {
            _h.SeedEntity("hero", true).SeedEntity("narrator", false).Configure();
            _h.Store.Add("hero_line", CardBuilder.Line("Mine").Mode("inherit").Entity("hero").To("flow:end"));
            _h.Store.Add("npc_line", CardBuilder.Line("Theirs").Mode("inherit").Entity("narrator").To("flow:end"));

            _h.Start("hero_line");
            Assert.AreEqual(1, _h.Choices.Count, "a player entity yields a choice");

            _h.Start("npc_line");
            Assert.AreEqual(1, _h.Lines.Count, "a non-player entity yields a line");
        }

        [Test]
        public void AnUnknownEntityDefaultsToANonPlayerLine()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Line("Hi").Mode("inherit").Entity("missing").To("flow:end"));

            _h.Start("c1");

            Assert.AreEqual(1, _h.Lines.Count);
        }

        // =====================================================================
        // What kind of set it is: player lines are a menu, non-player lines a first match
        // =====================================================================

        [Test]
        public void SeveralNpcLinesPassingTheirGatesShowOnlyTheFirst()
        {
            // Two non-player lines whose conditions both pass are not a menu: the author
            // picks the line by condition, and the topmost match is the one the player hears.
            _h.Configure();
            BindSpy();
            _h.Store.Add("c1", CardBuilder.Blank().To("a", "b", "c"));
            _h.Store.Add("a", CardBuilder.Line("You told me Godot").Mode("npc").Geo(0).Condition("Fn.g.True()").To("flow:end"));
            _h.Store.Add("b", CardBuilder.Line("You have not visited").Mode("npc").Geo(10).Condition("Fn.g.True()").To("flow:end"));
            _h.Store.Add("c", CardBuilder.Line("Fallback").Mode("npc").Geo(20).To("flow:end"));

            _h.Start("c1");

            Assert.IsEmpty(_h.ChoiceSets, "no choice UI for a non-player set");
            Assert.AreEqual(1, _h.Lines.Count);
            Assert.AreEqual("You told me Godot", _h.LastLine.Line);
            Assert.AreEqual(DialogueState.NpcLine, _h.Runtime.State);
        }

        [Test]
        public void ANpcSetSkipsAFailingLineToTheNextMatch()
        {
            _h.Configure();
            BindSpy();
            _h.Store.Add("c1", CardBuilder.Blank().To("a", "b"));
            _h.Store.Add("a", CardBuilder.Line("Gated").Mode("npc").Geo(0).Condition("Fn.g.False()").To("flow:end"));
            _h.Store.Add("b", CardBuilder.Line("Fallback").Mode("npc").Geo(10).To("flow:end"));

            _h.Start("c1");

            Assert.AreEqual("Fallback", _h.LastLine.Line);
        }

        [Test]
        public void ANpcSetDecidesByTheSpeakerWhenLineModeIsInherited()
        {
            _h.SeedEntity("narrator", false).Configure();
            _h.Store.Add("c1", CardBuilder.Blank().To("a", "b"));
            _h.Store.Add("a", CardBuilder.Line("First").Mode("inherit").Entity("narrator").Geo(0).To("flow:end"));
            _h.Store.Add("b", CardBuilder.Line("Second").Mode("inherit").Entity("narrator").Geo(10).To("flow:end"));

            LogAssert.Expect(LogType.Warning, new Regex("sharing the same condition"));
            _h.Start("c1");

            Assert.IsEmpty(_h.ChoiceSets);
            Assert.AreEqual("First", _h.LastLine.Line);
        }

        [Test]
        public void AMixedSetOffersThePlayerLinesAndDropsTheNpcLines()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Blank().To("a", "b", "c"));
            _h.Store.Add("a", CardBuilder.Line("Say this").Mode("pc").Geo(0));
            _h.Store.Add("b", CardBuilder.Line("Narration").Mode("npc").Geo(10));
            _h.Store.Add("c", CardBuilder.Line("Or this").Mode("pc").Geo(20));

            LogAssert.Expect(LogType.Warning, new Regex("both player and non-player"));
            _h.Start("c1");

            CollectionAssert.AreEqual(new[] { "Say this", "Or this" }, LinesOf(_h.Choices));
            Assert.AreEqual(1, _h.Choices[1].Index, "indices are contiguous after dropping");
        }

        // =====================================================================
        // Card functions
        // =====================================================================

        [Test]
        public void FunctionsOnAnNpcLineRunWhenItIsShown()
        {
            _h.Configure();
            var spy = BindSpy();
            _h.Store.Add("c1", CardBuilder.Line("Hi").Mode("npc")
                .Data("effect", "Fn.g.Effect()").To("flow:end"));

            _h.Start("c1");

            CollectionAssert.AreEqual(new[] { "Effect" }, spy.Calls);
        }

        [Test]
        public void FunctionsOnUnchosenOptionsNeverRun()
        {
            // The whole reason line cards defer their functions: side effects must not
            // fire for options the player never picks.
            _h.Configure();
            var spy = BindSpy();
            _h.Store.Add("c1", CardBuilder.Blank().To("taken", "ignored"));
            _h.Store.Add("taken", CardBuilder.Line("Taken").Mode("pc").Geo(0)
                .Data("effect", "Fn.g.First()").To("flow:end"));
            _h.Store.Add("ignored", CardBuilder.Line("Ignored").Mode("pc").Geo(10)
                .Data("effect", "Fn.g.Second()").To("flow:end"));

            _h.Start("c1");
            CollectionAssert.IsEmpty(spy.Calls, "offering choices must have no side effects");

            _h.Select(0);
            CollectionAssert.AreEqual(new[] { "First" }, spy.Calls);
        }

        [Test]
        public void FunctionsRunInThePropsDeclaredOrder()
        {
            _h.Configure();
            var spy = BindSpy();
            _h.Store.Add("c1", CardBuilder.Line("Hi").Mode("npc")
                .Data("second_prop", "Fn.g.Second()")
                .Data("first_prop", "Fn.g.First()")
                .Props("first_prop", "second_prop")
                .To("flow:end"));

            _h.Start("c1");

            CollectionAssert.AreEqual(new[] { "First", "Second" }, spy.Calls,
                "authors sequence side effects deliberately");
        }

        [Test]
        public void FunctionsMissingFromPropsRunLast()
        {
            // props can lag behind a template change, so it is an ordering hint only.
            _h.Configure();
            var spy = BindSpy();
            _h.Store.Add("c1", CardBuilder.Line("Hi").Mode("npc")
                .Data("undeclared", "Fn.g.Second()")
                .Data("declared", "Fn.g.First()")
                .Props("declared")
                .To("flow:end"));

            _h.Start("c1");

            CollectionAssert.AreEqual(new[] { "First", "Second" }, spy.Calls);
        }

        [Test]
        public void AnUnfilledTemplateInAFunctionIsSkipped()
        {
            _h.Configure();
            var spy = BindSpy();
            _h.Store.Add("c1", CardBuilder.Line("Hi").Mode("npc")
                .Data("effect", "Fn.g.Effect($arg)").To("flow:end"));

            LogAssert.Expect(LogType.Warning, new Regex("unfilled template"));
            _h.Start("c1");

            CollectionAssert.IsEmpty(spy.Calls);
        }

        [Test]
        public void AnUnboundFunctionIsReportedAndTheLineStillShows()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Line("Hi").Mode("npc")
                .Data("effect", "Fn.nobody.Missing()").To("flow:end"));

            LogAssert.Expect(LogType.Error, new Regex("which is not bound"));
            _h.Start("c1");

            Assert.AreEqual("Hi", _h.LastLine.Line, "a binding mistake must not lose the line");
        }

        [Test]
        public void NonFunctionDataIsLeftAlone()
        {
            _h.Configure();
            BindSpy();
            _h.Store.Add("c1", CardBuilder.Line("Hi").Mode("npc")
                .Data("mood", "happy").Data("count", 3).To("flow:end"));

            _h.Start("c1");

            Assert.AreEqual("happy", (string)_h.LastLine.Data["mood"]);
        }

        // =====================================================================
        // Named pins and output selectors
        // =====================================================================

        [Test]
        public void AnOutputSelectorRoutesToTheNamedPin()
        {
            _h.Configure();
            var spy = BindSpy();
            spy.PinToReturn = "success";

            _h.Store.Add("c1", CardBuilder.Blank().Selector("Fn.g.PickPin()")
                .Connect("success>>good", "failure>>bad"));
            _h.Store.Add("good", CardBuilder.Line("Succeeded").Mode("npc").To("flow:end"));
            _h.Store.Add("bad", CardBuilder.Line("Failed").Mode("npc").To("flow:end"));

            _h.Start("c1");

            Assert.AreEqual("Succeeded", _h.LastLine.Line);
        }

        [Test]
        public void ASelectorReturningAnUnknownPinStallsRatherThanGuessing()
        {
            _h.Configure();
            var spy = BindSpy();
            spy.PinToReturn = "nonexistent";

            _h.Store.Add("c1", CardBuilder.Blank().Selector("Fn.g.PickPin()")
                .Connect("success>>good", "failure>>bad"));

            LogAssert.Expect(LogType.Error, new Regex("has no pin 'nonexistent'"));
            _h.Start("c1");

            Assert.AreEqual(1, _h.Errors.Count);
            Assert.AreEqual(0, _h.EndedCount, "stalling is not ending — the mistake stays visible");
            Assert.IsEmpty(_h.Lines);
        }

        [Test]
        public void AnUnboundSelectorFallsBackToPickingThePinByHand()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Blank().Selector("Fn.nobody.Missing()")
                .Connect("a>>x", "b>>y"));

            LogAssert.Expect(LogType.Error, new Regex("not bound"));
            _h.Start("c1");

            Assert.AreEqual(DialogueState.AwaitingPin, _h.Runtime.State);
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, _h.PinRequests[0]);
        }

        [Test]
        public void AnUnfilledTemplateInASelectorFallsBackToPickingByHand()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Blank().Selector("Fn.g.Pick($arg)")
                .Connect("a>>x", "b>>y"));

            LogAssert.Expect(LogType.Warning, new Regex("unfilled template"));
            _h.Start("c1");

            Assert.AreEqual(DialogueState.AwaitingPin, _h.Runtime.State);
        }

        [Test]
        public void NamedPinsWithNoSelectorAskForThePin()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Blank().Connect("a>>x", "b>>y"));

            _h.Start("c1");

            Assert.AreEqual(DialogueState.AwaitingPin, _h.Runtime.State);
        }

        [Test]
        public void SelectingAPinFollowsIt()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Blank().Connect("a>>x", "b>>y"));
            _h.Store.Add("y", CardBuilder.Line("Down path b").Mode("npc").To("flow:end"));

            _h.Start("c1");
            _h.SelectPin("b");

            Assert.AreEqual("Down path b", _h.LastLine.Line);
        }

        [Test]
        public void SelectingAnUnknownPinReportsAnError()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Blank().Connect("a>>x"));

            _h.Start("c1");
            LogAssert.Expect(LogType.Error, new Regex("no pin named 'zzz'"));
            _h.SelectPin("zzz");

            Assert.AreEqual(1, _h.Errors.Count);
        }

        [Test]
        public void DuplicateNamedPinsKeepTheFirstTarget()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Blank().Connect("a>>first", "a>>second"));
            _h.Store.Add("first", CardBuilder.Line("First").Mode("npc").To("flow:end"));

            _h.Start("c1");
            _h.SelectPin("a");

            Assert.AreEqual("First", _h.LastLine.Line);
        }

        // =====================================================================
        // System lines
        // =====================================================================

        [Test]
        public void APostedSystemLineIsShownBeforeTheRoutedCard()
        {
            _h.Configure();
            var runtime = _h.Runtime;
            runtime.Registry.BindFunctions("g", (object)new SystemLineFunctions(runtime));

            _h.Store.Add("c1", CardBuilder.Blank().Selector("Fn.g.CheckSkill()")
                .Connect("success>>good"));
            _h.Store.Add("good", CardBuilder.Line("You made it").Mode("npc").To("flow:end"));

            _h.Start("c1");

            Assert.AreEqual(1, _h.Lines.Count);
            Assert.IsTrue(_h.LastLine.IsSystem);
            Assert.AreEqual("You rolled well.", _h.LastLine.Line);
            Assert.AreEqual("system", _h.LastLine.LineMode);

            _h.Advance();

            Assert.AreEqual("You made it", _h.LastLine.Line);
            Assert.IsFalse(_h.LastLine.IsSystem);
        }

        [Test]
        public void OnlyTheFirstPostedSystemLineIsShown()
        {
            _h.Configure();
            var runtime = _h.Runtime;
            var functions = new SystemLineFunctions(runtime) { ExtraLines = new[] { "Second", "Third" } };
            runtime.Registry.BindFunctions("g", (object)functions);

            _h.Store.Add("c1", CardBuilder.Blank().Selector("Fn.g.CheckSkill()")
                .Connect("success>>good"));
            _h.Store.Add("good", CardBuilder.Line("Arrived").Mode("npc").To("flow:end"));

            _h.Start("c1");
            Assert.AreEqual("You rolled well.", _h.LastLine.Line);

            _h.Advance();
            Assert.AreEqual("Arrived", _h.LastLine.Line, "queued extras are discarded");
        }

        class SystemLineFunctions
        {
            readonly TarinoiRuntime _runtime;
            public string[] ExtraLines = new string[0];

            public SystemLineFunctions(TarinoiRuntime runtime) => _runtime = runtime;

            public string CheckSkill()
            {
                _runtime.PostSystemLine("You rolled well.");
                foreach (var extra in ExtraLines)
                {
                    _runtime.PostSystemLine(extra);
                }

                return "success";
            }
        }

        // =====================================================================
        // Loop detection
        // =====================================================================

        [Test]
        public void ALoopThatNeverReachesThePlayerIsBroken()
        {
            _h.Configure();
            _h.Store.Add("a", CardBuilder.Blank().To("b"));
            _h.Store.Add("b", CardBuilder.Blank().To("a"));

            LogAssert.Expect(LogType.Error, new Regex("loops back"));
            _h.Start("a");

            Assert.AreEqual(1, _h.Errors.Count);
        }

        [Test]
        public void RevisitingACardAcrossTurnsIsAllowed()
        {
            // The guard only catches looping without ever stopping for input; a dialogue
            // that legitimately returns to an earlier line must keep working.
            _h.Configure();
            _h.Store.Add("a", CardBuilder.Line("Again?").Mode("npc").To("b"));
            _h.Store.Add("b", CardBuilder.Line("Yes").Mode("npc").To("a"));

            _h.Start("a");
            _h.Advance();
            _h.Advance();

            Assert.AreEqual(3, _h.Lines.Count);
            Assert.AreEqual("Again?", _h.LastLine.Line);
            Assert.IsEmpty(_h.Errors);
        }

        // =====================================================================
        // Input guards
        // =====================================================================

        [Test]
        public void AdvancingWhenNothingIsShownIsIgnored()
        {
            _h.Configure();

            LogAssert.Expect(LogType.Warning, new Regex("nothing to advance past"));
            _h.Advance();

            Assert.IsEmpty(_h.Lines);
        }

        [Test]
        public void SelectingAChoiceWhenNoneAreOpenIsIgnored()
        {
            _h.Configure();

            LogAssert.Expect(LogType.Warning, new Regex("no choices are open"));
            _h.Select(0);

            Assert.IsEmpty(_h.ChoicesMade);
        }

        [TestCase(-1)]
        [TestCase(99)]
        public void AnOutOfRangeChoiceIsIgnored(int index)
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Line("Only option").Mode("pc").To("flow:end"));
            _h.Start("c1");

            LogAssert.Expect(LogType.Warning, new Regex("out of range"));
            _h.Select(index);

            Assert.IsEmpty(_h.ChoicesMade);
            Assert.AreEqual(DialogueState.PcChoice, _h.Runtime.State, "the choice stays open");
        }

        [Test]
        public void SelectingAPinWhenNotWaitingIsIgnored()
        {
            _h.Configure();

            LogAssert.Expect(LogType.Warning, new Regex("not waiting for a pin"));
            _h.SelectPin("a");
        }

        // =====================================================================
        // Visited-choice history
        // =====================================================================

        [Test]
        public void ChoicesAreNotMarkedVisitedWithoutAHistoryStore()
        {
            _h.Configure();
            _h.Store.Add("c1", CardBuilder.Line("Option").Mode("pc").To("flow:end"));

            _h.Start("c1");
            Assert.IsFalse(_h.Choices[0].Visited);
        }

        [Test]
        public void PreviouslyChosenOptionsAreMarkedVisited()
        {
            _h.Configure();
            _h.Runtime.HistoryStore = new InMemoryHistoryStore();
            _h.Store.Add("c1", CardBuilder.Line("Option").Mode("pc").To("flow:end"));

            _h.Start("c1");
            Assert.IsFalse(_h.Choices[0].Visited, "not seen yet");

            _h.Select(0);
            _h.Advance();

            _h.Start("c1");
            Assert.IsTrue(_h.Choices[0].Visited, "the same option on a second visit");
        }

        [Test]
        public void VisitedChoicesArePersistedWhenTheDialogueEnds()
        {
            var history = new InMemoryHistoryStore();
            _h.Configure();
            _h.Runtime.HistoryStore = history;
            _h.Store.Add("c1", CardBuilder.Line("Option").Mode("pc").To("flow:end"));

            _h.Start("c1");
            _h.Select(0);
            _h.Advance();

            CollectionAssert.Contains(history.GetVisited("c1").ToList(), "c1");
        }

        [Test]
        public void AbortingAlsoPersistsVisitedChoices()
        {
            var history = new InMemoryHistoryStore();
            _h.Configure();
            _h.Runtime.HistoryStore = history;
            _h.Store.Add("c1", CardBuilder.Line("Option").Mode("pc").To("c2"));
            _h.Store.Add("c2", CardBuilder.Line("Next").Mode("npc").To("flow:end"));

            _h.Start("c1");
            _h.Select(0);
            _h.Runtime.AbortDialogue();

            CollectionAssert.Contains(history.GetVisited("c1").ToList(), "c1");
        }

        // =====================================================================
        // shown_once
        //
        // A card carrying shown_once stops being a valid continuation once the player
        // has seen it, so authors get "say this only once" without a flag per card.
        // Spent among other options it is simply dropped; spent as the only way
        // forward it dead-ends the dialogue, like any card with nowhere valid to go.
        // =====================================================================

        /// <summary>Hub offering three player lines, the first show-once and looping back.</summary>
        void SeedShownOnceHub(string loopTarget = "hub")
        {
            _h.Store.Add("hub", CardBuilder.Blank().To("a", "b", "c"));
            _h.Store.Add("a", CardBuilder.Line("Once only").Mode("pc").Geo(0).ShownOnce().To(loopTarget));
            _h.Store.Add("b", CardBuilder.Line("Again").Mode("pc").Geo(10).To("hub"));
            _h.Store.Add("c", CardBuilder.Line("And again").Mode("pc").Geo(20).To("hub"));
        }

        static List<string> LinesOf(IEnumerable<DialogueChoice> choices) =>
            choices.Select(choice => choice.Line).ToList();

        [Test]
        public void ShownOnceOptionDisappearsOnceThePlayerTakesIt()
        {
            _h.Configure();
            SeedShownOnceHub();

            _h.Start("hub");
            CollectionAssert.AreEqual(new[] { "Once only", "Again", "And again" },
                LinesOf(_h.Choices), "offered normally the first time");

            _h.Select(0);   // take it, loop back to the hub

            CollectionAssert.AreEqual(new[] { "Again", "And again" },
                LinesOf(_h.Choices), "the spent option is filtered out");
        }

        [Test]
        public void ShownOnceSurvivesTheDialogueThroughTheHistoryStore()
        {
            _h.Configure();
            _h.Runtime.HistoryStore = new InMemoryHistoryStore();
            SeedShownOnceHub("flow:end");

            _h.Start("hub");
            _h.Select(0);   // take it; the dialogue ends and history is flushed

            _h.Start("hub");

            CollectionAssert.AreEqual(new[] { "Again", "And again" },
                LinesOf(_h.Choices), "still filtered on a later visit");
        }

        [Test]
        public void ShownOnceResetsBetweenDialoguesWithoutAHistoryStore()
        {
            _h.Configure();
            SeedShownOnceHub("flow:end");

            _h.Start("hub");
            _h.Select(0);

            _h.Start("hub");

            CollectionAssert.AreEqual(new[] { "Once only", "Again", "And again" },
                LinesOf(_h.Choices), "nothing persists the seen set, so it is offered again");
        }

        [Test]
        public void ShownOnceCountsAnNpcLineFromTheMomentItIsDisplayed()
        {
            _h.Configure();
            // n1 plays as a plain line first, then turns up again as a candidate.
            _h.Store.Add("start", CardBuilder.Blank().To("n1"));
            _h.Store.Add("n1", CardBuilder.Line("Greeting").Mode("npc").Geo(0).ShownOnce().To("hub"));
            _h.Store.Add("hub", CardBuilder.Blank().To("n1", "n2", "n3"));
            _h.Store.Add("n2", CardBuilder.Line("Small talk").Mode("npc").Geo(10).To("flow:end"));
            _h.Store.Add("n3", CardBuilder.Line("More small talk").Mode("npc").Geo(20).To("flow:end"));

            _h.Start("start");
            Assert.AreEqual("Greeting", _h.LastLine.Line);

            LogAssert.Expect(LogType.Warning, new Regex("sharing the same condition"));
            _h.Advance();   // through n1 to the hub

            // A non-player set shows its first eligible line; the spent n1 is no longer eligible.
            Assert.IsEmpty(_h.ChoiceSets);
            Assert.AreEqual("Small talk", _h.LastLine.Line, "a displayed NPC line counts as seen");
        }

        [Test]
        public void ASpentShownOnceCardReachedOnItsOwnEndsTheDialogue()
        {
            _h.Configure();
            _h.Runtime.HistoryStore = new InMemoryHistoryStore();
            _h.Store.Add("s1", CardBuilder.Blank().To("a"));
            _h.Store.Add("a", CardBuilder.Line("Only way through").Mode("npc").ShownOnce().To("flow:end"));

            _h.Start("s1");
            Assert.AreEqual("Only way through", _h.LastLine.Line, "shown the first time");
            _h.Advance();

            _h.Lines.Clear();
            LogAssert.Expect(LogType.Error, new Regex("nothing else continues from here"));
            _h.Start("s1");

            Assert.IsEmpty(_h.Lines, "not shown a second time");
            Assert.AreEqual(2, _h.EndedCount,
                "a spent card is no continuation at all — the dialogue ends");
        }

        [Test]
        public void TheDialogueEndsWhenEveryCandidateIsASpentShownOnceCard()
        {
            _h.Configure();
            _h.Store.Add("hub", CardBuilder.Blank().To("a", "b"));
            _h.Store.Add("a", CardBuilder.Line("First").Mode("pc").Geo(0).ShownOnce().To("hub"));
            _h.Store.Add("b", CardBuilder.Line("Second").Mode("pc").Geo(10).ShownOnce().To("hub"));

            _h.Start("hub");
            _h.Select(0);   // "First" spent; only "Second" is left, so it is forced

            LogAssert.Expect(LogType.Error, new Regex("already seen"));
            _h.Select(0);   // "Second" spent; the hub now has nothing to offer

            Assert.AreEqual(1, _h.EndedCount, "the dialogue ends rather than hanging");
        }

        // =====================================================================
        // Start cards and caches
        // =====================================================================

        [Test]
        public void StartCardsAreGroupedByCollectionLabel()
        {
            _h.SeedCollection("col1", "Zebra chapter").SeedCollection("col2", "Alpha chapter").Configure();
            _h.Store.StartCards.Add(new StartCardRow
            {
                DocumentId = "s1", CollectionId = "col1", Label = "In Zebra",
            });
            _h.Store.StartCards.Add(new StartCardRow
            {
                DocumentId = "s2", CollectionId = "col2", Label = "In Alpha",
            });

            var cards = _h.GetStartCards();

            CollectionAssert.AreEqual(new[] { "Alpha chapter", "Zebra chapter" },
                cards.Select(c => c.CollectionLabel).ToList());
            StringAssert.Contains("In Alpha", cards[0].Label);
            StringAssert.Contains("s2", cards[0].Label, "the card id disambiguates repeated labels");
        }

        [Test]
        public void AStartCardWithoutALabelGetsADefaultOne()
        {
            _h.Configure();
            _h.Store.StartCards.Add(new StartCardRow
            {
                DocumentId = "s1", CollectionId = "col1", Label = null,
            });

            StringAssert.StartsWith("Start", _h.GetStartCards()[0].Label);
        }

        [Test]
        public void EntitiesAreLoadedFromTheDatabaseIntoTheCache()
        {
            _h.SeedEntity("narrator", false, "The Narrator").Configure();

            var entity = _h.Runtime.GetEntity("narrator");
            Assert.IsNotNull(entity);
            Assert.AreEqual("The Narrator", (string)entity["label"]);
            Assert.IsNull(_h.Runtime.GetEntity("nobody"));
        }

        [Test]
        public void EvalExpressionEvaluatesAgainstTheBindings()
        {
            _h.Configure();
            BindSpy();

            Assert.AreEqual(true, _h.Runtime.EvalExpression("Fn.g.True()"));
        }

        [Test]
        public void ListReferencesResolveAgainstRealSyncedCollectionManifests()
        {
            // Regression test: SeedCollection puts the Ls.* name in the payload, which
            // real synced content never does — the name only lives in the document's
            // own identifier column. That gap meant every Ls.* list silently failed to
            // load, so e.g. a skill check's threshold (Ls.global.thresholds.heroic)
            // always evaluated to null and, via the double-fallback, to zero.
            _h.SeedCollectionByIdentifier("lists-col", "global", "Global lists")
              .SeedListSpec("lists-col", "thresholds", "Thresholds",
                  ("easy", 3), ("heroic", 9))
              .Configure();

            Assert.AreEqual(9d, _h.Runtime.EvalExpression("Ls.global.thresholds.heroic"));
            Assert.AreEqual(3d, _h.Runtime.EvalExpression("Ls.global.thresholds.easy"));
        }
    }
}
