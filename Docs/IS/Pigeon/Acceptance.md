# Pigeon acceptance check

Unity 6000.6.4f1. Real pigeon sandbox, prefab and shared motor. No scenes saved.
Editor infrastructure issue (excluded from runtime result): ArgumentOutOfRangeException: Index was out of range. Must be non-negative and less than the size of the collection.
Parameter name: index
```
System.Collections.Generic.List`1[T].get_Item (System.Int32 index) (at <c5ae8a4eeb824526b5df2932bc2395e0>:0)
UnityEditor.Search.SearchDatabase+<EnumerateAll>d__78.MoveNext () (at <76871fb05cc145ceba4a3eea6daeab4d>:0)
System.Linq.Enumerable.TryGetFirst[TSource] (System.Collections.Generic.IEnumerable`1[T] source, System.Boolean& found) (at <f1b392d8fe224ca2b1f9fab1000b77eb>:0)
System.Linq.Enumerable.First[TSource] (System.Collections.Generic.IEnumerable`1[T] source) (at <f1b392d8fe224ca2b1f9fab1000b77eb>:0)
UnityEditor.Search.SearchDatabase.GetDefaultSearchDatabase () (at <76871fb05cc145ceba4a3eea6daeab4d>:0)
UnityEditor.Search.SearchInit.IndexationOnStartup () (at <76871fb05cc145ceba4a3eea6daeab4d>:0)
UnityEditor.EditorApplication.Internal_CallDelayFunctions () (at <b5e1ac9fdc894216a0eeb18353904190>:0)
UnityEngine.StackTraceUtility:ExtractStringFromExceptionInternal(Object, String&, String&)

```
- PASS: Six spawned pigeons join one flock
- PASS: All six pigeons move more than two metres
- PASS: Reachable rally candidates selected through shared search
- PASS: Flock dispatches attacks
- PASS: Real contact attack damages the player
- PASS: Real damage events raise flock alarm and trigger scattering
- PASS: Panic releases attack reservations
- PASS: Panic expires and flock regroups
- PASS: Shop opens in sandbox
- PASS: Shop freezes flock decisions
- PASS: Shop stops pigeon motor after braking
- PASS: Death immediately releases flock membership
- PASS: Timeout-style despawn leaves no members or attack claims
- PASS: Empty flock returns to idle
- PASS: Flock respects configured capacity
- PASS: Overflow pigeon uses Solo fallback
- PASS: Wave-start lifecycle hook accepts a new wave
- PASS: New wave clears alarm and attack reservations
- PASS: Pause freezes decisions and physical movement
- PASS: Pigeons resume movement after pause
- PASS: Solo bird retries and fills a vacated flock slot
- PASS: Destination safety rejects an occupied point
- PASS: Swept direct-flight query rejects an intervening wall
- PASS: Shot perception uses closest point on the finite shot segment
- PASS: Capacity fixture cleans up all flock claims
- PASS: Last member death releases the final membership
- PASS: Revived and re-enabled pigeon rejoins without stale dead state
- PASS: Player death interrupts combat with roost/rest behavior
- PASS: Pigeon resumes tactics after player respawn
- PASS: Revive/respawn fixture releases all claims
- PASS: No runtime errors or exceptions during fixture

Runtime acceptance failures: 0. Separate Unity Editor indexing issues: 1. Dives: 36; hits: 33; path failures: 0.

Six-bird scoped brain + flock strategy CPU: mean 0.0325 ms; observed peak 5.7404 ms. Editor samples only; excludes motor/rendering/other species. Not a standalone or whole-frame certification.
