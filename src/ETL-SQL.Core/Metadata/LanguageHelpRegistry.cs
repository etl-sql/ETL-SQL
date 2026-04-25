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
