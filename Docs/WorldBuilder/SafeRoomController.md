# Timed shelter controller (B3)

Main now has two room controllers, each connected to its existing door and Crow `ShelterZone`. An independent small status display appears near either shelter during gameplay. The geometry, shared prefabs and S2's HUD remain unchanged.

The initial, unbalanced settings are 15 seconds of use followed by a 30-second cooldown. When the single player is inside the existing zone and the door is commanded closed, the timer starts. Briefly reopening the door does not reset the timer. Leaving or reaching the time limit starts cooldown and opens the door away from the player. During cooldown, each close command is immediately followed by one open command; the controller never locks the door. It relies on the shared Door remaining unlocked: another system locking it would prevent opening and needs coordination with S2.

Timers run only in the Playing state using scaled time, so pause and shop do not consume them. Death or a wave boundary ends active use and starts cooldown. An existing cooldown persists through death and wave boundaries. Game over, victory, main menu, disabling the component, or reloading the scene reset it to available. Occupancy is a single player position checked against the existing zone's box, not a count of overlapping colliders.

This supplies no invulnerability or healing: walls, roof and door must provide physical cover. Actual falling FOD coverage must still be checked in normal gameplay. The timer uses the door's commanded state (`IsOpen`), not a measurement of the hinge's physical angle.

For future setup, assign the existing zone and door references before enabling the component (for example, configure it on an inactive object). Missing references disable the controller with a warning. Keep each zone's bounds inside its walls and leave the approved door geometry unchanged. The scene setup tool refuses to duplicate existing room controllers.

## Focused check

On 2026-10-18, `Tools > Runway Panic > Run Timed Shelter Smoke Check` passed 14 checks in Main. It verified both controllers were enabled and exercised Shelter A's occupancy, Crow zone, doorway capsule clearance, active timer, brief reopening, pause, timeout, cooldown, and early exit. It temporarily shortened the runtime timers to 3/2 seconds, disabled player input and wave spawning, and called the Door API; these changes were not saved. This is not a keyboard-E test, a visual HUD check, an actual 15/30-second timing test, a Shelter B lifecycle test, or a falling FOD test. Both saved scene controllers retain 15/30 seconds.
