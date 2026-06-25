using System;
using System.Collections.Generic;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine.Services;

namespace ETL_SQL.Engine;
public partial class Evaluator
{
    public IVariableContext VarContext => _variableScopeManager;

    public object? GetVariable(string name)
    {
        if (SystemVariableProvider.IsSystemVariable(name))
            return SystemVariableProvider.Resolve(name, this);
        return VarContext.GetVariable(name);
    }

    public void SetVariable(string name, object? value) => VarContext.SetVariable(name, value);

    public void DeclareVariable(string name, object? value, VariableMetadata? metadata = null)
        => VarContext.DeclareVariable(name, value, metadata);

    public bool ContainsVariable(string name) => VarContext.ContainsVariable(name);

    public void PushScope(Dictionary<string, object?> vars, Dictionary<string, VariableMetadata>? metadata = null)
        => VarContext.PushScope(vars, metadata);

    public void PopScope() => VarContext.PopScope();


    public IDictionary<string, (object? Value, VariableMetadata Metadata)> GetVariablesWithMetadata(Func<VariableMetadata, bool>? predicate = null)
        => VarContext.GetVariablesWithMetadata(predicate);

    public bool ContainsVariableInCurrentScope(string name) => VarContext.ContainsVariableInCurrentScope(name);

    public IDictionary<string, object?> Variables => VarContext.Variables;
    public IDictionary<string, object?> CurrentVariables => VarContext.CurrentVariables;
    public IDictionary<string, VariableMetadata> VariableMetadata => VarContext.VariableMetadata;
    public IDictionary<string, VariableMetadata> CurrentMetadata => VarContext.CurrentMetadata;

    public void EvaluateCreateProcedure(CreateProcedureStatement stmt) => _variableScopeManager.SetProcedure(stmt.ProcedureName, stmt);
    public void EvaluateCreateFunction(CreateFunctionStatement stmt) => _variableScopeManager.SetFunction(stmt.FunctionName, stmt);
    public bool ProcedureExists(string name) => _variableScopeManager.TryGetProcedure(name, out _);
    public bool FunctionExists(string name) => _variableScopeManager.TryGetFunction(name, out _);

    public void Reset() => VarContext.Reset();
    public void SetProcedure(string name, CreateProcedureStatement stmt) => VarContext.SetProcedure(name, stmt);
    public bool TryGetProcedure(string name, out CreateProcedureStatement? stmt) => VarContext.TryGetProcedure(name, out stmt);
    public void SetFunction(string name, CreateFunctionStatement stmt) => VarContext.SetFunction(name, stmt);
    public bool RemoveFunction(string name) => VarContext.RemoveFunction(name);
    public bool TryGetFunction(string name, out CreateFunctionStatement? stmt) => VarContext.TryGetFunction(name, out stmt);
    public void SetView(string name, CreateViewStatement stmt) => VarContext.SetView(name, stmt);
    public bool RemoveView(string name) => VarContext.RemoveView(name);
    public bool TryGetView(string name, out CreateViewStatement? stmt) => VarContext.TryGetView(name, out stmt);
    public IReadOnlyDictionary<string, CreateViewStatement> GetViews() => VarContext.GetViews();
    public bool RemoveProcedure(string name) => VarContext.RemoveProcedure(name);
}
