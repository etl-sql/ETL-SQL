using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "LRU cache capacity must be greater than zero.");
            _capacity = capacity;
            _map = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>(capacity, comparer);
        }

        public Action<TValue>? OnEvicted { get; set; }
        public Func<TValue, ValueTask>? OnEvictedAsync { get; set; }

        public int Count => _map.Count;
        public IEnumerable<TValue> Values => System.Linq.Enumerable.Select(_list, n => n.value);

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
                OnEvicted?.Invoke(existing.Value.value);
                _list.Remove(existing);
                _map.Remove(key);
            }
            else if (_map.Count >= _capacity)
            {
                var lru = _list.Last!;
                OnEvicted?.Invoke(lru.Value.value);
                _list.RemoveLast();
                _map.Remove(lru.Value.key);
            }
            var node = _list.AddFirst((key, value));
            _map[key] = node;
        }

        public async ValueTask SetAsync(TKey key, TValue value)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                await InvokeEvictedAsync(existing.Value.value);
                _list.Remove(existing);
                _map.Remove(key);
            }
            else if (_map.Count >= _capacity)
            {
                var lru = _list.Last!;
                await InvokeEvictedAsync(lru.Value.value);
                _list.RemoveLast();
                _map.Remove(lru.Value.key);
            }
            var node = _list.AddFirst((key, value));
            _map[key] = node;
        }

        public void Clear()
        {
            if (OnEvicted != null)
            {
                foreach (var item in _list) OnEvicted(item.value);
            }
            _map.Clear();
            _list.Clear();
        }

        public async ValueTask ClearAsync()
        {
            if (OnEvicted != null || OnEvictedAsync != null)
            {
                foreach (var item in _list) await InvokeEvictedAsync(item.value);
            }
            _map.Clear();
            _list.Clear();
        }

        private async ValueTask InvokeEvictedAsync(TValue value)
        {
            OnEvicted?.Invoke(value);
            if (OnEvictedAsync != null)
                await OnEvictedAsync(value);
        }
    }
}
