namespace ETL_SQL.Data
{
    public class VariableMetadata
    {
        public bool IsSensitive { get; set; }
        public bool IsSecret { get; set; }
        public bool IsInput { get; set; }
        public bool IsOutput { get; set; }
        /// <summary>True if the variable has been explicitly declared in the current script (not just injected as a parameter).</summary>
        public bool IsDeclared { get; set; }
        public string? DataType { get; set; }
    }
}
