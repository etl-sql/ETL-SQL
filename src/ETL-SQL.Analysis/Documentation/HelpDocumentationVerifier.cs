using ETL_SQL.Core.Interfaces;

namespace ETL_SQL.Analysis.Documentation
{
    public sealed record HelpDocumentationCheck(string Topic, string? SubTopic, bool Found, string Message);

    public class HelpDocumentationVerifier
    {
        private readonly ILanguageHelpRegistry _registry;

        public HelpDocumentationVerifier(ILanguageHelpRegistry registry)
        {
            _registry = registry;
        }

        public IReadOnlyList<HelpDocumentationCheck> VerifyRequiredTopics(IEnumerable<string> topics, int minimumLength = 10)
        {
            return topics
                .Select(topic => Verify(topic, null, minimumLength))
                .ToList();
        }

        public IReadOnlyList<HelpDocumentationCheck> VerifyRequiredSubTopics(string topic, IEnumerable<string> subTopics, int minimumLength = 1)
        {
            return subTopics
                .Select(subTopic => Verify(topic, subTopic, minimumLength))
                .ToList();
        }

        private HelpDocumentationCheck Verify(string topic, string? subTopic, int minimumLength)
        {
            var help = subTopic == null
                ? _registry.GetHelp(topic)
                : _registry.GetHelp(topic, subTopic);

            var found = !string.IsNullOrWhiteSpace(help) && help.Length >= minimumLength;
            var label = subTopic == null ? topic : $"{topic}/{subTopic}";
            var message = found
                ? $"Documentation found for {label}."
                : $"Documentation for {label} is missing or shorter than {minimumLength} characters.";

            return new HelpDocumentationCheck(topic, subTopic, found, message);
        }
    }
}
