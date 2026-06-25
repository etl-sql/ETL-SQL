using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Data;

namespace ETL_SQL.Engine;
/// <summary>
/// Manages nested transactions, variable snapshots, and data source enlistment for ACID-like behavior in scripts.
/// </summary>
public class TransactionManager
{
    private int _trancount = 0;
    private readonly Stack<TransactionSnapshot> _snapshots = new();

    public int TranCount => _trancount;

    /// <summary>Starts a new transaction by creating a snapshot of current variables and in-memory data.</summary>
    public async Task BeginTransaction(IDictionary<string, object?> variables, IDictionary<string, IDataSource> connections)
    {
        var snapshot = new TransactionSnapshot
        {
            Variables = new Dictionary<string, object?>(variables),
            DataSnapshots = new Dictionary<string, object?>()
        };

        foreach (var kvp in connections)
        {
            if (kvp.Value is InMemoryDataSource imds)
            {
                snapshot.DataSnapshots[kvp.Key] = imds.Snapshot();
            }
        }

        _snapshots.Push(snapshot);
        _trancount++;
        await Task.CompletedTask;
    }

    /// <summary>Enlists a data source into the current transaction scope if it supports transactions.</summary>
    public async Task EnlistDataSource(IDataSource ds)
    {
        if (_trancount == 0 || _snapshots.Count == 0) return;
        if (ds is ITransactionalDataSource tds)
        {
            var current = _snapshots.Peek();
            if (!current.EnlistedDataSources.Contains(tds))
            {
                await tds.BeginTransactionAsync();
                current.EnlistedDataSources.Add(tds);
            }
        }
    }

    /// <summary>Commits the current transaction level. If it's the root transaction, clears snapshots.</summary>
    public async Task CommitTransaction()
    {
        if (_trancount > 0)
        {
            var snapshot = _snapshots.Count > 0 ? _snapshots.Peek() : null;
            if (snapshot != null)
            {
                foreach (var tds in snapshot.EnlistedDataSources)
                {
                    await tds.CommitAsync();
                }
            }

            _trancount--;
            if (_trancount == 0) _snapshots.Clear();
            else if (_snapshots.Count > 0) _snapshots.Pop();
        }
    }

    /// <summary>Rolls back the current transaction level (defaults to full rollback in this implementation).</summary>
    public async Task RollbackTransaction(IDictionary<string, object?> variables, IDictionary<string, IDataSource> connections)
    {
        if (_trancount > 0)
        {
            // Full rollback by default (standard SQL)
            await RollbackAll(variables, connections);
        }
    }

    /// <summary>Rolls back all nested transactions and restores variables and data to the initial state.</summary>
    public async Task RollbackAll(IDictionary<string, object?> variables, IDictionary<string, IDataSource> connections)
    {
        if (_snapshots.Count > 0)
        {
            // Rollback all external transactions in reverse order of snapshots
            var allSnapshots = _snapshots.ToList();
            foreach (var snapshot in allSnapshots)
            {
                foreach (var tds in snapshot.EnlistedDataSources)
                {
                    await tds.RollbackAsync();
                }
            }

            // Revert to the VERY FIRST snapshot (the root)
            TransactionSnapshot? root = allSnapshots.LastOrDefault();

            if (root != null)
            {
                // Restore variables
                variables.Clear();
                foreach (var kvp in root.Variables) variables[kvp.Key] = kvp.Value;

                // Restore in-memory data
                foreach (var kvp in root.DataSnapshots)
                {
                    if (connections.TryGetValue(kvp.Key, out var ds))
                    {
                        ds.Restore(kvp.Value);
                    }
                }
            }
        }
        _trancount = 0;
        _snapshots.Clear();
    }
}

/// <summary>Represents a snapshot of state at the start of a transaction.</summary>
public class TransactionSnapshot
{
    public Dictionary<string, object?> Variables { get; set; } = new();
    public Dictionary<string, object?> DataSnapshots { get; set; } = new();
    public List<ITransactionalDataSource> EnlistedDataSources { get; set; } = new();
}
