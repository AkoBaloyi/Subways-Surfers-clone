using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player
{
    /// <summary>
    /// Dedicated <c>Grounded_Query</c> probe. It gathers the physical facts a
    /// <c>Valid_Ground_Contact</c> needs - contact layer, <see cref="IRunningSurface"/> marker,
    /// trigger flag, contact distance, and support normal - and hands each candidate to the pure
    /// <see cref="GroundContactPredicate"/>, which stays the single place the rule lives.
    ///
    /// A downward capsule sweep that starts already overlapping geometry reports no usable normal,
    /// so overlapping candidates never assume an upward support normal. Their true support normal is
    /// resolved from the most recent controller collision normal for that collider, from the closest
    /// point on the collider surface, or from a supplementary sweep started clear of the overlap.
    /// When no source resolves a normal, the candidate is not treated as support.
    ///
    /// All queries are read-only and reuse pre-sized buffers, so probing allocates nothing per step
    /// and never mutates environment geometry.
    /// </summary>
    public sealed class GroundProbe
    {
        private const int QueryBufferLength = 16;
        private const int ContactSampleLength = 8;
        private const float DegenerateNormalEpsilon = 1e-6f;
        private const float MinimumProbeRadius = 0.001f;

        private readonly RaycastHit[] sweepBuffer = new RaycastHit[QueryBufferLength];
        private readonly RaycastHit[] resolveBuffer = new RaycastHit[QueryBufferLength];
        private readonly Collider[] overlapBuffer = new Collider[QueryBufferLength];
        private readonly Collider[] contactColliders = new Collider[ContactSampleLength];
        private readonly Vector3[] contactNormals = new Vector3[ContactSampleLength];

        private int contactSampleCount;

        /// <summary>Drops the collision normals sampled for the previous displacement.</summary>
        public void ClearContactSamples()
        {
            for (var index = 0; index < contactSampleCount; index++) contactColliders[index] = null;
            contactSampleCount = 0;
        }

        /// <summary>
        /// Records a controller collision normal. The newest normal for a collider replaces the
        /// previous one, so the probe always reads the most recent contact for that collider.
        /// </summary>
        public void RecordContactSample(Collider contact, Vector3 normal)
        {
            if (contact == null) return;

            for (var index = 0; index < contactSampleCount; index++)
            {
                if (contactColliders[index] != contact) continue;

                contactNormals[index] = normal;
                return;
            }

            if (contactSampleCount == ContactSampleLength)
            {
                for (var index = 1; index < ContactSampleLength; index++)
                {
                    contactColliders[index - 1] = contactColliders[index];
                    contactNormals[index - 1] = contactNormals[index];
                }

                contactSampleCount--;
            }

            contactColliders[contactSampleCount] = contact;
            contactNormals[contactSampleCount] = normal;
            contactSampleCount++;
        }

        /// <summary>
        /// True when at least one candidate satisfies <see cref="GroundContactPredicate"/> for the
        /// supplied capsule profile, ground mask, contact tolerance, and support-normal threshold.
        /// </summary>
        public bool SampleGrounded(
            CharacterController capsule,
            ColliderProfile profile,
            int groundLayerMask,
            float contactTolerance,
            float normalThreshold)
        {
            if (capsule == null || groundLayerMask == 0) return false;

            var owner = capsule.transform;
            var skinWidth = Mathf.Max(0f, capsule.skinWidth);
            var radius = Mathf.Max(MinimumProbeRadius, profile.Radius);
            var bottom = LowerSphereCenter(owner, profile, radius);
            var top = UpperSphereCenter(owner, profile, radius);
            var castDistance = Mathf.Max(MinimumProbeRadius, contactTolerance + skinWidth);

            var hitCount = Physics.CapsuleCastNonAlloc(
                bottom, top, radius, Vector3.down, sweepBuffer, castDistance,
                groundLayerMask, QueryTriggerInteraction.Ignore);
            for (var index = 0; index < hitCount; index++)
            {
                var hit = sweepBuffer[index];
                var hitCollider = hit.collider;
                if (hitCollider == null || IsOwnCollider(owner, hitCollider)) continue;

                float supportNormalUp;
                float contactDistance;
                if (IsOverlappingHit(hit))
                {
                    // Already overlapping: the sweep carries no usable normal, so the true support
                    // normal has to be resolved before the candidate can carry the player.
                    contactDistance = 0f;
                    if (!TryResolveSupportNormalUp(
                            owner, hitCollider, profile, bottom, radius, groundLayerMask,
                            out supportNormalUp))
                    {
                        continue;
                    }
                }
                else
                {
                    // The skin width is the controller's own standoff, not clearance from the surface.
                    contactDistance = Mathf.Max(0f, hit.distance - skinWidth);
                    supportNormalUp = hit.normal.y;
                }

                if (IsValidGroundContact(
                        hitCollider, contactDistance, supportNormalUp,
                        groundLayerMask, contactTolerance, normalThreshold))
                {
                    return true;
                }
            }

            // Geometry the capsule already penetrates is not guaranteed to appear in a sweep, so an
            // overlap query supplies the remaining penetrating candidates.
            var overlapCount = Physics.OverlapCapsuleNonAlloc(
                bottom, top, radius, overlapBuffer, groundLayerMask, QueryTriggerInteraction.Ignore);
            for (var index = 0; index < overlapCount; index++)
            {
                var overlapping = overlapBuffer[index];
                if (overlapping == null || IsOwnCollider(owner, overlapping)) continue;

                float supportNormalUp;
                if (!TryResolveSupportNormalUp(
                        owner, overlapping, profile, bottom, radius, groundLayerMask,
                        out supportNormalUp))
                {
                    continue;
                }

                if (IsValidGroundContact(
                        overlapping, 0f, supportNormalUp,
                        groundLayerMask, contactTolerance, normalThreshold))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsValidGroundContact(
            Collider candidate,
            float contactDistance,
            float supportNormalUp,
            int groundLayerMask,
            float contactTolerance,
            float normalThreshold)
        {
            return GroundContactPredicate.IsValid(
                candidate.gameObject.layer,
                candidate.GetComponentInParent<IRunningSurface>() != null,
                candidate.isTrigger,
                contactDistance,
                supportNormalUp,
                groundLayerMask,
                contactTolerance,
                normalThreshold);
        }

        private static bool IsOverlappingHit(RaycastHit hit)
        {
            return hit.distance <= 0f || hit.normal.sqrMagnitude <= DegenerateNormalEpsilon;
        }

        /// <summary>
        /// Resolves the outward support normal of geometry the capsule penetrates, newest controller
        /// collision normal first, then the closest surface point, then a sweep started clear of the
        /// overlap. Returns false when no source yields a usable normal.
        /// </summary>
        private bool TryResolveSupportNormalUp(
            Transform owner,
            Collider candidate,
            ColliderProfile profile,
            Vector3 lowerSphereCenter,
            float radius,
            int groundLayerMask,
            out float supportNormalUp)
        {
            if (TryReadContactSample(candidate, out supportNormalUp)) return true;
            if (TryReadClosestSurfaceNormal(candidate, lowerSphereCenter, out supportNormalUp)) return true;
            return TryReadClearedSweepNormal(
                owner, candidate, profile, lowerSphereCenter, radius, groundLayerMask,
                out supportNormalUp);
        }

        private bool TryReadContactSample(Collider candidate, out float supportNormalUp)
        {
            supportNormalUp = 0f;
            for (var index = 0; index < contactSampleCount; index++)
            {
                if (contactColliders[index] != candidate) continue;

                var normal = contactNormals[index];
                if (normal.sqrMagnitude <= DegenerateNormalEpsilon) return false;

                supportNormalUp = normal.normalized.y;
                return true;
            }

            return false;
        }

        private static bool TryReadClosestSurfaceNormal(
            Collider candidate,
            Vector3 lowerSphereCenter,
            out float supportNormalUp)
        {
            supportNormalUp = 0f;

            // ClosestPoint returns the query point itself for a point inside the collider and for
            // shapes it cannot evaluate, which leaves no direction to read.
            var surfacePoint = candidate.ClosestPoint(lowerSphereCenter);
            var separation = lowerSphereCenter - surfacePoint;
            if (separation.sqrMagnitude <= DegenerateNormalEpsilon) return false;

            supportNormalUp = separation.normalized.y;
            return true;
        }

        private bool TryReadClearedSweepNormal(
            Transform owner,
            Collider candidate,
            ColliderProfile profile,
            Vector3 lowerSphereCenter,
            float radius,
            int groundLayerMask,
            out float supportNormalUp)
        {
            supportNormalUp = 0f;

            var clearance = Mathf.Max(profile.Height, 2f * radius) + radius;
            var origin = lowerSphereCenter + owner.up * clearance;
            var sweepDistance = clearance + radius;

            var hitCount = Physics.SphereCastNonAlloc(
                origin, radius, Vector3.down, resolveBuffer, sweepDistance,
                groundLayerMask, QueryTriggerInteraction.Ignore);
            for (var index = 0; index < hitCount; index++)
            {
                var hit = resolveBuffer[index];
                if (hit.collider == null || hit.collider != candidate) continue;
                if (IsOverlappingHit(hit)) continue;

                supportNormalUp = hit.normal.y;
                return true;
            }

            return false;
        }

        private static bool IsOwnCollider(Transform owner, Collider candidate)
        {
            return candidate.transform == owner || candidate.transform.IsChildOf(owner);
        }

        private static Vector3 LowerSphereCenter(Transform owner, ColliderProfile profile, float radius)
        {
            return owner.TransformPoint(profile.Center) - owner.up * SphereOffset(profile, radius);
        }

        private static Vector3 UpperSphereCenter(Transform owner, ColliderProfile profile, float radius)
        {
            return owner.TransformPoint(profile.Center) + owner.up * SphereOffset(profile, radius);
        }

        private static float SphereOffset(ColliderProfile profile, float radius)
        {
            return Mathf.Max(0f, profile.Height * 0.5f - radius);
        }
    }
}
