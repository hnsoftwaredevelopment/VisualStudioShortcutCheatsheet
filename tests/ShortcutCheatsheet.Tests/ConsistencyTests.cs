using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ExtensionCommon.VisualStudio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ShortcutCheatsheet.Tests
{
    /// <summary>Values that live in two places (C# and .vsct, or the shared code) and must stay equal.</summary>
    [TestClass]
    public class ConsistencyTests
    {
        private static readonly XNamespace Vsct = "http://schemas.microsoft.com/VisualStudio/2005-10-18/CommandTable";

        [TestMethod]
        public void VsctGuidsMatchPackageConstants()
        {
            var symbols = XDocument.Load(Path.Combine(TestPaths.RepositoryRoot, @"src\ShortcutCheatsheet\ShortcutCheatsheetPackage.vsct"))
                .Descendants(Vsct + "GuidSymbol")
                .ToDictionary(e => (string)e.Attribute("name"), e => e);

            Assert.AreEqual(new Guid(ShortcutCheatsheetPackage.PackageGuidString), new Guid((string)symbols["guidShortcutCheatsheetPackage"].Attribute("value")));
            Assert.AreEqual(ShortcutCheatsheetPackage.CommandSet, new Guid((string)symbols["guidShortcutCheatsheetCmdSet"].Attribute("value")));

            string openWindow = (string)symbols["guidShortcutCheatsheetCmdSet"].Elements(Vsct + "IDSymbol")
                .Single(e => (string)e.Attribute("name") == "cmdidOpenWindow").Attribute("value");
            Assert.AreEqual(ShortcutCheatsheetPackage.OpenWindowCommandId, Convert.ToInt32(openWindow, 16));
        }

        [TestMethod]
        public void SharedCodeIsCompiledIn()
        {
            // The submodule external\ExtensionCommon must be checked out; otherwise this does not compile at all.
            const string ide = @"C:\VS\Common7\IDE\";
            Assert.IsTrue(InstalledExtensions.IsVisualStudioFolder(@"C:\VS\Common7\IDE\CommonExtensions\Microsoft\Editor", ide));
            Assert.IsFalse(InstalledExtensions.IsVisualStudioFolder(@"C:\VS\Common7\IDE\Extensions\qbhos1mt.vs5", ide));
            Assert.IsFalse(InstalledExtensions.IsVisualStudioFolder(@"C:\Users\me\AppData\Local\Microsoft\VisualStudio\18.0_x\Extensions\abc", ide));
        }
    }
}
