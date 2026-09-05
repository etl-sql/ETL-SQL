using System.Reflection;
using System.Text;
using ETL_SQL.Analysis.Services;
using ETL_SQL.Portal.Models;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Emits the TypeScript declarations for the C# types the browser code talks to.
///
/// <para>Most of the defects that actually bite cross this boundary rather than living inside the
/// JavaScript: a route name, a DTO field name, a kind id. <c>endLine</c> was threaded through two
/// hosts and a mock by hand, and nothing would have caught a miss. Generating the declarations from
/// the types themselves means the browser sources are checked against what the server really sends,
/// and means the check is not another regex pulling literals out of a .js file.</para>
///
/// <para>Reflection, not parsing. The shapes come from the compiled records, so a renamed property
/// or a new enum member arrives here whether or not anyone remembered this file existed.</para>
/// </summary>
internal static class BrowserContractsGenerator
{
    /// <summary>
    /// The enums whose values cross the wire as strings.
    ///
    /// <para>Both are matched case-insensitively on the way in (<see cref="PipelineTaskKinds.Parse"/>),
    /// but the canvas writes and the palette declares the lowercase form, so that is the form
    /// declared here. A union of the other casings would let a chip id that no palette uses pass.</para>
    /// </summary>
    private static readonly Type[] StringEnums = [typeof(PipelineTaskKind), typeof(PipelineEdgeCondition)];

    /// <summary>
    /// The records the browser receives or sends. Nested record types they reference are pulled in
    /// automatically, so this list only has to name the entry points.
    /// </summary>
    private static readonly Type[] Records =
    [
        typeof(PipelineTaskDto),
        typeof(PipelineTaskResponse),
        typeof(PipelineDependencyDto),
        typeof(PipelineScopeRequest),
        typeof(PipelineRunPlanRequest),
        typeof(DataModelRequest),
        typeof(DataModelResponse),
        typeof(DataModelEntityDto),
        typeof(DataModelColumnDto),
        typeof(DataModelRelationshipDto),
        typeof(ScriptDagNodeDto),
        typeof(ScriptDagEdgeDto),
        typeof(ScriptDagDto),
        typeof(ScriptDagProjection),
    ];

    /// <summary>
    /// Properties the C# declares as <c>string</c> whose vocabulary is one of the enums above.
    ///
    /// <para>These cross the wire as strings because the DTO is the browser's shape and an unknown
    /// name has to be reportable rather than a deserialisation failure — but the set of legal names
    /// is the enum's, and both records say so in their own doc comments. Declaring them as the union
    /// is what lets the browser sources be checked against the vocabulary instead of against
    /// "any string at all", which is the whole reason this generator exists.</para>
    ///
    /// <para>Keyed by declaring type and property so a same-named property elsewhere is untouched.</para>
    /// </summary>
    private static readonly Dictionary<(Type Declaring, string Property), string> VocabularyOverrides = new()
    {
        [(typeof(PipelineTaskDto), nameof(PipelineTaskDto.Kind))] = nameof(PipelineTaskKind),
        [(typeof(PipelineDependencyDto), nameof(PipelineDependencyDto.Condition))] = nameof(PipelineEdgeCondition),
    };

    public static string Generate()
    {
        var builder = new StringBuilder();
        builder.Append("""
            /**
             * GENERATED FILE - DO NOT EDIT.
             *
             * The C# types the browser code talks to, as TypeScript declarations. Regenerate with:
             *
             *   ETLSQL_UPDATE_BROWSER_CONTRACTS=1 dotnet test tests/ETL-SQL.Portal.Tests \
             *     --filter FullyQualifiedName~BrowserContractsGeneratorTests
             *
             * Source of truth: the records and enums themselves, read by reflection in
             * tests/ETL-SQL.Portal.Tests/BrowserContractsGenerator.cs. Editing this file by hand
             * makes the browser sources check against a shape the server does not send.
             *
             * Field names are camelCase because the hosts serialise with JsonSerializerDefaults.Web.
             */


            """.ReplaceLineEndings("\n"));

        foreach (var type in StringEnums)
        {
            var members = Enum.GetNames(type).Select(name => $"'{name.ToLowerInvariant()}'");
            builder.Append($"/** {type.Name}, as it crosses the wire. Matched case-insensitively on the way in. */\n");
            builder.Append($"type {type.Name} =\n    | {string.Join("\n    | ", members)};\n\n");
        }

        var seen = new HashSet<Type>();
        var queue = new Queue<Type>(Records);
        var emitted = new SortedDictionary<string, string>(StringComparer.Ordinal);

        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            if (!seen.Add(type)) continue;

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead)
                // EqualityContract is a record implementation detail and never serialised.
                .Where(p => p.Name != "EqualityContract")
                .ToList();

            var body = new StringBuilder();
            body.Append($"interface {type.Name} {{\n");
            foreach (var property in properties)
            {
                var (declared, optional) = TypeScriptFor(property, queue);
                if (VocabularyOverrides.TryGetValue((type, property.Name), out var vocabulary))
                    declared = vocabulary;
                body.Append($"    {Camel(property.Name)}{(optional ? "?" : "")}: {declared};\n");
            }
            body.Append("}\n");
            emitted[type.Name] = body.ToString();
        }

        builder.Append(string.Join("\n", emitted.Values));
        return builder.ToString();
    }

    /// <summary>The TypeScript for one property, queueing any record type it reaches.</summary>
    private static (string Declared, bool Optional) TypeScriptFor(PropertyInfo property, Queue<Type> queue)
    {
        var type = property.PropertyType;

        // A nullable value type is optional and its underlying type is what is declared.
        var underlying = Nullable.GetUnderlyingType(type);
        var optional = underlying is not null || IsNullableReference(property);
        type = underlying ?? type;

        return (Declare(type, queue), optional);
    }

    private static string Declare(Type type, Queue<Type> queue)
    {
        if (StringEnums.Contains(type)) return type.Name;
        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(int) || type == typeof(long) || type == typeof(double)
            || type == typeof(decimal) || type == typeof(float)) return "number";

        // `object?` on a DTO is a payload the browser is expected to inspect but the server does not
        // constrain — `unknown` says exactly that, and forces a check at the point of use.
        if (type == typeof(object)) return "unknown";

        if (type.IsEnum) return string.Join(" | ", Enum.GetNames(type).Select(n => $"'{n}'"));

        var element = ElementTypeOf(type);
        if (element is not null) return $"{Declare(element, queue)}[]";

        if (IsRecord(type))
        {
            queue.Enqueue(type);
            return type.Name;
        }

        return "unknown";
    }

    /// <summary>The item type of a list-shaped property, or null when the type is not one.</summary>
    private static Type? ElementTypeOf(Type type)
    {
        if (type.IsArray) return type.GetElementType();
        if (!type.IsGenericType) return null;
        var definition = type.GetGenericTypeDefinition();
        if (definition == typeof(List<>) || definition == typeof(IReadOnlyList<>)
            || definition == typeof(IList<>) || definition == typeof(IEnumerable<>)
            || definition == typeof(ICollection<>) || definition == typeof(IReadOnlyCollection<>))
        {
            return type.GetGenericArguments()[0];
        }
        return null;
    }

    private static bool IsRecord(Type type) =>
        type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null;

    /// <summary>
    /// Whether a reference-typed property is declared nullable.
    /// </summary>
    /// <remarks>
    /// Read from the compiler's nullable metadata rather than guessed, so `string?` becomes an
    /// optional field and `string` does not. Getting this backwards is the whole point of the
    /// exercise: an optional field the browser treats as always-present is exactly the defect class
    /// this file is meant to close.
    /// </remarks>
    private static bool IsNullableReference(PropertyInfo property)
    {
        if (property.PropertyType.IsValueType) return false;
        var context = new NullabilityInfoContext();
        return context.Create(property).ReadState == NullabilityState.Nullable;
    }

    private static string Camel(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
