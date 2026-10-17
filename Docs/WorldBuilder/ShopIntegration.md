# Main shop — integration batch, milestone 4 of 4

Unity: **6000.6.4f1**. Branch: **feature/world-level-integration**. PR/merge base: **develop**, not main.

## Saved integration

Main now contains one connected S2 `P_Shop` prefab at the scene root. Its existing ShopManager, ShopCanvas, catalogue and UI bindings are retained without overrides. The player's existing CoinManager/inventory and GameManager are found by the shared systems; the existing Input System UI helper ensures one EventSystem in Play. No shared prefab, shop asset, runtime script or player starting balance was changed.

This is a between-wave screen, not a physical vendor. `ShopPosition_Reserved` remains a reserved placement marker; approaching it or pressing E there does not open this screen.

The supplied catalogue is Assault Rifle (40 coins), Sniper (75), RPG (100), Medkit (15), Shield (25). Weapon prices come from WeaponData; consumable prices come from ShopItem assets. Buttons show BUY / OWNED / FULL / NEED, and successful gun purchases equip the weapon immediately.

Visual preview at the current Full HD Game view confirmed all five rows, prices, NEED labels and Next wave button are visible. Existing content follow-up for S2: the RPG description still mentions "eagles", while the confirmed species are Gull, Pigeon, Crow and Hawk. Review that copy alongside the shared weapon/species design; this integration does not change species enums or rebalance weapons.

## Wave handoff

1. Agent Controller's production WaveManager calls `GameManager.BeginWave(n)` when a wave starts.
2. It calls `CompleteWave()` when that wave ends. Non-final completion enters Shopping, showing this shop and freeing the cursor. Final-wave completion follows the existing Victory path instead.
3. The shop's **Next wave** button calls `CloseShop()`, returning to Playing and emitting `ShopClosed`.
4. WaveManager listens to `ShopClosed` and starts the next wave via `BeginWave(n+1)`. The button does not spawn enemies or increment the wave itself.

Main still needs production wave/bird integration. Normal Play does not automatically reach the shop yet; the editor smoke check supplies wave events temporarily, not a saved fake wave manager.

## Preview and focused verification

Open Main, press Play and let it load, then choose **Tools > Runway Panic > Preview Main Shop (Play Mode)**. This calls the normal `OpenShop` hook without granting coins. New sessions start with the player's original balance (currently zero), so NEED labels are expected. Use Next wave to close or stop Play to reset. The preview refuses to interrupt an active wave.

**Integrate Main Shop** installs a missing connected prefab and saves; repeat calls do not duplicate it, and unexpected/duplicate shops are refused.

**Run Main Shop Smoke Check** runs one short flow in Main. It invokes real UI button listeners (not simulated mouse clicks), the shared purchase APIs for refusal cases, and the real GameManager wave/pause hooks. Test-only coins and inventory filling are reset when it exits Play. `ShopValidation.txt` records **21 passed checks, zero failures**: one UI/controller and Input System EventSystem, five live catalogue rows, initially hidden shop, real wave-completion opening, cursor release, closed/poor/owned/full refusals without spending, gun grant/equip and exact charge, consumable delivery and exact charge, row labels/interactability, pause/resume, Next wave event and ability to begin wave 2 afterwards. The test acts as WaveManager only for those public calls; this does not verify enemy spawning, wave timing, every weapon purchase, every resolution or final-wave Victory. Other feature suites were not run.

## Manual commit and team handoff

Commit this milestone separately, preserving previous checkpoint, door and pickup commits:

```text
feat(level): integrate between-wave shop into airport scene
```

After reviewing/pushing, open a PR from `feature/world-level-integration` into **develop**. Keep meaningful individual commits where repository policy allows; main remains the release branch. Ask teammates to pull develop after merge.

Tell Agent Controller: Main now contains the shared player/GameManager, active checkpoints, fitted shelter doors, pickup spawner and shop. Use the existing GameManager hooks above, not a second shop/player/manager or a parallel wave-state system. Tell S2 the prefab integration is complete; further shared-system fixes stay with its owner. No Gull AI, flight grid/species markers, movable barricades, safe-room protection or production waves were added in this batch.
