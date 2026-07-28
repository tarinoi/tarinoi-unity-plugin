using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tarinoi.Tests
{
    public class TarinoiLogTests
    {
        static readonly Regex Anything = new Regex(".*");

        TarinoiLogLevel _originalLevel;

        [SetUp]
        public void SetUp()
        {
            _originalLevel = TarinoiLog.Level;
        }

        [TearDown]
        public void TearDown()
        {
            TarinoiLog.Level = _originalLevel;
        }

        [Test]
        public void DefaultLevelIsInfo()
        {
            Assert.AreEqual(TarinoiLogLevel.Info, _originalLevel);
        }

        [Test]
        public void InfoIsEmittedAtInfoLevel()
        {
            TarinoiLog.Level = TarinoiLogLevel.Info;
            LogAssert.Expect(LogType.Log, new Regex(@"\[Tarinoi\] hello"));
            TarinoiLog.Info("hello");
        }

        [Test]
        public void DebugIsSuppressedAtInfoLevel()
        {
            TarinoiLog.Level = TarinoiLogLevel.Info;
            TarinoiLog.Debug("should not appear");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void DebugIsEmittedAtDebugLevel()
        {
            TarinoiLog.Level = TarinoiLogLevel.Debug;
            LogAssert.Expect(LogType.Log, new Regex(@"\[DEBUG\].*\[Tarinoi\] visible"));
            TarinoiLog.Debug("visible");
        }

        [Test]
        public void WarnUsesTheWarningChannel()
        {
            TarinoiLog.Level = TarinoiLogLevel.Warn;
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Tarinoi\] careful"));
            TarinoiLog.Warn("careful");
        }

        [Test]
        public void ErrorUsesTheErrorChannel()
        {
            TarinoiLog.Level = TarinoiLogLevel.Error;
            LogAssert.Expect(LogType.Error, new Regex(@"\[Tarinoi\] broken"));
            TarinoiLog.Error("broken");
        }

        [Test]
        public void OffSuppressesEverything()
        {
            TarinoiLog.Level = TarinoiLogLevel.Off;
            TarinoiLog.Debug("x");
            TarinoiLog.Info("x");
            TarinoiLog.Warn("x");
            TarinoiLog.Error("x");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void WarnIsSuppressedAtErrorLevel()
        {
            TarinoiLog.Level = TarinoiLogLevel.Error;
            TarinoiLog.Warn("quiet");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
