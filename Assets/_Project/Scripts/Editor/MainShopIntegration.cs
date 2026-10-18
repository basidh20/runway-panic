using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunwayPanic.Core;
using RunwayPanic.Data;
using RunwayPanic.Player;
using RunwayPanic.UI;
using RunwayPanic.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RunwayPanic.EditorTools
{
    /// <summary>Places the shared shop unchanged. Optional smoke flow uses its real UI button listeners.</summary>
    [InitializeOnLoad]
    public static class MainShopIntegration
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Core/P_Shop.prefab";
        private const string SessionKey = "RunwayPanic.MainShopSmoke";
        private const string ReportPath = "Docs/WorldBuilder/ShopValidation.txt";
        private static readonly List<string> Report = new List<string>();
        private static ShopManager shop;
        private static GameManager game;
        private static CoinManager coins;
        private static PlayerInventory inventory;
        private static Transform panel;
        private static ShopItem gun, medkit;
        private static bool running;
        private static int phase, failures, closedEvents;
        private static double phaseStarted, began;

        static MainShopIntegration() { EditorApplication.playModeStateChanged += OnPlayMode; }

        [MenuItem("Tools/Runway Panic/Integrate Main Shop")]
        public static void Integrate()
        {
            RequireMain();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var existing = MainPlayerIntegration.InScene<ShopManager>(true);
            if (prefab == null || existing.Length > 1 ||
                (existing.Length == 1 && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(existing[0].gameObject) != PrefabPath))
                throw new InvalidOperationException("Expected the supplied shop prefab and no duplicate/unexpected shop.");
            if (existing.Length == 0)
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene());
                Undo.RegisterCreatedObjectUndo(root, "Install shared between-wave shop");
            }
            Undo.FlushUndoRecordObjects();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new IOException("Could not save Main.");
            Debug.Log("[MainShop] Installed one connected P_Shop; opens after a non-final wave completes. Shared assets unchanged.");
        }

        [MenuItem("Tools/Runway Panic/Run Main Shop Smoke Check")]
        public static void RunSmokeCheck()
        {
            RequireMain();
            if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save Main first.");
            SessionState.SetBool(SessionKey, true);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Tools/Runway Panic/Preview Main Shop (Play Mode)")]
        public static void Preview()
        {
            if (!EditorApplication.isPlaying || SceneManager.GetActiveScene().path != MainPlayerIntegration.ScenePath ||
                GameManager.Instance == null || ShopManager.Instance == null)
                throw new InvalidOperationException("Enter Play in Main and let it finish loading first.");
            if (!GameManager.Instance.IsShopOpen && !GameManager.Instance.OpenShop())
                throw new InvalidOperationException("Cannot preview during an active wave or outside Playing.");
            Debug.Log("[MainShop] Editor-only preview opened through the normal shop hook. No coins granted; stop Play to reset.");
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(SessionKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                phase = failures = closedEvents = 0; running = true;
                began = phaseStarted = EditorApplication.timeSinceStartup;
                Report.Clear();
                Report.Add("Main shop — one focused integration flow; Unity " + Application.unityVersion);
                Report.Add("Uses real GameManager wave/pause hooks and shop Button.onClick listeners, not simulated mouse input.");
                Report.Add("Test coins, inventory filling and wave completions exist only in this opt-in Play session.");
                EditorApplication.update += Tick;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode && running)
            { Check(false, "Flow interrupted"); Finish(false); }
            else if (state == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(SessionKey, false);
        }

        private static void Tick()
        {
            if (!running || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
            try
            {
                double elapsed = EditorApplication.timeSinceStartup - phaseStarted;
                if (EditorApplication.timeSinceStartup - began > 25) throw new TimeoutException("Shop flow exceeded 25 seconds.");
                if (phase == 0 && elapsed > 2)
                {
                    shop = MainPlayerIntegration.InScene<ShopManager>().Single();
                    Check(MainPlayerIntegration.InScene<ShopPanelUI>().Length == 1, "Exactly one active shop controller and UI");
                    game = GameManager.Instance; coins = CoinManager.Instance;
                    inventory = coins.GetComponent<PlayerInventory>();
                    panel = shop.transform.Find("ShopCanvas/Panel");
                    gun = shop.Items.First(item => item.ItemKind == ShopItem.Kind.Weapon && !inventory.OwnsWeapon(item.WeaponPrefab));
                    medkit = shop.Items.Single(item => item.ItemKind == ShopItem.Kind.Item && item.Item == ItemType.Medkit);
                    Check(shop.Items.Length == 5 && shop.Items.All(item => item != null), "All five supplied catalogue items connected");
                    Check(!panel.gameObject.activeInHierarchy && game.CurrentState == GameState.Playing, "Shop stays hidden during ordinary gameplay");
                    Refused(gun, ShopManager.PurchaseResult.ShopClosed, "Purchases refused while closed");
                    Check(game.BeginWave(1) && game.CompleteWave() && game.IsShopOpen && panel.gameObject.activeInHierarchy,
                        "Completing real wave 1 opens the between-wave shop");
                    Check(Cursor.lockState == CursorLockMode.None && Cursor.visible, "Shopping frees and shows cursor");
                    var systems = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude);
                    Check(systems.Length == 1 && systems[0].GetComponent<InputSystemUIInputModule>() != null,
                        "One EventSystem with the Input System UI module");
                    Check(panel.Find("Rows").Cast<Transform>().Count(row => row.gameObject.activeSelf) == 5,
                        "Five live item rows built, template excluded");
                    coins.TrySpend(coins.Coins);
                    Refused(gun, ShopManager.PurchaseResult.NotEnoughCoins, "Insufficient funds refused without spending");
                    Check(!BuyButton(gun).interactable && BuyButton(gun).GetComponentInChildren<Text>().text.StartsWith("NEED"),
                        "Poor-player row displays NEED and disables Buy");
                    coins.AddCoins(200);
                    Check(BuyButton(gun).interactable, "Buy becomes available when coins change");
                    game.ShopClosed += OnClosed;
                    Next(); // Leave the real screen visible briefly for inspection.
                }
                else if (phase == 1 && elapsed > 8)
                {
                    int balance = coins.Coins;
                    BuyButton(gun).onClick.Invoke();
                    var weapons = inventory.GetComponent<WeaponSwitcher>();
                    Check(coins.Coins == balance - gun.Price && inventory.OwnsWeapon(gun.WeaponPrefab) &&
                        weapons.OwnedWeapons[weapons.CurrentIndex] == gun.WeaponPrefab, "Buy row charges exact gun price, grants and equips gun");
                    Check(!BuyButton(gun).interactable && BuyButton(gun).GetComponentInChildren<Text>().text == "OWNED",
                        "Owned gun row cannot be bought again");
                    Refused(gun, ShopManager.PurchaseResult.AlreadyOwned, "Duplicate weapon refused without spending");
                    balance = coins.Coins;
                    int count = inventory.GetCount(ItemType.Medkit);
                    BuyButton(medkit).onClick.Invoke();
                    Check(coins.Coins == balance - medkit.Price && inventory.GetCount(ItemType.Medkit) == count + medkit.Amount,
                        "Consumable Buy row charges exact price and delivers item");
                    inventory.AddItem(ItemType.Medkit, inventory.GetMaxCount(ItemType.Medkit));
                    // Adding inventory directly is test setup; coin event asks the existing UI to refresh.
                    coins.AddCoins(1);
                    Refused(medkit, ShopManager.PurchaseResult.InventoryFull, "Full inventory refused without spending");
                    Check(!BuyButton(medkit).interactable && BuyButton(medkit).GetComponentInChildren<Text>().text == "FULL",
                        "Full consumable row disables Buy and shows FULL");
                    game.Pause();
                    Check(game.IsPaused && !panel.gameObject.activeInHierarchy, "Pause hides shop");
                    game.Resume();
                    Check(game.IsShopOpen && panel.gameObject.activeInHierarchy, "Resume restores shopping screen");
                    Next();
                }
                else if (phase == 2 && elapsed > .5)
                {
                    panel.Find("NextWaveButton").GetComponent<Button>().onClick.Invoke();
                    Check(closedEvents == 1 && game.CurrentState == GameState.Playing && !panel.gameObject.activeInHierarchy,
                        "Next wave UI button closes shop and emits one ShopClosed handoff");
                    Check(game.BeginWave(2) && game.CurrentWave == 2 && game.IsWaveActive,
                        "Wave 2 can start through public hook after shop closes (test acts as WaveManager)");
                    Finish();
                }
            }
            catch (Exception exception) { Check(false, exception.ToString()); Finish(); }
        }

        private static Button BuyButton(ShopItem item) => panel.Find("Rows/Row_" + item.name + "/BuyButton").GetComponent<Button>();
        private static void Refused(ShopItem item, ShopManager.PurchaseResult expected, string message)
        { int before = coins.Coins; Check(shop.TryBuy(item) == expected && coins.Coins == before, message); }
        private static void OnClosed() { closedEvents++; }
        private static void Next() { phase++; phaseStarted = EditorApplication.timeSinceStartup; }
        private static void Check(bool ok, string message) { Report.Add((ok ? "PASS: " : "FAIL: ") + message); if (!ok) failures++; }
        private static void Finish(bool exitPlay = true)
        {
            running = false; EditorApplication.update -= Tick;
            if (game != null) game.ShopClosed -= OnClosed;
            Report.Add("Failures: " + failures);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)); File.WriteAllLines(ReportPath, Report);
            Debug.Log("[MainShop] Flow finished. Failures: " + failures + "; " + ReportPath);
            if (exitPlay) EditorApplication.isPlaying = false;
        }
        private static void RequireMain()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != MainPlayerIntegration.ScenePath)
                throw new InvalidOperationException("Open Main outside Play mode first.");
        }
    }
}
