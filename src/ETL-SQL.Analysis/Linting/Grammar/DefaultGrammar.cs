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
        var tree = new GrammarStateTree(requireParserAcceptance: true);

        var sharedWithNode = new StateNode("FILE_WITH");
        var sharedAtNode = new StateNode("FILE_AT");
        var sharedToNode = new StateNode("FILE_TO");

        ConfigureCreateConnection(tree, metadata);
        ConfigureFileOperations(tree, sharedWithNode, sharedAtNode, sharedToNode);
        ConfigureDmlAndQueries(tree, sharedWithNode);
        ConfigureControlFlow(tree);
        ConfigureSpecializedOperations(tree, sharedWithNode, sharedAtNode, sharedToNode);
        ConfigureCommonStatements(tree);
        ConfigureCreateAlterReplace(tree);
        ConfigureExecute(tree);
        ConfigureParallel(tree);

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
        var alterConnectionNode = new StateNode("ALTER_CONNECTION");
        var alterNameNode = new StateNode("ALTER_CONN_NAME");
        var alterWithNode = new StateNode("ALTER_CONN_WITH");

        // CREATE -> CONNECTION or OR -> ALTER -> CONNECTION
        createNode.AddTransitionTo("CONNECTION", connectionNode, SuggestionType.Keyword);
        createNode.AddTransitionTo("OR", orNode, SuggestionType.Keyword);
        orNode.AddTransitionTo("ALTER", alterNode, SuggestionType.Keyword);
        alterNode.AddTransitionTo("CONNECTION", connectionNode, SuggestionType.Keyword);

        // CREATE SETS !name BEGIN ... END
        var createSets = new StateNode("CREATE_SETS");
        var createSetsBang = new StateNode("CREATE_SETS_BANG");
        var createSetsName = new StateNode("CREATE_SETS_NAME");
        var createSetsContent = new StateNode("CREATE_SETS_CONTENT");

        createNode.AddTransitionTo("SETS", createSets, SuggestionType.Keyword);
        createSets.AddTokenTransition(TokenType.BANG, createSetsBang, "!");
        createSetsBang.AddWildcardTransition(createSetsName, "<set_name>");
        createSetsName.AddTransitionTo("BEGIN", createSetsContent, SuggestionType.Keyword);

        createSetsContent.AddTransition(new StateTransition(
            t => !t.Value.Equals("END", StringComparison.OrdinalIgnoreCase),
            createSetsContent,
            "<sets_assignment_token>"
        ));
        createSetsContent.AddTransitionTo("END", tree.Root, SuggestionType.Keyword);

        tree.RegisterStartNode("CREATE", createNode);

        // ALTER CONNECTION
        alterStartNode.AddTransitionTo("CONNECTION", alterConnectionNode, SuggestionType.Keyword);
        tree.RegisterStartNode("ALTER", alterStartNode);

        alterConnectionNode.AddTransition(new StateTransition(
            t => t.Type == TokenType.IDENTIFIER || t.Type == TokenType.STRING_LITERAL || IsWord(t.Value),
            alterNameNode,
            "<connection_name>"
        ));
        alterNameNode.AddTransitionTo("AS", asNode, SuggestionType.Keyword);
        alterNameNode.AddTransitionTo("WITH", alterWithNode, SuggestionType.Keyword);
        alterWithNode.AddTokenTransition(TokenType.LPAREN, openParenNode, "(");

        // CONNECTION -> name (wildcard identifier, string literal, or keyword)
        connectionNode.AddTransition(new StateTransition(
            t => t.Type == TokenType.IDENTIFIER || t.Type == TokenType.STRING_LITERAL || IsWord(t.Value),
            nameNode,
            "<connection_name>"
        ));

        // The production parser accepts only CREATE CONNECTION name AS TYPE(...).
        // Keep completion grammar aligned with that contract.
        nameNode.AddTransitionTo("AS", asNode, SuggestionType.Keyword);

        // AS -> type (FLATFILE, MSSQL, etc. - can be lexed as keywords)
        asNode.AddTransition(new StateTransition(
            t => t.Type == TokenType.IDENTIFIER || LanguageMetadata.IsConnectorType(t.Value),
            typeNode,
            "<connector_type>",
            SuggestionType.Connection,
            context => LanguageMetadata.ConnectorTypes
        ));

        // Connection arguments are always parenthesized.
        typeNode.AddTokenTransition(TokenType.LPAREN, openParenNode, "(");

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
            (token, walker) =>
            {
                // Save option value to walker state bag for later reference
                walker.StateBag["LastOptionValue"] = token.Value;
            });

        // Named options must be comma-separated, matching ParseCreateConnection.
        optionValueNode.AddTokenTransition(TokenType.COMMA, commaNode, ",");
        optionValueNode.AddTokenTransition(TokenType.RPAREN, closeParenNode, ")");
        optionValueNode.AddTransition(new StateTransition(
            t => t.Type != TokenType.COMMA && t.Type != TokenType.RPAREN,
            optionValueNode,
            "<expression_token>",
            contextCondition: (token, walker) => token.Type != TokenType.EQUALS ||
                (walker.StateBag.TryGetValue("LastOptionValue", out var firstValue) &&
                 firstValue?.ToString()?.Equals("ENC", StringComparison.OrdinalIgnoreCase) == true)
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

    private static void ConfigureFileOperations(GrammarStateTree tree, StateNode withNode, StateNode atNode, StateNode toNode)
    {
        // COMPRESS / ENCRYPT / DECRYPT / MOVE / COPY
        var compressNode = new StateNode("COMPRESS");
        var encryptNode = new StateNode("ENCRYPT");
        var decryptNode = new StateNode("DECRYPT");
        var moveNode = new StateNode("MOVE");
        var copyNode = new StateNode("COPY");

        var fileKeywordNode = new StateNode("FILE_KEYWORD");
        var sourceNode = new StateNode("FILE_SOURCE");
        var destinationNode = new StateNode("FILE_DESTINATION");

        // Option Nodes
        var passwordKeywordNode = new StateNode("PWD_KEYWORD");
        var passwordValNode = new StateNode("PWD_VAL");
        var keyfileKeywordNode = new StateNode("KF_KEYWORD");
        var keyfileValNode = new StateNode("KF_VAL");
        var pgpkeyKeywordNode = new StateNode("PK_KEYWORD");
        var pgpkeyValNode = new StateNode("PK_VAL");

        var withOpenParenNode = new StateNode("FILE_WITH_PAREN_OPEN");
        var withOptionNameNode = new StateNode("FILE_WITH_OPTION_NAME");
        var withEqualsNode = new StateNode("FILE_WITH_EQUALS");
        var withOptionValueNode = new StateNode("FILE_WITH_OPTION_VAL");
        var withCommaNode = new StateNode("FILE_WITH_COMMA");
        var withCloseParenNode = new StateNode("FILE_WITH_PAREN_CLOSE");

        var atConnectionNode = new StateNode("FILE_AT_CONN");

        // Register start nodes
        tree.RegisterStartNode("COMPRESS", compressNode);
        tree.RegisterStartNode("ENCRYPT", encryptNode);
        tree.RegisterStartNode("DECRYPT", decryptNode);
        tree.RegisterStartNode("MOVE", moveNode);
        tree.RegisterStartNode("COPY", copyNode);

        // COMPRESS/ENCRYPT/DECRYPT/MOVE/COPY -> FILE or source directly
        compressNode.AddTransitionTo("FILE", fileKeywordNode, SuggestionType.Keyword);
        compressNode.AddWildcardTransition(sourceNode, "<source>");

        encryptNode.AddTransitionTo("FILE", fileKeywordNode, SuggestionType.Keyword);
        encryptNode.AddWildcardTransition(sourceNode, "<source>");

        decryptNode.AddTransitionTo("FILE", fileKeywordNode, SuggestionType.Keyword);
        decryptNode.AddWildcardTransition(sourceNode, "<source>");

        moveNode.AddTransitionTo("FILE", fileKeywordNode, SuggestionType.Keyword);
        moveNode.AddWildcardTransition(sourceNode, "<source>");

        copyNode.AddTransitionTo("FILE", fileKeywordNode, SuggestionType.Keyword);
        copyNode.AddWildcardTransition(sourceNode, "<source>");

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
            t => t.Type == TokenType.IDENTIFIER || t.Type == TokenType.VARIABLE || IsWord(t.Value),
            withOptionNameNode,
            "<option_name>"
        ));

        withOptionNameNode.AddTokenTransition(TokenType.EQUALS, withEqualsNode, "=");

        withEqualsNode.AddWildcardTransition(withOptionValueNode, "<option_value>");

        withOptionValueNode.AddTokenTransition(TokenType.COMMA, withCommaNode, ",");
        withOptionValueNode.AddTokenTransition(TokenType.RPAREN, withCloseParenNode, ")");

        withOptionValueNode.AddTransition(new StateTransition(
            t => t.Type == TokenType.IDENTIFIER || t.Type == TokenType.VARIABLE || IsWord(t.Value),
            withOptionNameNode,
            "<option_name>"
        ));
        withOptionValueNode.AddTransition(new StateTransition(
            t => t.Type != TokenType.COMMA && t.Type != TokenType.RPAREN,
            withOptionValueNode,
            "<expression_token>"
        ));

        withCommaNode.AddTransition(new StateTransition(
            t => t.Type == TokenType.IDENTIFIER || t.Type == TokenType.VARIABLE || IsWord(t.Value),
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

    private static void ConfigureDmlAndQueries(GrammarStateTree tree, StateNode withNode)
    {
        // 1. SELECT Node
        var selectNode = new StateNode("SELECT");
        var selectExpr = new StateNode("SELECT_EXPR");
        var intoNode = new StateNode("SELECT_INTO");
        var intoTable = new StateNode("SELECT_INTO_TABLE");
        var fromNode = new StateNode("SELECT_FROM");
        var fromSource = new StateNode("SELECT_FROM_SOURCE");

        var joinTypeNode = new StateNode("SELECT_JOIN_TYPE");
        var joinNode = new StateNode("SELECT_JOIN");
        var joinSource = new StateNode("SELECT_JOIN_SOURCE");
        var joinOnNode = new StateNode("SELECT_JOIN_ON");

        var whereNode = new StateNode("SELECT_WHERE");
        var groupKey = new StateNode("SELECT_GROUP");
        var groupBy = new StateNode("SELECT_GROUP_BY");
        var orderKey = new StateNode("SELECT_ORDER");
        var orderBy = new StateNode("SELECT_ORDER_BY");

        tree.RegisterStartNode("SELECT", selectNode);

        selectNode.AddWildcardTransition(selectExpr, "<expression>");

        // selectExpr self-loop until FROM or INTO
        selectExpr.AddTransition(new StateTransition(
            t => !t.Value.Equals("FROM", StringComparison.OrdinalIgnoreCase) && !t.Value.Equals("INTO", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            selectExpr,
            "<expression_token>"
        ));
        selectExpr.AddTransitionTo("INTO", intoNode, SuggestionType.Keyword);
        selectExpr.AddTransitionTo("FROM", fromNode, SuggestionType.Keyword);

        // INTO
        intoNode.AddWildcardTransition(intoTable, "<temp_table>");
        intoTable.AddTransition(new StateTransition(
            t => !t.Value.Equals("FROM", StringComparison.OrdinalIgnoreCase) &&
                 !t.Value.Equals("UNION", StringComparison.OrdinalIgnoreCase) &&
                 !t.Value.Equals("EXCEPT", StringComparison.OrdinalIgnoreCase) &&
                 !t.Value.Equals("INTERSECT", StringComparison.OrdinalIgnoreCase) &&
                 t.Type != TokenType.SEMICOLON,
            intoTable,
            "<into_table_token>"
        ));
        intoTable.AddTransitionTo("FROM", fromNode, SuggestionType.Keyword);

        // FROM
        fromNode.AddWildcardTransition(fromSource, "<table_source>");
        fromSource.AddTransition(new StateTransition(
            t => !IsQueryKeyword(t.Value) && t.Type != TokenType.SEMICOLON,
            fromSource,
            "<from_source_token>"
        ));

        // JOINs
        fromSource.AddTransitionTo("JOIN", joinNode, SuggestionType.Keyword);
        foreach (var jt in new[] { "INNER", "LEFT", "RIGHT", "FULL", "CROSS" })
        {
            fromSource.AddTransitionTo(jt, joinTypeNode, SuggestionType.Keyword);
            joinOnNode.AddTransitionTo(jt, joinTypeNode, SuggestionType.Keyword);
        }
        joinTypeNode.AddTransitionTo("JOIN", joinNode, SuggestionType.Keyword);

        joinNode.AddWildcardTransition(joinSource, "<join_table>");
        joinSource.AddTransition(new StateTransition(
            t => !t.Value.Equals("ON", StringComparison.OrdinalIgnoreCase) && !IsQueryKeyword(t.Value) && t.Type != TokenType.SEMICOLON,
            joinSource,
            "<join_source_token>"
        ));
        joinSource.AddTransitionTo("ON", joinOnNode, SuggestionType.Keyword);

        joinOnNode.AddTransition(new StateTransition(
            t => !t.Value.Equals("JOIN", StringComparison.OrdinalIgnoreCase) && !IsJoinTypeKeyword(t.Value) && !IsQueryKeyword(t.Value) && t.Type != TokenType.SEMICOLON,
            joinOnNode,
            "<join_on_token>"
        ));
        joinOnNode.AddTransitionTo("JOIN", joinNode, SuggestionType.Keyword);

        // WHERE / GROUP BY / ORDER BY routing from FROM & JOIN ON
        fromSource.AddTransitionTo("WHERE", whereNode, SuggestionType.Keyword);
        fromSource.AddTransitionTo("GROUP", groupKey, SuggestionType.Keyword);
        fromSource.AddTransitionTo("ORDER", orderKey, SuggestionType.Keyword);

        joinOnNode.AddTransitionTo("WHERE", whereNode, SuggestionType.Keyword);
        joinOnNode.AddTransitionTo("GROUP", groupKey, SuggestionType.Keyword);
        joinOnNode.AddTransitionTo("ORDER", orderKey, SuggestionType.Keyword);

        joinSource.AddTransitionTo("WHERE", whereNode, SuggestionType.Keyword);
        joinSource.AddTransitionTo("GROUP", groupKey, SuggestionType.Keyword);
        joinSource.AddTransitionTo("ORDER", orderKey, SuggestionType.Keyword);

        // WHERE
        whereNode.AddTransition(new StateTransition(
            t => !t.Value.Equals("GROUP", StringComparison.OrdinalIgnoreCase) && !t.Value.Equals("ORDER", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            whereNode,
            "<where_token>"
        ));
        whereNode.AddTransitionTo("GROUP", groupKey, SuggestionType.Keyword);
        whereNode.AddTransitionTo("ORDER", orderKey, SuggestionType.Keyword);

        // GROUP BY
        groupKey.AddTransitionTo("BY", groupBy, SuggestionType.Keyword);
        groupBy.AddTransition(new StateTransition(
            t => !t.Value.Equals("ORDER", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            groupBy,
            "<group_by_token>"
        ));
        groupBy.AddTransitionTo("ORDER", orderKey, SuggestionType.Keyword);

        // ORDER BY
        orderKey.AddTransitionTo("BY", orderBy, SuggestionType.Keyword);
        orderBy.AddTransition(new StateTransition(
            t => t.Type != TokenType.SEMICOLON,
            orderBy,
            "<order_by_token>"
        ));

        // Set operators (UNION, UNION ALL, EXCEPT, INTERSECT)
        var unionNode = new StateNode("UNION");

        void AddUnionTransitions(StateNode fromState)
        {
            fromState.AddTransition(new StateTransition(
                t => t.Value.Equals("UNION", StringComparison.OrdinalIgnoreCase) ||
                     t.Value.Equals("EXCEPT", StringComparison.OrdinalIgnoreCase) ||
                     t.Value.Equals("INTERSECT", StringComparison.OrdinalIgnoreCase),
                unionNode,
                "UNION/EXCEPT/INTERSECT"
            ));
        }

        AddUnionTransitions(selectExpr);
        AddUnionTransitions(intoTable);
        AddUnionTransitions(fromSource);
        AddUnionTransitions(joinOnNode);
        AddUnionTransitions(whereNode);
        AddUnionTransitions(groupBy);
        AddUnionTransitions(orderBy);

        // From unionNode: allow optional ALL/DISTINCT, then must go to SELECT
        var unionAll = new StateNode("UNION_ALL");
        unionNode.AddTransitionTo("ALL", unionAll, SuggestionType.Keyword);
        unionNode.AddTransitionTo("DISTINCT", unionAll, SuggestionType.Keyword);

        unionNode.AddTransitionTo("SELECT", selectNode, SuggestionType.Keyword);
        unionAll.AddTransitionTo("SELECT", selectNode, SuggestionType.Keyword);

        // Subqueries (nested SELECT queries inside FROM and JOIN clauses)
        var querySubqueryStart = new StateNode("QUERY_SUBQUERY_START");
        var querySubqueryEnd = new StateNode("QUERY_SUBQUERY_END");
        var querySubqueryAlias = new StateNode("QUERY_SUBQUERY_ALIAS");

        static int GetQueryDepth(TokenWalker walker) => walker.StateBag.TryGetValue("QueryParenDepth", out var d) ? (int)d : 0;

        fromSource.AddTokenTransition(TokenType.LPAREN, querySubqueryStart, "(", null, null,
            (token, walker) =>
            {
                int d = GetQueryDepth(walker);
                walker.StateBag["QueryParenDepth"] = d + 1;
            }
        );

        joinSource.AddTokenTransition(TokenType.LPAREN, querySubqueryStart, "(", null, null,
            (token, walker) =>
            {
                int d = GetQueryDepth(walker);
                walker.StateBag["QueryParenDepth"] = d + 1;
            }
        );

        querySubqueryStart.AddTransitionTo("SELECT", selectNode, SuggestionType.Keyword);

        void AddSubqueryEndTransition(StateNode fromState)
        {
            fromState.AddTransition(new StateTransition(
                t => t.Type == TokenType.RPAREN,
                querySubqueryEnd,
                ")",
                null, null,
                (token, walker) =>
                {
                    int d = GetQueryDepth(walker);
                    walker.StateBag["QueryParenDepth"] = Math.Max(0, d - 1);
                },
                contextCondition: (_, walker) => GetQueryDepth(walker) > 0
            ));
        }

        AddSubqueryEndTransition(selectExpr);
        AddSubqueryEndTransition(fromSource);
        AddSubqueryEndTransition(joinOnNode);
        AddSubqueryEndTransition(whereNode);
        AddSubqueryEndTransition(groupBy);
        AddSubqueryEndTransition(orderBy);

        // AS alias
        querySubqueryEnd.AddTransitionTo("AS", querySubqueryAlias, SuggestionType.Keyword);
        querySubqueryAlias.AddWildcardTransition(fromSource, "<alias_name>");

        // Direct alias (without AS)
        querySubqueryEnd.AddTransition(new StateTransition(
            t => t.Type == TokenType.IDENTIFIER || IsWord(t.Value),
            fromSource,
            "<alias_name>"
        ));

        // No alias (e.g. subquery in IN clause or just without alias)
        querySubqueryEnd.AddTransition(new StateTransition(
            t => t.Type != TokenType.IDENTIFIER && !IsWord(t.Value) && t.Type != TokenType.SEMICOLON,
            fromSource,
            "<subquery_end>"
        ));

        // 2. INSERT Node
        var insertNode = new StateNode("INSERT");
        var insertInto = new StateNode("INSERT_INTO");
        var insertTable = new StateNode("INSERT_TABLE");
        var insertValues = new StateNode("INSERT_VALUES");

        var metadataMutateNode = new StateNode("METADATA_MUTATE");
        metadataMutateNode.AddTransition(new StateTransition(
            t => t.Type != TokenType.SEMICOLON,
            metadataMutateNode,
            "<metadata_token>"
        ));

        tree.RegisterStartNode("INSERT", insertNode);
        insertNode.AddTransitionTo("INTO", insertInto, SuggestionType.Keyword);
        insertNode.AddTransitionTo("TAG", metadataMutateNode, SuggestionType.Keyword);
        insertNode.AddTransitionTo("LINEAGE", metadataMutateNode, SuggestionType.Keyword);
        insertInto.AddWildcardTransition(insertTable, "<target_table>");
        insertTable.AddTransition(new StateTransition(
            t => !t.Value.Equals("VALUES", StringComparison.OrdinalIgnoreCase) && !t.Value.Equals("SELECT", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            insertTable,
            "<insert_column_token>"
        ));
        insertTable.AddTransitionTo("VALUES", insertValues, SuggestionType.Keyword);
        insertTable.AddTransitionTo("SELECT", selectNode, SuggestionType.Keyword); // insert-select

        insertValues.AddTransition(new StateTransition(
            t => t.Type != TokenType.SEMICOLON,
            insertValues,
            "<insert_value_token>"
        ));

        // 3. UPDATE Node
        var updateNode = new StateNode("UPDATE");
        var updateTable = new StateNode("UPDATE_TABLE");
        var updateSet = new StateNode("UPDATE_SET");

        tree.RegisterStartNode("UPDATE", updateNode);
        updateNode.AddTransitionTo("TAG", metadataMutateNode, SuggestionType.Keyword);
        updateNode.AddWildcardTransition(updateTable, "<target_table>");
        updateTable.AddTransition(new StateTransition(
            t => !t.Value.Equals("SET", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            updateTable,
            "<target_table_token>"
        ));
        updateTable.AddTransitionTo("SET", updateSet, SuggestionType.Keyword);
        updateSet.AddTransition(new StateTransition(
            t => !t.Value.Equals("FROM", StringComparison.OrdinalIgnoreCase) && !t.Value.Equals("WHERE", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            updateSet,
            "<update_set_token>"
        ));
        updateSet.AddTransitionTo("FROM", fromNode, SuggestionType.Keyword);
        updateSet.AddTransitionTo("WHERE", whereNode, SuggestionType.Keyword);

        // 4. DELETE Node
        var deleteNode = new StateNode("DELETE");
        var deleteTable = new StateNode("DELETE_TABLE");

        tree.RegisterStartNode("DELETE", deleteNode);
        deleteNode.AddTransitionTo("TAG", metadataMutateNode, SuggestionType.Keyword);
        deleteNode.AddTransitionTo("LINEAGE", metadataMutateNode, SuggestionType.Keyword);
        deleteNode.AddTransitionTo("FROM", fromNode, SuggestionType.Keyword); // DELETE FROM table
        deleteNode.AddWildcardTransition(deleteTable, "<target_table>");     // DELETE target WHERE ...
        deleteTable.AddTransition(new StateTransition(
            t => !t.Value.Equals("FROM", StringComparison.OrdinalIgnoreCase) && !t.Value.Equals("WHERE", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            deleteTable,
            "<target_table_token>"
        ));
        deleteTable.AddTransitionTo("FROM", fromNode, SuggestionType.Keyword);
        deleteTable.AddTransitionTo("WHERE", whereNode, SuggestionType.Keyword);

        // 5. MERGE Node
        var mergeNode = new StateNode("MERGE");
        var mergeInto = new StateNode("MERGE_INTO");
        var mergeTarget = new StateNode("MERGE_TARGET");
        var mergeUsing = new StateNode("MERGE_USING");
        var mergeSource = new StateNode("MERGE_SOURCE");
        var mergeOn = new StateNode("MERGE_ON");
        var mergeWhen = new StateNode("MERGE_WHEN");
        var mergeMatched = new StateNode("MERGE_MATCHED");
        var mergeThen = new StateNode("MERGE_THEN");
        var mergeAction = new StateNode("MERGE_ACTION");

        tree.RegisterStartNode("MERGE", mergeNode);
        mergeNode.AddTransitionTo("INTO", mergeInto, SuggestionType.Keyword);
        mergeInto.AddWildcardTransition(mergeTarget, "<target_table>");
        mergeTarget.AddTransition(new StateTransition(
            t => !t.Value.Equals("USING", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            mergeTarget,
            "<target_table_token>"
        ));
        mergeTarget.AddTransitionTo("USING", mergeUsing, SuggestionType.Keyword);
        mergeUsing.AddWildcardTransition(mergeSource, "<source_table>");
        mergeSource.AddTransition(new StateTransition(
            t => !t.Value.Equals("ON", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            mergeSource,
            "<source_table_token>"
        ));
        mergeSource.AddTransitionTo("ON", mergeOn, SuggestionType.Keyword);

        mergeOn.AddTransition(new StateTransition(
            t => !t.Value.Equals("WHEN", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            mergeOn,
            "<merge_condition_token>"
        ));
        mergeOn.AddTransitionTo("WHEN", mergeWhen, SuggestionType.Keyword);

        mergeWhen.AddTransition(new StateTransition(
            t => t.Value.Equals("MATCHED", StringComparison.OrdinalIgnoreCase) || t.Value.Equals("NOT", StringComparison.OrdinalIgnoreCase),
            mergeMatched,
            "MATCHED/NOT"
        ));

        var mergeMatchedWord = new StateNode("MERGE_MATCHED_WORD");
        var mergeBy = new StateNode("MERGE_BY");
        var mergeCond = new StateNode("MERGE_COND");

        // From mergeMatched: if we got NOT, we expect MATCHED.
        mergeMatched.AddTransition(new StateTransition(
            t => t.Value.Equals("MATCHED", StringComparison.OrdinalIgnoreCase),
            mergeMatchedWord,
            "MATCHED"
        ));

        // Setup transitions from both mergeMatched (for MATCHED) and mergeMatchedWord (for NOT MATCHED)
        void AddMergeOptionalTransitions(StateNode node)
        {
            node.AddTransitionTo("BY", mergeBy, SuggestionType.Keyword);
            node.AddTransitionTo("AND", mergeCond, SuggestionType.Keyword);
            node.AddTransitionTo("THEN", mergeThen, SuggestionType.Keyword);
        }

        AddMergeOptionalTransitions(mergeMatched);
        AddMergeOptionalTransitions(mergeMatchedWord);

        // From mergeBy: expect TARGET or SOURCE, then transition back to mergeMatchedWord
        mergeBy.AddTransition(new StateTransition(
            t => t.Value.Equals("TARGET", StringComparison.OrdinalIgnoreCase) || t.Value.Equals("SOURCE", StringComparison.OrdinalIgnoreCase),
            mergeMatchedWord,
            "TARGET/SOURCE"
        ));

        // From mergeCond: loop until THEN
        mergeCond.AddTransition(new StateTransition(
            t => !t.Value.Equals("THEN", StringComparison.OrdinalIgnoreCase),
            mergeCond,
            "<condition_token>"
        ));
        mergeCond.AddTransitionTo("THEN", mergeThen, SuggestionType.Keyword);

        // From mergeThen: expect action UPDATE/INSERT/DELETE
        mergeThen.AddTransition(new StateTransition(
            t => t.Value.Equals("UPDATE", StringComparison.OrdinalIgnoreCase) || t.Value.Equals("INSERT", StringComparison.OrdinalIgnoreCase) || t.Value.Equals("DELETE", StringComparison.OrdinalIgnoreCase),
            mergeAction,
            "UPDATE/INSERT/DELETE"
        ));
        mergeAction.AddTransition(new StateTransition(
            t => !t.Value.Equals("WHEN", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            mergeAction,
            "<merge_action_token>"
        ));
        mergeAction.AddTransitionTo("WHEN", mergeWhen, SuggestionType.Keyword);

        // MERGE FILES <source> TO <dest>
        var mergeFiles = new StateNode("MERGE_FILES");
        var mergeFilesSource = new StateNode("MERGE_FILES_SOURCE");
        var mergeFilesTo = new StateNode("MERGE_FILES_TO");
        var mergeFilesDest = new StateNode("MERGE_FILES_DEST");

        mergeNode.AddTransitionTo("FILES", mergeFiles, SuggestionType.Keyword);
        mergeFiles.AddWildcardTransition(mergeFilesSource, "<source_pattern>");
        mergeFilesSource.AddTransition(new StateTransition(
            t => !t.Value.Equals("TO", StringComparison.OrdinalIgnoreCase),
            mergeFilesSource,
            "<source_token>"
        ));
        mergeFilesSource.AddTransitionTo("TO", mergeFilesTo, SuggestionType.Keyword);
        mergeFilesTo.AddWildcardTransition(mergeFilesDest, "<destination>");
        mergeFilesDest.AddTransition(new StateTransition(
            t => !t.Value.Equals("WITH", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON && tree.GetStartNode(t.Value) == null,
            mergeFilesDest,
            "<dest_token>"
        ));

        mergeFilesDest.AddTransitionTo("WITH", withNode, SuggestionType.Keyword);

        mergeFilesDest.AddTransition(new StateTransition(
            t => tree.GetStartNode(t.Value) != null,
            tree.Root,
            "<next_statement>"
        ));

        // 6. WITH <cte> AS ( <subquery> ) [, <cte> AS ( ... )] <SELECT|INSERT|UPDATE|DELETE|MERGE>
        // The parser accepts CTEs, so the grammar must too (otherwise completion stops offering valid
        // next tokens inside/after a WITH). The subquery body is consumed with balanced-paren tracking
        // via the walker StateBag, then control hands off to the existing DML start nodes.
        var withStart = new StateNode("WITH");
        var withName = new StateNode("WITH_NAME");
        var withAs = new StateNode("WITH_AS");
        var withBody = new StateNode("WITH_BODY");
        var withAfter = new StateNode("WITH_AFTER");

        tree.RegisterStartNode("WITH", withStart);

        var withCols = new StateNode("WITH_COLS");
        var withColsDone = new StateNode("WITH_COLS_DONE");

        withStart.AddTransition(new StateTransition(
            t => t.Type == TokenType.IDENTIFIER || IsWord(t.Value),
            withName,
            "<cte_name>"));
        withName.AddTransitionTo("AS", withAs, SuggestionType.Keyword);

        // Optional explicit column list: WITH name ( col1, col2 ) AS ( ... )
        withName.AddTokenTransition(TokenType.LPAREN, withCols, "(");
        withCols.AddTransition(new StateTransition(
            t => t.Type != TokenType.RPAREN && t.Type != TokenType.EOF,
            withCols,
            "<cte_column>"));
        withCols.AddTokenTransition(TokenType.RPAREN, withColsDone, ")");
        withColsDone.AddTransitionTo("AS", withAs, SuggestionType.Keyword);

        // AS -> "(" enters the balanced body at depth 0.
        withAs.AddTokenTransition(TokenType.LPAREN, withBody, "(",
            onTransition: (t, w) => w.StateBag["cteDepth"] = 0);

        // Nested "(" deepens; ")" while nested returns a level; ")" at depth 0 closes the CTE.
        withBody.AddTransition(new StateTransition(
            t => t.Type == TokenType.LPAREN,
            withBody,
            "(",
            onTransition: (t, w) => w.StateBag["cteDepth"] = GetCteDepth(w) + 1));
        withBody.AddTransition(new StateTransition(
            t => t.Type == TokenType.RPAREN,
            withBody,
            ")",
            contextCondition: (t, w) => GetCteDepth(w) > 0,
            onTransition: (t, w) => w.StateBag["cteDepth"] = GetCteDepth(w) - 1));
        withBody.AddTransition(new StateTransition(
            t => t.Type == TokenType.RPAREN,
            withAfter,
            ")",
            contextCondition: (t, w) => GetCteDepth(w) == 0));
        withBody.AddTransition(new StateTransition(
            t => t.Type != TokenType.LPAREN && t.Type != TokenType.RPAREN && t.Type != TokenType.EOF,
            withBody,
            "<cte_body_token>"));

        // After ")": another CTE (comma) or the main statement.
        withAfter.AddTokenTransition(TokenType.COMMA, withStart, ",");
        withAfter.AddTransitionTo("SELECT", selectNode, SuggestionType.Keyword);
        withAfter.AddTransitionTo("INSERT", insertNode, SuggestionType.Keyword);
        withAfter.AddTransitionTo("UPDATE", updateNode, SuggestionType.Keyword);
        withAfter.AddTransitionTo("DELETE", deleteNode, SuggestionType.Keyword);
        withAfter.AddTransitionTo("MERGE", mergeNode, SuggestionType.Keyword);
    }

    private static int GetCteDepth(TokenWalker walker) =>
        walker.StateBag.TryGetValue("cteDepth", out var d) && d is int i ? i : 0;

    private static bool IsQueryKeyword(string val)
    {
        return val.Equals("JOIN", StringComparison.OrdinalIgnoreCase) ||
               val.Equals("WHERE", StringComparison.OrdinalIgnoreCase) ||
               val.Equals("GROUP", StringComparison.OrdinalIgnoreCase) ||
               val.Equals("ORDER", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsJoinTypeKeyword(string val)
    {
        return val.Equals("INNER", StringComparison.OrdinalIgnoreCase) ||
               val.Equals("LEFT", StringComparison.OrdinalIgnoreCase) ||
               val.Equals("RIGHT", StringComparison.OrdinalIgnoreCase) ||
               val.Equals("FULL", StringComparison.OrdinalIgnoreCase) ||
               val.Equals("CROSS", StringComparison.OrdinalIgnoreCase);
    }

    private static void ConfigureControlFlow(GrammarStateTree tree)
    {
        // 1. IF / ELSE Statement
        var ifNode = new StateNode("IF");
        var ifCondition = new StateNode("IF_CONDITION");
        var elseNode = new StateNode("ELSE");

        tree.RegisterStartNode("IF", ifNode);
        ifNode.AddWildcardTransition(ifCondition, "<condition>");

        // IF condition can transition to BEGIN or reset to Root, or to ELSE
        ifCondition.AddTransition(new StateTransition(
            t => !t.Value.Equals("BEGIN", StringComparison.OrdinalIgnoreCase) && !t.Value.Equals("ELSE", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            ifCondition,
            "<condition_token>"
        ));
        ifCondition.AddTransitionTo("BEGIN", tree.Root, SuggestionType.Keyword);
        ifCondition.AddTransitionTo("ELSE", elseNode, SuggestionType.Keyword);
        elseNode.AddTransitionTo("BEGIN", tree.Root, SuggestionType.Keyword);
        elseNode.AddTransition(new StateTransition(t => t.Type != TokenType.SEMICOLON, tree.Root, "<statement_start>")); // Else can be followed directly by a single statement

        // 2. WHILE Loop
        var whileNode = new StateNode("WHILE");
        var whileCondition = new StateNode("WHILE_CONDITION");

        tree.RegisterStartNode("WHILE", whileNode);
        whileNode.AddWildcardTransition(whileCondition, "<condition>");
        whileCondition.AddTransition(new StateTransition(
            t => !t.Value.Equals("BEGIN", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            whileCondition,
            "<condition_token>"
        ));
        whileCondition.AddTransitionTo("BEGIN", tree.Root, SuggestionType.Keyword);

        // 3. FOR Loop
        var forNode = new StateNode("FOR");
        var forParams = new StateNode("FOR_PARAMS");

        tree.RegisterStartNode("FOR", forNode);
        forNode.AddWildcardTransition(forParams, "<parameters>");
        forParams.AddTransition(new StateTransition(
            t => !t.Value.Equals("BEGIN", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            forParams,
            "<for_parameter_token>"
        ));
        forParams.AddTransitionTo("BEGIN", tree.Root, SuggestionType.Keyword);

        // 4. FOREACH Loop
        var foreachNode = new StateNode("FOREACH");
        var foreachVar = new StateNode("FOREACH_VAR");
        var foreachIn = new StateNode("FOREACH_IN");
        var foreachSource = new StateNode("FOREACH_SOURCE");

        tree.RegisterStartNode("FOREACH", foreachNode);
        foreachNode.AddTokenTransition(TokenType.VARIABLE, foreachVar, "<variable>");
        foreachVar.AddTransitionTo("IN", foreachIn, SuggestionType.Keyword);
        foreachIn.AddWildcardTransition(foreachSource, "<source>");
        foreachSource.AddTransition(new StateTransition(
            t => !t.Value.Equals("BEGIN", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            foreachSource,
            "<source_token>"
        ));
        foreachSource.AddTransitionTo("BEGIN", tree.Root, SuggestionType.Keyword);

        // 5. BEGIN / END blocks (Reset to Root)
        // Also handles BEGIN TRY / END TRY / BEGIN CATCH / END CATCH
        var beginNode = new StateNode("BEGIN");
        var endNode = new StateNode("END");

        tree.RegisterStartNode("BEGIN", beginNode);
        tree.RegisterStartNode("END", endNode);

        // BEGIN -> Root, or BEGIN -> TRY/CATCH -> Root, or BEGIN -> TRANSACTION -> name -> Root
        var beginTransaction = new StateNode("BEGIN_TRANSACTION");
        var beginTransactionName = new StateNode("BEGIN_TRANSACTION_NAME");

        beginNode.AddTransitionTo("TRY", tree.Root, SuggestionType.Keyword);
        beginNode.AddTransitionTo("CATCH", tree.Root, SuggestionType.Keyword);
        beginNode.AddTransitionTo("TRANSACTION", beginTransaction, SuggestionType.Keyword);

        beginTransaction.AddWildcardTransition(beginTransactionName, "<transaction_name>");
        beginTransactionName.AddTransition(new StateTransition(t => t.Type != TokenType.SEMICOLON, beginTransactionName, "<transaction_token>"));
        beginTransactionName.AddTransition(new StateTransition(t => tree.GetStartNode(t.Value) != null, tree.Root, "<next_statement>"));
        beginTransaction.AddTransition(new StateTransition(t => tree.GetStartNode(t.Value) != null, tree.Root, "<next_statement>"));

        beginNode.AddTransition(new StateTransition(t => !t.Value.Equals("TRY", StringComparison.OrdinalIgnoreCase) && !t.Value.Equals("CATCH", StringComparison.OrdinalIgnoreCase) && !t.Value.Equals("TRANSACTION", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON, tree.Root, "<statement_start>"));

        // END -> Root, or END -> TRY/CATCH -> Root
        endNode.AddTransitionTo("TRY", tree.Root, SuggestionType.Keyword);
        endNode.AddTransitionTo("CATCH", tree.Root, SuggestionType.Keyword);
        endNode.AddTransition(new StateTransition(t => !t.Value.Equals("TRY", StringComparison.OrdinalIgnoreCase) && !t.Value.Equals("CATCH", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON, tree.Root, "<statement_start>"));
    }

    private static void ConfigureSpecializedOperations(GrammarStateTree tree, StateNode withNode, StateNode atNode, StateNode toNode)
    {
        // 1. RUN SCRIPT 'path'
        var runNode = new StateNode("RUN");
        var runScriptNode = new StateNode("RUN_SCRIPT");
        var scriptPath = new StateNode("RUN_SCRIPT_PATH");

        tree.RegisterStartNode("RUN", runNode);
        runNode.AddTransitionTo("SCRIPT", runScriptNode, SuggestionType.Keyword);
        runScriptNode.AddWildcardTransition(scriptPath, "<script_path>");

        scriptPath.AddTransition(new StateTransition(
            t => !t.Value.Equals("WITH", StringComparison.OrdinalIgnoreCase) &&
                 !t.Value.Equals("AT", StringComparison.OrdinalIgnoreCase) &&
                 t.Type != TokenType.SEMICOLON &&
                 tree.GetStartNode(t.Value) == null,
            scriptPath,
            "<expression_token>"
        ));

        scriptPath.AddTransitionTo("WITH", withNode, SuggestionType.Keyword);
        scriptPath.AddTransitionTo("AT", atNode, SuggestionType.Keyword);

        // 2. WAITFOR DELAY 'hh:mm:ss'
        var waitforNode = new StateNode("WAITFOR");
        var waitforDelay = new StateNode("WAITFOR_DELAY");
        var waitforTime = new StateNode("WAITFOR_TIME");
        var waitforCondition = new StateNode("WAITFOR_CONDITION");

        tree.RegisterStartNode("WAITFOR", waitforNode);
        waitforNode.AddTransitionTo("DELAY", waitforDelay, SuggestionType.Keyword);
        waitforNode.AddTransitionTo("TIME", waitforTime, SuggestionType.Keyword);
        waitforNode.AddTokenTransition(TokenType.LPAREN, waitforCondition, "(", null, null,
            (token, walker) => walker.StateBag["WaitForParenDepth"] = 1);

        // 2b. WAITFOR FILE UNLOCKED '<path>' [WITH(TIMEOUT = <n>, POLL_INTERVAL_MS = <n>)]
        // The parser accepts this (ExtensionParser.ParseWaitFor branches on FILE first), so the
        // grammar must too — otherwise a valid script lints as a syntax error and completion stops
        // offering the next token.
        var waitforFile = new StateNode("WAITFOR_FILE");
        var waitforFileUnlocked = new StateNode("WAITFOR_FILE_UNLOCKED");
        var waitforFilePath = new StateNode("WAITFOR_FILE_PATH");

        waitforNode.AddTransitionTo("FILE", waitforFile, SuggestionType.Keyword);
        waitforFile.AddTransitionTo("UNLOCKED", waitforFileUnlocked, SuggestionType.Keyword);
        waitforFileUnlocked.AddWildcardTransition(waitforFilePath, "<file_path>");

        // Absorb the rest of the path expression and the bare TIMEOUT/POLL_INTERVAL_MS form, stopping
        // at WITH, a statement terminator, or the start of the next statement.
        //
        // Deliberately no "<next_statement>" transition to the root here: such a transition *consumes*
        // the following statement's first token to make the move, so `WAITFOR ...; SELECT * FROM t;`
        // would swallow the SELECT and then reject the `*`. The semicolon already ends the statement.
        waitforFilePath.AddTransition(new StateTransition(
            t => !t.Value.Equals("WITH", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON && tree.GetStartNode(t.Value) == null,
            waitforFilePath,
            "<option_token>"
        ));
        waitforFilePath.AddTransitionTo("WITH", withNode, SuggestionType.Keyword);

        // Wait FOR UNTIL condition
        var waitNode = new StateNode("WAIT");
        var waitUntilCondition = new StateNode("WAIT_UNTIL_CONDITION");

        tree.RegisterStartNode("WAIT", waitNode);
        waitNode.AddTransitionTo("UNTIL", waitUntilCondition, SuggestionType.Keyword);

        waitforDelay.AddWildcardTransition(tree.Root, "<time_expression>");
        waitforTime.AddWildcardTransition(tree.Root, "<time_expression>");

        // Wait until parenthesized/unparenthesized condition transitions
        static int GetWaitUntilDepth(TokenWalker walker) => walker.StateBag.TryGetValue("WaitUntilParenDepth", out var d) ? (int)d : 0;

        waitUntilCondition.AddTransition(new StateTransition(
            t => t.Type == TokenType.LPAREN,
            waitUntilCondition,
            "(",
            null, null,
            (token, walker) =>
            {
                int d = GetWaitUntilDepth(walker);
                walker.StateBag["WaitUntilParenDepth"] = d + 1;
            }
        ));

        waitUntilCondition.AddTransition(new StateTransition(
            t => t.Type == TokenType.RPAREN,
            waitUntilCondition,
            ")",
            null, null,
            (token, walker) =>
            {
                int d = GetWaitUntilDepth(walker);
                walker.StateBag["WaitUntilParenDepth"] = Math.Max(0, d - 1);
            },
            contextCondition: (_, walker) => GetWaitUntilDepth(walker) > 0
        ));

        waitUntilCondition.AddTransition(new StateTransition(
            t => tree.GetStartNode(t.Value) != null,
            tree.Root,
            "<next_statement>",
            null, null,
            (token, walker) => walker.StateBag.Remove("WaitUntilParenDepth"),
            contextCondition: (_, walker) => GetWaitUntilDepth(walker) == 0
        ));

        waitUntilCondition.AddTransition(new StateTransition(
            t => t.Type != TokenType.LPAREN && t.Type != TokenType.RPAREN && t.Type != TokenType.SEMICOLON,
            waitUntilCondition,
            "<condition_token>",
            contextCondition: (token, walker) => tree.GetStartNode(token.Value) == null || GetWaitUntilDepth(walker) > 0
        ));

        // Parenthesized condition nesting depth support
        static int GetWaitForDepth(TokenWalker walker) => walker.StateBag.TryGetValue("WaitForParenDepth", out var d) ? (int)d : 0;

        waitforCondition.AddTransition(new StateTransition(
            t => t.Type == TokenType.LPAREN,
            waitforCondition,
            "(",
            null, null,
            (token, walker) =>
            {
                int d = GetWaitForDepth(walker);
                walker.StateBag["WaitForParenDepth"] = d + 1;
            }
        ));

        waitforCondition.AddTransition(new StateTransition(
            t => t.Type == TokenType.RPAREN,
            waitforCondition,
            ")",
            null, null,
            (token, walker) =>
            {
                int d = GetWaitForDepth(walker);
                walker.StateBag["WaitForParenDepth"] = Math.Max(1, d - 1);
            },
            contextCondition: (_, walker) => GetWaitForDepth(walker) > 1
        ));

        waitforCondition.AddTransition(new StateTransition(
            t => t.Type == TokenType.RPAREN,
            tree.Root,
            ")",
            null, null,
            (token, walker) => walker.StateBag.Remove("WaitForParenDepth"),
            contextCondition: (_, walker) => GetWaitForDepth(walker) <= 1
        ));

        waitforCondition.AddTransition(new StateTransition(
            t => t.Type != TokenType.LPAREN && t.Type != TokenType.RPAREN && t.Type != TokenType.SEMICOLON,
            waitforCondition,
            "<condition_token>"
        ));

        // 3. SEND EMAIL / FILE
        var sendNode = new StateNode("SEND");
        var sendEmail = new StateNode("SEND EMAIL");
        var emailSubject = new StateNode("SEND EMAIL SUBJECT");

        var sendFile = new StateNode("SEND FILE");
        var sendFilePath = new StateNode("SEND FILE PATH");

        tree.RegisterStartNode("SEND", sendNode);

        // Email path
        sendNode.AddTransitionTo("EMAIL", sendEmail, SuggestionType.Keyword);
        sendEmail.AddWildcardTransition(emailSubject, "<subject>");

        emailSubject.AddTransition(new StateTransition(
            t => !t.Value.Equals("AT", StringComparison.OrdinalIgnoreCase) && !t.Value.Equals("WITH", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            emailSubject,
            "<subject_token>"
        ));
        emailSubject.AddTransitionTo("AT", atNode, SuggestionType.Keyword);
        emailSubject.AddTransitionTo("WITH", withNode, SuggestionType.Keyword);

        // File path: SEND FILE 'path' TO 'destination'
        sendNode.AddTransitionTo("FILE", sendFile, SuggestionType.Keyword);
        sendFile.AddWildcardTransition(sendFilePath, "<file_path>");
        sendFilePath.AddTransition(new StateTransition(
            t => !t.Value.Equals("TO", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            sendFilePath,
            "<file_path_token>"
        ));
        sendFilePath.AddTransitionTo("TO", toNode, SuggestionType.Keyword);

        // 3.5 RECEIVE FILE FROM 'path' TO 'destination'
        var receiveNode = new StateNode("RECEIVE");
        var receiveFile = new StateNode("RECEIVE FILE");
        var receiveFrom = new StateNode("RECEIVE FILE FROM");
        var receiveFilePath = new StateNode("RECEIVE FILE PATH");

        tree.RegisterStartNode("RECEIVE", receiveNode);
        receiveNode.AddTransitionTo("FILE", receiveFile, SuggestionType.Keyword);
        receiveFile.AddTransitionTo("FROM", receiveFrom, SuggestionType.Keyword);
        receiveFrom.AddWildcardTransition(receiveFilePath, "<file_path>");
        receiveFile.AddWildcardTransition(receiveFilePath, "<file_path>");
        receiveFilePath.AddTransition(new StateTransition(
            t => !t.Value.Equals("TO", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            receiveFilePath,
            "<file_path_token>"
        ));
        receiveFilePath.AddTransitionTo("TO", toNode, SuggestionType.Keyword);

        // 4. EXPORT DATASET / SCRIPT / PORTAL / REPORT
        var exportNode = new StateNode("EXPORT");
        var exportDataset = new StateNode("EXPORT_DATASET");
        var datasetName = new StateNode("EXPORT_DATASET_NAME");

        var exportScript = new StateNode("EXPORT_SCRIPT");
        var scriptSource = new StateNode("EXPORT_SCRIPT_SOURCE");

        var exportPortal = new StateNode("EXPORT_PORTAL");
        var portalConfig = new StateNode("EXPORT_PORTAL_CONFIG");

        var exportReport = new StateNode("EXPORT_REPORT");
        var reportSource = new StateNode("EXPORT_REPORT_SOURCE");
        var reportFormatKeyword = new StateNode("EXPORT_REPORT_FORMAT_KW");
        var reportFormatVal = new StateNode("EXPORT_REPORT_FORMAT_VAL");

        var exportLineage = new StateNode("EXPORT_LINEAGE");
        var exportLineageFor = new StateNode("EXPORT_LINEAGE_FOR");
        var exportLineageForType = new StateNode("EXPORT_LINEAGE_FOR_TYPE");
        var exportLineageForName = new StateNode("EXPORT_LINEAGE_FOR_NAME");
        var exportLineageColumn = new StateNode("EXPORT_LINEAGE_COLUMN");
        var exportLineageColName = new StateNode("EXPORT_LINEAGE_COL_NAME");
        var exportLineageAs = new StateNode("EXPORT_LINEAGE_AS");
        var exportLineageFormat = new StateNode("EXPORT_LINEAGE_FORMAT");

        var exportTo = new StateNode("EXPORT_TO");
        var exportDest = new StateNode("EXPORT_DEST");

        tree.RegisterStartNode("EXPORT", exportNode);

        // IMPORT LINEAGE FOR [TABLE] <name> [AS OPENLINEAGE] FROM <file|json>
        var importNode = new StateNode("IMPORT");
        var importLineage = new StateNode("IMPORT_LINEAGE");
        var importLineageFor = new StateNode("IMPORT_LINEAGE_FOR");
        var importLineageName = new StateNode("IMPORT_LINEAGE_NAME");
        var importLineageAs = new StateNode("IMPORT_LINEAGE_AS");
        var importLineageFormat = new StateNode("IMPORT_LINEAGE_FORMAT");
        var importFrom = new StateNode("IMPORT_FROM");
        var importSource = new StateNode("IMPORT_SOURCE");

        tree.RegisterStartNode("IMPORT", importNode);
        importNode.AddTransitionTo("LINEAGE", importLineage, SuggestionType.Keyword);
        importLineage.AddTransitionTo("FOR", importLineageFor, SuggestionType.Keyword);
        importLineageFor.AddTransitionTo("TABLE", importLineageFor, SuggestionType.Keyword);
        importLineageFor.AddWildcardTransition(importLineageName, "<table_name>");

        importLineageName.AddTransitionTo("AS", importLineageAs, SuggestionType.Keyword);
        importLineageName.AddTransitionTo("FROM", importFrom, SuggestionType.Keyword);
        // Multi-part names, after the terminating keywords so they are not swallowed.
        importLineageName.AddTransition(new StateTransition(
            t => t.Type == TokenType.DOT
                 || ((t.Type == TokenType.IDENTIFIER || IsWord(t.Value))
                     && !t.Value.Equals("AS", StringComparison.OrdinalIgnoreCase)
                     && !t.Value.Equals("FROM", StringComparison.OrdinalIgnoreCase)),
            importLineageName,
            "<name_part>"
        ));

        importLineageAs.AddWildcardTransition(importLineageFormat, "<format>");
        importLineageFormat.AddTransitionTo("FROM", importFrom, SuggestionType.Keyword);
        importFrom.AddWildcardTransition(importSource, "<source_path_or_json>");

        // DATASET path
        exportNode.AddTransitionTo("DATASET", exportDataset, SuggestionType.Keyword);
        exportDataset.AddWildcardTransition(datasetName, "<dataset_name>");
        datasetName.AddTransition(new StateTransition(
            t => !t.Value.Equals("TO", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            datasetName,
            "<dataset_name_token>"
        ));
        datasetName.AddTransitionTo("TO", exportTo, SuggestionType.Keyword);

        // SCRIPT path
        exportNode.AddTransitionTo("SCRIPT", exportScript, SuggestionType.Keyword);
        exportScript.AddWildcardTransition(scriptSource, "<script_source>");
        scriptSource.AddTransition(new StateTransition(
            t => !t.Value.Equals("TO", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            scriptSource,
            "<script_source_token>"
        ));
        scriptSource.AddTransitionTo("TO", exportTo, SuggestionType.Keyword);

        // PORTAL path
        exportNode.AddTransitionTo("PORTAL", exportPortal, SuggestionType.Keyword);
        exportPortal.AddTransitionTo("CONFIGURATION", portalConfig, SuggestionType.Keyword);
        portalConfig.AddTransitionTo("TO", exportTo, SuggestionType.Keyword);

        // REPORT path
        exportNode.AddTransitionTo("REPORT", exportReport, SuggestionType.Keyword);
        exportReport.AddWildcardTransition(reportSource, "<report_source>");
        reportSource.AddTransition(new StateTransition(
            t => !t.Value.Equals("FORMAT", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            reportSource,
            "<report_source_token>"
        ));
        reportSource.AddTransitionTo("FORMAT", reportFormatKeyword, SuggestionType.Keyword);
        reportFormatKeyword.AddWildcardTransition(reportFormatVal, "<format>");
        reportFormatVal.AddTransitionTo("TO", exportTo, SuggestionType.Keyword);

        // LINEAGE path
        exportNode.AddTransitionTo("LINEAGE", exportLineage, SuggestionType.Keyword);
        exportLineage.AddTransitionTo("FOR", exportLineageFor, SuggestionType.Keyword);
        exportLineageFor.AddTransitionTo("REPORT", exportLineageForType, SuggestionType.Keyword);
        exportLineageFor.AddTransitionTo("DATASET", exportLineageForType, SuggestionType.Keyword);
        exportLineageFor.AddTransitionTo("TABLE", exportLineageForType, SuggestionType.Keyword);
        exportLineageFor.AddWildcardTransition(exportLineageForName, "<table_name>");

        exportLineageForType.AddWildcardTransition(exportLineageForName, "<name>");

        exportLineageForName.AddTransitionTo("COLUMN", exportLineageColumn, SuggestionType.Keyword);
        exportLineageForName.AddTransitionTo("AS", exportLineageAs, SuggestionType.Keyword);

        // Multi-part names: hospital.dbo.Patient. Registered after COLUMN/AS so those keywords
        // still terminate the name rather than being swallowed as another part of it.
        exportLineageForName.AddTransition(new StateTransition(
            t => t.Type == TokenType.DOT
                 || ((t.Type == TokenType.IDENTIFIER || IsWord(t.Value))
                     && !t.Value.Equals("AS", StringComparison.OrdinalIgnoreCase)
                     && !t.Value.Equals("COLUMN", StringComparison.OrdinalIgnoreCase)),
            exportLineageForName,
            "<name_part>"
        ));

        exportLineageColumn.AddWildcardTransition(exportLineageColName, "<column_name>");
        exportLineageColName.AddTransitionTo("AS", exportLineageAs, SuggestionType.Keyword);

        exportLineage.AddTransitionTo("AS", exportLineageAs, SuggestionType.Keyword);

        exportLineageAs.AddWildcardTransition(exportLineageFormat, "<format>");
        exportLineageFormat.AddTransitionTo("TO", exportTo, SuggestionType.Keyword);

        // Common TO -> destination
        exportTo.AddWildcardTransition(exportDest, "<destination_path>");
        exportDest.AddTransition(new StateTransition(
            t => !t.Value.Equals("WITH", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            exportDest,
            "<dest_path_token>"
        ));
        exportDest.AddTransitionTo("WITH", withNode, SuggestionType.Keyword);

        // 5. SET LINEAGE
        var setNode = new StateNode("SET");
        var setLineage = new StateNode("SET_LINEAGE");
        var setEquals = new StateNode("SET_EQUALS");

        tree.RegisterStartNode("SET", setNode);
        setNode.AddTransitionTo("LINEAGE", setLineage, SuggestionType.Keyword);
        setLineage.AddTokenTransition(TokenType.EQUALS, setEquals, "=");
        setEquals.AddWildcardTransition(tree.Root, "<lineage_value>");
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

    private static void ConfigureCommonStatements(GrammarStateTree tree)
    {
        // 1. PRINT
        var printNode = new StateNode("PRINT");
        var printExpr = new StateNode("PRINT_EXPR");
        tree.RegisterStartNode("PRINT", printNode);
        printNode.AddWildcardTransition(printExpr, "<expression>");
        printExpr.AddTransition(new StateTransition(
            t => t.Type != TokenType.SEMICOLON,
            printExpr,
            "<expression_token>"
        ));

        // 2. DECLARE
        var declareNode = new StateNode("DECLARE");
        var declareVar = new StateNode("DECLARE_VAR");
        tree.RegisterStartNode("DECLARE", declareNode);
        declareNode.AddTokenTransition(TokenType.VARIABLE, declareVar, "<variable_name>");
        declareVar.AddTransition(new StateTransition(
            t => t.Type != TokenType.SEMICOLON,
            declareVar,
            "<declaration_token>"
        ));

        // 3. SET (extend existing setNode)
        var setNode = tree.GetStartNode("SET");
        if (setNode != null)
        {
            var setVar = new StateNode("SET_VAR");
            var setVarEquals = new StateNode("SET_VAR_EQUALS");
            var setVarExpr = new StateNode("SET_VAR_EXPR");
            var setVarMember = new StateNode("SET_VAR_MEMBER");

            setNode.AddTokenTransition(TokenType.VARIABLE, setVar, "<variable_name>");
            setVar.AddTokenTransition(TokenType.DOT, setVarMember, ".");
            setVarMember.AddTransition(new StateTransition(
                t => t.Type == TokenType.IDENTIFIER || IsWord(t.Value),
                setVar,
                "<member_name>"
            ));
            setVar.AddTokenTransition(TokenType.EQUALS, setVarEquals, "=");
            setVarEquals.AddWildcardTransition(setVarExpr, "<expression>");
            setVarExpr.AddTransition(new StateTransition(
                t => t.Type != TokenType.SEMICOLON,
                setVarExpr,
                "<expression_token>"
            ));

            // Report metadata options (e.g. SET REPORT TITLE = Monthly Sales)
            var setReport = new StateNode("SET_REPORT");
            var setReportKey = new StateNode("SET_REPORT_KEY");
            var setReportEquals = new StateNode("SET_REPORT_EQUALS");
            var setReportVal = new StateNode("SET_REPORT_VAL");

            setNode.AddTransitionTo("REPORT", setReport, SuggestionType.Keyword);
            setReport.AddTransition(new StateTransition(
                t => t.Type == TokenType.IDENTIFIER || IsWord(t.Value),
                setReportKey,
                "<metadata_key>"
            ));
            setReportKey.AddTokenTransition(TokenType.EQUALS, setReportEquals, "=");
            setReportEquals.AddWildcardTransition(setReportVal, "<value>");
            setReportVal.AddTransition(new StateTransition(
                t => t.Type != TokenType.SEMICOLON,
                setReportVal,
                "<value_token>"
            ));

            // ON/OFF options (e.g. SET WHAT_IF ON, SET PROFILING OFF, etc.)
            var onOffOptions = new[] {
                "WHAT_IF", "PROFILING", "PROFILE", "SHOW_SECRETS", "SHOW_PASSWORD",
                "ALLOW_PLAINTEXT_SECRETS", "NO_SAVE_SENSITIVE", "NO_SAVE_CONNECTION",
                "CONNECTION_ENCRYPTION", "PERSIST", "SPILL_ENCRYPTION", "SPILL_COMPRESSION",
                "TELEMETRY", "INTERACTIVE_MODE", "CASE_SENSITIVE", "LINEAGE",
                "TRUNCATE_STRING", "SKIP_ERROR", "WITH_PROMPT"
            };

            foreach (var opt in onOffOptions)
            {
                var optNode = new StateNode("SET_" + opt);
                setNode.AddTransitionTo(opt, optNode, SuggestionType.Keyword);

                // Allow option name -> ON/OFF
                optNode.AddTransitionTo("ON", tree.Root, SuggestionType.Keyword);
                optNode.AddTransitionTo("OFF", tree.Root, SuggestionType.Keyword);

                // Allow option name -> = -> ON/OFF
                var optEqualsNode = new StateNode("SET_" + opt + "_EQUALS");
                optNode.AddTokenTransition(TokenType.EQUALS, optEqualsNode, "=");
                optEqualsNode.AddTransitionTo("ON", tree.Root, SuggestionType.Keyword);
                optEqualsNode.AddTransitionTo("OFF", tree.Root, SuggestionType.Keyword);
            }

            // Options that take arbitrary values/expressions (e.g. SET ALLOW_FILE_OPERATIONS = 100)
            var valueOptions = new[] {
                "ALLOW_FILE_OPERATIONS", "ALLOW_RECURSIVE_LAYERS", "ALLOW_FILE_TYPE_ACCESS", "BATCH_SIZE", "MAX_ERRORS",
                "JOIN_SPILL_THRESHOLD", "TEMP_TABLE_SPILL_THRESHOLD", "WINDOW_SPILL_THRESHOLD",
                "EXTERNAL_HASH_PARTITIONS", "EXTERNAL_SORT_CHUNK_SIZE", "BATCHSIZE",
                "MAX_RECURSIVE_DEPTH", "MAX_IN_MEMORY_BATCHES", "FOREACH_PAGE_SIZE",
                "MAX_MESSAGES", "MAX_FILE_OPERATIONS", "MAX_PARALLEL_DEGREE",
                "MAX_STRING_RESULT_SIZE", "REGEX_MATCH_TIMEOUT", "MAX_GROUPING_SETS",
                "SET_CUBE_LIMIT", "MAX_SESSION_SIZE", "MAX_LAST_RESULT_ROWS",
                "MAX_GENERATE_ROWS", "MAX_SMTP_EMAILS_PER_SCRIPT", "MAX_INTERNAL_OPERATIONS",
                "OPERATOR_MEMORY_GRANT", "CONNECTION_PREVIEW_LIMIT", "TEMPLATE_PATH",
                "LINEAGE_NAMESPACE", "LINEAGE_IMPORT_CATALOG", "WEEK_START_DAY", "SCRIPT_HASH_POLICY"
            };

            foreach (var opt in valueOptions)
            {
                var optNode = new StateNode("SET_" + opt);
                setNode.AddTransitionTo(opt, optNode, SuggestionType.Keyword);

                // Allow option name -> = -> value
                var optEqualsNode = new StateNode("SET_" + opt + "_EQUALS");
                optNode.AddTokenTransition(TokenType.EQUALS, optEqualsNode, "=");

                var optValNode = new StateNode("SET_" + opt + "_VAL");
                optEqualsNode.AddWildcardTransition(optValNode, "<value>");
                optValNode.AddTransition(new StateTransition(
                    t => t.Type != TokenType.SEMICOLON,
                    optValNode,
                    "<value>"
                ));

                // Also allow option name -> value directly
                optNode.AddWildcardTransition(optValNode, "<value>");
            }
        }

        // 4. THROW
        var throwNode = new StateNode("THROW");
        var throwExpr = new StateNode("THROW_EXPR");
        tree.RegisterStartNode("THROW", throwNode);
        throwNode.AddWildcardTransition(throwExpr, "<expression>");
        throwExpr.AddTransition(new StateTransition(
            t => t.Type != TokenType.SEMICOLON,
            throwExpr,
            "<expression_token>"
        ));

        // 5. DROP
        var dropNode = new StateNode("DROP");
        var dropExpr = new StateNode("DROP_EXPR");
        tree.RegisterStartNode("DROP", dropNode);
        dropNode.AddWildcardTransition(dropExpr, "<expression>");
        dropExpr.AddTransition(new StateTransition(
            t => t.Type != TokenType.SEMICOLON,
            dropExpr,
            "<expression_token>"
        ));

        // 6. Generic administrative/enterprise statements
        foreach (var cmd in new[] {
            "REBUILD", "PUBLISH", "REFRESH", "DISCONNECT", "REVOKE", "RESTART", "SHUTDOWN",
            "SHOW", "GRANT", "USE", "COMMIT", "ROLLBACK", "RETURN", "BREAK", "CONTINUE",
            "RAISERROR", "RAISEERROR", "ASSERT", "KILL"
        })
        {
            var node = new StateNode(cmd);
            var expr = new StateNode(cmd + "_EXPR");
            tree.RegisterStartNode(cmd, node);
            node.AddWildcardTransition(expr, "<expression>");
            expr.AddTransition(new StateTransition(
                t => t.Type != TokenType.SEMICOLON,
                expr,
                "<expression_token>"
            ));
        }

    }

    private static void ConfigureCreateAlterReplace(GrammarStateTree tree)
    {
        var createNode = tree.GetStartNode("CREATE");
        var alterStartNode = tree.GetStartNode("ALTER");

        var replaceNode = new StateNode("REPLACE");

        StateNode? alterNode = null;
        if (createNode != null)
        {
            var orNode = createNode.Transitions.FirstOrDefault(t => t.Label != null && t.Label.Equals("OR", StringComparison.OrdinalIgnoreCase))?.Target;
            if (orNode != null)
            {
                orNode.AddTransitionTo("REPLACE", replaceNode, SuggestionType.Keyword);
                alterNode = orNode.Transitions.FirstOrDefault(t => t.Label != null && t.Label.Equals("ALTER", StringComparison.OrdinalIgnoreCase))?.Target;
            }
        }

        var ddlExpr = new StateNode("DDL_EXPR");
        ddlExpr.AddTransition(new StateTransition(
            t => t.Type != TokenType.SEMICOLON,
            ddlExpr,
            "<expression_token>"
        ));

        var createKeywords = new[] {
            "TABLE", "VIEW", "VISUAL", "PAGE", "DATASET", "STYLE", "CONTAINER",
            "NAVIGATION", "JOB", "SCHEDULE", "NOTIFICATION", "DIRECTORY", "PROCEDURE", "FUNCTION",
            "INDEX", "TAG", "LINEAGE", "FOLDER", "USER", "GROUP", "REFRESH", "SUBSCRIPTION",
            "SHARE", "EMBED", "SAVED", "ALERT", "BUTTON", "TEMPLATE", "THEME", "SSH_KEYPAIR",
            "PGP_KEYPAIR", "SSH_KEY_PAIR", "PGP_KEY_PAIR", "UNIQUE"
        };

        var createOrAlterKeywords = new[] {
            "CONNECTION", "PROCEDURE", "FUNCTION", "VIEW", "JOB", "SCHEDULE", "NOTIFICATION",
            "VISUAL", "PAGE", "DATASET", "CONTAINER", "BUTTON", "STYLE", "NAVIGATION", "TEMPLATE",
            "THEME", "ALERT"
        };

        var createOrReplaceKeywords = new[] {
            "CONNECTION", "TABLE", "PROCEDURE", "FUNCTION", "VIEW", "JOB", "SCHEDULE", "NOTIFICATION",
            "VISUAL", "PAGE", "DATASET", "CONTAINER", "BUTTON", "STYLE", "NAVIGATION", "TEMPLATE",
            "THEME", "ALERT"
        };

        var alterKeywords = new[] {
            "CONNECTION", "TABLE", "PROCEDURE", "FUNCTION", "VIEW", "JOB", "SCHEDULE", "NOTIFICATION",
            "VISUAL", "PAGE", "CONTAINER", "BUTTON", "TEMPLATE", "USER", "FOLDER", "REPORT",
            "DATASET", "SUBSCRIPTION", "ALERT"
        };

        foreach (var keyword in createKeywords)
        {
            if (createNode != null)
            {
                createNode.AddTransitionTo(keyword, ddlExpr, SuggestionType.Keyword);
            }
        }

        foreach (var keyword in alterKeywords)
        {
            if (alterStartNode != null)
            {
                alterStartNode.AddTransitionTo(keyword, ddlExpr, SuggestionType.Keyword);
            }
        }

        foreach (var keyword in createOrAlterKeywords)
        {
            if (alterNode != null)
            {
                alterNode.AddTransitionTo(keyword, ddlExpr, SuggestionType.Keyword);
            }
        }

        foreach (var keyword in createOrReplaceKeywords)
        {
            replaceNode.AddTransitionTo(keyword, ddlExpr, SuggestionType.Keyword);
        }

    }

    private static void ConfigureExecute(GrammarStateTree tree)
    {
        var executeNode = new StateNode("EXECUTE");
        var execNode = new StateNode("EXEC");

        tree.RegisterStartNode("EXECUTE", executeNode);
        tree.RegisterStartNode("EXEC", execNode);

        var execTarget = new StateNode("EXEC_TARGET");
        var execExprInParen = new StateNode("EXEC_EXPR_IN_PAREN");
        var execAfterTarget = new StateNode("EXEC_AFTER_TARGET");

        // Transition from EXECUTE/EXEC to target
        executeNode.AddTokenTransition(TokenType.LPAREN, execExprInParen, "(");
        executeNode.AddTransition(new StateTransition(
            t => t.Type == TokenType.IDENTIFIER || t.Type == TokenType.VARIABLE || IsWord(t.Value),
            execAfterTarget,
            "<procedure_or_connection>"
        ));

        execNode.AddTokenTransition(TokenType.LPAREN, execExprInParen, "(");
        execNode.AddTransition(new StateTransition(
            t => t.Type == TokenType.IDENTIFIER || t.Type == TokenType.VARIABLE || IsWord(t.Value),
            execAfterTarget,
            "<procedure_or_connection>"
        ));

        // Inside LPAREN: consume any token until RPAREN
        execExprInParen.AddTransition(new StateTransition(
            t => t.Type != TokenType.RPAREN && t.Type != TokenType.SEMICOLON,
            execExprInParen,
            "<expression_token>"
        ));
        execExprInParen.AddTokenTransition(TokenType.RPAREN, execAfterTarget, ")");

        // Stored procedure parameters support
        var execParamsNode = new StateNode("EXEC_PARAMS");
        execAfterTarget.AddTransition(new StateTransition(
            t => !t.Value.Equals("INTO", StringComparison.OrdinalIgnoreCase) &&
                 !t.Value.Equals("WITH", StringComparison.OrdinalIgnoreCase) &&
                 !t.Value.Equals("AT", StringComparison.OrdinalIgnoreCase) &&
                 !t.Value.Equals("BEGIN", StringComparison.OrdinalIgnoreCase) &&
                 t.Type != TokenType.SEMICOLON &&
                 tree.GetStartNode(t.Value) == null,
            execParamsNode,
            "<parameter_token>"
        ));

        execParamsNode.AddTransition(new StateTransition(
            t => !t.Value.Equals("INTO", StringComparison.OrdinalIgnoreCase) &&
                 !t.Value.Equals("WITH", StringComparison.OrdinalIgnoreCase) &&
                 !t.Value.Equals("AT", StringComparison.OrdinalIgnoreCase) &&
                 !t.Value.Equals("BEGIN", StringComparison.OrdinalIgnoreCase) &&
                 t.Type != TokenType.SEMICOLON &&
                 tree.GetStartNode(t.Value) == null,
            execParamsNode,
            "<parameter_token>"
        ));

        execParamsNode.AddTransition(new StateTransition(
            t => tree.GetStartNode(t.Value) != null,
            tree.Root,
            "<next_statement>"
        ));

        // Optional INTO #tempTable
        var execInto = new StateNode("EXEC_INTO");
        var execIntoTable = new StateNode("EXEC_INTO_TABLE");
        execAfterTarget.AddTransitionTo("INTO", execInto, SuggestionType.Keyword);
        execInto.AddTransition(new StateTransition(
            t => t.Type == TokenType.IDENTIFIER || t.Type == TokenType.VARIABLE || t.Value.StartsWith("#") || IsWord(t.Value),
            execIntoTable,
            "<temp_table>"
        ));

        // Optional WITH (params)
        var execWith = new StateNode("EXEC_WITH");
        var execWithParen = new StateNode("EXEC_WITH_PAREN");
        var execWithParams = new StateNode("EXEC_WITH_PARAMS");
        var execWithEnd = new StateNode("EXEC_WITH_END");

        void AddWithTransitions(StateNode fromNode)
        {
            fromNode.AddTransitionTo("WITH", execWith, SuggestionType.Keyword);
        }
        AddWithTransitions(execAfterTarget);
        AddWithTransitions(execIntoTable);

        execWith.AddTokenTransition(TokenType.LPAREN, execWithParen, "(");
        // Also allow unparenthesized single parameter or list of parameters
        execWith.AddWildcardTransition(execWithParams, "<parameters>");

        execWithParen.AddTransition(new StateTransition(
            t => t.Type != TokenType.RPAREN && t.Type != TokenType.SEMICOLON,
            execWithParen,
            "<parameter_token>"
        ));
        execWithParen.AddTokenTransition(TokenType.RPAREN, execWithEnd, ")");

        execWithParams.AddTransition(new StateTransition(
            t => t.Type != TokenType.SEMICOLON && !t.Value.Equals("AT", StringComparison.OrdinalIgnoreCase) && !t.Value.Equals("BEGIN", StringComparison.OrdinalIgnoreCase),
            execWithParams,
            "<parameter_token>"
        ));

        // Optional AT connection
        var execAt = new StateNode("EXEC_AT");
        var execAtConn = new StateNode("EXEC_AT_CONN");

        void AddAtTransitions(StateNode fromNode)
        {
            fromNode.AddTransitionTo("AT", execAt, SuggestionType.Keyword);
        }
        AddAtTransitions(execAfterTarget);
        AddAtTransitions(execIntoTable);
        AddAtTransitions(execWithEnd);
        AddAtTransitions(execWithParams);

        execAt.AddTransition(new StateTransition(
            t => t.Type == TokenType.IDENTIFIER || t.Type == TokenType.VARIABLE || IsWord(t.Value),
            execAtConn,
            "<connection_name>"
        ));

        // BEGIN pushdown block
        var execPushdownContent = new StateNode("EXEC_PUSHDOWN_CONTENT");

        void AddBeginTransitions(StateNode fromNode)
        {
            fromNode.AddTransitionTo("BEGIN", execPushdownContent, SuggestionType.Keyword, null,
                (token, walker) => walker.StateBag["ExecBlockDepth"] = 1);
        }
        AddBeginTransitions(execAfterTarget);
        AddBeginTransitions(execIntoTable);
        AddBeginTransitions(execWithEnd);
        AddBeginTransitions(execWithParams);
        AddBeginTransitions(execAtConn);

        // Pushdown content transitions
        static int GetDepth(TokenWalker walker) => walker.StateBag.TryGetValue("ExecBlockDepth", out var d) ? (int)d : 0;

        execPushdownContent.AddTransition(new StateTransition(
            t => t.Value.Equals("BEGIN", StringComparison.OrdinalIgnoreCase),
            execPushdownContent,
            "BEGIN",
            null, null,
            (token, walker) =>
            {
                int d = GetDepth(walker);
                walker.StateBag["ExecBlockDepth"] = d + 1;
            }
        ));

        execPushdownContent.AddTransition(new StateTransition(
            t => t.Value.Equals("END", StringComparison.OrdinalIgnoreCase),
            execPushdownContent,
            "END",
            null, null,
            (token, walker) =>
            {
                int d = GetDepth(walker);
                walker.StateBag["ExecBlockDepth"] = Math.Max(1, d - 1);
            },
            contextCondition: (_, walker) => GetDepth(walker) > 1
        ));

        execPushdownContent.AddTransition(new StateTransition(
            t => t.Value.Equals("END", StringComparison.OrdinalIgnoreCase),
            tree.Root,
            "END",
            null, null,
            (token, walker) => walker.StateBag.Remove("ExecBlockDepth"),
            contextCondition: (_, walker) => GetDepth(walker) <= 1
        ));

        execPushdownContent.AddTransition(new StateTransition(
            t => !t.Value.Equals("BEGIN", StringComparison.OrdinalIgnoreCase) && !t.Value.Equals("END", StringComparison.OrdinalIgnoreCase),
            execPushdownContent,
            "<pushdown_token>"
        ));

        // Also allow EXEC/EXECUTE statements to end at target or temp table, or with/at if no BEGIN block
        void AddStartKeywordTransitions(StateNode node)
        {
            node.AddTransition(new StateTransition(
                t => tree.GetStartNode(t.Value) != null,
                tree.Root,
                "<next_statement>"
            ));
        }
        AddStartKeywordTransitions(execAfterTarget);
        AddStartKeywordTransitions(execIntoTable);
        AddStartKeywordTransitions(execWithEnd);
        AddStartKeywordTransitions(execWithParams);
        AddStartKeywordTransitions(execAtConn);

        // Support flexible order for INTO, WITH, and AT in EXEC/EXECUTE
        execAtConn.AddTransitionTo("INTO", execInto, SuggestionType.Keyword);
        execWithEnd.AddTransitionTo("INTO", execInto, SuggestionType.Keyword);
        execWithParams.AddTransitionTo("INTO", execInto, SuggestionType.Keyword);
        execAtConn.AddTransitionTo("WITH", execWith, SuggestionType.Keyword);
    }

    private static void ConfigureParallel(GrammarStateTree tree)
    {
        // 6. PARALLEL block and PARALLEL FOR loop
        var parallelNode = new StateNode("PARALLEL");
        var parallelAfterLimit = new StateNode("PARALLEL_LIMIT");
        var parallelFor = new StateNode("PARALLEL_FOR");

        tree.RegisterStartNode("PARALLEL", parallelNode);

        // Optional concurrency limit (integer or variable, optionally parenthesized)
        parallelNode.AddTokenTransition(TokenType.NUMBER, parallelAfterLimit, "<concurrency_limit>");
        parallelNode.AddTokenTransition(TokenType.VARIABLE, parallelAfterLimit, "<concurrency_limit_variable>");

        var parallelParenLimit = new StateNode("PARALLEL_PAREN_LIMIT");
        var parallelParenLimitVal = new StateNode("PARALLEL_PAREN_LIMIT_VAL");
        parallelNode.AddTokenTransition(TokenType.LPAREN, parallelParenLimit, "(");
        parallelParenLimit.AddTokenTransition(TokenType.NUMBER, parallelParenLimitVal, "<concurrency_limit>");
        parallelParenLimit.AddTokenTransition(TokenType.VARIABLE, parallelParenLimitVal, "<concurrency_limit_variable>");
        parallelParenLimitVal.AddTokenTransition(TokenType.RPAREN, parallelAfterLimit, ")");

        // Transitions to BEGIN or FOR
        parallelNode.AddTransitionTo("BEGIN", tree.Root, SuggestionType.Keyword);
        parallelNode.AddTransitionTo("FOR", parallelFor, SuggestionType.Keyword);

        parallelAfterLimit.AddTransitionTo("BEGIN", tree.Root, SuggestionType.Keyword);
        parallelAfterLimit.AddTransitionTo("FOR", parallelFor, SuggestionType.Keyword);

        // FOR path: FOR @var = start TO end [STEP n] BEGIN
        var parallelForVar = new StateNode("PARALLEL_FOR_VAR");
        var parallelForEquals = new StateNode("PARALLEL_FOR_EQUALS");
        var parallelForStart = new StateNode("PARALLEL_FOR_START");
        var parallelForTo = new StateNode("PARALLEL_FOR_TO");
        var parallelForEnd = new StateNode("PARALLEL_FOR_END");

        parallelFor.AddTokenTransition(TokenType.VARIABLE, parallelForVar, "<loop_variable>");
        parallelForVar.AddTokenTransition(TokenType.EQUALS, parallelForEquals, "=");
        parallelForEquals.AddWildcardTransition(parallelForStart, "<start_expression>");

        parallelForStart.AddTransition(new StateTransition(
            t => !t.Value.Equals("TO", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            parallelForStart,
            "<expression_token>"
        ));
        parallelForStart.AddTransitionTo("TO", parallelForTo, SuggestionType.Keyword);

        parallelForTo.AddWildcardTransition(parallelForEnd, "<end_expression>");
        parallelForEnd.AddTransition(new StateTransition(
            t => !t.Value.Equals("STEP", StringComparison.OrdinalIgnoreCase) && !t.Value.Equals("BEGIN", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            parallelForEnd,
            "<expression_token>"
        ));

        // Optional STEP
        var parallelForStep = new StateNode("PARALLEL_FOR_STEP");
        var parallelForStepVal = new StateNode("PARALLEL_FOR_STEP_VAL");
        parallelForEnd.AddTransitionTo("STEP", parallelForStep, SuggestionType.Keyword);
        parallelForStep.AddWildcardTransition(parallelForStepVal, "<step_value>");
        parallelForStepVal.AddTransition(new StateTransition(
            t => !t.Value.Equals("BEGIN", StringComparison.OrdinalIgnoreCase) && t.Type != TokenType.SEMICOLON,
            parallelForStepVal,
            "<expression_token>"
        ));

        // Transition to BEGIN
        parallelForEnd.AddTransitionTo("BEGIN", tree.Root, SuggestionType.Keyword);
        parallelForStepVal.AddTransitionTo("BEGIN", tree.Root, SuggestionType.Keyword);
    }
}


