using System.Text.RegularExpressions;
using NUnit.Framework;
using Tarinoi.Data;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tarinoi.Tests
{
    public class DataVersionTests
    {
        static readonly Regex MajorMismatch = new Regex("MAJOR data format mismatch");

        DataVersion _dv;

        [SetUp]
        public void SetUp()
        {
            _dv = new DataVersion();
            TarinoiLog.Level = TarinoiLogLevel.Info;
        }

        [Test]
        public void MatchingVersionIsCompatible()
        {
            Assert.AreEqual("", _dv.Check(DataVersion.SupportedVersion),
                "matching data_version should not be fatal");
        }

        [Test]
        public void NullDataVersionIsIgnored()
        {
            Assert.AreEqual("", _dv.Check(null),
                "null data_version (pre-versioning legacy doc) should not be checked");
        }

        [Test]
        public void EmptyDataVersionIsIgnored()
        {
            Assert.AreEqual("", _dv.Check(""), "empty data_version should not be checked");
        }

        [Test]
        public void PatchMismatchIsNotFatal()
        {
            Assert.AreEqual("", _dv.Check("1.0.1"), "patch mismatch should not be fatal");
        }

        [Test]
        public void MinorMismatchIsNotFatal()
        {
            LogAssert.Expect(LogType.Warning, new Regex("minor data format mismatch"));
            Assert.AreEqual("", _dv.Check("1.1.0"), "minor mismatch should not be fatal");
        }

        [Test]
        public void MajorMismatchIsFatal()
        {
            LogAssert.Expect(LogType.Error, MajorMismatch);
            Assert.AreNotEqual("", _dv.Check("2.0.0"),
                "major mismatch must return a non-empty fatal error");
        }

        [Test]
        public void UnparseableVersionIsNotFatal()
        {
            LogAssert.Expect(LogType.Warning, new Regex("unparseable data_version"));
            Assert.AreEqual("", _dv.Check("not-a-version"),
                "unparseable version should be logged and skipped, not fatal");
        }

        [Test]
        public void TooFewComponentsIsNotFatal()
        {
            LogAssert.Expect(LogType.Warning, new Regex("unparseable data_version"));
            Assert.AreEqual("", _dv.Check("1.0"));
        }

        [Test]
        public void RepeatedMajorMismatchStillReturnsFatalEachTime()
        {
            // The message is only logged once per distinct version, but every call must
            // still report the fatal condition so callers keep aborting. Exactly one
            // error is expected; a second would be an unexpected log and fail the test.
            LogAssert.Expect(LogType.Error, MajorMismatch);

            var first = _dv.Check("2.0.0");
            var second = _dv.Check("2.0.0");

            Assert.AreNotEqual("", first);
            Assert.AreNotEqual("", second);
            Assert.AreEqual(first, second);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase("1.0.0", 1, 0, 0)]
        [TestCase("0.0.0", 0, 0, 0)]
        [TestCase("12.34.56", 12, 34, 56)]
        public void TryParseAcceptsWellFormedVersions(string version, int major, int minor, int patch)
        {
            Assert.IsTrue(DataVersion.TryParse(version, out var m, out var n, out var p));
            Assert.AreEqual(major, m);
            Assert.AreEqual(minor, n);
            Assert.AreEqual(patch, p);
        }

        [TestCase("")]
        [TestCase("1.0")]
        [TestCase("1.0.0.0")]
        [TestCase("1.0.x")]
        [TestCase("-1.0.0")]
        [TestCase("1. 0.0")]
        public void TryParseRejectsMalformedVersions(string version)
        {
            Assert.IsFalse(DataVersion.TryParse(version, out _, out _, out _));
        }
    }
}
