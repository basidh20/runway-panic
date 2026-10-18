# Gull — small autonomous accuracy sample

Status: Completed all six attempts; overlay counters matched every resolved impact

Unity 6000.6.4f1; Mac OS X 15.7.7; Apple M1.
Editor sandbox, one actual Gull per attempt, natural FOD collection/kettle/telegraph/drop. No forced attack state or fabricated drops.
Three starts per condition: (-8.5,3,-10), (8.5,3,10), (-8.5,3,10). Player resets to (0,0.12,0) and full HP between attempts.
Standing versus scripted +Z walking commanded at 2 m/s from attack approach until impact. Movement is driven once per observed Play frame from the Editor; measured hub speed at the warning is recorded below. Not a human-input test or guaranteed frame-perfect locomotion.
No deliberate shooting, shield activation or shelter use. One piece per attempt; debris/supply/kettle state and debug counters reset between attempts. Other bird prefabs/species/balance and saved scenes unchanged.

| Attempt | Condition | Outcome | HP hit | HP loss | First-impact-to-player (m) | Hub speed at warning (m/s) | Warning to release (s) |
|---|---|---|---|---:|---:|---:|---:|
| 1 | Standing | Resolved | Yes | 6.00 | 0.44 | 0.00 | 0.61 |
| 2 | Standing | Resolved | Yes | 6.00 | 0.45 | 0.00 | 0.61 |
| 3 | Standing | Resolved | Yes | 6.00 | 0.45 | 0.00 | 0.61 |
| 4 | Straight walk | Resolved | Yes | 6.00 | 0.44 | 2.00 | 0.61 |
| 5 | Straight walk | Resolved | Yes | 6.00 | 1.44 | 1.38 | 0.61 |
| 6 | Straight walk | Resolved | Yes | 6.00 | 0.51 | 1.98 | 0.61 |

Standing: 3 attempts; 3 resolved impacts; 3 HP hits; resolved-impact hit rate 100.00%; average impact-to-player distance 0.44 m. Timeouts are not silently counted as resolved misses.
Straight walking: 3 attempts; 3 resolved impacts; 3 HP hits; resolved-impact hit rate 100.00%; average impact-to-player distance 0.80 m. Timeouts are not silently counted as resolved misses.

Only three attempts per condition: descriptive functional evidence, not a statistically reliable hit rate or final wave balance. No eight-Gull Profiler measurement is made here.
