using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace SubwaySurfers.Player.Tests.Generated
{
    public static class SnapshotAssert
    {
        public static void Preserved<TSnapshot>(
            TSnapshot before,
            TSnapshot after,
            IEqualityComparer<TSnapshot> comparer = null,
            string context = null)
        {
            comparer = comparer ?? EqualityComparer<TSnapshot>.Default;
            Assert.That(comparer.Equals(before, after), Is.True,
                context ?? "The complete snapshot must be preserved.");
        }

        public static void Field<TField>(
            string fieldName,
            TField expected,
            TField actual,
            IEqualityComparer<TField> comparer = null)
        {
            if (string.IsNullOrEmpty(fieldName)) throw new ArgumentException("A field name is required.", nameof(fieldName));
            comparer = comparer ?? EqualityComparer<TField>.Default;
            Assert.That(comparer.Equals(expected, actual), Is.True,
                "Snapshot field '{0}' differs. Expected: {1}; Actual: {2}",
                fieldName,
                expected,
                actual);
        }
    }
}
