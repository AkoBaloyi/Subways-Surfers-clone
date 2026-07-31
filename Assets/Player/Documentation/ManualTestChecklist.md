# Manual Test Checklist — Player Controller

Unity version required: **6000.5.4f1**. Scene: `Assets/Player/Scenes/PlayerTestScene.unity`.

**This checklist has been authored, not executed.** Every result cell below is empty on purpose. A
scenario counts as executed only when a real person runs it and records their name, the date, and what
they actually observed. Do not fill these in from an automated run, and do not infer a result from a
passing test suite. Authoring a checklist and running it are separate acts.

Automated coverage already exists for most of these behaviors. These scenarios exist for what a human
observer adds: that the behavior is visible, readable, and correct on screen, not merely correct in an
assertion.

---

## Common setup

Perform once per session, then once more before any scenario marked **fresh scene**.

1. Open the project in Unity 6000.5.4f1 and let import finish.
2. Confirm the Console shows no compile errors.
3. Open `Assets/Player/Scenes/PlayerTestScene.unity`.
4. Confirm the harness inspector shows the player reference bound and the configuration status reads `Valid`.
5. Enter Play mode.
6. Confirm the diagnostics display reads `status=Valid` with `diagnostics=0`.

Every scenario below assumes this setup is complete. Controls are the harness controls in the scene, not
production input bindings, except where a scenario names the Input System action explicitly.

## How to record a result

| Field | Meaning |
|---|---|
| Result | `Pass`, `Fail`, or `Blocked` |
| Tester | The real name of the person who ran it |
| Date | The date they ran it, `YYYY-MM-DD` |
| Observed | What actually appeared on screen, in their words |
| Defect | Issue reference if the result is `Fail` or `Blocked`, otherwise blank |

---

## MT-01 Automatic forward running

_Criteria: 2.1, 2.2, 2.5 — Requirement 12.15, 12.16_

| Step | Action | Expected observable result |
|---|---|---|
| 1 | Enter Play mode and touch nothing | The character advances along positive Z continuously without any input |
| 2 | Watch the result display | `state=Running`, `grounded=True`, `speed` equals the configured forward speed |
| 3 | Press the failure control | Forward motion stops completely and the character holds position |

| Result | Tester | Date | Observed | Defect |
|---|---|---|---|---|
|  |  |  |  |  |

## MT-02 Speed change and rejection

_Criteria: 2.3, 2.4, 2.6, 2.7, 2.8 — Requirement 12.15, 12.16_

| Step | Action | Expected observable result |
|---|---|---|
| 1 | Set the supplied speed to a higher finite value and press the speed control | Result display shows `Accepted` with the supplied value; the character visibly runs faster |
| 2 | Set the supplied speed to `-1` and press the speed control | Result display shows `Rejected`; the character keeps the previous speed with no stutter or reversal |
| 3 | Set the supplied speed to `0` and press the speed control | Result shows `Accepted`; the character stops advancing while remaining in `Running` |

| Result | Tester | Date | Observed | Defect |
|---|---|---|---|---|
|  |  |  |  |  |

## MT-03 Lane boundaries

_Criteria: 3.1, 3.3, 3.4, 3.5, 3.9 — Requirement 12.16 lane-boundary scenario_

| Step | Action | Expected observable result |
|---|---|---|
| 1 | Press lane left once | The character moves smoothly to the left lane and settles on its center |
| 2 | Press lane left three more times | The character stays in the left lane. No error, no stutter, no queued movement fires later |
| 3 | Press lane right four times | The character moves left to center to right in order, then stays in the right lane |

| Result | Tester | Date | Observed | Defect |
|---|---|---|---|---|
|  |  |  |  |  |

## MT-04 Lane request ordering

_Criteria: 3.1, 3.2, 3.6, 3.7, 3.8 — Requirement 12.16 request-order scenario_

| Step | Action | Expected observable result |
|---|---|---|
| 1 | From center, press lane left then lane right rapidly | The character completes the move to the left lane first, then returns to center. Both movements happen, in that order |
| 2 | Watch the character throughout | It never overshoots a lane center, never crosses past a target lane, and never ends between lanes |
| 3 | Press lane left twice rapidly | The character moves center to left, then left stays left as the second request resolves as a boundary no-op |

| Result | Tester | Date | Observed | Defect |
|---|---|---|---|---|
|  |  |  |  |  |

## MT-05 Jump over an obstacle

_Criteria: 4.3, 4.4, 4.6, 4.8, 4.9 — Requirement 12.15, 12.16_

| Step | Action | Expected observable result |
|---|---|---|
| 1 | Steer into the lane containing `JumpObstacle` and press jump before reaching it | The character rises, clears the obstacle, and lands |
| 2 | Watch the result display during the jump | `state=Jumping`, then `state=Running` on landing |
| 3 | Press jump repeatedly while airborne | Nothing happens. No second jump, no mid-air boost, no jump firing on landing |
| 4 | Press lane left while airborne | The lane change happens in mid-air and forward motion continues |

| Result | Tester | Date | Observed | Defect |
|---|---|---|---|---|
|  |  |  |  |  |

## MT-06 Slide under an obstruction

_Criteria: 5.1, 5.2, 5.3, 5.4, 5.5, 5.13 — Requirement 12.15, 12.16_

| Step | Action | Expected observable result |
|---|---|---|
| 1 | Steer into the lane containing the raised obstruction and press slide before reaching it | The character drops low and passes under it without contact |
| 2 | Watch the result display | `state=Sliding`, then `state=Running` once the slide ends |
| 3 | Press slide repeatedly while sliding | The slide does not extend and ends at its configured duration |
| 4 | Press jump while sliding | Nothing happens |

| Result | Tester | Date | Observed | Defect |
|---|---|---|---|---|
|  |  |  |  |  |

## MT-07 Blocked restoration under the ceiling

_Criteria: 5.6, 5.7, 5.8, 5.9, 5.10 — Requirement 12.16 overhead-obstruction scenario_

**Fresh scene.** This is the scenario most worth a human eye, because the correct behavior looks like the
character choosing to stay down.

| Step | Action | Expected observable result |
|---|---|---|
| 1 | Steer into the lane containing the overhead ceiling fixture and press slide so the character is under it when the slide duration expires | The character stays low past the end of the slide duration instead of standing into the ceiling |
| 2 | Watch the result display while under the ceiling | `state=Sliding` persists; the character keeps moving forward |
| 3 | Keep watching as the character clears the ceiling | The character stands up at the first moment the space above is clear, and the state returns to `Running` |
| 4 | Confirm the character never intersects the ceiling geometry | No clipping through or popping into the ceiling at any point |

| Result | Tester | Date | Observed | Defect |
|---|---|---|---|---|
|  |  |  |  |  |

## MT-08 Invalid actions preserve state

_Criteria: 6.9-6.13, 6.15-6.18 — Requirement 12.16 invalid-action scenario_

| Step | Action | Expected observable result |
|---|---|---|
| 1 | Press the failure control, then press lane left, lane right, jump, slide, and failure again | Every control reports `Rejected`. The character does not move at all: no drift, no sag, no lane shift |
| 2 | Note the character's pose before and after step 1 | The pose is identical. Nothing accumulated |
| 3 | Check the event log | No new state-changed entries appeared for the rejected commands |

| Result | Tester | Date | Observed | Defect |
|---|---|---|---|---|
|  |  |  |  |  |

## MT-09 Failure

_Criteria: 6.5, 6.9, 7.7, 8.1 — Requirement 12.16 failure scenario_

| Step | Action | Expected observable result |
|---|---|---|
| 1 | While running, press the failure control | The character stops immediately and holds its pose |
| 2 | Watch the result display | `state=Failed` |
| 3 | Check the event log | Exactly one state-changed entry for the transition into `Failed`, with previous state, current state, and cause |
| 4 | Watch the camera | The camera keeps following and the character stays framed |

| Result | Tester | Date | Observed | Defect |
|---|---|---|---|---|
|  |  |  |  |  |

## MT-10 Repeated reset

_Criteria: 10.1-10.11, 11.12 — Requirement 12.16 repeated-reset scenario_

| Step | Action | Expected observable result |
|---|---|---|
| 1 | Note the character's start position and lane on entering Play mode | Center lane, at the configured start position |
| 2 | Run, change lanes, jump, then press the reset control with a fresh request id | The character returns to the start position, center lane, upright, and immediately resumes running |
| 3 | Repeat step 2 four more times, each with a different fresh request id | Every reset lands in the identical starting condition. No drift accumulates across repeats |
| 4 | Press reset again reusing a previously used request id | The result display shows `Rejected` with a duplicate reason, and nothing about the character changes |
| 5 | Check the event log across all resets | Each accepted reset produced one started and one completed entry; the rejected one produced none. Event ids never repeat |

| Result | Tester | Date | Observed | Defect |
|---|---|---|---|---|
|  |  |  |  |  |

## MT-11 Event payload completeness

_Criteria: 7.2, 7.5, 7.7, 7.10, 7.11, 11.10 — Requirement 12.16 event-payload scenario_

| Step | Action | Expected observable result |
|---|---|---|
| 1 | Run the character into the obstacle stand-in | The event log shows exactly one hit entry, carrying event id, contact id, environment object id, and contact position |
| 2 | Confirm the run continues | The character does not enter `Failed` from the hit alone |
| 3 | Run the character into the coin stand-in | Exactly one coin entry, carrying event id, contact id, environment object id, and the coin value |
| 4 | Confirm the coin is untouched | The coin is not removed, hidden, or moved by the contact |
| 5 | Reverse away from an object and touch it again | A second entry appears with a new contact id |
| 6 | Read down the whole log | Entries are in publication order and no event id repeats |

| Result | Tester | Date | Observed | Defect |
|---|---|---|---|---|
|  |  |  |  |  |

## MT-12 Camera convergence

_Criteria: 9.1-9.6, 9.9, 9.10 — Requirement 12.16 camera-convergence scenario_

| Step | Action | Expected observable result |
|---|---|---|
| 1 | Watch the camera while running straight | It trails at a steady relative view without jitter |
| 2 | Change lanes and watch the camera | It follows smoothly and does not overshoot or swing past the character before settling |
| 3 | Jump and watch the camera | It follows the vertical movement smoothly |
| 4 | Trigger failure and watch the camera | It continues following and settles with the character framed |
| 5 | Reset and watch the camera | It returns to the exact starting view with no residual drift |

| Result | Tester | Date | Observed | Defect |
|---|---|---|---|---|
|  |  |  |  |  |

## MT-13 Animation receiver failure isolation

_Criteria: 8.6, 8.7, 8.8, 8.9 — Requirement 12.16 animation-receiver failure scenario_

**Fresh scene.** The point of this scenario is that a broken animation layer must be invisible to gameplay.

| Step | Action | Expected observable result |
|---|---|---|
| 1 | Before entering Play mode, disable or clear the scene's animation receiver | The player's animation reference is absent |
| 2 | Enter Play mode and run, steer, jump, slide, fail, and reset | Every behavior works exactly as in the scenarios above. Movement, lanes, jump arc, slide, and reset are unaffected |
| 3 | Watch the diagnostics display | A categorized diagnostic appears per attempted animation command, naming the receiver as absent |
| 4 | Restore the receiver and re-enter Play mode | Animations resume; no queued commands replay in a burst |

| Result | Tester | Date | Observed | Defect |
|---|---|---|---|---|
|  |  |  |  |  |

## MT-14 Configuration fallback and diagnostics

_Criteria: 15.1-15.5, 15.7, 15.8 — Requirement 12.16 prefab-fallback scenario_

**Fresh scene.** Restore the configuration asset afterwards.

| Step | Action | Expected observable result |
|---|---|---|
| 1 | In `Assets/Player/Config/PlayerConfiguration.asset`, set `forwardSpeed` to `-5` | The field holds an invalid value |
| 2 | Enter Play mode | The character still runs. The documented safe default is used in place of the invalid value |
| 3 | Watch the diagnostics display | Exactly one diagnostic for `forwardSpeed`, naming the field, the constraint, and the fallback applied. No diagnostics for fields that are still valid |
| 4 | Set `slideHeight` smaller than twice `slideRadius` and re-enter Play mode | One diagnostic attributed to the dependent field only, and sliding still works |
| 5 | Restore the original values and re-enter Play mode | `status=Valid` with `diagnostics=0` |

| Result | Tester | Date | Observed | Defect |
|---|---|---|---|---|
|  |  |  |  |  |

## MT-15 Input System bindings

_Criteria: 3.2, 4.3, 5.1, 11.8, 11.9 — Requirement 12.15_

| Step | Action | Expected observable result |
|---|---|---|
| 1 | Using the keyboard bindings of `Player/Move`, steer left and right | One lane change per actuation. Holding the key does not repeat until it returns to neutral |
| 2 | Using `Player/Jump`, jump | One jump per press |
| 3 | Using `Player/Crouch`, slide | One slide per press |
| 4 | Confirm the input action asset is unmodified | `Assets/InputSystem_Actions.inputactions` shows no pending changes in version control |

| Result | Tester | Date | Observed | Defect |
|---|---|---|---|---|
|  |  |  |  |  |

## MT-16 Isolation from production systems

_Criteria: 11.11, 11.13, 12.13 — Requirement 12.15_

| Step | Action | Expected observable result |
|---|---|---|
| 1 | Inspect the scene hierarchy | No game manager, score, HUD, user interface, track generator, or environment generation component is present |
| 2 | Inspect each environment stand-in | Each declares a non-production notice and lives under `Assets/Player` |
| 3 | Run every scenario above | All behavior is observable with no production system present |

| Result | Tester | Date | Observed | Defect |
|---|---|---|---|---|
|  |  |  |  |  |

---

## Session summary

Fill in only after executing the scenarios above.

| Field | Value |
|---|---|
| Unity version | 6000.5.4f1 |
| Tester name | _not executed_ |
| Date | _not executed_ |
| Scenarios passed | _not executed_ |
| Scenarios failed | _not executed_ |
| Scenarios blocked | _not executed_ |
| Defect references | _not executed_ |

Record the completed summary in `DevelopmentLog.md` under Validation Results, including the tester name,
the date, the Unity version, and the evidence location. Criterion 12.17 is satisfied only by that record,
and only when a real person produced it.
