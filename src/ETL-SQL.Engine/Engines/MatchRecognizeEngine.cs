using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Engines
{
    public sealed class MatchRecognizeEngine
    {
        private readonly IExecutionContext _context;

        public MatchRecognizeEngine(IExecutionContext context)
        {
            _context = context;
        }

        public async Task<List<Row>> Apply(List<Row> rows, MatchRecognizeClause clause)
        {
            if (rows.Count == 0 || string.IsNullOrWhiteSpace(clause.Pattern)) return new List<Row>();

            var pattern = ParsePattern(clause.Pattern);
            var partitions = await PartitionRows(rows, clause.PartitionBy);
            var output = new List<Row>();
            int matchNumber = 1;

            foreach (var partition in partitions)
            {
                var ordered = await OrderRows(partition, clause.OrderBy);
                for (int i = 0; i < ordered.Count;)
                {
                    var match = await TryMatch(ordered, i, pattern, clause.Definitions);
                    if (match == null)
                    {
                        i++;
                        continue;
                    }

                    if (clause.AllRowsPerMatch)
                    {
                        foreach (var rowIndex in match.Captures.Values.SelectMany(v => v).Distinct().OrderBy(v => v))
                        {
                            output.Add(await BuildOutputRow(clause, ordered, match, matchNumber, rowIndex));
                        }
                    }
                    else
                    {
                        output.Add(await BuildOutputRow(clause, ordered, match, matchNumber, null));
                    }

                    matchNumber++;
                    i = Math.Max(i + 1, match.EndExclusive);
                }
            }

            return output;
        }

        private async Task<List<List<Row>>> PartitionRows(List<Row> rows, List<Expression> partitionBy)
        {
            if (partitionBy.Count == 0) return new List<List<Row>> { rows };

            var groups = new Dictionary<string, List<Row>>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var keyParts = new List<string>();
                foreach (var expr in partitionBy)
                {
                    keyParts.Add((await _context.EvaluateValue(expr, row))?.ToString() ?? "<NULL>");
                }
                var key = string.Join('\u001f', keyParts);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<Row>();
                    groups[key] = list;
                }
                list.Add(row);
            }
            return groups.Values.ToList();
        }

        private async Task<List<Row>> OrderRows(List<Row> rows, List<OrderByClause> orderBy)
        {
            if (orderBy.Count == 0) return rows;

            var keyed = new List<(Row Row, List<object?> Keys)>();
            foreach (var row in rows)
            {
                var keys = new List<object?>();
                foreach (var order in orderBy)
                {
                    keys.Add(await _context.EvaluateValue(order.Expression, row));
                }
                keyed.Add((row, keys));
            }

            keyed.Sort((a, b) =>
            {
                for (int i = 0; i < orderBy.Count; i++)
                {
                    int cmp = _context.CompareConstants(a.Keys[i], b.Keys[i]);
                    if (cmp != 0) return orderBy[i].Descending ? -cmp : cmp;
                }
                return 0;
            });
            return keyed.Select(k => k.Row).ToList();
        }

        private async Task<PatternMatch?> TryMatch(List<Row> rows, int start, List<PatternPart> pattern, Dictionary<string, Expression> definitions)
        {
            var captures = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            int index = start;

            foreach (var part in pattern)
            {
                int count = 0;
                while (index < rows.Count && await IsDefined(rows, index, part.Variable, definitions))
                {
                    if (!captures.TryGetValue(part.Variable, out var list))
                    {
                        list = new List<int>();
                        captures[part.Variable] = list;
                    }
                    list.Add(index);
                    index++;
                    count++;
                    if (part.Quantifier is '\0' or '?') break;
                }

                if ((part.Quantifier == '\0' || part.Quantifier == '+') && count == 0) return null;
            }

            return index > start ? new PatternMatch(captures, index) : null;
        }

        private async Task<bool> IsDefined(List<Row> rows, int index, string variable, Dictionary<string, Expression> definitions)
        {
            if (!definitions.TryGetValue(variable, out var definition)) return true;
            var mrRow = new MatchRecognizeRow(rows, index, variable);
            var value = await _context.EvaluateValue(definition, mrRow);
            return value switch
            {
                null => false,
                bool b => b,
                decimal d => d != 0m,
                int i => i != 0,
                string s => bool.TryParse(s, out var b) ? b : !string.IsNullOrEmpty(s),
                _ => Convert.ToBoolean(value)
            };
        }

        private async Task<Row> BuildOutputRow(MatchRecognizeClause clause, List<Row> rows, PatternMatch match, int matchNumber, int? currentRowIndex)
        {
            var output = new Row();

            if (currentRowIndex != null)
            {
                output["MATCH_NUMBER"] = (decimal)matchNumber;

                // ALL ROWS PER MATCH: copy source row columns and emit CLASSIFIER
                if (rows.Count > currentRowIndex.Value)
                {
                    foreach (var (col, val) in rows[currentRowIndex.Value].Columns)
                        output[col] = val;
                }

                string? classifier = null;
                foreach (var (variable, indices) in match.Captures)
                {
                    if (indices.Contains(currentRowIndex.Value)) { classifier = variable; break; }
                }
                output["CLASSIFIER"] = classifier;
            }

            if (rows.Count > 0)
            {
                foreach (var expr in clause.PartitionBy)
                {
                    string name = expr.ToSql();
                    output[name] = await _context.EvaluateValue(expr, rows[0]);
                }
            }

            foreach (var measure in clause.Measures)
            {
                string name = measure.Alias ?? measure.Expression.ToSql();
                output[name] = await EvaluateMeasure(measure.Expression, rows, match, currentRowIndex);
            }
            return output;
        }

        private async Task<object?> EvaluateMeasure(Expression expression, List<Row> rows, PatternMatch match, int? currentRowIndex)
        {
            if (expression is FunctionCallExpression f &&
                f.Arguments.Count == 1 &&
                (f.FunctionName.Equals("FIRST", StringComparison.OrdinalIgnoreCase) || f.FunctionName.Equals("LAST", StringComparison.OrdinalIgnoreCase)))
            {
                return EvaluateFirstLast(f, rows, match);
            }

            if (currentRowIndex != null)
            {
                return await _context.EvaluateValue(expression, rows[currentRowIndex.Value]);
            }

            var context = BuildMatchContext(rows, match);
            return await _context.EvaluateValue(expression, context);
        }

        private static object? EvaluateFirstLast(FunctionCallExpression f, List<Row> rows, PatternMatch match)
        {
            if (f.Arguments[0] is not IdentifierExpression id) return null;
            var (variable, column) = SplitPatternColumn(id.Name);
            if (!match.Captures.TryGetValue(variable, out var indices) || indices.Count == 0) return null;
            int rowIndex = f.FunctionName.Equals("FIRST", StringComparison.OrdinalIgnoreCase) ? indices[0] : indices[^1];
            return FindValue(rows[rowIndex], column);
        }

        private static Row BuildMatchContext(List<Row> rows, PatternMatch match)
        {
            var context = new Row();
            foreach (var (variable, indices) in match.Captures)
            {
                if (indices.Count == 0) continue;
                var row = rows[indices[0]];
                foreach (var (column, value) in row.Columns)
                {
                    context[$"{variable}.{column.Split('.').Last()}"] = value;
                }
            }
            return context;
        }

        private static Row WithVariablePrefix(Row source, string variable)
        {
            var row = source.Clone();
            foreach (var (column, value) in source.Columns)
            {
                row[$"{variable}.{column.Split('.').Last()}"] = value;
            }
            return row;
        }

        private static List<PatternPart> ParsePattern(string pattern)
        {
            var raw = pattern.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<PatternPart>();
            foreach (var token in raw)
            {
                if ((token == "+" || token == "*" || token == "?") && result.Count > 0)
                {
                    var last = result[^1];
                    result[^1] = last with { Quantifier = token[0] };
                    continue;
                }

                char quantifier = token[^1] is '+' or '*' or '?' ? token[^1] : '\0';
                string variable = quantifier == '\0' ? token : token[..^1];
                if (!string.IsNullOrWhiteSpace(variable)) result.Add(new PatternPart(variable, quantifier));
            }
            return result;
        }

        private static (string Variable, string Column) SplitPatternColumn(string name)
        {
            var dot = name.IndexOf('.');
            if (dot < 0) return ("", name);
            return (name[..dot], name[(dot + 1)..]);
        }

        private static object? FindValue(Row row, string column)
        {
            if (row.Columns.TryGetValue(column, out var value)) return value;
            var match = row.Columns.Keys.FirstOrDefault(k => k.EndsWith("." + column, StringComparison.OrdinalIgnoreCase));
            return match != null ? row[match] : null;
        }

        private sealed record PatternMatch(Dictionary<string, List<int>> Captures, int EndExclusive);
        private sealed record PatternPart(string Variable, char Quantifier);
    }
}

namespace ETL_SQL.Engine
{
    public class MatchRecognizeRow : Row
    {
        public List<Row> Rows { get; }
        public int Index { get; }
        public string Variable { get; }

        public MatchRecognizeRow(List<Row> rows, int index, string variable)
        {
            Rows = rows;
            Index = index;
            Variable = variable;

            var source = rows[index];
            foreach (var (column, value) in source.Columns)
            {
                this[$"{variable}.{column.Split('.').Last()}"] = value;
                this[column] = value;
            }
        }
    }
}
