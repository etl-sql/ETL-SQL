using System;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Core;

public sealed class ColumnarPredicateCompilerTests
{
    [Fact]
    public void BindsQualifiedFixedWidthComparisonAndReversedLiteral()
    {
        using var batch = CreateBatch();
        var predicate = new BinaryExpression(
            new IdentifierExpression("t.Id"),
            TokenType.GREATER_THAN,
            new LiteralExpression(1m, TokenType.NUMBER));

        Assert.True(ColumnarPredicateCompiler.TrySelect(batch, predicate, out var selected));
        using (selected) Assert.Equal(new[] { 2 }, selected!.Indices.ToArray());

        var reversed = new BinaryExpression(
            new LiteralExpression(1m, TokenType.NUMBER),
            TokenType.LESS_THAN,
            new IdentifierExpression("Id"));
        Assert.True(ColumnarPredicateCompiler.TrySelect(batch, reversed, out selected));
        using (selected) Assert.Equal(new[] { 2 }, selected!.Indices.ToArray());
    }

    [Fact]
    public void BindsArithmeticAndBooleanConjunctionCompositionally()
    {
        using var batch = CreateBatch();
        var arithmetic = new BinaryExpression(
            new BinaryExpression(
                new IdentifierExpression("Id"),
                TokenType.STAR,
                new LiteralExpression(2m, TokenType.NUMBER)),
            TokenType.GREATER_THAN,
            new LiteralExpression(2m, TokenType.NUMBER));
        var active = new BinaryExpression(
            new IdentifierExpression("Active"),
            TokenType.EQUALS,
            new LiteralExpression(true, TokenType.TRUE));
        var predicate = new BinaryExpression(arithmetic, TokenType.AND, active);

        Assert.True(ColumnarPredicateCompiler.TrySelect(batch, predicate, out var selected));
        using (selected) Assert.Equal(new[] { 2 }, selected!.Indices.ToArray());
    }

    [Fact]
    public void UnsupportedExpressionsReturnFallbackWithoutAllocatingAResult()
    {
        using var batch = CreateBatch();
        var stringPredicate = new BinaryExpression(
            new IdentifierExpression("Name"),
            TokenType.EQUALS,
            new LiteralExpression("one", TokenType.STRING_LITERAL));
        Assert.False(ColumnarPredicateCompiler.TrySelect(batch, stringPredicate, out var selected));
        Assert.Null(selected);

        var missing = new BinaryExpression(
            new IdentifierExpression("Missing"),
            TokenType.EQUALS,
            new LiteralExpression(1, TokenType.NUMBER));
        Assert.False(ColumnarPredicateCompiler.TrySelect(batch, missing, out selected));
        Assert.Null(selected);
    }

    [Fact]
    public void BindsNullPredicates()
    {
        using var batch = CreateBatch();
        Assert.True(ColumnarPredicateCompiler.TrySelect(
            batch, new IsNullExpression(new IdentifierExpression("Id"), isNot: false), out var selected));
        using (selected) Assert.Equal(new[] { 1 }, selected!.Indices.ToArray());

        Assert.True(ColumnarPredicateCompiler.TrySelect(
            batch, new IsNullExpression(new IdentifierExpression("Id"), isNot: true), out selected));
        using (selected) Assert.Equal(new[] { 0, 2, 3 }, selected!.Indices.ToArray());
    }

    [Fact]
    public void BindsOrWithoutDuplicateRowsAndPreservesOrdinalOrder()
    {
        using var batch = CreateBatch();
        var one = new BinaryExpression(
            new IdentifierExpression("Id"), TokenType.EQUALS, new LiteralExpression(1, TokenType.NUMBER));
        var three = new BinaryExpression(
            new IdentifierExpression("Id"), TokenType.EQUALS, new LiteralExpression(3, TokenType.NUMBER));

        Assert.True(ColumnarPredicateCompiler.TrySelect(
            batch, new BinaryExpression(one, TokenType.OR, three), out var selected));
        using (selected) Assert.Equal(new[] { 0, 2, 3 }, selected!.Indices.ToArray());
    }

    private static ColumnBatch CreateBatch()
    {
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Id", typeof(int), "INT"),
            new ColumnBatchField("Name", typeof(string), "VARCHAR(20)"),
            new ColumnBatchField("Active", typeof(byte), "BOOLEAN")
        });
        return new ColumnBatch(schema, new IColumnBuffer[]
        {
            new ColumnBuffer<int>(new[] { 1, 0, 3, 1 }, 4, new byte[] { 0b0000_0010 }),
            Utf8ColumnBuffer.FromStrings(new string?[] { "one", "null-id", "three", "one-again" }),
            new ColumnBuffer<byte>(new byte[] { 1, 1, 1, 0 }, 4)
        }, 4);
    }
}
