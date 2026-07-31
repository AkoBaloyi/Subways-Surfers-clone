# Game Design Document — Player Controller Section

Scope: the player-facing behavior of the Player Controller for the endless runner. Every behavior and
boundary below carries the acceptance criterion identifiers it maps to, in `R.C` form, matching
`.kiro/specs/player-controller/requirements.md`.

This section describes intended behavior from the player's perspective. Component structure, contracts,
and algorithms are in `TechnicalDesignDocument.md`.

---

## 1. Automatic forward running

The player runs forward on their own. There is no accelerate control and no stop control: from the
moment a run begins, the character advances along positive Z and keeps advancing until the run ends in
failure or is reset. Forward motion is what makes the game a runner rather than a walker, so it is
never something the player has to hold down.

Forward motion continues while jumping and while sliding. Leaving the ground or dropping into a slide
never costs forward progress, so an evasive move is never punished by falling behind.

Forward motion stops completely in exactly two situations: after a failure, and during a reset. In both
the character holds position rather than drifting.

_Criteria: 2.1, 2.2, 2.5_

## 2. Run speed

Run speed is a configurable value rather than a fixed constant, and it can be changed while a run is in
progress. Raising it is how difficulty ramps as a run goes on. The change takes effect on the next
simulation step, so the player sees speed respond immediately rather than at some later checkpoint.

Speed is owned as a number the run coordinator sets. The Player Controller accepts any finite,
non-negative value and imposes no arbitrary ceiling, so pacing and difficulty curves stay a design
decision rather than a hard-coded limit. A nonsensical value, such as a negative speed, is refused and
the previous speed continues to apply, so a bad input can never stall or reverse the character.

_Criteria: 2.3, 2.4, 2.6, 2.7, 2.8_

## 3. Three-lane movement

The track has three lanes: left, center, and right. The player starts centered. A steer input moves the
character one lane toward that side, and the character slides smoothly across to the new lane rather
than snapping, so the movement reads as a dodge.

Requests are honored in the order they arrive. Two quick taps in the same direction move two lanes in
sequence, not simultaneously, and a tap in each direction moves out and back. The character never
overshoots a lane or drifts past it, and never ends a movement between lanes.

Steering toward a wall the player is already against is accepted rather than rejected, and simply
resolves as staying put. This matters for feel: at the outer lanes, mashing the steer control does not
produce an error state, a stutter, or a queue of movements that fire later and pull the player off the
lane they wanted. It resolves instantly and costs nothing.

Lane changes continue during a jump and during a slide, so the player can dodge sideways mid-air or
mid-slide.

_Criteria: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9_

## 4. Jumping

A jump lifts the character off the ground to clear low obstacles. It is available only while running on
the ground. A jump input while already airborne is refused and does nothing at all: no double jump, no
mid-air boost, no queued second jump that fires on landing. The character rises, slows, falls, and
returns to running the moment they touch down on a valid surface.

Jump height and fall speed are configured values, so the arc is tunable for the game's feel without
changing behavior rules.

_Criteria: 4.3, 4.4, 4.5, 4.6, 4.7, 4.8, 4.9_

## 5. Sliding

A slide drops the character low to pass under raised obstacles. Like the jump it is available only while
running on the ground, and it lasts a configured duration. While sliding, the character's collision
shape shrinks so the low gap can actually be cleared.

Repeating the slide input while already sliding does nothing and, importantly, does not extend the
slide. A slide is a committed move with a known length, not something the player can hold indefinitely.

When the slide's time is up, the character stands only if there is room. Under a low ceiling the
character stays sliding, keeps moving forward, and stands up at the first moment the space above is
actually clear. The player is never forced up into an obstacle, and never gets stuck in a slide once the
obstruction has passed.

_Criteria: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9, 5.10, 5.11, 5.12, 5.13_

## 6. Hitting an obstacle and collecting coins

Contact with an obstacle is reported as an event the moment it happens, once per obstacle. Contact with
a coin is likewise reported once, carrying the coin's value.

Two deliberate design decisions here:

Touching an obstacle does not by itself end the run. The Player Controller reports the hit; deciding
whether that hit is fatal, costs a life, or is shrugged off by a power-up belongs to the run
coordinator. This keeps the door open for shields, revives, and difficulty settings without changing
player movement.

Collecting a coin does not by itself change the score, and the Player Controller never removes, moves,
or hides the coin. It reports the contact and its value, and the systems that own scoring and the world
act on it. A multi-part object reports one contact, not one per piece it is built from, so brushing a
wide obstacle is a single hit rather than a burst.

Re-touching the same object after fully separating from it reports a new contact, so repeated passes
through a hazard behave as the player would expect.

_Criteria: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6_

## 7. Failure

Failure is a state the run coordinator triggers, not something the Player Controller decides. On
failure the character stops dead: no forward motion, no lane movement, no falling, no queued moves
firing afterwards. The pose is preserved so a failure can be shown, photographed, or replayed without
the character sliding out of frame. Steering, jumping, and sliding are all refused while failed. The
only way out is a reset.

_Criteria: 6.5, 6.9, 6.10, 6.11, 6.12, 6.13_

## 8. Reset

A reset returns the player to the exact starting condition of a run: start position, center lane,
configured speed, upright collision shape, no leftover velocity, no queued moves, no in-progress slide,
and the camera back at its starting view. It works from running, jumping, sliding, and from failure,
which is what makes retry-after-failure possible.

Reset finishes before the next simulation step, so the player never sees a partial reset, a frame of the
old pose, or motion carrying over from the previous run. Resetting repeatedly produces the same clean
starting condition every time, with no accumulated drift, which is what makes rapid retry feel
identical to a first attempt.

_Criteria: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7, 10.8, 10.9, 10.10, 10.11_

## 9. Camera

The camera trails the character at a fixed relative view established at the start of the run. It follows
smoothly rather than rigidly, so lane changes and jumps read as motion instead of a snap, and it never
overshoots or oscillates past the character. It catches up within a configured settling time.

The camera keeps following after a failure, so the moment stays on screen, and it returns to its exact
starting view on reset.

_Criteria: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7, 9.8, 9.9, 9.10_

## 10. Animation

Each of the character's states maps to one animation: running, jumping, sliding, the failure reaction,
and the reset return. Animation is presentation only. It never drives movement, and if the animation
layer is missing, misconfigured, or broken, the character still runs, steers, jumps, slides, fails, and
resets exactly the same way. Visual problems never become gameplay problems.

_Criteria: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 8.8, 8.9_

---

## Ownership boundaries

Three systems are built in parallel. The boundaries below are design constraints, not just code
structure, and they determine what this section is allowed to describe.

### Rachel's Game Manager owns

Global game state and run coordination; score and currency totals, including applying the value of a
collected coin; all user interface and HUD; and the run lifecycle of starting, handling failure,
retrying, and progression.

The Player Controller exposes failure and reset commands, speed setting, state and grounded and speed
queries, and its events for these systems to consume. It never writes score, never draws interface, and
never decides that a run has ended.

_Criteria: 13.2, and the contracts at 14.1 through 14.10_

### Lucky's Environment System owns

Endless track generation; the running surfaces the character stands on; the raised obstructions a slide
passes under and that block standing up; and the placement, geometry, and lifecycle of obstacles and
coins.

The Player Controller reads these as marked world objects with stable identities. It never generates
track, never places or moves an object, and never destroys, deactivates, or collects one it touches.

_Criteria: 13.3, and the contracts at 14.11, 14.12_

---

## Explicitly out of scope

The following are deliberately not part of the Player Controller, and nothing in this section should be
read as specifying them:

- **Progression systems.** Levels, unlocks, ranks, and run-to-run advancement.
- **Shops and economy.** Currency spending, pricing, and purchasable items. The Player Controller reports
  a coin's value and nothing further.
- **Missions and objectives.** Goals, challenges, and their tracking or rewards.
- **Cosmetics.** Characters, skins, boards, and trails. The controller drives one configured visual
  child and issues animation commands; it owns no wardrobe.
- **Unrelated power-ups.** Magnets, multipliers, shields, revives, and boosts. Where a power-up would
  change whether a hit is fatal, that decision sits with the run coordinator, since the controller
  reports hits rather than resolving them.
- **Production balancing.** Final speed curves, difficulty ramps, obstacle density, spawn rates, and coin
  economy tuning. Speed, jump, gravity, and durations are exposed as configuration with documented safe
  defaults so balancing can happen elsewhere; the values shipped in the configuration asset are
  validation defaults, not balanced production values.

_Criteria: 13.4_

---

## Behavior to criterion map

Every behavior and boundary in this section, mapped for traceability. This satisfies 13.14.

| Section | Behavior or boundary | Criteria |
|---|---|---|
| 1 | Automatic forward running, continues in jump and slide, zero when failed or resetting | 2.1, 2.2, 2.5 |
| 2 | Configurable run speed, next-step effect, invalid value refused | 2.3, 2.4, 2.6, 2.7, 2.8 |
| 3 | Three lanes, ordered one-lane steering, smooth non-overshooting movement, boundary no-op | 3.1-3.9 |
| 4 | Grounded-only jump, single impulse, landing returns to running | 4.3-4.9 |
| 5 | Grounded-only slide, fixed duration, shrunken shape, no extension, stand at first safe moment | 5.1-5.13 |
| 6 | One hit event per obstacle, one coin event with value, no world mutation, re-entry reports again | 7.1-7.6 |
| 7 | Coordinator-triggered failure, motion stops, pose preserved, actions refused | 6.5, 6.9-6.13 |
| 8 | Reset to exact start condition from any state, completes before next step, repeat-safe | 10.1-10.11 |
| 9 | Trailing camera, smooth non-overshooting, settles in time, follows in failure, restored on reset | 9.1-9.10 |
| 10 | One animation per state, presentation only, failures never affect gameplay | 8.1-8.9 |
| Ownership | Rachel owns global state, interface, score, run coordination | 13.2 |
| Ownership | Lucky owns track generation, surfaces, obstructions, object placement | 13.3 |
| Out of scope | Progression, shops, missions, cosmetics, unrelated power-ups, production balancing | 13.4 |
| This table | Behavior to criterion mapping | 13.14 |
