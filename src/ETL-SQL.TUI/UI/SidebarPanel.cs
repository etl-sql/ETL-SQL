using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using ETL_SQL.Core;

namespace ETL_SQL.TUI.UI
{
    /// <summary>What a sidebar row represents — drives its icon, expand behaviour, and action.</summary>
    public enum SidebarNodeKind { Directory, File, ModeToggle, Refresh, Connection, Table, Column, TempTable, View, Group }

    public class SidebarNode
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsDirectory { get; set; }
        public bool IsExpanded { get; set; }
        public bool IsLoaded { get; set; }
        public bool IsParentNav { get; set; } // synthetic ".." entry that re-roots one level up
        public SidebarNodeKind Kind { get; set; } = SidebarNodeKind.Directory;
        /// <summary>For metadata leaves: the text inserted at the cursor when activated.</summary>
        public string? InsertText { get; set; }
        public List<SidebarNode> Children { get; set; } = new();

        /// <summary>Metadata branches and directories expand; leaves (file/column) don't.</summary>
        public bool IsExpandable => IsDirectory ||
            Kind is SidebarNodeKind.Connection or SidebarNodeKind.Table or SidebarNodeKind.TempTable
                  or SidebarNodeKind.View or SidebarNodeKind.Group;

        /// <summary>Metadata branches whose children are fetched on demand (vs. an eager Group).</summary>
        public bool IsLazyMetadata =>
            Kind is SidebarNodeKind.Connection or SidebarNodeKind.TempTable
                  or SidebarNodeKind.Table or SidebarNodeKind.View;
    }

    /// <summary>The sidebar's two browsing modes.</summary>
    public enum SidebarMode { Files, Metadata }

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

        public SidebarMode Mode { get; private set; } = SidebarMode.Files;
        public List<SidebarNode> MetadataRoots { get; } = new();
        private MetadataManager? _metadata;

        public SidebarPanel(EditorRenderer renderer, Evaluator evaluator)
        {
            _renderer = renderer;
            _evaluator = evaluator;
        }

        /// <summary>Supplies the metadata source for the schema-explorer mode (set once at startup).</summary>
        public void SetMetadata(MetadataManager metadata) => _metadata = metadata;

        /// <summary>Switches between the file explorer and the schema explorer, loading metadata on demand.</summary>
        public async Task ToggleModeAsync(string scriptText)
        {
            Mode = Mode == SidebarMode.Files ? SidebarMode.Metadata : SidebarMode.Files;
            SelectedIndex = 0;
            if (Mode == SidebarMode.Metadata) await BuildMetadataRootsAsync(scriptText);
            _renderer.ForceFullRepaint();
        }

        /// <summary>Reloads the schema tree from the current script's connections.</summary>
        public async Task RefreshMetadataAsync(string scriptText)
        {
            await BuildMetadataRootsAsync(scriptText);
            _renderer.ForceFullRepaint();
        }

        // Builds just the connection rows; tables/views/columns load lazily on expand so large
        // schemas don't pay an upfront cost. Triggered only by an explicit toggle/refresh.
        private async Task BuildMetadataRootsAsync(string scriptText)
        {
            MetadataRoots.Clear();
            if (_metadata == null) return;

            _metadata.RefreshConnections(scriptText, force: true);
            await Task.Yield();

            foreach (var conn in _metadata.GetConnections().OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
            {
                bool isTemp = conn.StartsWith("#");
                MetadataRoots.Add(new SidebarNode
                {
                    Name = conn,
                    Path = conn, // connection name, used as the lookup key when loading children
                    Kind = isTemp ? SidebarNodeKind.TempTable : SidebarNodeKind.Connection,
                    InsertText = conn
                });
            }

            _renderer.ShowStatus($"Schema: {MetadataRoots.Count} connection(s).");
        }

        /// <summary>
        /// Lazily fetches a metadata node's children on first expand: a connection's tables +
        /// (grouped) views, or a table/view's columns. Connection name is carried in Path.
        /// </summary>
        public async Task EnsureLoadedAsync(SidebarNode node)
        {
            if (node.IsLoaded || _metadata == null || !node.IsLazyMetadata) return;

            _renderer.ShowStatus($"Loading {node.Name}…");
            try
            {
                if (node.Kind is SidebarNodeKind.Connection or SidebarNodeKind.TempTable)
                {
                    bool isTemp = node.Kind == SidebarNodeKind.TempTable;
                    foreach (var table in (await _metadata.GetTablesAsync(node.Path)).OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
                        node.Children.Add(new SidebarNode
                        {
                            Name = table, Path = node.Path, Kind = SidebarNodeKind.Table,
                            InsertText = isTemp ? table : $"{node.Path}.{table}"
                        });

                    // Views, when present, live under a single "Views" group to keep them distinct.
                    var views = (await _metadata.GetViewsAsync(node.Path)).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();
                    if (views.Count > 0)
                    {
                        var group = new SidebarNode { Name = $"Views ({views.Count})", Kind = SidebarNodeKind.Group, IsLoaded = true };
                        foreach (var view in views)
                            group.Children.Add(new SidebarNode
                            {
                                Name = view, Path = node.Path, Kind = SidebarNodeKind.View,
                                InsertText = $"{node.Path}.{view}"
                            });
                        node.Children.Add(group);
                    }
                }
                else // Table or View → its columns
                {
                    foreach (var col in await _metadata.GetColumnsAsync(node.Path, node.Name))
                        node.Children.Add(new SidebarNode { Name = col, Kind = SidebarNodeKind.Column, InsertText = col });
                }
            }
            catch { }

            node.IsLoaded = true;
            _renderer.ShowStatus($"{node.Name}: {node.Children.Count} item(s).");
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

            var cap = TerminalCapabilities.Current;

            // A clickable mode toggle is always the first row (mouse- and keyboard-addressable).
            string swap = cap.Glyph("⇄", "<>");
            list.Add(new FlatItem
            {
                Node = new SidebarNode { Name = $"{swap} {(Mode == SidebarMode.Files ? "Switch to Schema" : "Switch to Files")}", Kind = SidebarNodeKind.ModeToggle },
                Depth = 0
            });

            if (Mode == SidebarMode.Metadata)
            {
                list.Add(new FlatItem
                {
                    Node = new SidebarNode { Name = $"{cap.Glyph("⟳", "@")} Refresh schema", Kind = SidebarNodeKind.Refresh },
                    Depth = 0
                });
                foreach (var r in MetadataRoots) AddFlatItem(r, 0, list);
                return list;
            }

            // Files mode: a ".." entry so a tree rooted on a file's directory can navigate upward.
            var root = RootNodes.FirstOrDefault();
            string? parent = root != null ? TryGetParent(root.Path) : null;
            if (parent != null)
            {
                list.Add(new FlatItem
                {
                    Node = new SidebarNode { Name = "..", Path = parent, IsDirectory = true, IsParentNav = true },
                    Depth = 0
                });
            }

            foreach (var r in RootNodes)
            {
                AddFlatItem(r, 0, list);
            }
            return list;
        }

        private static string? TryGetParent(string path)
        {
            try
            {
                var parent = Directory.GetParent(path);
                if (parent != null && parent.Exists) return parent.FullName;
            }
            catch { }
            return null;
        }

        /// <summary>Re-roots the tree one directory up (the ".." action).</summary>
        public void NavigateUp()
        {
            var root = RootNodes.FirstOrDefault();
            if (root == null) return;
            string? parent = TryGetParent(root.Path);
            if (parent == null) return;

            _lastRootPath = parent;
            RootNodes.Clear();
            string name = Path.GetFileName(parent);
            var node = new SidebarNode
            {
                Path = parent,
                Name = string.IsNullOrEmpty(name) ? parent : name,
                IsDirectory = true,
                IsExpanded = true
            };
            LoadChildren(node);
            RootNodes.Add(node);
            SelectedIndex = 0;
        }

        private void AddFlatItem(SidebarNode node, int depth, List<FlatItem> list)
        {
            list.Add(new FlatItem { Node = node, Depth = depth });
            if (node.IsExpandable && node.IsExpanded)
            {
                if (!node.IsLoaded && node.Kind == SidebarNodeKind.Directory)
                {
                    LoadChildren(node); // filesystem only; metadata nodes load via EnsureLoadedAsync
                }
                foreach (var child in node.Children)
                {
                    AddFlatItem(child, depth + 1, list);
                }
            }
        }

        /// <summary>
        /// Shortens a name to <paramref name="maxLen"/> columns. For files it keeps the
        /// extension (e.g. "really_long_repo…sql") so the file type stays visible.
        /// </summary>
        public static string TruncateName(string name, int maxLen, bool isDirectory)
        {
            if (maxLen <= 0 || name.Length <= maxLen) return name;
            if (maxLen <= 1) return name.Substring(0, maxLen);

            if (!isDirectory)
            {
                int dot = name.LastIndexOf('.');
                if (dot > 0 && dot < name.Length - 1)
                {
                    string ext = name.Substring(dot); // ".sql"
                    int head = maxLen - 1 - ext.Length; // room for at least one head char + ellipsis
                    if (head >= 1) return name.Substring(0, head) + "…" + ext;
                }
            }
            return name.Substring(0, maxLen - 1) + "…";
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
                // One column of indent per level (was two) so deep trees keep more room
                // for the name.
                string indent = new string(' ', item.Depth);
                var cap = TerminalCapabilities.Current;
                string open = item.Node.IsExpanded ? cap.Glyph("▼", "-") : cap.Glyph("▶", "+");
                string prefix = item.Node.Kind switch
                {
                    SidebarNodeKind.ModeToggle => "",
                    SidebarNodeKind.Refresh    => "",
                    SidebarNodeKind.Connection => $"{open} {cap.Glyph("🔌", "#")} ",
                    SidebarNodeKind.TempTable  => $"{open} {cap.Glyph("🧪", "~")} ",
                    SidebarNodeKind.Table      => $"{open} {cap.Glyph("▤", "=")} ",
                    SidebarNodeKind.View       => $"{open} {cap.Glyph("👁", "v")} ",
                    SidebarNodeKind.Group      => $"{open} {cap.Glyph("📂", ">")} ",
                    SidebarNodeKind.Column     => "  " + cap.Glyph("•", "-") + " ",
                    _ when item.Node.IsParentNav => cap.Glyph("↑ .. ", "^ .. "),
                    _ when item.Node.IsDirectory => $"{open} {cap.Glyph("📁", "/")} ",
                    _ => "  " + cap.Glyph("📄", ".") + " "
                };
                string displayName = item.Node.IsParentNav ? "" : item.Node.Name;

                // Truncate to fit width, preserving the file extension when possible.
                int maxTextLen = width - 4 - item.Depth - prefix.Length;
                displayName = TruncateName(displayName, maxTextLen, item.Node.IsDirectory);

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
                    string colorStyle = item.Node.Kind switch
                    {
                        SidebarNodeKind.ModeToggle => "bold cyan",
                        SidebarNodeKind.Refresh    => "cyan",
                        SidebarNodeKind.Connection => "bold blue",
                        SidebarNodeKind.TempTable  => "magenta",
                        SidebarNodeKind.Table      => "yellow",
                        SidebarNodeKind.View       => "green",
                        SidebarNodeKind.Group      => "grey",
                        SidebarNodeKind.Column     => "white",
                        _ => item.Node.IsDirectory ? "yellow" : "white"
                    };
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
                Header = new PanelHeader(Mode == SidebarMode.Metadata ? "[bold]Schema[/]" : "[bold]Explorer[/]"),
                Height = height,
                Width = width,
                Border = TerminalCapabilities.Current.Box(),
                Padding = new Padding(1, 0, 1, 0)
            };
            panel.BorderColor(TuiTheme.Instance.GetColor(borderStyle, Color.Grey));

            console.SetCursorPosition(x, y);
            console.WriteWidget(panel);
        }

        public async Task HandleEnter(ConsoleEditor editor)
        {
            var items = GetFlatVisibleItems();
            if (SelectedIndex < 0 || SelectedIndex >= items.Count) return;
            var node = items[SelectedIndex].Node;

            switch (node.Kind)
            {
                case SidebarNodeKind.ModeToggle:
                    await ToggleModeAsync(editor.CurrentScriptText);
                    return;
                case SidebarNodeKind.Refresh:
                    await RefreshMetadataAsync(editor.CurrentScriptText);
                    return;
                case SidebarNodeKind.Column:
                    // Leaf — insert its name at the cursor.
                    editor.InsertAtCursor(node.InsertText ?? node.Name);
                    _renderer.Focus = EditorFocus.Editor;
                    _renderer.ShowStatus($"Inserted: {node.Name}");
                    _renderer.ForceFullRepaint();
                    return;
            }

            if (node.IsParentNav)
            {
                NavigateUp();
                _renderer.ForceFullRepaint();
                return;
            }

            if (node.IsExpandable)
            {
                if (!node.IsExpanded) await EnsureLoadedAsync(node);
                node.IsExpanded = !node.IsExpanded;
                if (node.IsExpanded && !node.IsLoaded && node.Kind == SidebarNodeKind.Directory) LoadChildren(node);
                _renderer.ForceFullRepaint();
                return;
            }

            // Files-mode leaf: open the file.
            await editor.OpenFileInTab(node.Path);
            _renderer.Focus = EditorFocus.Editor;
            _renderer.ShowStatus($"Opened: {Path.GetFileName(node.Path)}");
            _renderer.ForceFullRepaint();
        }

        /// <summary>Inserts the selected metadata node's name at the cursor (no-op for non-leaf rows).</summary>
        public void InsertSelected(ConsoleEditor editor)
        {
            var items = GetFlatVisibleItems();
            if (SelectedIndex < 0 || SelectedIndex >= items.Count) return;
            var node = items[SelectedIndex].Node;
            if (string.IsNullOrEmpty(node.InsertText)) return;

            editor.InsertAtCursor(node.InsertText);
            _renderer.Focus = EditorFocus.Editor;
            _renderer.ShowStatus($"Inserted: {node.InsertText}");
            _renderer.ForceFullRepaint();
        }

        public void HandleLeft()
        {
            var items = GetFlatVisibleItems();
            if (SelectedIndex >= 0 && SelectedIndex < items.Count)
            {
                var item = items[SelectedIndex];
                if (item.Node.IsExpandable && item.Node.IsExpanded)
                {
                    item.Node.IsExpanded = false;
                    _renderer.ForceFullRepaint();
                }
            }
        }

        public async Task HandleRight()
        {
            var items = GetFlatVisibleItems();
            if (SelectedIndex >= 0 && SelectedIndex < items.Count)
            {
                var item = items[SelectedIndex];
                if (item.Node.IsExpandable && !item.Node.IsExpanded && !item.Node.IsParentNav)
                {
                    await EnsureLoadedAsync(item.Node);
                    item.Node.IsExpanded = true;
                    if (!item.Node.IsLoaded && item.Node.Kind == SidebarNodeKind.Directory)
                    {
                        LoadChildren(item.Node);
                    }
                    _renderer.ForceFullRepaint();
                }
            }
        }
    }
}
