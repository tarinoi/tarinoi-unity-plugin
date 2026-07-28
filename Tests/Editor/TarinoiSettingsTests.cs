using NUnit.Framework;

namespace Tarinoi.Tests
{
    public class TarinoiSettingsTests
    {
        [Test]
        public void InstanceNeverReturnsNull()
        {
            Assert.IsNotNull(TarinoiSettings.Instance,
                "callers must be able to read settings before the asset is created");
        }

        [Test]
        public void DefaultsAreSafeForAFreshProject()
        {
            var settings = UnityEngine.ScriptableObject.CreateInstance<TarinoiSettings>();

            Assert.AreEqual("", settings.apiPath);
            Assert.IsFalse(settings.offlineMode);
            Assert.IsFalse(settings.committedOnly);
            Assert.IsFalse(settings.skipTlsVerify, "TLS verification must default to on");
            Assert.IsFalse(settings.pollEnabled);
            Assert.AreEqual(10, settings.pollInterval);
            Assert.AreEqual(TarinoiLogLevel.Info, settings.logLevel);
            Assert.AreEqual("Assets/Tarinoi/Generated", settings.codegenOutputPath);
        }

        [TestCase("https://app.tarinoi.com/api/projects/proj123/documents", "proj123")]
        [TestCase("https://app.tarinoi.com/api/projects/proj123/documents/", "proj123")]
        [TestCase("https://app.tarinoi.com/api/projects/proj123", "proj123")]
        [TestCase("https://app.tarinoi.com/api/projects/proj123/", "proj123")]
        [TestCase("  https://app.tarinoi.com/api/projects/proj123/documents  ", "proj123")]
        [TestCase("https://app.tarinoi.com/api/projects/proj123/DOCUMENTS", "proj123")]
        public void ProjectIdIsTheSegmentBeforeDocuments(string path, string expected)
        {
            Assert.AreEqual(expected, TarinoiSettings.ProjectIdFromApiPath(path));
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("   ")]
        [TestCase("https://app.tarinoi.com")]
        public void ProjectIdIsEmptyWhenThePathCannotYieldOne(string path)
        {
            Assert.AreEqual("", TarinoiSettings.ProjectIdFromApiPath(path),
                "a misconfigured path must not produce a bogus project id");
        }

        [Test]
        public void ProjectIdReadsFromTheConfiguredApiPath()
        {
            var settings = UnityEngine.ScriptableObject.CreateInstance<TarinoiSettings>();
            settings.apiPath = "https://app.tarinoi.com/api/projects/my-game/documents";

            Assert.AreEqual("my-game", settings.ProjectId);
        }
    }
}
