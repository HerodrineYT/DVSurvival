using System;
using System.Collections;
using System.Collections.Generic;

namespace DVSurvival.Core.Runtime
{
    // MPAPI identities are bytes. Dense iteration + direct lookup, no hashing/resizing/boxed enumeration.
    public sealed class PlayerSlots<T> : IEnumerable<KeyValuePair<byte, T>>
    {
        private readonly T[] values = new T[256];
        private readonly byte[] ids = new byte[256];
        private readonly int[] positions = new int[256]; // Dense index + 1, zero means absent.
        public int Count { get; private set; }
        public T this[byte id]
        {
            get { if (!ContainsKey(id)) throw new KeyNotFoundException(); return values[id]; }
            set { if (!ContainsKey(id)) { ids[Count] = id; positions[id] = ++Count; } values[id] = value; }
        }
        public bool ContainsKey(byte id) => positions[id] != 0;
        public bool TryGetValue(byte id, out T value) { value = values[id]; return positions[id] != 0; }
        public bool Remove(byte id)
        {
            int position = positions[id] - 1;
            if (position < 0) return false;
            byte last = ids[--Count]; ids[position] = last; positions[last] = position + 1;
            positions[id] = 0; values[id] = default(T); ids[Count] = 0; return true;
        }
        public void Clear()
        { Array.Clear(values, 0, 256); Array.Clear(positions, 0, 256); Array.Clear(ids, 0, 256); Count = 0; }
        public byte IdAt(int index)
        { if ((uint)index >= Count) throw new ArgumentOutOfRangeException(nameof(index)); return ids[index]; }
        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<KeyValuePair<byte, T>> IEnumerable<KeyValuePair<byte, T>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public struct Enumerator : IEnumerator<KeyValuePair<byte, T>>
        {
            private readonly PlayerSlots<T> owner;
            private int index;
            internal Enumerator(PlayerSlots<T> owner) { this.owner = owner; index = -1; }
            public bool MoveNext() => ++index < owner.Count;
            public KeyValuePair<byte, T> Current { get { byte id = owner.ids[index]; return new KeyValuePair<byte, T>(id, owner.values[id]); } }
            object IEnumerator.Current => Current;
            public void Reset() { index = -1; }
            public void Dispose() { }
        }
    }
}
