using SubwaySurfers.Player.Contracts;
using SubwaySurfers.Player.Domain;
using UnityEngine;

namespace SubwaySurfers.Player
{
    /// <summary>
    /// Unity-side owner of environment contact lifetime. After every physics step the adapter samples
    /// the player capsule with a non-allocating overlap query and diffs the sampled child colliders
    /// against the previous sample; the colliders that appeared are enter samples and the colliders
    /// that disappeared are exit samples. Those samples are the only source of contact lifetime, and
    /// they are routed to the pure <see cref="EnvironmentContactTracker"/>, which stays the single
    /// owner of logical contact identity, deduplication, and publication.
    ///
    /// Callback arrival is deliberately not a lifetime source. <c>OnControllerColliderHit</c> and
    /// trigger callbacks fire for geometry the capsule is resolving against, in an order the physics
    /// engine chooses, and they never report the moment an overlap ends. They may therefore only
    /// supplement a sample with a world-space contact point through
    /// <see cref="RecordContactPosition"/>: a recorded point for geometry the sample does not report
    /// opens no contact, and a recorded point for geometry the sample still reports closes none.
    ///
    /// The adapter is a <see cref="Behaviour"/>, so disabling it suspends automatic per-step sampling
    /// while <see cref="ReconcileOverlapSamples"/> stays callable directly. Every query is read-only
    /// over pre-sized buffers, so a steady step allocates nothing and Lucky-owned geometry is never
    /// mutated: sampled colliders are held only to recognise the same child again on the next step.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class EnvironmentContactAdapter : MonoBehaviour
    {
        private const int SampleBufferLength = 32;
        private const int PositionSampleLength = 8;
        private const float MinimumProbeRadius = 0.001f;

        private readonly Collider[] overlapBuffer = new Collider[SampleBufferLength];
        private readonly Collider[] recordedColliders = new Collider[PositionSampleLength];
        private readonly Vector3[] recordedPositions = new Vector3[PositionSampleLength];

        // The sample of the last reconciliation and the sample it was diffed against. The buffer sets
        // are swapped instead of copied, so retiring a sample costs no allocation.
        private Collider[] sampledColliders = new Collider[SampleBufferLength];
        private int[] sampledColliderIds = new int[SampleBufferLength];
        private IEnvironmentObject[] sampledObjects = new IEnvironmentObject[SampleBufferLength];
        private Collider[] previousColliders = new Collider[SampleBufferLength];
        private int[] previousColliderIds = new int[SampleBufferLength];
        private IEnvironmentObject[] previousObjects = new IEnvironmentObject[SampleBufferLength];

        private EnvironmentContactTracker tracker;
        private CharacterController capsule;
        private int environmentLayerMask;
        private int sampledCount;
        private int previousCount;
        private int recordedCount;
        private int childColliderSequence;
        private int reconcileCount;

        /// <summary>Reconciliations performed, one per physics step while sampling is enabled.</summary>
        public int ReconcileCount { get { return reconcileCount; } }

        /// <summary>
        /// Environment colliders in the most recent sample. The player's own capsule and hierarchy are
        /// never part of a sample, and neither is geometry without an <see cref="IEnvironmentObject"/>.
        /// </summary>
        public int SampledColliderCount { get { return sampledCount; } }

        private CharacterController Capsule
        {
            get
            {
                if (capsule == null) capsule = GetComponent<CharacterController>();
                return capsule;
            }
        }

        /// <summary>
        /// Binds the pure contact tracker that owns logical contact identity and the layer mask that
        /// selects environment geometry. An empty mask describes a world without environment geometry,
        /// which samples nothing.
        /// </summary>
        public void Configure(EnvironmentContactTracker tracker, int environmentLayerMask)
        {
            this.tracker = tracker;
            this.environmentLayerMask = environmentLayerMask;
        }

        /// <summary>
        /// Supplies a world-space contact point for one collider, valid for the step that produced it.
        /// The point is used only if the overlap sample opens a contact for that collider; it never
        /// starts or ends one.
        /// </summary>
        public void RecordContactPosition(Collider contact, Vector3 contactPosition)
        {
            if (contact == null) return;

            for (var index = 0; index < recordedCount; index++)
            {
                if (recordedColliders[index] != contact) continue;

                // The newest point for a collider is the one its contact belongs to.
                recordedPositions[index] = contactPosition;
                return;
            }

            if (recordedCount == PositionSampleLength)
            {
                for (var index = 1; index < PositionSampleLength; index++)
                {
                    recordedColliders[index - 1] = recordedColliders[index];
                    recordedPositions[index - 1] = recordedPositions[index];
                }

                recordedCount--;
            }

            recordedColliders[recordedCount] = contact;
            recordedPositions[recordedCount] = contactPosition;
            recordedCount++;
        }

        /// <summary>
        /// Samples the capsule once and reconciles the result against the previous sample. Exits are
        /// routed before enters so a full exit and a re-entry observed in one step end one contact
        /// interval and open a new one, rather than collapsing into an unchanged contact.
        /// </summary>
        public void ReconcileOverlapSamples()
        {
            reconcileCount++;

            RetireLastSample();
            SampleOverlappingEnvironment();
            if (tracker == null)
            {
                ReleaseRecordedPositions();
                return;
            }

            for (var index = 0; index < previousCount; index++)
            {
                if (Contains(sampledColliders, sampledCount, previousColliders[index])) continue;

                tracker.Exit(previousColliderIds[index], previousObjects[index]);
            }

            for (var index = 0; index < sampledCount; index++)
            {
                if (Contains(previousColliders, previousCount, sampledColliders[index])) continue;

                tracker.Enter(
                    sampledColliderIds[index],
                    sampledObjects[index],
                    ContactPosition(index));
            }

            ReleaseRecordedPositions();
        }

        /// <summary>
        /// One reconciliation per physics step. Suspending the component suspends automatic sampling
        /// without disabling direct reconciliation.
        /// </summary>
        private void FixedUpdate()
        {
            ReconcileOverlapSamples();
        }

        /// <summary>
        /// Makes the last sample the comparison baseline by swapping buffers, then clears the buffers
        /// the fresh sample will fill so nothing from an older sample survives into it.
        /// </summary>
        private void RetireLastSample()
        {
            var retiredColliders = previousColliders;
            previousColliders = sampledColliders;
            sampledColliders = retiredColliders;

            var retiredIds = previousColliderIds;
            previousColliderIds = sampledColliderIds;
            sampledColliderIds = retiredIds;

            var retiredObjects = previousObjects;
            previousObjects = sampledObjects;
            sampledObjects = retiredObjects;

            previousCount = sampledCount;
            sampledCount = 0;
            for (var index = 0; index < SampleBufferLength; index++)
            {
                sampledColliders[index] = null;
                sampledObjects[index] = null;
            }
        }

        /// <summary>
        /// Fills the sample with the environment colliders the capsule currently overlaps. Triggers
        /// participate, because coin geometry is commonly a trigger and its contact lifetime still has
        /// to come from sampling rather than from callbacks. A child already present in the previous
        /// sample keeps the identity it was given, so an uninterrupted overlap stays one child entry
        /// for the tracker.
        /// </summary>
        private void SampleOverlappingEnvironment()
        {
            if (tracker == null || environmentLayerMask == 0) return;

            var controller = Capsule;
            if (controller == null) return;

            var owner = controller.transform;
            float radius;
            Vector3 lower;
            Vector3 upper;
            WorldCapsule(owner, controller, out lower, out upper, out radius);

            var overlapCount = Physics.OverlapCapsuleNonAlloc(
                lower, upper, radius, overlapBuffer, environmentLayerMask,
                QueryTriggerInteraction.Collide);
            for (var index = 0; index < overlapCount; index++)
            {
                var overlapping = overlapBuffer[index];
                if (overlapping == null || IsOwnCollider(owner, overlapping)) continue;
                if (sampledCount == SampleBufferLength) break;

                IEnvironmentObject environmentObject;
                if (!TryResolveEnvironmentObject(overlapping, out environmentObject)) continue;

                sampledColliders[sampledCount] = overlapping;
                sampledColliderIds[sampledCount] = ChildColliderId(overlapping);
                sampledObjects[sampledCount] = environmentObject;
                sampledCount++;
            }
        }

        /// <summary>
        /// The identity the tracker knows this child collider by: the one carried over from the
        /// previous sample, or a fresh identity for a child the previous sample did not report.
        /// </summary>
        private int ChildColliderId(Collider candidate)
        {
            for (var index = 0; index < previousCount; index++)
            {
                if (previousColliders[index] == candidate) return previousColliderIds[index];
            }

            childColliderSequence++;
            return childColliderSequence;
        }

        /// <summary>
        /// The contact position of an entering collider: the point a callback supplied for it this
        /// step, otherwise the closest point on the collider to the capsule center, otherwise the
        /// capsule center itself.
        /// </summary>
        private Vector3 ContactPosition(int sampleIndex)
        {
            var contact = sampledColliders[sampleIndex];
            for (var index = 0; index < recordedCount; index++)
            {
                if (recordedColliders[index] == contact) return recordedPositions[index];
            }

            var controller = Capsule;
            if (controller == null) return transform.position;

            var center = controller.transform.TransformPoint(controller.center);
            return contact == null ? center : contact.ClosestPoint(center);
        }

        /// <summary>
        /// Drops the callback points of the finished step: each describes one step only, and none may
        /// keep a Lucky-owned collider reachable past it.
        /// </summary>
        private void ReleaseRecordedPositions()
        {
            for (var index = 0; index < recordedCount; index++) recordedColliders[index] = null;
            recordedCount = 0;
        }

        private static bool Contains(Collider[] colliders, int count, Collider candidate)
        {
            for (var index = 0; index < count; index++)
            {
                if (colliders[index] == candidate) return true;
            }

            return false;
        }

        /// <summary>
        /// Walks the candidate's ancestors for an <see cref="IEnvironmentObject"/>, so an identity on a
        /// parent body covers its child colliders. The walk uses <c>TryGetComponent</c> at each level,
        /// which keeps the per-step sample free of the temporary results a component search produces.
        /// </summary>
        private static bool TryResolveEnvironmentObject(
            Collider candidate,
            out IEnvironmentObject environmentObject)
        {
            var current = candidate.transform;
            while (current != null)
            {
                if (current.TryGetComponent(out environmentObject)) return true;

                current = current.parent;
            }

            environmentObject = null;
            return false;
        }

        /// <summary>
        /// Maps the live capsule onto the world capsule its transform produces, matching the
        /// convention the collider profile and ground queries use: the center through the
        /// local-to-world transform, the axis through the transform's up direction, the radius through
        /// the larger lateral scale, and the height through the vertical scale.
        /// </summary>
        private static void WorldCapsule(
            Transform owner,
            CharacterController controller,
            out Vector3 lowerSphereCenter,
            out Vector3 upperSphereCenter,
            out float radius)
        {
            var scale = owner.lossyScale;
            var lateralScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            var verticalScale = Mathf.Abs(scale.y);

            radius = Mathf.Max(MinimumProbeRadius, controller.radius * lateralScale);
            var height = controller.height * verticalScale;
            var sphereOffset = Mathf.Max(0f, height * 0.5f - radius);

            var center = owner.TransformPoint(controller.center);
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
