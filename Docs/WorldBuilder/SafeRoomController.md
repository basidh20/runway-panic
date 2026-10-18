# Timed shelter controller (B3)

This component is not yet connected to the Main scene. B4 will connect the two existing doors and Crow `ShelterZone` objects, add status feedback, and play-test the complete feature.

The initial, unbalanced settings are 15 seconds of use followed by a 30-second cooldown. When the single player is inside the existing zone and the door is commanded closed, the timer starts. Briefly reopening the door does not reset the timer. Leaving or reaching the time limit starts cooldown and opens the door away from the player. During cooldown, each close command is immediately followed by one open command; the controller never locks the door. It relies on the shared Door remaining unlocked: another system locking it would prevent opening and needs coordination with S2.

Timers run only in the Playing state using scaled time, so pause and shop do not consume them. Death or a wave boundary ends active use and starts cooldown. An existing cooldown persists through death and wave boundaries. Game over, victory, main menu, disabling the component, or reloading the scene reset it to available. Occupancy is a single player position checked against the existing zone's box, not a count of overlapping colliders.

This supplies no invulnerability or healing: walls, roof and door must provide physical cover. B4 must verify FOD coverage, Crow awareness, passability, and the full timer lifecycle. The timer uses the door's commanded state (`IsOpen`), not a measurement of the hinge's physical angle.

For B4, assign the existing zone and door references before enabling the component (for example, configure it on an inactive object). Missing references disable the controller with a warning. Keep each zone's bounds inside its walls and leave the approved door geometry unchanged.
