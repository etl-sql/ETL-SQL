using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core.Interfaces;

namespace ETL_SQL.Core.Metadata
{
    public class LanguageHelpRegistry : ILanguageHelpRegistry
    {
        private readonly Dictionary<string, string> _topLevelHelp = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, string>> _subTopicHelp = new(StringComparer.OrdinalIgnoreCase);

        public LanguageHelpRegistry()
        {
            LoadResources();
        }

        private void LoadResources()
        {
            var assembly = typeof(LanguageHelpRegistry).Assembly;
            var resourceNames = assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith("ETL_SQL.Core.Resources.Help.") && n.EndsWith(".md"));

            foreach (var name in resourceNames)
            {
                // Format: ETL_SQL.Core.Resources.Help.Category.Topic.md
                // Or: ETL_SQL.Core.Resources.Help.Category.Topic.SubTopic.md
                var parts = name.Replace("ETL_SQL.Core.Resources.Help.", "").Replace(".md", "").Split('.');
                if (parts.Length < 2) continue;

                var category = parts[0]; // e.g. Keywords, Visuals
                var topic = parts[1];    // e.g. SELECT, BAR
                var subTopic = parts.Length > 2 ? parts[2] : null;

                using var stream = assembly.GetManifestResourceStream(name);
                if (stream == null) continue;
                using var reader = new System.IO.StreamReader(stream);
                var content = reader.ReadToEnd();

                string? mappedTopic = null;
                string? mappedSubTopic = null;

                switch (category.ToUpperInvariant())
                {
                    case "KEYWORDS":
                        mappedTopic = topic;
                        break;
                    case "VISUALS":
                        mappedTopic = "VISUAL";
                        mappedSubTopic = topic;
                        break;
                    case "CONNECTORS":
                        mappedTopic = "CONNECTION";
                        mappedSubTopic = topic;
                        break;
                    case "VARIABLES":
                        mappedTopic = "VARIABLES";
                        mappedSubTopic = topic;
                        break;
                    case "FUNCTIONS":
                        mappedTopic = "FUNCTION";
                        mappedSubTopic = topic;
                        break;
                    case "OPERATIONS":
                        mappedTopic = topic;
                        break;
                    case "REPORT":
                        mappedTopic = "REPORT";
                        mappedSubTopic = topic;
                        break;
                    default:
                        mappedTopic = topic;
                        mappedSubTopic = subTopic;
                        break;
                }

                if (mappedTopic != null)
                {
                    // If the subtopic is INDEX, it's actually the help for the topic itself
                    if (string.Equals(mappedSubTopic, "INDEX", StringComparison.OrdinalIgnoreCase))
                    {
                        RegisterHelp(mappedTopic, content, null);
                    }
                    else
                    {
                        RegisterHelp(mappedTopic, content, mappedSubTopic);
                    }
                }
            }
        }

        public void RegisterHelp(string topic, string helpText, string? subTopic = null)
        {
            if (string.IsNullOrEmpty(subTopic))
            {
                _topLevelHelp[topic] = helpText;
            }
            else
            {
                if (!_subTopicHelp.TryGetValue(topic, out var subs))
                {
                    subs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    _subTopicHelp[topic] = subs;
                }
                subs[subTopic] = helpText;
            }
        }

        public string? GetHelp(string topic, string? subTopic = null)
        {
            if (string.IsNullOrEmpty(subTopic))
            {
                if (_topLevelHelp.TryGetValue(topic, out var help)) return help;
                
                // Fallback: if user asked for a subtopic directly (e.g. HELP SELECT)
                foreach (var subDict in _subTopicHelp.Values)
                {
                    if (subDict.TryGetValue(topic, out var subHelp)) return subHelp;
                }
                
                return null;
            }

            if (_subTopicHelp.TryGetValue(topic, out var subs) && subs.TryGetValue(subTopic, out var helpText))
            {
                return helpText;
            }

            return null;
        }

        public IEnumerable<string> GetTopics() => _topLevelHelp.Keys.Concat(_subTopicHelp.Keys).Distinct();

        public IEnumerable<string> GetSubTopics(string topic)
        {
            if (_subTopicHelp.TryGetValue(topic, out var subs)) return subs.Keys;
            return Enumerable.Empty<string>();
        }
    }
}
