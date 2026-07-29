using System;
using System.Collections;
using System.Collections.Generic;

namespace SubwaySurfers.Player.Domain
{
    public sealed class ImmutableValueSequence<T> : IReadOnlyList<T>, IEquatable<ImmutableValueSequence<T>>
    {
        private static readonly ImmutableValueSequence<T> EmptyInstance =
            new ImmutableValueSequence<T>(Array.Empty<T>(), false);
        private readonly T[] items;

        public ImmutableValueSequence(IEnumerable<T> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            items = Copy(values);
        }

        private ImmutableValueSequence(T[] values, bool copy)
        {
            items = copy ? (T[])values.Clone() : values;
        }

        public static ImmutableValueSequence<T> Empty { get { return EmptyInstance; } }
        public int Count { get { return items.Length; } }
        public T this[int index] { get { return items[index]; } }

        public IEnumerator<T> GetEnumerator()
        {
            for (var index = 0; index < items.Length; index++) yield return items[index];
        }

        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

        public bool Equals(ImmutableValueSequence<T> other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (ReferenceEquals(other, null) || Count != other.Count) return false;
            var comparer = EqualityComparer<T>.Default;
            for (var index = 0; index < Count; index++)
                if (!comparer.Equals(items[index], other.items[index])) return false;
            return true;
        }

        public override bool Equals(object obj) { return Equals(obj as ImmutableValueSequence<T>); }
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                var comparer = EqualityComparer<T>.Default;
                for (var index = 0; index < items.Length; index++)
                    hash = hash * 31 + (items[index] == null ? 0 : comparer.GetHashCode(items[index]));
                return hash;
            }
        }

        private static T[] Copy(IEnumerable<T> values)
        {
            var array = values as T[];
            if (array != null) return (T[])array.Clone();
            var collection = values as ICollection<T>;
            if (collection != null)
            {
                var result = new T[collection.Count];
                collection.CopyTo(result, 0);
                return result;
            }
            return new List<T>(values).ToArray();
        }
    }
}