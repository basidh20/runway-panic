# Timed shelters — handoff to S2

Main contains two World Builder-owned controllers and an independent status display. Each controller references S2's existing shelter Door and the Crow owner's existing ShelterZone. No shared prefab, player health, camera, wave setup or S2 HUD was changed.

Starting balance is 15 seconds of shelter use followed by a 30-second cooldown, not final playtested balance. Closing a door while inside starts the timer; leaving or timeout starts cooldown and opens it away from the player. During cooldown, a close command is immediately reversed. The doors are never locked by the controller; please avoid externally locking a shelter door because the shared Door API cannot open a locked door.

Timers stop in pause and shop, and cooldown continues across death and wave boundaries. No invulnerability or healing is added. Please coordinate if Door events, game states, or the player setup change. Final FOD roof/door coverage and normal four-wave play still need team playtesting.

For setup and reset details, see `Docs/WorldBuilder/SafeRoomController.md`.
