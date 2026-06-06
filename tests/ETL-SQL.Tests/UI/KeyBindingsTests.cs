using System;
using System.Linq;
using Xunit;
using ETL_SQL.TUI.UI;

namespace ETL_SQL.Tests.UI
{
    /// <summary>
    /// Guards the F1 help catalog (<see cref="KeyBindings"/>) against drift and
    /// against producing a card that overflows the help panel. The help overlay
    /// renders from this catalog, so these structural checks stand in for
    /// pixel-level rendering tests.
    /// </summary>
    public class KeyBindingsTests
    {
        // Bindings that were previously missing/mis-filed in the hand-maintained
        // help and are the most likely to silently drift again. If any of these
        // is added to InputHandler dispatch, it must stay documented here.
        [Theory]
        [InlineData("F9 / Ctrl+B")]        // toggle file explorer
        [InlineData("Ctrl+I / Alt+F / F12")] // format (F12 was undocumented)
        [InlineData("Ctrl+F5")]            // run selected text
        [InlineData("Alt+R")]              // report preview
        [InlineData("Ctrl+T")]             // new tab
        [InlineData("Ctrl+W")]             // close tab
        [InlineData("Alt+← / →")]          // switch tab
        [InlineData("Ctrl+P")]             // export CSV
        public void Catalog_Documents_DriftProneBinding(string keys)
        {
            Assert.Contains(KeyBindings.All, b => b.Keys == keys);
        }

        [Fact]
        public void Catalog_HasDedicatedExplorerSection()
        {
            Assert.NotEmpty(KeyBindings.InCategory(KeyCategory.Explorer));
        }

        [Fact]
        public void Catalog_EveryCategory_HasEntriesAndTitle()
        {
            foreach (KeyCategory cat in Enum.GetValues(typeof(KeyCategory)))
            {
                Assert.NotEmpty(KeyBindings.InCategory(cat));
                Assert.True(KeyBindings.CategoryTitles.ContainsKey(cat),
                    $"Missing title for category {cat}");
            }
        }

        [Fact]
        public void Catalog_NoDuplicateKeys_WithinCategory()
        {
            foreach (var group in KeyBindings.All.GroupBy(b => b.Category))
            {
                var dups = group.GroupBy(b => b.Keys)
                                .Where(g => g.Count() > 1)
                                .Select(g => g.Key)
                                .ToList();
                Assert.True(dups.Count == 0,
                    $"Duplicate keys in {group.Key}: {string.Join(", ", dups)}");
            }
        }

        [Fact]
        public void HelpColumnLayout_CoversEveryCategoryExactlyOnce()
        {
            var flat = KeyBindings.HelpColumnLayout().SelectMany(c => c).ToList();
            var all = Enum.GetValues(typeof(KeyCategory)).Cast<KeyCategory>().ToList();

            Assert.Equal(all.Count, flat.Count);
            Assert.Equal(all.OrderBy(x => x), flat.OrderBy(x => x));
        }

        [Fact]
        public void HelpColumnLayout_EveryColumn_FitsPanelHeight()
        {
            foreach (var col in KeyBindings.HelpColumnLayout())
            {
                int height = KeyBindings.ColumnHeight(col);
                Assert.True(height <= KeyBindings.MaxHelpColumnRows,
                    $"Column height {height} exceeds budget {KeyBindings.MaxHelpColumnRows}");
            }
        }

        [Fact]
        public void Essentials_AreNonEmpty_AndPartOfCatalog()
        {
            Assert.NotEmpty(KeyBindings.Essentials);
            Assert.All(KeyBindings.Essentials, b => Assert.Contains(b, KeyBindings.All));
        }

        [Fact]
        public void FocusAndPanelBindings_CarryLiveAnnotations()
        {
            var f6 = KeyBindings.All.Single(b => b.Keys == "F6");
            var f4 = KeyBindings.All.Single(b => b.Keys == "F4");

            Assert.NotNull(f6.LiveAnnotation);
            Assert.NotNull(f4.LiveAnnotation);
        }
    }
}
