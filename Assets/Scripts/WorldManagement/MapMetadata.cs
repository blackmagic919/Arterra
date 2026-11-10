using System;
using System.Linq;

namespace Arterra.Core.Storage {
    /// <summary>Typed metadata at one voxel. Set and Remove return a new snapshot.</summary>
    public readonly struct MapMetadata {
        private readonly Entry[] entries;
        internal Entry[] Entries => entries ?? Array.Empty<Entry>();
        public int Count => entries?.Length ?? 0;

        internal MapMetadata(Entry[] entries) {
            this.entries = entries;
        }

        [Serializable]
        public struct Entry {
            public object Value;

            public Entry(object value) {
                Value = value;
            }
        }

        // Preserve the array representation used by saves and network messages.
        internal static MapMetadata Normalize(object value) => value switch {
            null => default,
            MapMetadata metadata => metadata,
            Entry[] entries => new(entries),
            _ => new(new[] { new Entry(value) })
        };

        public bool TryGet<T>(out T value) {
            foreach (var entry in Entries) {
                if (entry.Value is T result) {
                    value = result;
                    return true;
                }
            }
            value = default;
            return false;
        }

        public MapMetadata Set<T>(T value) {
            if (value == null) return Remove<T>();
            Type type = value.GetType();
            return new(Entries.Where(entry => entry.Value?.GetType() != type)
                .Append(new Entry(value)).ToArray());
        }

        public MapMetadata Remove<T>() {
            return new(Entries.Where(entry => !(entry.Value is T)).ToArray());
        }
    }
}
