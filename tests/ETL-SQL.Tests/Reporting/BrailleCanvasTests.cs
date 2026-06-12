using ETL_SQL.Reporting.Renderers;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    /// <summary>The braille plotting surface used by the terminal line chart.</summary>
    public class BrailleCanvasTests
    {
        [Fact]
        public void Dimensions_AreEightTimesDenserThanCells()
        {
            var c = new BrailleCanvas(50, 12);
            Assert.Equal(100, c.DotWidth);
            Assert.Equal(48, c.DotHeight);
        }

        [Fact]
        public void EmptyCell_RendersAsSpace()
        {
            var c = new BrailleCanvas(2, 1);
            Assert.Equal(' ', c.ToLines()[0][0]);
        }

        [Fact]
        public void Set_TopLeftDot_ProducesBrailleDotOne()
        {
            var c = new BrailleCanvas(2, 1);
            c.Set(0, 0);
            Assert.Equal('⠁', c.ToLines()[0][0]); // ⠁
        }

        [Fact]
        public void Set_CombinesDotsWithinACell()
        {
            var c = new BrailleCanvas(2, 1);
            c.Set(0, 0); // dot 1 (0x01)
            c.Set(1, 3); // dot 8 (0x80)
            Assert.Equal('⢁', c.ToLines()[0][0]); // ⢁
        }

        [Fact]
        public void Line_Horizontal_FillsTopRowDots()
        {
            var c = new BrailleCanvas(4, 1);
            c.Line(0, 0, 3, 0);
            Assert.StartsWith("⠉⠉", c.ToLines()[0]); // ⠉⠉
        }

        [Fact]
        public void ToRenderable_WithColor_IsValidMarkup()
        {
            var c = new BrailleCanvas(2, 1);
            c.Set(0, 0, "blue");
            Assert.NotNull(c.ToRenderable()); // builds Markup internally; invalid tokens would throw
        }
    }
}
