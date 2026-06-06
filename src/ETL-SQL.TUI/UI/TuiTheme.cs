using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace ETL_SQL.TUI.UI
{
    public class TuiTheme
    {
        private static TuiTheme _instance = new TuiTheme();
        public static TuiTheme Instance
        {
            get => _instance;
            set => _instance = value;
        }

        [JsonPropertyName("editor")]
        public EditorTheme Editor { get; set; } = new EditorTheme();

        [JsonPropertyName("syntax")]
        public SyntaxTheme Syntax { get; set; } = new SyntaxTheme();

        [JsonPropertyName("ui")]
        public UiTheme Ui { get; set; } = new UiTheme();

        public static void Load(string? filePath = null)
        {
            try
            {
                // Ensure presets folder is initialized on startup
                try
                {
                    if (!Directory.Exists("themes")) Directory.CreateDirectory("themes");
                    EnsurePresetThemesWritten("themes");
                }
                catch { }

                string path = filePath ?? "tui-theme.json";
                if (!File.Exists(path))
                {
                    _instance = new TuiTheme();
                    return;
                }

                string json = File.ReadAllText(path);
                var theme = JsonSerializer.Deserialize<TuiTheme>(json);
                if (theme != null)
                {
                    _instance = theme;
                }
            }
            catch
            {
                _instance = new TuiTheme();
            }
        }

        public static void SaveDefault(string filePath)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(new TuiTheme(), options);
                File.WriteAllText(filePath, json);
            }
            catch { }
        }

        public static string CycleTheme()
        {
            try
            {
                string themesDir = "themes";
                if (!Directory.Exists(themesDir)) Directory.CreateDirectory(themesDir);
                EnsurePresetThemesWritten(themesDir);

                var files = Directory.GetFiles(themesDir, "*.json");
                if (files.Length == 0) return "default";

                string activeTrackerPath = Path.Combine(themesDir, "active.txt");
                string currentTheme = "default";
                if (File.Exists(activeTrackerPath))
                {
                    currentTheme = File.ReadAllText(activeTrackerPath).Trim();
                }

                int currentIndex = -1;
                for (int i = 0; i < files.Length; i++)
                {
                    if (string.Equals(Path.GetFileNameWithoutExtension(files[i]), currentTheme, StringComparison.OrdinalIgnoreCase))
                    {
                        currentIndex = i;
                        break;
                    }
                }

                int nextIndex = (currentIndex + 1) % (files.Length + 1);
                string nextThemeName;

                if (nextIndex == files.Length)
                {
                    _instance = new TuiTheme();
                    nextThemeName = "default";
                    if (File.Exists("tui-theme.json"))
                    {
                        try { File.Delete("tui-theme.json"); } catch { }
                    }
                }
                else
                {
                    string nextFile = files[nextIndex];
                    nextThemeName = Path.GetFileNameWithoutExtension(nextFile);
                    string json = File.ReadAllText(nextFile);
                    var theme = JsonSerializer.Deserialize<TuiTheme>(json);
                    if (theme != null)
                    {
                        _instance = theme;
                        File.WriteAllText("tui-theme.json", json);
                    }
                }

                File.WriteAllText(activeTrackerPath, nextThemeName);
                return nextThemeName;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        private static void EnsurePresetThemesWritten(string dir)
        {
            try
            {
                // Dracula Preset
                string draculaPath = Path.Combine(dir, "dracula.json");
                if (!File.Exists(draculaPath))
                {
                    var dracula = new TuiTheme
                    {
                        Editor = new EditorTheme { Gutter = "grey", Selection = "black on #ff79c6", SecondaryCursor = "reverse" },
                        Syntax = new SyntaxTheme
                        {
                            String = "#f1fa8c", Bracket = "#8be9fd", Variable = "#ffb86c", Docker = "#ff5555",
                            DmlKeyword = "bold #ff79c6", DdlKeyword = "bold #bd93f9", ControlFlow = "bold #ff79c6",
                            JoinKeyword = "bold #8be9fd", OperatorKeyword = "bold #ff79c6", OtherKeyword = "#ff79c6",
                            DataType = "#8be9fd", Function = "#50fa7b", Comment = "#6272a4", CommentTag = "#bd93f9",
                            CommentValue = "#f1fa8c", Alias = "#bd93f9", Table = "#8be9fd"
                        },
                        Ui = new UiTheme
                        {
                            StatusBackground = "white on #282a36", HelpBackground = "white on #191a21",
                            EditorFocusedBorder = "#bd93f9", EditorUnfocusedBorder = "#44475a",
                            PanelFocusedBorder = "#ff79c6", PanelUnfocusedBorder = "#44475a",
                            CompareFocusedBorder = "#bd93f9", CompareUnfocusedBorder = "#44475a",
                            ResultsFocusedBorder = "#50fa7b", ResultsUnfocusedBorder = "#8be9fd"
                        }
                    };
                    File.WriteAllText(draculaPath, JsonSerializer.Serialize(dracula, new JsonSerializerOptions { WriteIndented = true }));
                }

                // Gruvbox Preset
                string gruvboxPath = Path.Combine(dir, "gruvbox.json");
                if (!File.Exists(gruvboxPath))
                {
                    var gruvbox = new TuiTheme
                    {
                        Editor = new EditorTheme { Gutter = "#928374", Selection = "black on #a89984", SecondaryCursor = "reverse" },
                        Syntax = new SyntaxTheme
                        {
                            String = "#b8bb26", Bracket = "#83a598", Variable = "#fe8019", Docker = "#fb4934",
                            DmlKeyword = "bold #fb4934", DdlKeyword = "bold #b16286", ControlFlow = "bold #fe8019",
                            JoinKeyword = "bold #8ec07c", OperatorKeyword = "bold #fe8019", OtherKeyword = "#fb4934",
                            DataType = "#fabd2f", Function = "#fabd2f", Comment = "#928374", CommentTag = "#b16286",
                            CommentValue = "#b8bb26", Alias = "#b16286", Table = "#83a598"
                        },
                        Ui = new UiTheme
                        {
                            StatusBackground = "white on #3c3836", HelpBackground = "white on #282828",
                            EditorFocusedBorder = "#fabd2f", EditorUnfocusedBorder = "#504945",
                            PanelFocusedBorder = "#fe8019", PanelUnfocusedBorder = "#504945",
                            CompareFocusedBorder = "#b16286", CompareUnfocusedBorder = "#504945",
                            ResultsFocusedBorder = "#b8bb26", ResultsUnfocusedBorder = "#8ec07c"
                        }
                    };
                    File.WriteAllText(gruvboxPath, JsonSerializer.Serialize(gruvbox, new JsonSerializerOptions { WriteIndented = true }));
                }

                // Nord Preset
                string nordPath = Path.Combine(dir, "nord.json");
                if (!File.Exists(nordPath))
                {
                    var nord = new TuiTheme
                    {
                        Editor = new EditorTheme { Gutter = "#4c566a", Selection = "black on #88c0d0", SecondaryCursor = "reverse" },
                        Syntax = new SyntaxTheme
                        {
                            String = "#a3be8c", Bracket = "#88c0d0", Variable = "#d8dee9", Docker = "#bf616a",
                            DmlKeyword = "bold #81a1c1", DdlKeyword = "bold #b48ead", ControlFlow = "bold #81a1c1",
                            JoinKeyword = "bold #8fbcbb", OperatorKeyword = "bold #81a1c1", OtherKeyword = "#81a1c1",
                            DataType = "#88c0d0", Function = "#88c0d0", Comment = "#4c566a", CommentTag = "#b48ead",
                            CommentValue = "#a3be8c", Alias = "#b48ead", Table = "#88c0d0"
                        },
                        Ui = new UiTheme
                        {
                            StatusBackground = "white on #3b4252", HelpBackground = "white on #2e3440",
                            EditorFocusedBorder = "#88c0d0", EditorUnfocusedBorder = "#434c5e",
                            PanelFocusedBorder = "#81a1c1", PanelUnfocusedBorder = "#434c5e",
                            CompareFocusedBorder = "#b48ead", CompareUnfocusedBorder = "#434c5e",
                            ResultsFocusedBorder = "#a3be8c", ResultsUnfocusedBorder = "#8fbcbb"
                        }
                    };
                    File.WriteAllText(nordPath, JsonSerializer.Serialize(nord, new JsonSerializerOptions { WriteIndented = true }));
                }

                // Light Preset
                string lightPath = Path.Combine(dir, "light.json");
                if (!File.Exists(lightPath))
                {
                    var light = new TuiTheme
                    {
                        Editor = new EditorTheme { Gutter = "grey", Selection = "white on black", SecondaryCursor = "reverse" },
                        Syntax = new SyntaxTheme
                        {
                            String = "darkgreen", Bracket = "blue", Variable = "darkviolet", Docker = "red",
                            DmlKeyword = "bold darkblue", DdlKeyword = "bold purple", ControlFlow = "bold darkorange",
                            JoinKeyword = "bold teal", OperatorKeyword = "bold darkblue", OtherKeyword = "darkblue",
                            DataType = "blue", Function = "darkcyan", Comment = "grey", CommentTag = "purple",
                            CommentValue = "darkgreen", Alias = "purple", Table = "blue"
                        },
                        Ui = new UiTheme
                        {
                            StatusBackground = "white on grey37", HelpBackground = "black on white",
                            EditorFocusedBorder = "black", EditorUnfocusedBorder = "grey",
                            PanelFocusedBorder = "black", PanelUnfocusedBorder = "grey",
                            CompareFocusedBorder = "darkblue", CompareUnfocusedBorder = "grey",
                            ResultsFocusedBorder = "darkgreen", ResultsUnfocusedBorder = "blue"
                        }
                    };
                    File.WriteAllText(lightPath, JsonSerializer.Serialize(light, new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch { }
        }

        public Color GetColor(string colorName, Color fallback)
        {
            try
            {
                return Style.Parse(colorName).Foreground;
            }
            catch
            {
                return fallback;
            }
        }

        public Style GetStyle(string styleString, Style fallback)
        {
            try
            {
                return Style.Parse(styleString);
            }
            catch
            {
                return fallback;
            }
        }
    }

    public class EditorTheme
    {
        [JsonPropertyName("gutter")] public string Gutter { get; set; } = "grey";
        [JsonPropertyName("selection")] public string Selection { get; set; } = "black on white";
        [JsonPropertyName("secondaryCursor")] public string SecondaryCursor { get; set; } = "reverse";
    }

    public class SyntaxTheme
    {
        [JsonPropertyName("string")] public string String { get; set; } = "darkorange3";
        [JsonPropertyName("bracket")] public string Bracket { get; set; } = "cyan";
        [JsonPropertyName("variable")] public string Variable { get; set; } = "green";
        [JsonPropertyName("docker")] public string Docker { get; set; } = "orange1";
        [JsonPropertyName("dmlKeyword")] public string DmlKeyword { get; set; } = "bold blue";
        [JsonPropertyName("ddlKeyword")] public string DdlKeyword { get; set; } = "bold plum1";
        [JsonPropertyName("controlFlow")] public string ControlFlow { get; set; } = "bold gold1";
        [JsonPropertyName("joinKeyword")] public string JoinKeyword { get; set; } = "bold springgreen3";
        [JsonPropertyName("operatorKeyword")] public string OperatorKeyword { get; set; } = "bold plum3";
        [JsonPropertyName("otherKeyword")] public string OtherKeyword { get; set; } = "blue";
        [JsonPropertyName("dataType")] public string DataType { get; set; } = "mediumpurple";
        [JsonPropertyName("function")] public string Function { get; set; } = "yellow";
        [JsonPropertyName("comment")] public string Comment { get; set; } = "grey70";
        [JsonPropertyName("commentTag")] public string CommentTag { get; set; } = "mediumpurple";
        [JsonPropertyName("commentValue")] public string CommentValue { get; set; } = "darkorange3";
        [JsonPropertyName("alias")] public string Alias { get; set; } = "purple";
        [JsonPropertyName("table")] public string Table { get; set; } = "cyan";
    }

    public class UiTheme
    {
        [JsonPropertyName("statusBackground")] public string StatusBackground { get; set; } = "white on grey15";
        [JsonPropertyName("helpBackground")] public string HelpBackground { get; set; } = "white on grey23";
        [JsonPropertyName("editorFocusedBorder")] public string EditorFocusedBorder { get; set; } = "yellow";
        [JsonPropertyName("editorUnfocusedBorder")] public string EditorUnfocusedBorder { get; set; } = "grey";
        [JsonPropertyName("panelFocusedBorder")] public string PanelFocusedBorder { get; set; } = "grey37";
        [JsonPropertyName("panelUnfocusedBorder")] public string PanelUnfocusedBorder { get; set; } = "grey23";
        [JsonPropertyName("compareFocusedBorder")] public string CompareFocusedBorder { get; set; } = "magenta";
        [JsonPropertyName("compareUnfocusedBorder")] public string CompareUnfocusedBorder { get; set; } = "grey23";
        [JsonPropertyName("resultsFocusedBorder")] public string ResultsFocusedBorder { get; set; } = "yellow";
        [JsonPropertyName("resultsUnfocusedBorder")] public string ResultsUnfocusedBorder { get; set; } = "cyan";
    }
}
