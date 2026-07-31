# Integration Cheat Sheet — Player Controller

Code-first, one page. Full detail lives in `TechnicalDesignDocument.md` if you want it later.

Assembly to reference: `SubwaySurfers.Player.Runtime`. Namespaces: `SubwaySurfers.Player`,
`SubwaySurfers.Player.Contracts`, `SubwaySurfers.Player.Domain`.

Instantiate `Assets/Player/Prefabs/Player.prefab`. Everything below is on the
`PlayerControllerFacade` component on its root.

---

## Lucky — environment side

### 1. Running surfaces (required, or jump and slide silently fail)

Every surface the player stands on needs the marker **and** a layer inside the configured
`groundLayerMask`. Default mask is layer 0 (`Default`). Non-trigger collider.

```csharp
using SubwaySurfers.Player.Contracts;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public sealed class RunningSurface : MonoBehaviour, IRunningSurface { }
```

`IRunningSurface` is an empty marker. Nothing to implement.

Grounded is true only when a contact intersects the ground mask **and** resolves `IRunningSurface`,
within the configured contact tolerance and upward-normal threshold. Layer alone is not enough, by
design. Triggers can never ground the player.

### 2. Anything that should block standing up from a slide

```csharp
[RequireComponent(typeof(Collider))]
public sealed class Obstruction : MonoBehaviour, IEnvironmentObstruction { }
```

Also an empty marker. Non-trigger, on a layer inside `obstructionLayerMask`. While one of these
overlaps the player's baseline capsule, the player stays sliding and stands at the first clear moment.

### 3. Obstacles and coins

```csharp
public sealed class EnvironmentObject : MonoBehaviour, IEnvironmentObject
{
    [SerializeField] private string environmentObjectId;
    [SerializeField] private EnvironmentObjectKind kind;   // Obstacle or Coin
    [SerializeField] private float collectibleValue;       // finite; coins only

    public string EnvironmentObjectId { get { return environmentObjectId; } }
    public EnvironmentObjectKind Kind { get { return kind; } }
    public float CollectibleValue { get { return collectibleValue; } }
}
```

**The one rule that will bite you:** `EnvironmentObjectId` must be non-empty, and **shared by every
collider of a multi-collider object**. Contacts are deduplicated by `(EnvironmentObjectId, Kind)`, never
by collider. Put the component on the parent, or give every piece the same id.

For pooled objects, keep the id stable while the object is active. Assign a fresh id when it is recycled
into a new placement, so a re-used object is not mistaken for a contact that never ended.

Missing id or marker produces a diagnostic and the contact is ignored. No crash, no event.

**The player never mutates your objects.** It will not destroy, deactivate, collect, move, or disable
anything it touches. Removing a collected coin is yours.

---

## Rachel — game state side

### 4. Consume events (required, or hits do nothing)

```csharp
using System;
using SubwaySurfers.Player;
using SubwaySurfers.Player.Contracts;
using UnityEngine;

public sealed class RunCoordinator : MonoBehaviour
{
    [SerializeField] private PlayerControllerFacade player;

    private void OnEnable()
    {
        player.Events.PlayerHit += OnHit;
        player.Events.CoinCollected += OnCoin;
        player.Events.StateChanged += OnStateChanged;
    }

    private void OnDisable()
    {
        player.Events.PlayerHit -= OnHit;
        player.Events.CoinCollected -= OnCoin;
        player.Events.StateChanged -= OnStateChanged;
    }

    private void OnHit(PlayerHitEvent e)
    {
        // A hit does NOT fail the player. That decision is yours.
        // Shields, revives, and difficulty all live here.
        player.RequestFailure();
    }

    private void OnCoin(CoinCollectedEvent e)
    {
        score += e.CollectibleValue;   // the player never touches score
    }

    private void OnStateChanged(PlayerStateChangedEvent e) { }
}
```

Available events on `IPlayerEventSource`:

```csharp
event Action<PlayerHitEvent> PlayerHit;
event Action<CoinCollectedEvent> CoinCollected;
event Action<PlayerStateChangedEvent> StateChanged;
event Action<PlayerResetStartedEvent> ResetStarted;
event Action<PlayerResetCompletedEvent> ResetCompleted;
event Action<ValidationDiagnostic> ValidationReported;
```

Every payload carries a unique session `EventId`. Each valid transition publishes exactly one
`StateChanged`. Each logical contact publishes exactly one event. A subscriber that throws is isolated:
the other subscribers still receive the event and the simulation is unaffected.

### 5. Commands

```csharp
FailureCommandResult RequestFailure();
ResetRequestResult   RequestReset(string requestId);
SpeedSetResult       SetForwardSpeed(float requestedSpeed);
```

Every result is synchronous and carries accepted or rejected status plus a stable rejection reason. A
rejection is a result, not an exception, and a rejected command changes nothing.

**Reset needs a unique id per call.** Empty is `MissingRequestId`, a previously used id is
`DuplicateRequestId`, and one already running is `ResetInProgress`. All three are refused without
mutating anything. Use a run counter or a GUID:

```csharp
player.RequestReset("run-" + (++resetCounter));
```

Reset works from `Running`, `Jumping`, `Sliding`, and `Failed`, and completes before the next physics
step. Repeating it produces an identical starting condition every time.

`SetForwardSpeed` accepts finite, non-negative values with no imposed maximum. Negative, NaN, and
infinity are rejected and the previous speed stays in effect. Effective on the next update.

### 6. Queries

```csharp
PlayerState    CurrentState { get; }   // Running, Jumping, Sliding, Failed, Resetting
bool           IsGrounded   { get; }
float          ForwardSpeed { get; }
PlayerSnapshot Snapshot     { get; }
```

All readable at any time, including during failure and reset.

**Do not write** the player transform, collider, state, velocity, lane queue, timers, contact tracker,
camera, or animation state. Use the commands.

---

## 7. Camera

`PlayerCameraFollow` goes on **your camera**, not on the player, and takes a serialized reference to the
facade. It derives its offset from the configured initial camera and player positions at startup, so if
you want a different framing, change `initialCameraPosition` in
`Assets/Player/Config/PlayerConfiguration.asset` rather than moving the camera at runtime.

---

## 8. Input

`Assets/InputSystem_Actions.inputactions` is read-only to the player feature. Current routes:

| Intent | Bindings |
|---|---|
| Change lane | A/D, left/right arrows, left stick X |
| Jump | W, up arrow, **Space**, gamepad South |
| Slide | S, down arrow, **C**, gamepad East |

Public commands remain available regardless of input, so you can drive the player entirely from code.

---

## 9. Tuning

`Assets/Player/Config/PlayerConfiguration.asset` holds **validation defaults, not balanced values**:
forward speed 8, jump velocity 7, gravity 20, lane change 0.2 s, slide 0.8 s, lane centers -3 / 0 / 3,
baseline capsule radius 0.5 height 2, slide capsule radius 0.5 height 1.

Expect to retune during playtesting. Invalid values are repaired to safe defaults with one diagnostic per
field rather than crashing, so a bad value shows up in the diagnostics list instead of breaking the run.

---

## 10. Known gaps on the player side

- **No production slide animation.** The slide shrinks the capsule and issues an animation command;
  visually it is currently invisible unless `ValidationSlideVisual` is attached. Driving a real slide
  animation through `IAnimationReceiver` is outstanding.
- The player has never run against production track generation, a real game manager, or a HUD.
- Manual test scenarios are authored but not executed, and no cross-team review has happened.
