#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Arterra.Utils {
    [JsonObject(MemberSerialization.OptIn)]
    public class IndirectDictionary<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
    {
        [JsonIgnore] private readonly Dictionary<TKey, int> _index;
        [JsonIgnore]  private readonly List<Entry> _entries;

        [JsonProperty]
        public List<KeyValuePair<TKey, TValue>> Serialized {
            get => _entries.Select(e => new KeyValuePair<TKey, TValue>(e.Key, e.Value)).ToList();
            set {
                Clear();
                if (value == null) return;
                foreach (var kv in value)
                    TryAdd(kv.Key, kv.Value);
            }
        }

        private struct Entry
        {
            public TKey Key;
            public TValue Value;

            public Entry(TKey key, TValue value)
            {
                Key = key;
                Value = value;
            }
        }

        public IndirectDictionary(int capacity = 0, IEqualityComparer<TKey>? comparer = null)
        {
            _index = new Dictionary<TKey, int>(capacity, comparer);
            _entries = new List<Entry>(capacity);
        }

        [JsonIgnore] public int Count => _entries.Count;

        public IEnumerable<TKey> Keys
        {
            get
            {
                foreach (var e in _entries)
                    yield return e.Key;
            }
        }

        public IEnumerable<TValue> Values
        {
            get
            {
                foreach (var e in _entries)
                    yield return e.Value;
            }
        }

        public TValue this[TKey key]
        {
            get
            {
                if (!_index.TryGetValue(key, out int i))
                    throw new KeyNotFoundException();
                return _entries[i].Value;
            }
            set
            {
                if (_index.TryGetValue(key, out int i))
                {
                    _entries[i] = new Entry(key, value);
                }
                else
                {
                    Add(key, value);
                }
            }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            if (_index.TryGetValue(key, out int i))
            {
                value = _entries[i].Value;
                return true;
            }

            value = default!;
            return false;
        }

        public bool ContainsKey(TKey key)
            => _index.ContainsKey(key);

        public void Add(TKey key, TValue value)
        {
            if (_index.ContainsKey(key))
                throw new ArgumentException("Key already exists.");

            int index = _entries.Count;
            _entries.Add(new Entry(key, value));
            _index.Add(key, index);
        }

        public bool TryAdd(TKey key, TValue value)
        {
            if (_index.ContainsKey(key))
                return false;

            int index = _entries.Count;
            _entries.Add(new Entry(key, value));
            _index.Add(key, index);
            return true;
        }

        public bool Remove(TKey key)
        {
            if (!_index.TryGetValue(key, out int index))
                return false;

            int lastIndex = _entries.Count - 1;
            var lastEntry = _entries[lastIndex];

            // Swap back if not removing last
            if (index != lastIndex)
            {
                _entries[index] = lastEntry;
                _index[lastEntry.Key] = index;
            }

            _entries.RemoveAt(lastIndex);
            _index.Remove(key);

            return true;
        }

        public void Clear()
        {
            _index.Clear();
            _entries.Clear();
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                yield return new KeyValuePair<TKey, TValue>(e.Key, e.Value);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}