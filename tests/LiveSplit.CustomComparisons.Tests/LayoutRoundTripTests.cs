using System.IO;
using System.Linq;
using System.Xml;
using Xunit;

namespace LiveSplit.CustomComparisons.Tests
{
    public class LayoutRoundTripTests
    {
        [Fact]
        public void An_unreadable_entry_survives_save_and_load_verbatim()
        {
            string path = Path.Combine(AppContextBase(), "Fixtures", "unreadable-layout.xml");
            var original = new XmlDocument();
            original.Load(path);
            string unreadable = ComparisonText(original.DocumentElement, "keep-this-verbatim");

            using (Session session = LiveSplitHarness.Open("edited-clone.lss"))
            {
                session.Component.SetSettings(original.DocumentElement);

                var savedDocument = new XmlDocument();
                XmlNode saved = session.Component.GetSettings(savedDocument);
                Assert.Equal(unreadable, ComparisonText(saved, "keep-this-verbatim"));
                Assert.False(session.Component.Library.Entries.Single(e => e.Json.Contains("keep-this-verbatim")).IsReadable);
                Assert.Contains(session.Component.Library.Entries, e => e.IsReadable && e.Name == "Kept");

                using (Session reloaded = LiveSplitHarness.Open("edited-clone.lss"))
                {
                    reloaded.Component.SetSettings(saved);
                    var savedAgain = new XmlDocument();
                    Assert.Equal(unreadable, ComparisonText(reloaded.Component.GetSettings(savedAgain), "keep-this-verbatim"));
                    Assert.Equal(unreadable, reloaded.Component.Library.Entries.Single(e => !e.IsReadable).Json);
                }
            }
        }

        private static string AppContextBase() => System.AppContext.BaseDirectory;

        private static string ComparisonText(XmlNode settings, string marker)
        {
            return settings["Comparisons"].ChildNodes
                .OfType<XmlElement>()
                .Where(e => e.Name == "Comparison")
                .Select(e => e.InnerText)
                .Single(text => text.Contains(marker));
        }
    }
}
