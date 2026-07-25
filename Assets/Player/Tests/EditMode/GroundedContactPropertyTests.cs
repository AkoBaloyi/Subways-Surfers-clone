using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using SubwaySurfers.Player.Domain;
using SubwaySurfers.Player.Tests.Generated;
using Random = System.Random;

namespace SubwaySurfers.Player.Tests
{
    public sealed class GroundedContactPropertyTests
    {
        private const int Seed = 440051;
        private const int CaseCount = 192;

        // **Validates: Requirements 4.1, 4.2**
        [Test]
        [Description("Feature: player-controller, Property 5: Grounded is equivalent to valid running-surface contact")]
        public void GroundedIsEquivalentToValidRunningSurfaceContact_Property5_Requirements_4_1_And_4_2()
        {
            var caseIndex = 0;
            var coverage = new Coverage();

            GeneratedCaseRunner.Run(
                Seed,
                CaseCount,
                random => Generate(random, caseIndex++),
                generated => AssertGroundedEquivalence(generated, coverage),
                render: generated => generated.ToString());

            Assert.That(coverage.EmptySet, Is.True);
            Assert.That(coverage.SingletonSet, Is.True);
            Assert.That(coverage.MultiContactSet, Is.True);
            Assert.That(coverage.SetWithValidContact, Is.True);
            Assert.That(coverage.SetWithoutValidContact, Is.True);
            Assert.That(coverage.LayerMembership, Is.EquivalentTo(new[] { false, true }));
            Assert.That(coverage.MarkerPresence, Is.EquivalentTo(new[] { false, true }));
            Assert.That(coverage.TriggerStatus, Is.EquivalentTo(new[] { false, true }));
            Assert.That(coverage.DistanceRelations, Is.EquivalentTo(
                new[] { BoundaryRelation.Below, BoundaryRelation.Equal, BoundaryRelation.Above }));
            Assert.That(coverage.NormalRelations, Is.EquivalentTo(
                new[] { BoundaryRelation.Below, BoundaryRelation.Equal, BoundaryRelation.Above }));
        }

        private static GeneratedCase Generate(Random random, int caseIndex)
        {
            var groundLayer = random.Next(0, 30);
            var secondGroundLayer = (groundLayer + random.Next(1, 30)) % 30;
            var groundMask = (1 << groundLayer) | (1 << secondGroundLayer);
            var tolerance = NextFloat(random, 0f, 2f);
            var normalThreshold = NextFloat(random, 0f, 1f);
            var contacts = new List<ContactCase>();

            if (caseIndex == 0)
                return new GeneratedCase(groundMask, tolerance, normalThreshold, contacts);

            if (caseIndex == 1)
            {
                contacts.Add(new ContactCase(
                    groundLayer,
                    true,
                    false,
                    tolerance,
                    normalThreshold,
                    BoundaryRelation.Equal,
                    BoundaryRelation.Equal));
                return new GeneratedCase(groundMask, tolerance, normalThreshold, contacts);
            }

            var contactCount = caseIndex % 6 == 0 ? 1 : random.Next(2, 7);
            for (var contactIndex = 0; contactIndex < contactCount; contactIndex++)
            {
                var combination = (caseIndex * 7 + contactIndex * 13) & 31;
                var onGroundLayer = (combination & 1) != 0;
                var hasMarker = (combination & 2) != 0;
                var isTrigger = (combination & 4) != 0;
                var distanceRelation = (BoundaryRelation)((caseIndex + contactIndex) % 3);
                var normalRelation = (BoundaryRelation)((caseIndex * 2 + contactIndex) % 3);
                var contactLayer = onGroundLayer
                    ? ((combination & 8) == 0 ? groundLayer : secondGroundLayer)
                    : FirstLayerOutsideMask(groundMask, groundLayer);
                var distance = ValueAtRelation(tolerance, distanceRelation, 0f, 2.5f);
                var normalUp = ValueAtRelation(normalThreshold, normalRelation, 0f, 1f);

                contacts.Add(new ContactCase(
                    contactLayer,
                    hasMarker,
                    isTrigger,
                    distance,
                    normalUp,
                    distanceRelation,
                    normalRelation));
            }

            if (caseIndex % 4 == 0)
            {
                contacts.Add(new ContactCase(
                    groundLayer,
                    true,
                    false,
                    tolerance,
                    normalThreshold,
                    BoundaryRelation.Equal,
                    BoundaryRelation.Equal));
            }

            return new GeneratedCase(groundMask, tolerance, normalThreshold, contacts);
        }

        private static void AssertGroundedEquivalence(GeneratedCase generated, Coverage coverage)
        {
            coverage.EmptySet |= generated.Contacts.Count == 0;
            coverage.SingletonSet |= generated.Contacts.Count == 1;
            coverage.MultiContactSet |= generated.Contacts.Count > 1;

            var expectedGrounded = false;
            var actualGrounded = false;
            for (var index = 0; index < generated.Contacts.Count; index++)
            {
                var contact = generated.Contacts[index];
                var onGroundLayer = IsLayerInMask(contact.Layer, generated.GroundLayerMask);
                coverage.LayerMembership.Add(onGroundLayer);
                coverage.MarkerPresence.Add(contact.HasRunningSurfaceMarker);
                coverage.TriggerStatus.Add(contact.IsTrigger);
                coverage.DistanceRelations.Add(contact.DistanceRelation);
                coverage.NormalRelations.Add(contact.NormalRelation);

                expectedGrounded |= IsValidOracle(contact, generated);
                actualGrounded |= GroundContactPredicateSeam.IsValid(
                    contact,
                    generated.GroundLayerMask,
                    generated.ContactTolerance,
                    generated.NormalThreshold);
            }

            coverage.SetWithValidContact |= expectedGrounded;
            coverage.SetWithoutValidContact |= !expectedGrounded;
            Assert.That(actualGrounded, Is.EqualTo(expectedGrounded),
                "Grounded must be true iff at least one valid contact exists. " + generated);
        }

        private static bool IsValidOracle(ContactCase contact, GeneratedCase generated)
        {
            return IsLayerInMask(contact.Layer, generated.GroundLayerMask) &&
                contact.HasRunningSurfaceMarker &&
                !contact.IsTrigger &&
                contact.Distance <= generated.ContactTolerance &&
                contact.SupportNormalUp >= generated.NormalThreshold;
        }

        private static bool IsLayerInMask(int layer, int layerMask)
        {
            return (layerMask & (1 << layer)) != 0;
        }

        private static int FirstLayerOutsideMask(int layerMask, int startLayer)
        {
            for (var offset = 1; offset < 31; offset++)
            {
                var layer = (startLayer + offset) % 31;
                if (!IsLayerInMask(layer, layerMask)) return layer;
            }

            throw new InvalidOperationException("The generated ground mask must leave a non-ground layer.");
        }

        private static float ValueAtRelation(
            float boundary,
            BoundaryRelation relation,
            float minimum,
            float maximum)
        {
            const float delta = 0.01f;
            switch (relation)
            {
                case BoundaryRelation.Below: return Math.Max(minimum, boundary - delta);
                case BoundaryRelation.Equal: return boundary;
                case BoundaryRelation.Above: return Math.Min(maximum, boundary + delta);
                default: throw new ArgumentOutOfRangeException(nameof(relation));
            }
        }

        private static float NextFloat(Random random, float minimum, float maximum)
        {
            return minimum + (float)random.NextDouble() * (maximum - minimum);
        }

        private enum BoundaryRelation
        {
            Below,
            Equal,
            Above
        }

        private sealed class ContactCase
        {
            public ContactCase(
                int layer,
                bool hasRunningSurfaceMarker,
                bool isTrigger,
                float distance,
                float supportNormalUp,
                BoundaryRelation distanceRelation,
                BoundaryRelation normalRelation)
            {
                Layer = layer;
                HasRunningSurfaceMarker = hasRunningSurfaceMarker;
                IsTrigger = isTrigger;
                Distance = distance;
                SupportNormalUp = supportNormalUp;
                DistanceRelation = distanceRelation;
                NormalRelation = normalRelation;
            }

            public int Layer { get; }
            public bool HasRunningSurfaceMarker { get; }
            public bool IsTrigger { get; }
            public float Distance { get; }
            public float SupportNormalUp { get; }
            public BoundaryRelation DistanceRelation { get; }
            public BoundaryRelation NormalRelation { get; }
        }

        private sealed class GeneratedCase
        {
            public GeneratedCase(
                int groundLayerMask,
                float contactTolerance,
                float normalThreshold,
                IReadOnlyList<ContactCase> contacts)
            {
                GroundLayerMask = groundLayerMask;
                ContactTolerance = contactTolerance;
                NormalThreshold = normalThreshold;
                Contacts = contacts;
            }

            public int GroundLayerMask { get; }
            public float ContactTolerance { get; }
            public float NormalThreshold { get; }
            public IReadOnlyList<ContactCase> Contacts { get; }

            public override string ToString()
            {
                var renderedContacts = new string[Contacts.Count];
                for (var index = 0; index < Contacts.Count; index++)
                {
                    var contact = Contacts[index];
                    renderedContacts[index] = string.Format(
                        CultureInfo.InvariantCulture,
                        "(Layer={0},Marker={1},Trigger={2},Distance={3:R}:{4},NormalUp={5:R}:{6})",
                        contact.Layer,
                        contact.HasRunningSurfaceMarker,
                        contact.IsTrigger,
                        contact.Distance,
                        contact.DistanceRelation,
                        contact.SupportNormalUp,
                        contact.NormalRelation);
                }

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "GroundMask={0}, Tolerance={1:R}, NormalThreshold={2:R}, Contacts=[{3}]",
                    GroundLayerMask,
                    ContactTolerance,
                    NormalThreshold,
                    string.Join(";", renderedContacts));
            }
        }

        private sealed class Coverage
        {
            public bool EmptySet;
            public bool SingletonSet;
            public bool MultiContactSet;
            public bool SetWithValidContact;
            public bool SetWithoutValidContact;
            public readonly HashSet<bool> LayerMembership = new HashSet<bool>();
            public readonly HashSet<bool> MarkerPresence = new HashSet<bool>();
            public readonly HashSet<bool> TriggerStatus = new HashSet<bool>();
            public readonly HashSet<BoundaryRelation> DistanceRelations =
                new HashSet<BoundaryRelation>();
            public readonly HashSet<BoundaryRelation> NormalRelations =
                new HashSet<BoundaryRelation>();
        }

        private static class GroundContactPredicateSeam
        {
            private const string RuntimeTypeName =
                "SubwaySurfers.Player.Domain.GroundContactPredicate";

            public static bool IsValid(
                ContactCase contact,
                int groundLayerMask,
                float contactTolerance,
                float normalThreshold)
            {
                var type = typeof(PlayerState).Assembly.GetType(RuntimeTypeName);
                Assert.That(type, Is.Not.Null,
                    "Task 4.5 must provide " + RuntimeTypeName + ".");
                var method = type.GetMethod(
                    "IsValid",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[]
                    {
                        typeof(int), typeof(bool), typeof(bool), typeof(float),
                        typeof(float), typeof(int), typeof(float), typeof(float)
                    },
                    null);
                Assert.That(method, Is.Not.Null,
                    RuntimeTypeName + " must expose IsValid(int contactLayer, " +
                    "bool hasRunningSurfaceMarker, bool isTrigger, float contactDistance, " +
                    "float supportNormalUp, int groundLayerMask, float contactTolerance, " +
                    "float normalThreshold).");
                Assert.That(method.ReturnType, Is.EqualTo(typeof(bool)));

                return (bool)method.Invoke(null, new object[]
                {
                    contact.Layer,
                    contact.HasRunningSurfaceMarker,
                    contact.IsTrigger,
                    contact.Distance,
                    contact.SupportNormalUp,
                    groundLayerMask,
                    contactTolerance,
                    normalThreshold
                });
            }
        }
    }
}