using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ETL_SQL.Core.Common
{
    /// <summary>
    /// Thread-safe container for the execution node hierarchy.
    /// </summary>
    public class ExecutionTree
    {
        private readonly ConcurrentDictionary<Guid, ExecutionNode> _nodes = new();
        
        /// <summary>The root nodes of the execution tree (usually one for the main script).</summary>
        public List<Guid> RootNodeIds { get; } = new();

        /// <summary>Optional callback fired each time a node is added to the tree.</summary>
        public Action<ExecutionNode>? OnNodeAdded { get; set; }

        /// <summary>Adds a node to the tree and optionally attaches it to a parent.</summary>
        public void AddNode(ExecutionNode node, Guid? parentId = null)
        {
            _nodes[node.Id] = node;
            if (parentId.HasValue && _nodes.TryGetValue(parentId.Value, out var parent))
            {
                lock (parent.ChildIds)
                {
                    parent.ChildIds.Add(node.Id);
                }
            }
            else
            {
                lock (RootNodeIds)
                {
                    RootNodeIds.Add(node.Id);
                }
            }
            OnNodeAdded?.Invoke(node);
        }

        /// <summary>Retrieves a node by its unique ID.</summary>
        public ExecutionNode? GetNode(Guid id) => _nodes.TryGetValue(id, out var node) ? node : null;

        /// <summary>Returns all nodes in the tree.</summary>
        public IEnumerable<ExecutionNode> GetAllNodes() => _nodes.Values;

        /// <summary>
        /// Generates a hierarchical snapshot of the execution tree for JSON serialization.
        /// </summary>
        public object ToSnapshot()
        {
            var result = new List<object>();
            List<Guid> ids;
            lock (RootNodeIds)
            {
                ids = new List<Guid>(RootNodeIds);
            }

            foreach (var rootId in ids)
            {
                var root = GetNode(rootId);
                if (root != null) result.Add(NodeToSnapshot(root));
            }
            return result;
        }

        private object NodeToSnapshot(ExecutionNode node)
        {
            var children = new List<object>();
            List<Guid> childIds;
            lock (node.ChildIds)
            {
                childIds = new List<Guid>(node.ChildIds);
            }

            foreach (var childId in childIds)
            {
                var child = GetNode(childId);
                if (child != null) children.Add(NodeToSnapshot(child));
            }

            return new
            {
                id = node.Id,
                name = node.Name,
                status = node.Status.ToString(),
                rows = node.RowsProcessed,
                durationMs = node.GetElapsedMs(),
                velocity = node.GetVelocity(),
                error = node.ErrorMessage,
                children = children
            };
        }
    }
}
