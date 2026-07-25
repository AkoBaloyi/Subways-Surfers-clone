using System;
using System.Collections.Generic;

namespace SubwaySurfers.Player.Tests.Generated
{
    public enum ContactOperation
    {
        Enter,
        Stay,
        Exit
    }

    public struct ContactStep<TIdentity>
    {
        public ContactStep(ContactOperation operation, TIdentity identity)
        {
            Operation = operation;
            Identity = identity;
        }

        public ContactOperation Operation { get; }
        public TIdentity Identity { get; }

        public override string ToString()
        {
            return Operation + ":" + Identity;
        }
    }

    public static class GeneratedSequences
    {
        public static IReadOnlyList<TCommand> Commands<TCommand>(
            Random random,
            IReadOnlyList<TCommand> commandDomain,
            int maximumLength)
        {
            ValidateSequenceArguments(random, commandDomain, maximumLength, nameof(commandDomain));

            var count = NextLength(random, maximumLength);
            var commands = new List<TCommand>(count);
            for (var index = 0; index < count; index++)
                commands.Add(commandDomain[random.Next(commandDomain.Count)]);
            return commands;
        }

        public static IReadOnlyList<ContactStep<TIdentity>> Contacts<TIdentity>(
            Random random,
            IReadOnlyList<TIdentity> identityDomain,
            int maximumLength)
        {
            ValidateSequenceArguments(random, identityDomain, maximumLength, nameof(identityDomain));

            var active = new List<TIdentity>();
            var result = new List<ContactStep<TIdentity>>();
            var count = NextLength(random, maximumLength);
            for (var index = 0; index < count; index++)
            {
                if (active.Count == 0 || random.Next(3) == 0)
                {
                    var identity = identityDomain[random.Next(identityDomain.Count)];
                    if (!active.Contains(identity)) active.Add(identity);
                    result.Add(new ContactStep<TIdentity>(ContactOperation.Enter, identity));
                    continue;
                }

                var activeIndex = random.Next(active.Count);
                var operation = random.Next(2) == 0 ? ContactOperation.Stay : ContactOperation.Exit;
                result.Add(new ContactStep<TIdentity>(operation, active[activeIndex]));
                if (operation == ContactOperation.Exit) active.RemoveAt(activeIndex);
            }

            return result;
        }

        public static IEnumerable<IReadOnlyList<T>> Shrink<T>(IReadOnlyList<T> sequence)
        {
            if (sequence == null) throw new ArgumentNullException(nameof(sequence));
            if (sequence.Count == 0) yield break;

            yield return Array.Empty<T>();
            for (var index = 0; index < sequence.Count; index++)
            {
                var candidate = new List<T>(sequence.Count - 1);
                for (var sourceIndex = 0; sourceIndex < sequence.Count; sourceIndex++)
                    if (sourceIndex != index) candidate.Add(sequence[sourceIndex]);
                yield return candidate;
            }
        }

        public static IEnumerable<IReadOnlyList<ContactStep<TIdentity>>> ShrinkContacts<TIdentity>(
            IReadOnlyList<ContactStep<TIdentity>> sequence)
        {
            if (sequence == null) throw new ArgumentNullException(nameof(sequence));
            if (sequence.Count == 0) yield break;

            yield return Array.Empty<ContactStep<TIdentity>>();
            for (var prefixLength = sequence.Count / 2; prefixLength > 0; prefixLength /= 2)
            {
                var prefix = new List<ContactStep<TIdentity>>(prefixLength);
                for (var index = 0; index < prefixLength; index++) prefix.Add(sequence[index]);
                yield return prefix;
            }
        }

        private static int NextLength(Random random, int maximumLength)
        {
            return maximumLength == int.MaxValue
                ? random.Next()
                : random.Next(0, maximumLength + 1);
        }

        private static void ValidateSequenceArguments<T>(
            Random random,
            IReadOnlyList<T> domain,
            int maximumLength,
            string domainParameterName)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (domain == null || domain.Count == 0)
                throw new ArgumentException("A non-empty sequence domain is required.", domainParameterName);
            if (maximumLength < 0) throw new ArgumentOutOfRangeException(nameof(maximumLength));
        }
    }
}
