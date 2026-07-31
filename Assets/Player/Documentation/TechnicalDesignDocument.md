# Technical Design Document — Player Controller Section

Environment: Unity 6000.5.4f1, C# 9 only, Input System 1.19.0, Unity Test Framework 1.7.0, URP 17.5.0,
built-in 3D physics. No additional package dependencies.

Player-facing behavior is in `GameDesignDocument.md`. This section covers structure, contracts,
configuration, state rules, algorithms, and test strategy. Every entry carries its acceptance criterion
identifiers, which satisfies 13.5.

---

## 1. Architecture

The controller splits into pure domain logic and Unity adapters. Domain types hold gameplay rules and are
testable in Edit Mode with no scene, no prefab, and no physics step. Adapters translate between Unity and
the domain and own no rules. `Vector3` and `LayerMask` appear in domain value types; `GameObject`,
`Component`, `Physics`, and `Time` do not.

### Pure domain

| Component | Responsibility | Criteria |
|---|---|---|
| `PlayerStateMachine` | The five states and every specified transition, one current state, synchronous results with stable rejection codes | 6.1-6.18, 14.4, 14.5 |
| `ForwardDisplacement` | State-dependent forward displacement from speed and elapsed time | 2.1, 2.2, 2.5 |
| `LanePlanner` | Ordered adjacent-lane requests, FIFO completion, clamped interpolation, residual time, boundary no-op | 3.1-3.11 |
| `SlideController` | Slide entry, once-per-update timing, minimum duration, blocked retention, first-safe restoration | 5.1-5.13 |
| Ground contact predicate | Grounded equivalence from layer, marker, tolerance, and normal inputs | 4.1, 4.2 |
| Jump and landing rules | One impulse on entry, one gravity decrement per update, landing condition | 4.3-4.9 |
| `EnvironmentContactTracker` | Logical contact identity keyed by environment id and kind, overlap sets, exactly-once publication | 7.1-7.6 |
| `PlayerEventHub` | Monotonic session event ids, synchronous ordered publication, subscriber exception isolation | 7.7-7.14 |
| `CameraConvergenceState` | Non-overshooting clamped convergence, offset derivation, resettable smoothing state | 9.1-9.10 |
| Configuration validator rules | Finiteness, scalar domains, cross-field constraints, ordered relational fallback | 15.1-15.10 |
| Handoff readiness predicate | Evidence and finding model, readiness as an exact predicate | 16.1-16.9 |
| `PlayerSnapshot`, `PlayerResetSnapshot`, command results, event payloads | Immutable observation and equality surfaces | 14.1-14.3, 14.11 |

### Unity adapters

| Component | Responsibility | Criteria |
|---|---|---|
| `PlayerControllerFacade` | Validates configuration in `Awake`, distributes one effective configuration, orchestrates the movement update, exposes the public contracts | 1.2, 2.7, 15.1-15.10 |
| `CharacterControllerMotor` | Exactly one `CharacterController.Move` per fixed step with combined displacement, realized transform reporting | 2.1, 2.5, 3.6-3.8, 4.9, 5.13 |
| `GroundContactProbe` | Non-trigger capsule probing against the ground mask plus `IRunningSurface`, support normal sampling | 4.1, 4.2, 14.10, 14.11 |
| `ColliderProfileApplicator` | World-space baseline capsule overlap query, atomic radius, height, and center restoration | 5.3, 5.6-5.10, 14.11 |
| `EnvironmentContactAdapter` | Non-allocating overlap sampling each physics step, child id diffing, routing samples to the tracker | 7.1-7.6, 14.11 |
| `PlayerInputAdapter` | Resolves `Player/Move`, `Player/Jump`, `Player/Crouch` read-only, threshold latching, symmetric subscription | 3.2, 4.3, 5.1, 11.8, 11.9, 15.3 |
| `PlayerAnimationAdapter` | One state-to-command mapping per state, one receiver call per transition, failure isolation | 8.1-8.9 |
| `AnimatorAnimationReceiver` | Optional default `IAnimationReceiver` wrapping an `Animator` | 8.2, 8.6 |
| `PlayerCameraFollow` | `LateUpdate` convergence, serialized facade reference resolved to the query contract, reset hooks | 9.1-9.10 |
| `PlayerResetService` | One atomic main-thread reset sequence completing before the next movement update | 10.1-10.12 |
| `PlayerTestSceneHarness` | Validation-only controls, result and diagnostic display, ordered event log | 11.8-11.12 |

Interface-typed fields are never serialized. Concrete component references are serialized, then validated
and cast to their interface during facade-controlled initialization. This applies to the animation
receiver, the camera's facade query reference, and environment marker references. _Criteria: 12.12, 15.1_

---

## 2. Public contracts

Commands and queries, all owned by the Player Controller and all interface-based.

| Contract | Members | Criteria |
|---|---|---|
| `IPlayerCommands` | Lane, jump, slide, failure, and reset requests, each returning a synchronous result | 14.1, 14.4 |
| `IForwardSpeedApi` | `SetForwardSpeed` plus the effective speed query | 2.3, 2.4, 2.6-2.8 |
| `IFailureCommandContract` | Failure request for the run coordinator | 14.1 |
| `IResetRequestContract` | Reset request carrying a caller-supplied request id | 14.1, 14.6-14.8 |
| `IPlayerQueries`, `IPlayerStateQuery`, `IGroundedStatusQuery` | Current state, grounded status, speed, snapshot | 14.2, 14.9, 14.10 |
| `IPlayerEventSource` | Subscription to all player-domain events | 14.3 |
| `IAnimationReceiver` | `TryApply(AnimationCommand)`, the controller's only animation dependency | 8.2, 8.6 |
| `IRunningSurface`, `IEnvironmentObstruction`, `IEnvironmentObject` | Environment markers and identity supplied by the environment owner | 14.10, 14.11 |
| `IPlayerConfigurationConsumer` | Receives one immutable effective configuration from the facade | 15.1, 15.2 |
| `IPlayerMotorSurface`, `IPlayerPoseRestoration`, `IPlayerResetParticipant`, `IPlayerStateTransitionSource`, `IPlayerInputLatchQuery` | Internal seams keeping domain logic free of Unity types | 1.2, 1.6 |

### Event payloads

All immutable, all carrying a monotonic session `EventId`.

| Payload | Fields | Criteria |
|---|---|---|
| `PlayerStateChangedEvent` | Event id, previous state, current state, transition cause | 7.7, 7.8 |
| `PlayerHitEvent` | Event id, contact id, environment object id, original object reference, contact position | 7.2, 7.5 |
| `CoinCollectedEvent` | Event id, contact id, environment object id, original object reference, collectible value | 7.5 |
| `PlayerResetStartedEvent` | Event id, correlated reset request id | 7.10, 14.6 |
| `PlayerResetCompletedEvent` | Event id, correlated reset request id, resulting state | 7.11, 14.6 |
| Validation diagnostic | Field identity, category, constraint, applied fallback | 8.8, 15.3 |

A published `PlayerHitEvent` does not transition the player. A rejected transition publishes zero events.
A subscriber exception is caught at the subscriber boundary, reported as a diagnostic, and the remaining
subscribers still receive the event. _Criteria: 7.9, 7.13, 7.14_

---

## 3. Configuration domains and relational order

Validation checks finiteness first, then scalar domains, then cross-field constraints, masks, mappings,
and references. Exactly one field-specific diagnostic is emitted per invalid field and none for valid
fields. _Criteria: 15.1, 15.2, 15.3_

| Field | Domain |
|---|---|
| `forwardSpeed` | Finite, non-negative. No maximum. |
| `jumpVelocity`, `gravityAcceleration` | Finite, positive |
| `laneChangeDuration`, `slideDuration`, `cameraSettleDuration` | Finite, positive |
| `lanePositionTolerance`, `groundContactTolerance`, `cameraFollowTolerance` | Finite, non-negative |
| `laneCenters` | Three strictly increasing values |
| `groundNormalThreshold` | Finite, inclusive 0 to 1 |
| `baselineRadius`, `slideRadius` | Finite, positive |
| `baselineHeight`, `slideHeight` | Finite, positive, at least twice the matching radius |
| `groundLayerMask`, `obstructionLayerMask` | Non-empty |
| `inputNeutralThreshold`, `inputActuationThreshold` | `0 <= neutral < actuation <= 1` |

**Relational dependency order.** Lane centers before lane tolerance; baseline collider before slide
collider; neutral input threshold before actuation threshold. A relational violation is attributed to the
**dependent** field, and only that field is repaired, deriving its fallback from the already-effective
dependency. Every field that remains valid is preserved. _Criteria: 15.4, 15.5_

If a fallback is itself invalid, status becomes `ConfigurationStatus.Fatal` and simulation is disabled; no
extra `PlayerState` is invented. If a field becomes invalid after simulation started, the documented
fallback is applied, the diagnostic is reported, and movement halts before the next movement update. A
missing Input System action disables only that binding and leaves public commands available.
_Criteria: 15.6, 15.8, 15.9, 15.10_

---

## 4. State model

### 4.1 State-transition diagram

Satisfies 13.6.

```
                   failure                        reset accepted
      +--------------------------------+   +---------------------------+
      |                                v   v                           |
      |                            +--------+                          |
      |                            | Failed |                          |
      |                            +--------+                          |
      |                                 |                              |
      |                                 | reset accepted               |
      |                                 v                              |
  +---------+   jump (grounded)   +-----------+                        |
  |         |-------------------->|  Jumping  |                        |
  |         |<--------------------|           |                        |
  | Running |   landing (grounded,+-----------+                        |
  |         |    velocity <= 0)                                        |
  |         |   slide (grounded)  +-----------+                        |
  |         |-------------------->|  Sliding  |                        |
  |         |<--------------------|           |                        |
  +---------+  safe restoration   +-----------+                        |
      ^                                                                |
      |                            +------------+                      |
      +----------------------------| Resetting  |<---------------------+
        reset completed            +------------+
                                   (rejects every
                                    public command)
```

Entry state is `Running`. Reset is available from `Running`, `Jumping`, `Sliding`, and `Failed`.
`Resetting` is transient and reached only through an accepted reset. _Criteria: 6.1-6.8, 6.14_

### 4.2 Accepted and rejected state-command combinations

Satisfies 13.7. Public commands, all 35 combinations defined by Requirement 6.

| State | Lane | Jump | Slide | Failure | Reset |
|---|---|---|---|---|---|
| `Running` | Accepted | Accepted if grounded, else `NotGrounded` | Accepted if grounded, else `NotGrounded` | Accepted | Accepted for a fresh id; `MissingRequestId` or `DuplicateRequestId` otherwise |
| `Jumping` | Accepted | Rejected `InvalidState` | Rejected `InvalidState` | Accepted | Accepted for a fresh id; `MissingRequestId` or `DuplicateRequestId` otherwise |
| `Sliding` | Accepted | Rejected `InvalidState` | Rejected `InvalidState` | Accepted | Accepted for a fresh id; `MissingRequestId` or `DuplicateRequestId` otherwise |
| `Failed` | Rejected `InvalidState` | Rejected `InvalidState` | Rejected `InvalidState` | Rejected `InvalidState` | Accepted for a fresh id; `MissingRequestId` or `DuplicateRequestId` otherwise |
| `Resetting` | Rejected `InvalidState` | Rejected `InvalidState` | Rejected `InvalidState` | Rejected `InvalidState` | Rejected `ResetInProgress` |

Internal transitions, driven by the movement update rather than by a caller.

| State | Landing | Safe restoration | Reset completed |
|---|---|---|---|
| `Running` | Rejected | Rejected | Rejected |
| `Jumping` | Accepted if grounded and vertical velocity is non-positive, else rejected | Rejected | Rejected |
| `Sliding` | Rejected | Accepted when the baseline volume is clear, rejected while obstructed | Rejected |
| `Failed` | Rejected | Rejected | Rejected |
| `Resetting` | Rejected | Rejected | Accepted |

A lane request beyond an outer boundary is **accepted** and completes as a boundary no-op consuming no
segment duration. Boundary no-op is a completion outcome, never a rejection code. _Criteria: 3.4, 3.5_

Every rejected command leaves the complete `PlayerSnapshot` unchanged, publishes zero events, and never
extends a slide timer. A repeated slide is rejected before any timer mutation.
_Criteria: 6.15-6.18, 12.18_

---

## 5. Algorithms

### 5.1 Movement update

`FixedUpdate` is one `Movement_Update` and `Time.fixedDeltaTime` is the elapsed simulation time. Each
update runs this order exactly once:

1. Sample grounded contact from the previous move plus a current downward probe.
2. Resolve any landing or slide restoration due before movement.
3. Consume lane time in queue order, carrying residual time to the next queued request.
4. Compute forward displacement, active states only.
5. Integrate vertical velocity: assign the jump impulse on `Jumping` entry, subtract gravity once.
6. Submit **exactly one** `CharacterController.Move` with combined forward, lateral, and vertical displacement.
7. Resample grounded, process a valid landing, record the realized transform, flush ordered events.

`dt == 0` produces no movement and no timer progress. Collision resolution may shorten realized
displacement. _Criteria: 2.1, 2.5, 3.6-3.8, 4.5, 4.9, 5.13, 12.4, 12.5_

### 5.2 Lane interpolation

Per segment the planner retains `startX`, `targetX`, and `segmentElapsed`. It advances with
`segmentElapsed = min(duration, segmentElapsed + consumedDt)` and
`x = Lerp(startX, targetX, segmentElapsed / duration)`. The normalized parameter is clamped, so lateral
position stays on the segment and cannot cross the target center. Current and target lanes change only at
ordered segment boundaries. _Criteria: 3.6, 3.7, 3.8_

### 5.3 Grounded predicate

Grounded is true if and only if a contact both intersects the ground mask and resolves an
`IRunningSurface`, while satisfying the configured contact tolerance and upward-normal threshold.
Triggers, wrong layers, unmarked geometry, walls, ceilings, coins, and obstacles can never ground the
player. Probing never mutates environment objects. _Criteria: 4.1, 4.2, 14.11_

### 5.4 Safe collider restoration

After duration expiry, the baseline capsule endpoints are transformed to world space and tested with an
overlap capsule query against the obstruction mask, ignoring triggers and the player's own hierarchy.
Restoration is safe only when no collider implementing `IEnvironmentObstruction` overlaps the baseline
volume. Exactly one eligible restoration query is exposed per movement update. A blocked result is not an
error: the player stays `Sliding`, retains the slide profile, keeps elapsed time, and rechecks next
update. The first safe result restores radius, height, and center atomically, then transitions to
`Running` before movement continues. _Criteria: 5.6-5.10_

### 5.5 Overlap-diff contact lifetime

The contact adapter samples the player capsule with a non-allocating overlap query after every physics
step and diffs child collider instance ids against the previous sample. Active contacts are keyed by
`(EnvironmentObjectId, kind)`, never by child collider. The first child entry creates a new `ContactId`,
stores an overlap set, and publishes **one** event. Additional child entries update the set without
publishing. Exits remove child identities and the logical contact ends only when the set empties. A later
re-entry creates a new `ContactId` and may publish once again. Duplicate callbacks for an existing child
are idempotent.

`OnControllerColliderHit` may supply a contact position but is never used to infer lifetime. Trigger
callbacks may supplement sampling but are never the sole lifetime source. A multi-collider object shares
one `EnvironmentObjectId` so deduplication stays stable. Missing identity or marker produces a diagnostic
and the contact is ignored. _Criteria: 7.1-7.6, 7.12-7.14, 14.11_

### 5.6 Camera convergence

`Camera_Follow_Offset` is derived at initialization and reset as configured initial camera position minus
configured start player position, and is never serialized independently, so restoring the initial poses
yields zero initial error. Target is `player.position + offset`. Each `LateUpdate` frame with finite
positive `dt`: `fraction = remaining <= dt ? 1 : dt / remaining`, then `Vector3.Lerp(current, target,
fraction)`, then decrement `remaining`. When the target changes, `remaining` resets to the settle
duration. The clamped `Lerp` keeps the camera on the segment from current position to target, so a
positive-time fixed-target update strictly decreases error while outside tolerance and a zero-time frame
preserves position and error. Follow continues in `Failed`. _Criteria: 9.1-9.10_

---

## 6. Forward Speed API result behavior

Satisfies the first half of 13.8.

`SetForwardSpeed` accepts finite, non-negative values only, and imposes no arbitrary maximum. On
acceptance it returns an accepted result and the value is visible to queries immediately and to the next
movement update. On rejection it returns a rejection result with a stable code, emits exactly one
categorized diagnostic, and preserves the prior effective speed: NaN, positive and negative infinity, and
any negative value are all refused without mutation. Speed is never applied partially, and a rejected
call cannot leave the controller in an intermediate state. _Criteria: 2.3, 2.4, 2.6, 2.7, 2.8_

---

## 7. Repeat-safe reset equivalence

Satisfies the second half of 13.8.

`RequestReset(requestId)` rejects empty (`MissingRequestId`), previously seen (`DuplicateRequestId`), and
currently active (`ResetInProgress`) ids with no mutation and no lifecycle events. For an accepted id the
id is recorded in the session ledger **before** any mutation, then one atomic main-thread sequence runs
and completes before the next movement update:

1. Publish `PlayerResetStartedEvent` and transition to `Resetting`.
2. Disable input consumption and suppress movement execution.
3. Clear lane and action queues, contact state and deduplication, timers, vertical and lateral transients, and smoothing progress.
4. Disable the `CharacterController`, assign configured start position and rotation, the baseline collider profile, `Center` current and target lanes, and configured speed, then re-enable it.
5. Restore the configured camera pose and clear convergence state.
6. Transition to `Running`, clear in-progress status, publish the correlated state event and `PlayerResetCompletedEvent`, and return accepted.

### The complete reset equality surface

Every completed reset equals `Player_Initial_State` across all of the following, which is the equality
surface `PlayerResetSnapshot` exposes:

Player position and rotation; state; grounded status; forward speed; current and target lane; lateral
position; lane segment start and target; lane change progress; vertical velocity; slide elapsed time;
collider profile radius, height, and center; lane request queue; pending action requests; reset in-progress
status; camera position and rotation; camera follow offset; previous camera target; camera convergence
velocity; remaining camera settle time; whether a camera target is held; input latch state; active logical
contact identities; and contact event deduplication identities.

**Session state deliberately excluded from reset.** The event id counter and the reset request ledger are
session integration state and survive reset, so identifiers are never reused. Contact tracking is cleared;
the counter and ledger are not. Repeating reset with distinct fresh ids therefore causes no cumulative
change while correlation ids stay unique. _Criteria: 9.7, 9.8, 10.1-10.12, 14.6-14.8_

---

## 8. Integration contracts

Satisfies 13.9.

### Rachel's Game Manager

| Kind | Contract | Ownership note | Criteria |
|---|---|---|---|
| Command | `IFailureCommandContract` failure request | Rachel decides a run ended; the controller only enters `Failed` | 14.1 |
| Command | `IResetRequestContract` reset request with a caller-supplied id | Rachel owns retry; the controller owns player-state restoration | 14.1, 14.6-14.8 |
| Command | `IForwardSpeedApi.SetForwardSpeed` | Rachel owns the difficulty curve; the controller validates and applies | 2.3, 14.1 |
| Query | State, grounded status, forward speed, snapshot | Read-only, current at call time, no concrete facade access needed | 14.2, 14.9, 14.10 |
| Event | `PlayerStateChangedEvent` | One per valid transition, zero for a rejected one | 7.7-7.9, 14.3 |
| Event | `PlayerHitEvent` | Reports contact; does **not** transition the player | 7.2, 7.9 |
| Event | `CoinCollectedEvent` | Carries value; the controller never applies score | 7.5 |
| Event | `PlayerResetStartedEvent`, `PlayerResetCompletedEvent` | Correlated to the accepted request id | 7.10, 7.11, 14.6 |
| Payload | All immutable with get-only properties and a session event id | Additive evolution only | 14.1-14.3, 14.11 |
| Boundary | Rachel never writes the player transform, collider, state, velocity, lane queue, timers, contact tracker, camera, or animation state | Non-negotiable | 14.12 |

### Lucky's Environment System

| Kind | Contract | Ownership note | Criteria |
|---|---|---|---|
| Provider | `IRunningSurface` | Required for grounding; layer membership alone is insufficient | 4.1, 4.2, 14.10 |
| Provider | `IEnvironmentObstruction` | Required to block safe collider restoration | 5.6-5.10, 14.11 |
| Provider | `IEnvironmentObject` | Stable non-empty `EnvironmentObjectId`, kind of `Obstacle` or `Coin`, finite value where applicable; one id shared across a multi-collider object | 7.3, 7.4, 14.11 |
| Payload | Contact payloads carry the original object reference | Passed through, never mutated | 7.5, 14.11 |
| Query | Overlap and capsule probing | Read-only; probing never mutates environment objects | 4.1, 14.11 |
| Boundary | The controller never destroys, deactivates, collects, repositions, or otherwise changes an object it contacts, and never generates track | Non-negotiable | 14.11, 14.12 |
| Boundary | Missing identity or marker yields a diagnostic and the contact is ignored, with no player-domain event | Fail visible, not silent | 7.12, 7.13 |

### Compatibility

Contract evolution is additive and immutable by preference. A breaking field or meaning change requires
this section and the Development Log to be updated, plus Rachel and Lucky review evidence, before it is
considered accepted. Contract growth to date has been additive; no breaking change has occurred.
_Criteria: 13.12, 14.12_

---

## 9. Test strategy

| Layer | Scope | Criteria |
|---|---|---|
| Edit Mode properties | All 17 correctness properties, at least 100 generated cases each, deterministic `System.Random` seeds from named constants, replayable seed reported on failure, explicit generator coverage assertions | 12.1-12.5, 12.18 |
| Edit Mode examples | State-command table, contracts and ownership, configuration edges, event payloads, grounded predicate, reset ordering and rejection | 12.3, 12.9, 12.11, 12.18 |
| Edit Mode meta | Assembly isolation, asset and `.meta` pairing, GUID uniqueness, criterion traceability resolution | 1.1, 1.3, 1.4, 1.6, 12.2, 12.13, 13.5, 16.8 |
| Play Mode | `CharacterController` movement and one-`Move`-per-step, physics queries, grounding, slide obstruction, `LateUpdate` camera timing, Unity lifecycle, prefab serialization and reload, scene wiring | 12.1, 12.6-12.13 |
| Play Mode Input | Input System action resolution, latching, subscription symmetry | 11.8, 11.9, 15.1, 15.3 |
| Manual | Scenarios needing human observation, recorded with tester and date | 12.14-12.17 |
| Review | Independent, Rachel, and Lucky review evidence | 16.1-16.9 |

Universal domain properties belong in Edit Mode wherever Unity physics is unnecessary; Play Mode is
reserved for behavior that genuinely needs the engine. Test doubles stay under `Assets/Player` in test
and validation namespaces, implement Player contracts only, and are explicitly marked non-production. The
prefab, test assemblies, and `PlayerTestScene` compile and run with zero production Rachel or Lucky
implementations. `Assets/InputSystem_Actions.inputactions` is resolved read-only and never edited by a
test. _Criteria: 1.4, 11.11, 11.13, 12.13, 14.12_

Current evidence counts and observed run results are recorded in `DevelopmentLog.md`; criterion-level
mapping is in `TraceabilityManifest.txt`.
