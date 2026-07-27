using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player
{
    /// <summary>
    /// Dedicated owner of <c>Player_Collider</c> profile application and the
    /// <c>Safe_Collider_Restoration</c> query. Application writes radius, height, and center as one
    /// operation, so a profile change is never observed half applied; the query answers whether the
    /// <c>Baseline_Collider_Profile</c> volume at the current transform contains an
    /// <see cref="IEnvironmentObstruction"/>.
    ///
    /// The queried volume is the profile mapped through the owning transform: position, rotation, and
    /// scale all participate. A <see cref="CharacterController"/> capsule is scaled by its transform -
    /// the radius by the larger of the lateral scale components, the height by the vertical one, the
    /// center by the full scale - so the volume the capsule will occupy after restoration is the
    /// volume this query has to test. Assuming an axis-aligned unit-scale transform would test the
    /// wrong region of the world and answer for geometry the player never occupies.
    ///
    /// Triggers, colliders in the player's own hierarchy, and geometry without an
    /// <see cref="IEnvironmentObstruction"/> marker never block restoration. Queries are read-only
    /// overlap tests over a pre-sized buffer, so a per-step query allocates nothing and never mutates
    /// environment geometry.
    /// </summary>
    public sealed class ColliderProfileApplicator
    {
        private const int QueryBufferLength = 16;
        private const float MinimumProbeRadius = 0.001f;

        private readonly Collider[] overlapBuffer = new Collider[QueryBufferLength];

        /// <summary>
        /// Writes radius, height, and center of <paramref name="profile"/> onto the capsule in one
        /// operation. The two dimension writes are ordered so the capsule never passes through an
        /// invalid intermediate shape: growing the radius raises the height first, shrinking it
        /// lowers the radius first. Either order leaves height of at least twice the radius intact.
        /// </summary>
        public void Apply(CharacterController capsule, ColliderProfile profile)
        {
            if (capsule == null) return;

            capsule.center = profile.Center;
            if (profile.Radius > capsule.radius)
            {
                capsule.height = profile.Height;
                capsule.radius = profile.Radius;
            }
            else
            {
                capsule.radius = profile.Radius;
                capsule.height = profile.Height;
            }
        }

        /// <summary>
        /// True when the <paramref name="baselineProfile"/> volume, mapped through the capsule's
        /// transform, contains no <see cref="IEnvironmentObstruction"/>. An empty obstruction mask
        /// describes a world without obstruction geometry, which is always safe.
        /// </summary>
        public bool IsRestorationSafe(
            CharacterController capsule,
            ColliderProfile baselineProfile,
            int obstructionLayerMask)
        {
            if (capsule == null || obstructionLayerMask == 0) return true;

            var owner = capsule.transform;
            float radius;
            Vector3 lower;
            Vector3 upper;
            WorldCapsule(owner, baselineProfile, out lower, out upper, out radius);

            var overlapCount = Physics.OverlapCapsuleNonAlloc(
                lower, upper, radius, overlapBuffer, obstructionLayerMask,
                QueryTriggerInteraction.Ignore);
            for (var index = 0; index < overlapCount; index++)
            {
                var overlapping = overlapBuffer[index];
                if (overlapping == null || IsOwnCollider(owner, overlapping)) continue;

                // Only marked Environment_Obstruction geometry blocks restoration; unmarked geometry
                // on the same mask is scenery the standing volume may share.
                if (ResolvesObstruction(overlapping)) return false;
            }

            return true;
        }

        /// <summary>
        /// Walks the candidate's ancestors for an <see cref="IEnvironmentObstruction"/> marker, so a
        /// marker on a parent body still covers its child colliders. The walk uses
        /// <c>TryGetComponent</c> at each level, which keeps a per-step query free of the temporary
        /// results a component search would otherwise produce.
        /// </summary>
        private static bool ResolvesObstruction(Collider candidate)
        {
            var current = candidate.transform;
            while (current != null)
            {
                IEnvironmentObstruction marker;
                if (current.TryGetComponent(out marker)) return true;

                current = current.parent;
            }

            return false;
        }

        /// <summary>
        /// Maps a profile onto the world capsule the transform would produce: the center through the
        /// full local-to-world transform, the axis through the transform's up direction, the radius
        /// through the larger lateral scale, and the height through the vertical scale.
        /// </summary>
        private static void WorldCapsule(
            Transform owner,
            ColliderProfile profile,
            out Vector3 lowerSphereCenter,
            out Vector3 upperSphereCenter,
            out float radius)
        {
            var scale = owner.lossyScale;
            var lateralScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            var verticalScale = Mathf.Abs(scale.y);

            radius = Mathf.Max(MinimumProbeRadius, profile.Radius * lateralScale);
            var height = profile.Height * verticalScale;
            var sphereOffset = Mathf.Max(0f, height * 0.5f - radius);

            var center = owner.TransformPoint(profile.Center);
            var axis = owner.up;
            lowerSphereCenter = center - axis * sphereOffset;
            upperSphereCenter = center + axis * sphereOffset;
        }

        private static bool IsOwnCollider(Transform owner, Collider candidate)
        {
            return candidate.transform == owner || candidate.transform.IsChildOf(owner);
        }
    }
}
