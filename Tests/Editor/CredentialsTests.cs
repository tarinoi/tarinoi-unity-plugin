using System.IO;
using NUnit.Framework;
using Tarinoi.Sync;

namespace Tarinoi.Tests
{
    /// <summary>
    /// Credentials live at a fixed path outside the project, so these tests back up and
    /// restore any real token rather than clobbering the developer's own.
    /// </summary>
    public class CredentialsTests
    {
        string _backup;
        bool _hadFile;

        [SetUp]
        public void SetUp()
        {
            _hadFile = File.Exists(Credentials.FilePath);
            _backup = _hadFile ? File.ReadAllText(Credentials.FilePath) : null;

            if (_hadFile)
            {
                File.Delete(Credentials.FilePath);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadFile)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Credentials.FilePath));
                File.WriteAllText(Credentials.FilePath, _backup);
            }
            else if (File.Exists(Credentials.FilePath))
            {
                File.Delete(Credentials.FilePath);
            }
        }

        [Test]
        public void ReadingWithNoFileReturnsEmpty()
        {
            Assert.AreEqual("", Credentials.Read(Credentials.ApiKeyName));
            Assert.IsFalse(Credentials.Has(Credentials.ApiKeyName));
        }

        [Test]
        public void WriteThenReadRoundTrips()
        {
            Assert.IsTrue(Credentials.Write(Credentials.ApiKeyName, "secret-token"));
            Assert.AreEqual("secret-token", Credentials.Read(Credentials.ApiKeyName));
            Assert.IsTrue(Credentials.Has(Credentials.ApiKeyName));
        }

        [Test]
        public void CredentialsAreStoredOutsideTheProject()
        {
            // The whole point of this class: a token must not be committable or shippable.
            StringAssert.DoesNotContain(UnityEngine.Application.dataPath, Credentials.FilePath);
        }

        [Test]
        public void RewritingReplacesRatherThanDuplicates()
        {
            Credentials.Write(Credentials.ApiKeyName, "first");
            Credentials.Write(Credentials.ApiKeyName, "second");

            Assert.AreEqual("second", Credentials.Read(Credentials.ApiKeyName));
            Assert.AreEqual(1, File.ReadAllLines(Credentials.FilePath).Length);
        }

        [Test]
        public void OtherKeysArePreservedOnWrite()
        {
            Credentials.Write("other_key", "keep-me");
            Credentials.Write(Credentials.ApiKeyName, "new-token");

            Assert.AreEqual("keep-me", Credentials.Read("other_key"));
            Assert.AreEqual("new-token", Credentials.Read(Credentials.ApiKeyName));
        }

        [Test]
        public void KeyMatchingIsCaseInsensitive()
        {
            File.WriteAllText(Credentials.FilePath, "API_KEY=upper-case-key\n");
            Assert.AreEqual("upper-case-key", Credentials.Read("api_key"));
        }

        [Test]
        public void SurroundingWhitespaceIsTrimmed()
        {
            File.WriteAllText(Credentials.FilePath, "  api_key=  padded-token  \n");
            Assert.AreEqual("padded-token", Credentials.Read(Credentials.ApiKeyName));
        }

        [Test]
        public void ValuesContainingEqualsSurviveIntact()
        {
            Credentials.Write(Credentials.ApiKeyName, "abc==def");
            Assert.AreEqual("abc==def", Credentials.Read(Credentials.ApiKeyName));
        }

        [Test]
        public void UnknownKeysReadAsEmpty()
        {
            Credentials.Write(Credentials.ApiKeyName, "token");
            Assert.AreEqual("", Credentials.Read("nope"));
        }

        [Test]
        public void ClearRemovesOnlyTheNamedKey()
        {
            Credentials.Write(Credentials.ApiKeyName, "token");
            Credentials.Write("other_key", "keep-me");

            Assert.IsTrue(Credentials.Clear(Credentials.ApiKeyName));
            Assert.AreEqual("", Credentials.Read(Credentials.ApiKeyName));
            Assert.AreEqual("keep-me", Credentials.Read("other_key"));
        }

        [Test]
        public void ClearingAnAbsentKeyReportsNothingRemoved()
        {
            Credentials.Write("other_key", "keep-me");
            Assert.IsFalse(Credentials.Clear(Credentials.ApiKeyName));
        }
    }
}
