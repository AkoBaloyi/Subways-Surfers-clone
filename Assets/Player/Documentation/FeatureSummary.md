# Player Controller Feature Summary

Ako Baloyi

The Player Controller is designed for a Subway Surfers-style endless runner. The player automatically
moves forward while the user focuses on changing lanes, jumping and sliding to avoid obstacles and
collect items.

## Core player experience

- The player runs automatically in the positive Z direction.
- Forward speed can be configured and changed through a controlled system.
- The player can move between three lanes: Left, Centre and Right.
- Lane changes should feel smooth and predictable.
- The player cannot move beyond the outside lanes.
- The player can jump only when grounded.
- The player can slide only when grounded and not already performing another action.
- Forward movement continues while jumping, sliding or changing lanes.

## Player states

The controller uses five clear states:

- **Running:** Normal movement. The player can change lanes, jump or slide.
- **Jumping:** The player is airborne. Lane changes remain available, but another jump or slide is rejected.
- **Sliding:** The player uses a shorter collider for a limited time. Another jump or slide is rejected.
- **Failed:** Player movement and normal actions stop after a failure command.
- **Resetting:** The player is being returned to the starting setup, so other actions are temporarily blocked.

## Forward movement

- Forward movement is automatic.
- Movement speed is configurable.
- Valid speed changes are applied from the next movement update.
- Negative or invalid speed values are rejected without changing the previous speed.
- Automatic forward movement stops while the player is Failed or Resetting.

## Lane movement

- The game uses exactly three lanes.
- Each lane request moves the player by a maximum of one lane.
- Multiple lane requests are handled in the order they were received.
- Requests that point outside the track keep the player in the boundary lane.
- Movement between lanes should be smooth and should not overshoot the target lane.

## Jumping

- A jump can begin only while the player is Running and grounded.
- Jump force is applied once at take-off.
- Gravity brings the player back down.
- The player returns to Running after a valid landing.
- Repeated or mid-air jump requests do not affect the current jump.

## Sliding

- A slide can begin only while the player is Running and grounded.
- Sliding temporarily changes the player to a shorter collision shape.
- The slide lasts for a configured minimum duration.
- The normal collider is restored only when there is enough space above the player.
- If something blocks the player from standing, the slide continues until standing is safe.
- Repeated slide requests do not extend the slide timer.

## Failure and reset

- The Game Manager can tell the Player Controller when the run has failed.
- Failure stops movement and clears temporary actions.
- Reset returns the player to the configured starting position and state.
- Reset restores the centre lane, normal collider, starting speed and camera position.
- Reset requests use IDs to prevent the same request from being processed more than once.

## Obstacles and coins

- The Player Controller reports obstacle hits and coin contacts through events.
- It does not directly change the score or global game state.
- One continuous contact should produce only one event.
- The Game Manager decides what happens after an obstacle hit.
- The score system decides how collected coins affect the score.

## Camera

- The camera follows the player using a configurable offset.
- Camera movement should be smooth and should not overshoot its target.
- The camera continues following the player during running, lane changes, jumping, sliding and failure.
- Reset restores the camera to its starting position.

## Animation

Animations match the current player state:

- Running animation
- Jumping animation
- Sliding animation
- Failed animation
- Resetting animation or pose

Animation problems must not change movement or player state.

## Team responsibilities

- **Player Controller:** movement, lanes, jumping, sliding, player states, collider changes, camera follow, animation commands and player events.
- **Game Manager team:** score, user interface, run flow and decisions after player events.
- **Environment team:** track generation, ground surfaces, obstacles, coins and overhead obstructions.

---

## Current development status

All of the player behaviour described above has now been built and tested, and the controller has been
integrated into the shared gameplay scene alongside the environment and game manager work.

### Completed and tested

- Project structure, configuration validation with safe defaults, and forward movement with speed control.
- Three-lane movement with request ordering, smooth interpolation and boundary handling.
- Jumping with a single take-off impulse, gravity, and landing back into Running.
- Sliding with the shorter collider, minimum duration, and standing up only when the space above is clear.
- Obstacle and coin contact events, reported once per contact.
- The five player states and every allowed and rejected action between them.
- Camera follow, reset behaviour, the player prefab, and a dedicated test scene.

### Testing

- **Edit Mode:** 242 tests, 241 passing. The single failure is a traceability check that stays red on
  purpose until the last few acceptance criteria have human sign-off.
- **Play Mode:** 59 tests covering physics, grounding, sliding, contacts, camera and prefab loading.
- 17 correctness properties, each generating at least 100 random cases with a fixed seed so any failure
  can be replayed exactly.

### Integration

The player now runs on the environment team's track in the shared scene, with the camera following, the
track spawning driven by the player's position, and obstacle contacts reporting hits. All the code that
connects the three systems lives in a separate integration folder, so the Player Controller itself still
compiles and runs without referencing anyone else's scripts.

---

## Two problems worth writing down

### The two systems were built on different axes

My controller runs along positive Z with lanes spread along X. The environment team's track runs along X
with lanes spread along Z. Neither was wrong, we just never agreed on it.

I considered rotating the track, but their track manager spawns new pieces along X and decides when to
spawn by reading the player's X position, so rotating it would have broken the endless spawning. I also
considered changing my movement rules, but that would have broken the requirement they were written
against and all the tests with them.

The solution was to convert between the two at the single point where my movement reaches Unity. The
movement rules still work in their own directions, and a setting turns that into the track's direction.
The default setting changes nothing, so all existing tests still pass.

**What I learned:** when you add a conversion between two coordinate systems, you have to check
*everything* that crosses the boundary, not just the obvious thing. I converted the movement but forgot
that the lane logic also reads the player's position. It read forward distance as sideways distance and
tried to slide the player 76 units off the track. That one oversight cost me several hours of debugging.

### Tests can pass while the feature is broken

Every piece of my contact system had passing tests, but obstacle hits never fired in the real scene. The
tests each built their own copy of the contact tracker, so they proved every piece worked while nothing
checked that the game actually connected them. In the real scene the connection was missing entirely.

**What I learned:** testing pieces separately is not enough. You also need a test that builds the thing
the way the game builds it, otherwise you can have full coverage and a feature that has never once run.

---

## Still to do

### Needs another person

- The manual test checklist has been written, but it has to be run by someone and their name and the date
  recorded. Writing a checklist is not the same as doing it.
- Three reviews are outstanding: an independent review, a review of the contracts by the Game Manager
  team, and a review of the environment contracts by the Environment team.

### Needs the other teams

- The Game Manager is not in the gameplay scene yet, so score and the game-over screen do not react to
  hits. My side reports the hit correctly.
- The environment prefabs do not mark which surfaces can be run on, which things block standing up, or
  which objects are obstacles and coins. My integration code adds these at run time so the MVP plays, but
  it is a temporary measure and the prefabs should carry them properly.
- A few of the environment scripts are still empty files, so trains do not move and difficulty does not
  increase yet.

### On my side

- Re-run both test suites. I changed two pieces of controller code during integration and have not tested
  since, so I cannot claim they pass.
- The slide currently shows by squashing the character. It should be driven by a real slide animation
  through the animation system, which is already built and tested but has no animation attached.
- The configuration values are safe defaults for testing, not balanced gameplay values, so speed, jump
  height and slide duration will need tuning once we playtest properly.
