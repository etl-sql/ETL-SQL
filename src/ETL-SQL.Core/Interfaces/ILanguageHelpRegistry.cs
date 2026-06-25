using System.Collections.Generic;

namespace ETL_SQL.Core.Interfaces;
/// <summary>
/// Shared registry for language documentation (statements, report objects, and keywords).
/// Used by both the HELP command and the LSP Hover provider.
/// </summary>
public interface ILanguageHelpRegistry
{
    void RegisterHelp(string topic, string helpText, string? subTopic = null);
    string? GetHelp(string topic, string? subTopic = null);
    IEnumerable<string> GetTopics();
    IEnumerable<string> GetSubTopics(string topic);
}
