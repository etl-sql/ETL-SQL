using System.Collections.Generic;

namespace ETL_SQL.Core.Common
{
    /// <summary>
    /// Thread-unsafe LRU cache with a fixed capacity. Oldest entry is evicted when full.
    /// </summary>
    public sealed class LruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<(TKey key, TValue value)>> _map;
        private readonly LinkedList<(TKey key, TValue value)> _list = new();

        public LruCache(int capacity, IEqualityComparer<TKey>? comparer = null)
        {
            _capacity = capacity;
            _map = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>(capacity, comparer);
        }

        public int Count => _map.Count;

        public bool TryGetValue(TKey key, out TValue? value)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _list.Remove(node);
                _list.AddFirst(node);
                value = node.Value.value;
                return true;
            }
            value = default;
            return false;
        }

        public void Set(TKey key, TValue value)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _list.Remove(existing);
                _map.Remove(key);
            }
            else if (_map.Count >= _capacity)
            {
                var lru = _list.Last!;
                _list.RemoveLast();
                _map.Remove(lru.Value.key);
            }
            var node = _list.AddFirst((key, value));
            _map[key] = node;
        }

        public void Clear()
        {
            _map.Clear();
            _list.Clear();
        }
    }
}
