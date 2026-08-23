using System;
using System.Collections.Generic;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace ETL_SQL.Reporting.Renderers
{
    /// <summary>
    /// A monochrome-per-cell braille plotting surface. Each terminal cell holds a 2×4 grid of
    /// braille dots (U+2800…U+28FF), giving ~8× the resolution of block characters — ideal for
    /// smooth line charts in the terminal. Colors are passed as Spectre markup tokens (e.g.
    /// "blue") so rendering needs no Color→markup conversion.
    /// </summary>
    public sealed class BrailleCanvas
    {
        // Dot bit value indexed by [columnInCell 0..1, rowInCell 0..3].
        private static readonly int[,] DotBit =
        {
            { 0x01, 0x02, 0x04, 0x40 },
            { 0x08, 0x10, 0x20, 0x80 },
        };

        private readonly int _cellW, _cellH;
        private readonly byte[,] _bits;
        private readonly string?[,] _color;

        public int DotWidth => _cellW * 2;
        public int DotHeight => _cellH * 4;

        public BrailleCanvas(int cellW, int cellH)
        {
            _cellW = Math.Max(1, cellW);
            _cellH = Math.Max(1, cellH);
            _bits = new byte[_cellW, _cellH];
            _color = new string?[_cellW, _cellH];
        }

        public void Set(int px, int py, string? color = null)
        {
            if (px < 0 || py < 0 || px >= DotWidth || py >= DotHeight) return;
            int cx = px / 2, cy = py / 4;
            _bits[cx, cy] |= (byte)DotBit[px % 2, py % 4];
            if (color != null) _color[cx, cy] = color; // last writer wins
        }

        public void Line(int x0, int y0, int x1, int y1, string? color = null)
        {
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            while (true)
            {
                Set(x0, y0, color);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>Plain braille rows (no color) — primarily for tests.</summary>
        public List<string> ToLines()
        {
            var lines = new List<string>();
            for (int cy = 0; cy < _cellH; cy++)
            {
                var sb = new StringBuilder();
                for (int cx = 0; cx < _cellW; cx++)
                    sb.Append(_bits[cx, cy] == 0 ? ' ' : (char)(0x2800 + _bits[cx, cy]));
                lines.Add(sb.ToString());
            }
            return lines;
        }

        public IRenderable ToRenderable()
        {
            var rows = new List<IRenderable>();
            for (int cy = 0; cy < _cellH; cy++)
            {
                var sb = new StringBuilder();
                for (int cx = 0; cx < _cellW; cx++)
                {
                    byte b = _bits[cx, cy];
                    if (b == 0) { sb.Append(' '); continue; }
                    char ch = (char)(0x2800 + b);
                    string? token = _color[cx, cy];
                    sb.Append(token == null ? ch.ToString() : $"[{token}]{ch}[/]");
                }
                rows.Add(new Markup(sb.ToString()));
            }
            return new Rows(rows);
        }

        public IRenderable ToRenderableWithAxis(decimal min, decimal max, int labelWidth = 6)
        {
            var rows = new List<IRenderable>();
            var mid = (min + max) / 2m;
            for (int cy = 0; cy < _cellH; cy++)
            {
                var sb = new StringBuilder();
                if (cy == 0)
                {
                    sb.Append($"[grey]{FormatTick(max, labelWidth)} ┼[/] ");
                }
                else if (cy == _cellH / 2)
                {
                    sb.Append($"[grey]{FormatTick(mid, labelWidth)} ┼[/] ");
                }
                else if (cy == _cellH - 1)
                {
                    sb.Append($"[grey]{FormatTick(min, labelWidth)} ┼[/] ");
                }
                else
                {
                    sb.Append($"[grey]{new string(' ', labelWidth)} │[/] ");
                }

                for (int cx = 0; cx < _cellW; cx++)
                {
                    byte b = _bits[cx, cy];
                    if (b == 0) { sb.Append(' '); continue; }
                    char ch = (char)(0x2800 + b);
                    string? token = _color[cx, cy];
                    sb.Append(token == null ? ch.ToString() : $"[{token}]{ch}[/]");
                }
                rows.Add(new Markup(sb.ToString()));
            }
            return new Rows(rows);
        }

        private static string FormatTick(decimal value, int width)
        {
            var formatted = Math.Abs(value) >= 1000m && Math.Abs(value) < 1_000_000m && value % 100 == 0
                ? (value / 1000m).ToString("G", System.Globalization.CultureInfo.InvariantCulture) + "k"
                : value.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
            return formatted.Length > width ? formatted[..width] : formatted.PadLeft(width);
        }
    }
}
