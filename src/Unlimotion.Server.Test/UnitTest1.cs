using NUnit.Framework;
using SemanticVersioning;
using System.Collections.Generic;
using System.Linq;

namespace Unlimotion.Server.Test
{
    public class Tests
    {
        [Test]
        [TestCase(new[] { "5.0.5" }, "5.0.6", "5.0.5")]
        [TestCase(new[] { "5.0.7" }, "5.0.6", "5.0.7")]
        [TestCase(new[] { "5.0.7-beta.3" }, "5.0.6", "5.0.7-beta.3")]
        [TestCase(new[] { "5.0.7" }, "5.0.6-beta.2", "5.0.7")]
        [TestCase(new[] { "5.0.66" }, "5.0.7", "5.0.66")]
        [TestCase(new[] { "5.0.99" }, "5.0.7", "5.0.99")]
        [TestCase(new[] { "5.0.0" }, "5.0.5", "5.0.0")]
        [TestCase(new[] { "5.4.0" }, "5.5.5", "")]
        [TestCase(new[] { "6.0.1" }, "5.5.5", "")]
        public void GetNearestVersionTest(string[] installedVersions, string targetVersion, string expectedVersion)
        {
            var versions = new DotNetVersionHelper(new FakeDotNetVersion(installedVersions));
            var version = versions.GetNearestDotNetVersion(targetVersion);
            Assert.AreEqual(expectedVersion, version);
        }

        public class FakeDotNetVersion : IHaveVersions
        {
            public FakeDotNetVersion(string[] arg)
            {
                Versions = arg.Select(s => new Version(s)).ToList();
            }

            public IEnumerable<Version> Versions { get; set; }
        }
    }
}