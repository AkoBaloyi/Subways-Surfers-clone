# Audio

Player sound effects, driven by the Player Controller's published events plus its public queries.

## Layout

```
Assets/Audio/
  *.wav                            imported source clips
  Runtime/
    SubwaySurfers.Audio.asmdef     references SubwaySurfers.Player.Runtime only
    PlayerAudioDirector.cs         listens to the player and plays clips
```

## Ownership

Audio depends on the player; the player never depends on audio. `Assets/Player` contains no audio code
and needs none, so the Player Controller still compiles, runs, and passes its tests with this folder
absent. Nothing here writes player state or touches an environment object.

Run-lifecycle audio — menu music, game-over stings, UI taps, mystery box, unlocks, mission rewards —
belongs with the Game State system. Train pass-bys, guard proximity, and power-up loops belong with the
Environment system or are out of scope for this project. This component covers player movement and
contact effects only.

## Wiring

1. Add `PlayerAudioDirector` to a GameObject in the gameplay scene. The player root works well.
2. Assign the **Player** field to the `PlayerControllerFacade` component on the player.
3. Assign the clips below.

The `AudioSource` is created automatically if you leave that field empty.

## Clip slots and suggested sources

| Slot | Fires on | Suggested clip |
|---|---|---|
| Jump | take-off | `Hr_run_jump #20826` |
| Land | valid landing | `Hr_landing #20833` |
| Slide | slide begins | `Hr_run_roll #20820` |
| Stand Up | baseline collider safely restored | `Hr_swishCShort #20840` |
| Lane Change | accepted lane movement begins | `Hr_run_dodge #20823` |
| Footstep Left | stride, while Running and grounded | `Hr_run_leftFoot #20806` |
| Footstep Right | stride, while Running and grounded | `Hr_run_rightFoot #20814` |
| Hit | one per logical obstacle contact | `Hr_H_crash #20828` |
| Coin | one per logical coin contact | `Hr_coin #20807` |
| Fail | run failure | `Hr_death #20836` |
| Reset Complete | reset finished | `Hr_intro_gameStart #20802` |

Every slot is optional; an empty slot is silent.

## How each sound is triggered

Most sounds come from `PlayerStateChangedEvent`, whose `TransitionCause` says exactly what happened —
`JumpRequested`, `Landed`, `SlideRequested`, `SlideRestored`, `FailureRequested`. Hits and coins come from
`PlayerHitEvent` and `CoinCollectedEvent`. Each of those publishes exactly once per occurrence, so each
sound plays once and needs no de-duplication here.

Two sounds have no event and are read from the query surface instead:

- **Footsteps.** No footstep event exists, and the source audio is an alternating left/right pair rather
  than a loop. Cadence is derived from `ForwardSpeed`, in steps per metre, so a speed change is audible
  without extra wiring. Steps play only while Running and grounded.
- **Lane change.** A lane change keeps the player in Running, so no transition is published. The
  director observes `Snapshot.TargetLane`; a change of target is exactly one accepted lane movement.
  Boundary requests leave the target unchanged and stay silent, which is correct.

Both are read-only observations of public queries. Neither requires a change to the Player Controller.

## Note on the source clips

The imported files carry the original game's internal naming, so they are very likely ripped assets
rather than licensed or original audio. Fine for a local prototype; worth replacing before the
repository is made public or the build is distributed.
