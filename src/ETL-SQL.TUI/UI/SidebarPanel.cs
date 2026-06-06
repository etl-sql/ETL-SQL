using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using ETL_SQL.Core;

namespace ETL_SQL.TUI.UI
{
    public class SidebarNode
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsDirectory { get; set; }
        public bool IsExpanded { get; set; }
        public bool IsLoaded { get; set; }
        public List<SidebarNode> Children { get; set; } = new();
    }

    public class FlatItem
    {
        public SidebarNode Node { get; set; } = new();
        public int Depth { get; set; }
    }

    public class SidebarPanel : IUIComponent
    {
        private readonly EditorRenderer _renderer;
        private readonly Evaluator _evaluator;

        public List<SidebarNode> RootNodes { get; } = new();
        public int SelectedIndex { get; set; } = 0;
        private string _lastRootPath = "";

        public SidebarPanel(EditorRenderer renderer, Evaluator evaluator)
        {
            _renderer = renderer;
            _evaluator = evaluator;
        }

        public void Initialize(string? currentFilePath)
        {
            string rootPath = Directory.GetCurrentDirectory();
            if (!string.IsNullOrEmpty(currentFilePath))
            {
                try
                {
                    string full = Path.GetFullPath(currentFilePath);
                    string? dir = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        rootPath = dir;
                    }
                }
                catch { }
            }

            if (rootPath == _lastRootPath && RootNodes.Count > 0)
            {
                return; // Already initialized for this directory
            }

            _lastRootPath = rootPath;
            RootNodes.Clear();

            var rootNode = new SidebarNode
            {
                Path = rootPath,
                Name = Path.GetFileName(rootPath) ?? rootPath,
                IsDirectory = true,
                IsExpanded = true
            };
            LoadChildren(rootNode);
            RootNodes.Add(rootNode);
            SelectedIndex = 0;
        }

        public void LoadChildren(SidebarNode node)
        {
            if (node.IsLoaded) return;
            node.Children.Clear();
            try
            {
                if (Directory.Exists(node.Path))
                {
                    var dirs = Directory.GetDirectories(node.Path)
                        .Select(d => new DirectoryInfo(d))
                        .Where(di => (di.Attributes & FileAttributes.Hidden) == 0)
                        .OrderBy(di => di.Name)
                        .ToList();

                    foreach (var dir in dirs)
                    {
                        node.Children.Add(new SidebarNode
                        {
                            Path = dir.FullName,
                            Name = dir.Name,
                            IsDirectory = true
                        });
                    }

                    var files = Directory.GetFiles(node.Path)
                        .Select(f => new FileInfo(f))
                        .Where(fi => (fi.Attributes & FileAttributes.Hidden) == 0)
                        .OrderBy(fi => fi.Name)
                        .ToList();

                    foreach (var file in files)
                    {
                        node.Children.Add(new SidebarNode
                        {
                            Path = file.FullName,
                            Name = file.Name,
                            IsDirectory = false
                        });
                    }
                }
            }
            catch { }
            node.IsLoaded = true;
        }

        public List<FlatItem> GetFlatVisibleItems()
        {
            var list = new List<FlatItem>();
            foreach (var root in RootNodes)
            {
                AddFlatItem(root, 0, list);
            }
            return list;
        }

        private void AddFlatItem(SidebarNode node, int depth, List<FlatItem> list)
        {
            list.Add(new FlatItem { Node = node, Depth = depth });
            if (node.IsDirectory && node.IsExpanded)
            {
                if (!node.IsLoaded)
                {
                    LoadChildren(node);
                }
                foreach (var child in node.Children)
                {
                    AddFlatItem(child, depth + 1, list);
                }
            }
        }

        public void Render(IConsoleInterface console, int x, int y, int width, int height, int scrollRow = 0)
        {
            // Clear space
            for (int i = 0; i < height; i++)
            {
                console.ClearLine(x, y + i, width);
            }

            var items = GetFlatVisibleItems();
            SelectedIndex = Math.Clamp(SelectedIndex, 0, Math.Max(0, items.Count - 1));

            int maxVisibleItems = height - 2; // Subtract border heights
            int startIdx = scrollRow;
            int endIdx = Math.Min(items.Count, startIdx + maxVisibleItems);

            var contentBuilder = new System.Text.StringBuilder();

            for (int i = startIdx; i < endIdx; i++)
            {
                var item = items[i];
                string indent = new string(' ', item.Depth * 2);
                string prefix = item.Node.IsDirectory ? (item.Node.IsExpanded ? "▼ 📁 " : "▶ 📁 ") : "  📄 ";
                string displayName = item.Node.Name;

                // Truncate to fit width
                int maxTextLen = width - 4 - (item.Depth * 2) - prefix.Length;
                if (maxTextLen > 0 && displayName.Length > maxTextLen)
                {
                    displayName = displayName.Substring(0, maxTextLen - 3) + "...";
                }

                string lineContent = indent + prefix + displayName;
                // Pad to full width to ensure selection highlight stretches
                int padLen = width - 4; // account for panel borders and padding
                if (lineContent.Length < padLen)
                {
                    lineContent = lineContent.PadRight(padLen);
                }

                if (i == SelectedIndex)
                {
                    string selectStyle = _renderer.Focus == EditorFocus.Sidebar ? "black on yellow" : "white on blue";
                    contentBuilder.AppendLine($"[{selectStyle}]{Markup.Escape(lineContent)}[/]");
                }
                else
                {
                    string colorStyle = item.Node.IsDirectory ? "yellow" : "white";
                    contentBuilder.AppendLine($"[{colorStyle}]{Markup.Escape(lineContent)}[/]");
                }
            }

            // Fill empty lines with spaces
            int printedCount = endIdx - startIdx;
            for (int i = printedCount; i < maxVisibleItems; i++)
            {
                contentBuilder.AppendLine();
            }

            string borderStyle = _renderer.Focus == EditorFocus.Sidebar
                ? TuiTheme.Instance.Ui.PanelFocusedBorder
                : TuiTheme.Instance.Ui.PanelUnfocusedBorder;

            var panel = new Panel(contentBuilder.ToString().TrimEnd())
            {
                Header = new PanelHeader($"[bold]Explorer[/]"),
                Height = height,
                Width = width,
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0, 1, 0)
            };
            panel.BorderColor(TuiTheme.Instance.GetColor(borderStyle, Color.Grey));

            console.SetCursorPosition(x, y);
            console.WriteWidget(panel);
        }

        public async Task HandleEnter(ConsoleEditor editor)
        {
            var items = GetFlatVisibleItems();
            if (SelectedIndex >= 0 && SelectedIndex < items.Count)
            {
                var item = items[SelectedIndex];
                if (item.Node.IsDirectory)
                {
                    item.Node.IsExpanded = !item.Node.IsExpanded;
                    if (item.Node.IsExpanded && !item.Node.IsLoaded)
                    {
                        LoadChildren(item.Node);
                    }
                    _renderer.ForceFullRepaint();
                }
                else
                {
                    await editor.OpenFileInTab(item.Node.Path);
                    _renderer.Focus = EditorFocus.Editor;
                    _renderer.ShowStatus($"Opened: {Path.GetFileName(item.Node.Path)}");
                    _renderer.ForceFullRepaint();
                }
            }
        }

        public void HandleLeft()
        {
            var items = GetFlatVisibleItems();
            if (SelectedIndex >= 0 && SelectedIndex < items.Count)
            {
                var item = items[SelectedIndex];
                if (item.Node.IsDirectory && item.Node.IsExpanded)
                {
                    item.Node.IsExpanded = false;
                    _renderer.ForceFullRepaint();
                }
            }
        }

        public void HandleRight()
        {
            var items = GetFlatVisibleItems();
            if (SelectedIndex >= 0 && SelectedIndex < items.Count)
            {
                var item = items[SelectedIndex];
                if (item.Node.IsDirectory && !item.Node.IsExpanded)
                {
                    item.Node.IsExpanded = true;
                    if (!item.Node.IsLoaded)
                    {
                        LoadChildren(item.Node);
                    }
                    _renderer.ForceFullRepaint();
                }
            }
        }
    }
}
