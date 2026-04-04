namespace ETL_SQL.Core.Parser
{
    public record Token(
        TokenType Type, 
        string Value, 
        int Line, 
        int Column, 
        int EndLine, 
        int EndColumn,
        int Offset = 0,
        int EndOffset = 0
    )
    {
        public override string ToString() => 
            $"{Type}('{Value}') at Line {Line}, Col {Column} - {EndLine}, {EndColumn}";
    }
}
