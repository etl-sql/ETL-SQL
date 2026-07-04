using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Services;

namespace ETL_SQL.Analysis.Linting.Grammar;

public static class DefaultGrammar
{
    public static GrammarStateTree Build(IMetadataManager? metadata = null)
    {
        var tree = new GrammarStateTree();

        ConfigureCreateConnection(tree, metadata);
        ConfigureFileOperations(tree);

        return tree;
    }

    private static void ConfigureCreateConnection(GrammarStateTree tree, IMetadataManager? metadata)
    {
        var createNode = new StateNode("CREATE");
        var connectionNode = new StateNode("CONNECTION");
        var nameNode = new StateNode("CONN_NAME");
        var asNode = new StateNode("CONN_AS");
        var typeNode = new StateNode("CONN_TYPE");
        var openParenNode = new StateNode("CONN_PAREN_OPEN");
        var optionNameNode = new StateNode("CONN_OPTION_NAME");
        var equalsNode = new StateNode("CONN_OPTION_EQUALS");
        var optionValueNode = new StateNode("CONN_OPTION_VALUE");
        var commaNode = new StateNode("CONN_OPTION_COMMA");
        var closeParenNode = new StateNode("CONN_PAREN_CLOSE");
        var singleStringValueNode = new StateNode("CONN_SINGLE_STRING");

        var orNode = new StateNode("CONN_OR");
        var alterNode = new StateNode("CONN_ALTER");
        var alterStartNode = new StateNode("ALTER");

        var typedConnectionNode = new StateNode("CONN_TYPED");

        // CREATE -> CONNECTION or OR -> ALTER -> CONNECTION
        createNode.AddTransitionTo("CONNECTION", connectionNode, SuggestionType.Keyword);
        createNode.AddTransitionTo("OR", orNode, SuggestionType.Keyword);
        orNode.AddTransitionTo("ALTER", alterNode, SuggestionType.Keyword);
        alterNode.AddTransitionTo("CONNECTION", connectionNode, SuggestionType.Keyword);
        
        // CREATE -> <type> -> CONNECTION
        createNode.AddTransition(new StateTransition(
            t => t.Type == TokenType.IDENTIFIER || LanguageMetadata.IsConnectorType(t.Value),
            typedConnectionNode,
            "<connector_type>",
            SuggestionType.Connection,
            context => LanguageMetadata.ConnectorTypes
        ));
        typedConnectionNode.AddTransitionTo("CONNECTION", connectionNode, SuggestionType.Keyword);

        tree.RegisterStartNode("CREATE", createNode);

        // ALTER CONNECTION
        alterStartNode.AddTransitionTo("CONNECTION", connectionNode, SuggestionType.Keyword);
        tree.RegisterStartNode("ALTER", alterStartNode);

        // CONNECTION -> name (wildcard identifier or keyword)
        connectionNode.AddTransition(new StateTransition(
            t => t.Type == TokenType.IDENTIFIER || IsWord(t.Value),
            nameNode,
            "<connection_name>"
        ));

        var connWithNode = new StateNode("CONN_WITH");

        // name -> AS or WITH
        nameNode.AddTransitionTo("AS", asNode, SuggestionType.Keyword);
        nameNode.AddTransitionTo("WITH", connWithNode, SuggestionType.Keyword);
        connWithNode.AddTokenTransition(TokenType.LPAREN, openParenNode, "(");

        // AS -> type (FLATFILE, MSSQL, etc. - can be lexed as keywords)
        asNode.AddTransition(new StateTransition(
            t => t.Type == TokenType.IDENTIFIER || LanguageMetadata.IsConnectorType(t.Value),
            typeNode,
            "<connector_type>",
            SuggestionType.Connection,
            context => LanguageMetadata.ConnectorTypes
        ));

        // type -> ( or option_name (unparenthesized options)
        typeNode.AddTokenTransition(TokenType.LPAREN, openParenNode, "(");
        typeNode.AddTransition(new StateTransition(
            t => t.Type == TokenType.IDENTIFIER || IsWord(t.Value),
            optionNameNode,
            "<option_name>",
            SuggestionType.OptionName,
            context => GetSupportedOptions(context, metadata)
        ));

        // ( -> option_name (options like PATH, COMPRESS can be lexed as keywords)
        openParenNode.AddTransition(new StateTransition(
            t => t.Type == TokenType.IDENTIFIER || IsWord(t.Value),
            optionNameNode,
            "<option_name>",
            SuggestionType.OptionName,
            context => GetSupportedOptions(context, metadata)
        ));

        // ( -> single string connection literal or variable or identifier reference
        openParenNode.AddTokenTransition(TokenType.STRING_LITERAL, singleStringValueNode, "<connection_string>");
        openParenNode.AddTokenTransition(TokenType.VARIABLE, singleStringValueNode, "<variable>");
        openParenNode.AddTokenTransition(TokenType.IDENTIFIER, singleStringValueNode, "<identifier>");
        
        // ( -> ) (empty options list)
        openParenNode.AddTokenTransition(TokenType.RPAREN, closeParenNode, ")");

        // single string connection literal -> ) or ,
        singleStringValueNode.AddTokenTransition(TokenType.RPAREN, closeParenNode, ")");
        singleStringValueNode.AddTokenTransition(TokenType.COMMA, commaNode, ",");
        singleStringValueNode.AddTransition(new StateTransition(
            t => t.Type == TokenType.DOT || t.Type == TokenType.IDENTIFIER || IsWord(t.Value),
            singleStringValueNode,
            "<property_path>"
        ));

        // option_name -> =
        optionNameNode.AddTokenTransition(TokenType.EQUALS, equalsNode, "=");

        // = -> option_value
        equalsNode.AddWildcardTransition(optionValueNode, "<option_value>", SuggestionType.OptionValue, null,
            (token, walker) => {
                // Save option value to walker state bag for later reference
                walker.StateBag["LastOptionValue"] = token.Value;
            });

        // option_value -> , or ) or next option_name (for space-separated lists)
        optionValueNode.AddTokenTransition(TokenType.COMMA, commaNode, ",");
        optionValueNode.AddTokenTransition(TokenType.RPAREN, closeParenNode, ")");
        optionValueNode.AddTransition(new StateTransition(
            t => t.Type == TokenType.IDENTIFIER || IsWord(t.Value),
            optionNameNode,
            "<option_name>",
            SuggestionType.OptionName,
            context => GetSupportedOptions(context, metadata)
        ));
        optionValueNode.AddTransition(new StateTransition(
            t => t.Type != TokenType.COMMA && t.Type != TokenType.RPAREN,
            optionValueNode,
            "<expression_token>"
        ));

        // , -> option_name
        commaNode.AddTransition(new StateTransition(
            t => t.Type == TokenType.IDENTIFIER || IsWord(t.Value),
            optionNameNode,
            "<option_name>",
            SuggestionType.OptionName,
            context => GetSupportedOptions(context, metadata)
        ));

        // Connect option values suggestions dynamically
        // Set suggestion provider for option values transition
        foreach (var t in equalsNode.Transitions)
        {
            if (t.SuggestType == SuggestionType.OptionValue)
            {
                // We recreate a transition with custom provider
                equalsNode.Transitions.Remove(t);
                equalsNode.Transitions.Add(new StateTransition(
                    t.Condition,
                    t.Target,
                    t.Label,
                    t.SuggestType,
                    context => GetOptionValues(context, metadata),
                    t.OnTransition
                ));
                break;
            }
        }
    }

    private static void ConfigureFileOperations(GrammarStateTree tree)
    {
        // COMPRESS / ENCRYPT / DECRYPT
        var compressNode = new StateNode("COMPRESS");
        var encryptNode = new StateNode("ENCRYPT");
        var decryptNode = new StateNode("DECRYPT");

        var fileKeywordNode = new StateNode("FILE_KEYWORD");
        var sourceNode = new StateNode("FILE_SOURCE");
        var toNode = new StateNode("FILE_TO");
        var destinationNode = new StateNode("FILE_DESTINATION");

        // Option Nodes
        var passwordKeywordNode = new StateNode("PWD_KEYWORD");
        var passwordValNode = new StateNode("PWD_VAL");
        var keyfileKeywordNode = new StateNode("KF_KEYWORD");
        var keyfileValNode = new StateNode("KF_VAL");
        var pgpkeyKeywordNode = new StateNode("PK_KEYWORD");
        var pgpkeyValNode = new StateNode("PK_VAL");

        var withNode = new StateNode("FILE_WITH");
        var withOpenParenNode = new StateNode("FILE_WITH_PAREN_OPEN");
        var withOptionNameNode = new StateNode("FILE_WITH_OPTION_NAME");
        var withEqualsNode = new StateNode("FILE_WITH_EQUALS");
        var withOptionValueNode = new StateNode("FILE_WITH_OPTION_VAL");
        var withCommaNode = new StateNode("FILE_WITH_COMMA");
        var withCloseParenNode = new StateNode("FILE_WITH_PAREN_CLOSE");

        var atNode = new StateNode("FILE_AT");
        var atConnectionNode = new StateNode("FILE_AT_CONN");

        // Register start nodes
        tree.RegisterStartNode("COMPRESS", compressNode);
        tree.RegisterStartNode("ENCRYPT", encryptNode);
        tree.RegisterStartNode("DECRYPT", decryptNode);

        // COMPRESS/ENCRYPT/DECRYPT -> FILE or source directly
        compressNode.AddTransitionTo("FILE", fileKeywordNode, SuggestionType.Keyword);
        compressNode.AddWildcardTransition(sourceNode, "<source>");

        encryptNode.AddTransitionTo("FILE", fileKeywordNode, SuggestionType.Keyword);
        encryptNode.AddWildcardTransition(sourceNode, "<source>");

        decryptNode.AddTransitionTo("FILE", fileKeywordNode, SuggestionType.Keyword);
        decryptNode.AddWildcardTransition(sourceNode, "<source>");

        fileKeywordNode.AddWildcardTransition(sourceNode, "<source>");

        // Source path transitions
        sourceNode.AddTransition(new StateTransition(
            t => !IsFileOperationTerminator(t.Value) && t.Type != TokenType.SEMICOLON,
            sourceNode,
            "<source_token>"
        ));
        sourceNode.AddTransitionTo("TO", toNode, SuggestionType.Keyword);
        sourceNode.AddTransitionTo("WITH", withNode, SuggestionType.Keyword);
        sourceNode.AddTransitionTo("PASSWORD", passwordKeywordNode, SuggestionType.Keyword);
        sourceNode.AddTransitionTo("KEYFILE", keyfileKeywordNode, SuggestionType.Keyword);
        sourceNode.AddTransitionTo("PGP_KEY", pgpkeyKeywordNode, SuggestionType.Keyword);
        sourceNode.AddTransitionTo("AT", atNode, SuggestionType.Keyword);

        toNode.AddWildcardTransition(destinationNode, "<destination>");

        // Destination transitions
        destinationNode.AddTransition(new StateTransition(
            t => !IsDestinationTerminator(t.Value) && t.Type != TokenType.SEMICOLON,
            destinationNode,
            "<destination_token>"
        ));
        destinationNode.AddTransitionTo("WITH", withNode, SuggestionType.Keyword);
        destinationNode.AddTransitionTo("PASSWORD", passwordKeywordNode, SuggestionType.Keyword);
        destinationNode.AddTransitionTo("KEYFILE", keyfileKeywordNode, SuggestionType.Keyword);
        destinationNode.AddTransitionTo("PGP_KEY", pgpkeyKeywordNode, SuggestionType.Keyword);
        destinationNode.AddTransitionTo("AT", atNode, SuggestionType.Keyword);

        // Password logic: PASSWORD 'val' or PASSWORD('val')
        passwordKeywordNode.AddTokenTransition(TokenType.LPAREN, passwordValNode, "(");
        passwordKeywordNode.AddWildcardTransition(passwordValNode, "<password>");
        
        passwordValNode.AddWildcardTransition(passwordValNode, "<password>"); // Handles inner expression
        passwordValNode.AddTokenTransition(TokenType.RPAREN, destinationNode, ")"); // Jump back to destination choices
        passwordValNode.AddTransitionTo("WITH", withNode, SuggestionType.Keyword);
        passwordValNode.AddTransitionTo("KEYFILE", keyfileKeywordNode, SuggestionType.Keyword);
        passwordValNode.AddTransitionTo("PGP_KEY", pgpkeyKeywordNode, SuggestionType.Keyword);
        passwordValNode.AddTransitionTo("AT", atNode, SuggestionType.Keyword);

        // Keyfile logic: KEYFILE 'val'
        keyfileKeywordNode.AddWildcardTransition(keyfileValNode, "<keyfile>");
        keyfileValNode.AddTransitionTo("WITH", withNode, SuggestionType.Keyword);
        keyfileValNode.AddTransitionTo("PASSWORD", passwordKeywordNode, SuggestionType.Keyword);
        keyfileValNode.AddTransitionTo("PGP_KEY", pgpkeyKeywordNode, SuggestionType.Keyword);
        keyfileValNode.AddTransitionTo("AT", atNode, SuggestionType.Keyword);

        // Pgpkey logic: PGP_KEY 'val'
        pgpkeyKeywordNode.AddWildcardTransition(pgpkeyValNode, "<pgpkey>");
        pgpkeyValNode.AddTransitionTo("WITH", withNode, SuggestionType.Keyword);
        pgpkeyValNode.AddTransitionTo("PASSWORD", passwordKeywordNode, SuggestionType.Keyword);
        pgpkeyValNode.AddTransitionTo("KEYFILE", keyfileKeywordNode, SuggestionType.Keyword);
        pgpkeyValNode.AddTransitionTo("AT", atNode, SuggestionType.Keyword);

        // AT connection
        atNode.AddTransition(new StateTransition(
            t => t.Type == TokenType.IDENTIFIER || IsWord(t.Value),
            atConnectionNode,
            "<connection_name>",
            SuggestionType.Connection
        ));
        atConnectionNode.AddTransitionTo("WITH", withNode, SuggestionType.Keyword);

        // WITH(OVERWRITE = ON/OFF, FORMAT = ZIP, etc.)
        withNode.AddTokenTransition(TokenType.LPAREN, withOpenParenNode, "(");

        withOpenParenNode.AddTransition(new StateTransition(
            t => t.Type == TokenType.IDENTIFIER || IsWord(t.Value),
            withOptionNameNode,
            "<option_name>"
        ));

        withOptionNameNode.AddTokenTransition(TokenType.EQUALS, withEqualsNode, "=");

        withEqualsNode.AddWildcardTransition(withOptionValueNode, "<option_value>");

        withOptionValueNode.AddTokenTransition(TokenType.COMMA, withCommaNode, ",");
        withOptionValueNode.AddTokenTransition(TokenType.RPAREN, withCloseParenNode, ")");
        
        withOptionValueNode.AddTransition(new StateTransition(
            t => t.Type == TokenType.IDENTIFIER || IsWord(t.Value),
            withOptionNameNode,
            "<option_name>"
        ));
        withOptionValueNode.AddTransition(new StateTransition(
            t => t.Type != TokenType.COMMA && t.Type != TokenType.RPAREN,
            withOptionValueNode,
            "<expression_token>"
        ));

        withCommaNode.AddTransition(new StateTransition(
            t => t.Type == TokenType.IDENTIFIER || IsWord(t.Value),
            withOptionNameNode,
            "<option_name>"
        ));
    }

    private static IEnumerable<string> GetSupportedOptions(SuggestionContext context, IMetadataManager? metadata)
    {
        string? connectorType = FindConnectorTypeInScriptBefore(context.ScriptBefore);
        if (connectorType != null && metadata != null)
        {
            var conn = metadata.GetConnector(connectorType);
            if (conn != null)
            {
                return conn.GetSupportedOptions().Keys;
            }
        }
        return new[] { "PATH", "HEADER", "DELIMITER", "COMPRESS", "ENCRYPT", "PASSWORD", "ALGORITHM" };
    }

    private static IEnumerable<string> GetOptionValues(SuggestionContext context, IMetadataManager? metadata)
    {
        string? connectorType = FindConnectorTypeInScriptBefore(context.ScriptBefore);
        string? optionName = FindLastOptionNameInScriptBefore(context.ScriptBefore);

        if (connectorType != null && optionName != null && metadata != null)
        {
            var conn = metadata.GetConnector(connectorType);
            if (conn != null)
            {
                var options = conn.GetSupportedOptions();
                if (options.TryGetValue(optionName, out var values))
                {
                    return values;
                }
            }
        }

        if (optionName != null && (optionName.Equals("COMPRESS", StringComparison.OrdinalIgnoreCase) || optionName.Equals("ENCRYPT", StringComparison.OrdinalIgnoreCase)))
        {
            return new[] { "ON", "OFF" };
        }

        return Array.Empty<string>();
    }

    private static string? FindConnectorTypeInScriptBefore(string scriptBefore)
    {
        // Simple scan backwards to find AS [connectorType]
        var match = RegexScan(scriptBefore, @"\bAS\s+(\w+)\s*\(");
        return match;
    }

    private static string? FindLastOptionNameInScriptBefore(string scriptBefore)
    {
        // Simple scan backwards to find the last option name preceding the equals sign
        var match = RegexScan(scriptBefore, @"\b(\w+)\s*=\s*$");
        return match;
    }

    private static string? RegexScan(string input, string pattern)
    {
        try
        {
            var match = System.Text.RegularExpressions.Regex.Match(input, pattern, System.Text.RegularExpressions.RegexOptions.RightToLeft | System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value;
            }
        }
        catch { }
        return null;
    }

    private static bool IsWord(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        char first = value[0];
        if (!char.IsLetter(first)) return false;
        
        for (int i = 1; i < value.Length; i++)
        {
            char c = value[i];
            if (!char.IsLetterOrDigit(c) && c != '_') return false;
        }
        return true;
    }

    private static bool IsFileOperationTerminator(string val)
    {
        if (string.IsNullOrEmpty(val)) return false;
        return val.Equals("TO", StringComparison.OrdinalIgnoreCase) ||
               val.Equals("WITH", StringComparison.OrdinalIgnoreCase) ||
               val.Equals("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
               val.Equals("KEYFILE", StringComparison.OrdinalIgnoreCase) ||
               val.Equals("PGP_KEY", StringComparison.OrdinalIgnoreCase) ||
               val.Equals("AT", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDestinationTerminator(string val)
    {
        if (string.IsNullOrEmpty(val)) return false;
        return val.Equals("WITH", StringComparison.OrdinalIgnoreCase) ||
               val.Equals("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
               val.Equals("KEYFILE", StringComparison.OrdinalIgnoreCase) ||
               val.Equals("PGP_KEY", StringComparison.OrdinalIgnoreCase) ||
               val.Equals("AT", StringComparison.OrdinalIgnoreCase);
    }
}
