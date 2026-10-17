using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RunwayPanic.Agents;
using RunwayPanic.Core;
using RunwayPanic.Data;
using UnityEditor;
using UnityEngine;

namespace RunwayPanic.EditorTools
{
    /// <summary>One-time shared-data correction, with a focused asset/reference/damage-rule check.</summary>
    public static class BirdProgressionMigration
    {
        private const string Folder = "Assets/_Project/ScriptableObjects/";
        private const string GullGuid = "f4a2ede88e10cac40a20b9a1ef4d7d94";
        private static readonly string[] Birds = { "Gull", "Pigeon", "Crow", "Hawk" };
        private static readonly string[] Guns = { "Tier1_Pistol", "Tier2_AssaultRifle", "Tier3_Sniper", "Tier4_RPG" };
        private static readonly BirdSpecies[] Ids = { BirdSpecies.Gull, BirdSpecies.Pigeon, BirdSpecies.Crow, BirdSpecies.Hawk };
        private static readonly int[] StableIds = { 3, 0, 1, 2 };
        private static readonly float[] HP = { 30, 20, 60, 300 };
        private static readonly float[] Speeds = { 6, 4, 5.5f, 8 };
        private static readonly float[] Damage = { 6, 5, 8, 20 };
        private static readonly float[] Ranges = { 1.5f, 1.5f, 1.5f, 2 };
        private static readonly float[] Cooldowns = { 2, 1.5f, 1.2f, 2 };
        private static readonly int[] Coins = { 8, 10, 15, 25 };
        private static readonly int[] Hits = { 3, 2, 1, 2 };

        [MenuItem("Tools/Runway Panic/Apply Confirmed Bird Progression")]
        public static void Apply()
        {
            RequireEditMode();
            string oldPath = Folder + "Birds/BirdSpecies_Eagle.asset";
            string gullPath = BirdPath(0);
            string currentPath = AssetDatabase.GUIDToAssetPath(GullGuid);
            if (currentPath != oldPath && currentPath != gullPath)
                throw new InvalidOperationException("Original Gull/Eagle GUID is missing or at an unexpected path; no changes made.");
            if (currentPath == oldPath && AssetDatabase.LoadAssetAtPath<BirdSpeciesData>(gullPath) != null)
                throw new InvalidOperationException("Both Eagle and Gull assets exist; resolve before migrating.");
            // Preflight every dependency before moving or changing a shared asset.
            for (int i = 0; i < 4; i++)
                if (AssetDatabase.LoadAssetAtPath<BirdSpeciesData>(i == 0 ? currentPath : BirdPath(i)) == null ||
                    AssetDatabase.LoadAssetAtPath<WeaponData>(GunPath(i)) == null || Prefab(i) == null ||
                    (i > 0 && AssetDatabase.LoadAssetAtPath<ShopItem>(ShopPath(i)) == null))
                    throw new InvalidOperationException("Missing bird/weapon/prefab/shop dependency for " + Birds[i]);
            if (currentPath != gullPath)
            {
                string error = AssetDatabase.MoveAsset(currentPath, gullPath);
                if (!string.IsNullOrEmpty(error)) throw new IOException(error);
            }
            for (int i = 0; i < 4; i++)
            {
                var bird = AssetDatabase.LoadAssetAtPath<BirdSpeciesData>(BirdPath(i));
                Undo.RecordObject(bird, "Correct confirmed bird progression");
                bird.name = "BirdSpecies_" + Birds[i];
                var data = new SerializedObject(bird);
                data.FindProperty("speciesName").stringValue = Birds[i];
                // intValue is the explicit stored enum ID; enumValueIndex would be unsafe if declaration order changes.
                data.FindProperty("species").intValue = (int)Ids[i];
                data.FindProperty("prefab").objectReferenceValue = Prefab(i);
                data.FindProperty("maxHP").floatValue = HP[i];
                data.FindProperty("moveSpeed").floatValue = Speeds[i];
                data.FindProperty("attackDamage").floatValue = Damage[i];
                data.FindProperty("attackRange").floatValue = Ranges[i];
                data.FindProperty("attackCooldown").floatValue = Cooldowns[i];
                data.FindProperty("coinReward").intValue = Coins[i];
                data.FindProperty("requiredWeaponRarity").intValue = i;
                data.FindProperty("wrongWeaponDamageMultiplier").floatValue = .1f;
                data.ApplyModifiedProperties();
                EditorUtility.SetDirty(bird);
                var weapon = new SerializedObject(AssetDatabase.LoadAssetAtPath<WeaponData>(GunPath(i)));
                weapon.FindProperty("targetSpecies").intValue = (int)Ids[i];
                weapon.ApplyModifiedProperties();
            }
            string[] descriptions =
            {
                "Uncommon. Rapid fire. Full damage against pigeons.",
                "Rare. One well-aimed hit can defeat a crow.",
                "Legendary. Full damage against hawks; two direct hits at full health."
            };
            for (int i = 1; i < 4; i++)
            {
                var item = new SerializedObject(AssetDatabase.LoadAssetAtPath<ShopItem>(ShopPath(i)));
                item.FindProperty("description").stringValue = descriptions[i - 1];
                item.ApplyModifiedProperties();
            }
            AssetDatabase.SaveAssets();
            Validate();
            Debug.Log("[BirdProgression] Corrected all four species, weapon targets and shop copy. Original Gull GUID retained; no scene/brain changes.");
        }

        [MenuItem("Tools/Runway Panic/Validate Confirmed Bird Progression")]
        public static void Validate()
        {
            RequireEditMode();
            var lines = new List<string> { "Confirmed bird progression — focused saved-asset/reference/damage-rule check", "Unity: " + Application.unityVersion };
            int failures = 0;
            Action<bool, string> check = (ok, message) => { lines.Add((ok ? "PASS: " : "FAIL: ") + message); if (!ok) failures++; };
            check(AssetDatabase.AssetPathToGUID(BirdPath(0)) == GullGuid, "Renamed Gull asset retains the original GUID");
            check(!File.Exists(Folder + "Birds/BirdSpecies_Eagle.asset") && !Enum.GetNames(typeof(BirdSpecies)).Contains("Eagle"), "No active Eagle enum member or species asset");
            for (int i = 0; i < 4; i++)
            {
                var bird = AssetDatabase.LoadAssetAtPath<BirdSpeciesData>(BirdPath(i));
                var gun = AssetDatabase.LoadAssetAtPath<WeaponData>(GunPath(i));
                var prefab = Prefab(i);
                check((int)Ids[i] == StableIds[i] && bird.Species == Ids[i] && bird.SpeciesName == Birds[i], Birds[i] + " name and stable serialized ID " + StableIds[i]);
                check(bird.Prefab == prefab && prefab.GetComponent<BirdHealth>()?.Species == bird, Birds[i] + " species/prefab references resolve in both directions");
                check(bird.MaxHP == HP[i] && bird.MoveSpeed == Speeds[i] && bird.AttackDamage == Damage[i] && bird.AttackRange == Ranges[i] &&
                    bird.AttackCooldown == Cooldowns[i] && bird.CoinReward == Coins[i], Birds[i] + " baseline health/movement/attack/reward values saved");
                check((int)bird.RequiredWeaponRarity == i && gun.TargetSpecies == Ids[i] && (int)gun.Rarity == i,
                    Birds[i] + " matches " + gun.WeaponName + " in both bird and weapon assets");
                for (int j = 0; j < 4; j++)
                {
                    var otherGun = AssetDatabase.LoadAssetAtPath<WeaponData>(GunPath(j));
                    float expected = otherGun.Damage * (i == j ? 1 : .1f);
                    check(Mathf.Approximately(bird.GetDamageTaken(otherGun.Damage, otherGun.Rarity), expected), Birds[i] + " / " + otherGun.WeaponName + " damage = " + expected);
                }
                check(Mathf.CeilToInt(bird.MaxHP / bird.GetDamageTaken(gun.Damage, gun.Rarity)) == Hits[i], Birds[i] + " intended full-health direct-hit count = " + Hits[i]);
                if (i > 0)
                    check(!AssetDatabase.LoadAssetAtPath<ShopItem>(ShopPath(i)).Description.ToLowerInvariant().Contains("eagle"), gun.WeaponName + " shop copy uses confirmed species");
            }
            lines.Add("No full-game or AI behaviour tests. Rocket direct-hit damage only; splash/missed shots and timed-wave economy need later playtesting.");
            lines.Add("Failures: " + failures);
            Directory.CreateDirectory("Docs/WorldBuilder");
            File.WriteAllLines("Docs/WorldBuilder/BirdProgressionValidation.txt", lines);
            if (failures != 0) throw new InvalidOperationException("Bird progression check failed; see BirdProgressionValidation.txt.");
        }

        private static string BirdPath(int index) => Folder + "Birds/BirdSpecies_" + Birds[index] + ".asset";
        private static string GunPath(int index) => Folder + "Weapons/Weapon_" + Guns[index] + ".asset";
        private static string ShopPath(int weaponIndex) => Folder + "Shop/Shop_" + new[] { "1_AssaultRifle", "2_Sniper", "3_RPG" }[weaponIndex - 1] + ".asset";
        private static GameObject Prefab(int index) => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Birds/P_Bird_" + Birds[index] + ".prefab");
        private static void RequireEditMode()
        { if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before changing shared bird assets."); }
    }
}
