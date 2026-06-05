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
