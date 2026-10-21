using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using RunwayPanic.Core;
using RunwayPanic.Player;
using RunwayPanic.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Opt-in live probe; outside Assets, never included in the game or saved to a scene.
public static class FirstPersonPolishCheck
{
    public static string Start()
    {
        if (!EditorApplication.isPlaying) throw new Exception("Enter Play mode first.");
        // Unity destroys its generated default settings when they are replaced; keep a value copy.
        var originalInputSettings = UnityEngine.Object.Instantiate(InputSystem.settings);
        originalInputSettings.hideFlags = HideFlags.HideAndDontSave;
        var testInputSettings = UnityEngine.Object.Instantiate(originalInputSettings);
        testInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        testInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings = testInputSettings;
        var holder = UnityEngine.Object.FindAnyObjectByType<PlayerWeaponHolder>();
        var rig = holder.GetComponent<PlayerCameraRig>();
        var player = holder.GetComponent<PlayerController>();
        var input = holder.GetComponent<PlayerInput>();
        var shooting = holder.GetComponent<PlayerShooting>();
        var health = holder.GetComponent<PlayerHealth>();
        var respawn = holder.GetComponent<PlayerRespawn>();
        var originalWeapon = holder.CurrentWeapon;
        var originalDevices = input.devices.ToArray();
        var originalScheme = input.currentControlScheme;
        bool originalAutoSwitch = input.neverAutoSwitchControlSchemes;
        input.neverAutoSwitchControlSchemes = true;
        var keyboard = InputSystem.AddDevice<Keyboard>("FirstPersonCheckKeyboard");
        var mouse = InputSystem.AddDevice<Mouse>("FirstPersonCheckMouse");
        input.SwitchCurrentControlScheme(keyboard, mouse);
        if (GameManager.Instance.IsPaused) GameManager.Instance.Resume();
        if (GameManager.Instance.IsShopOpen) GameManager.Instance.CloseShop();
        var wave = WaveManager.Instance;
        bool waveEnabled = wave != null && wave.enabled;
        if (wave != null) wave.enabled = false; // Runtime only; prevents shop interrupting this isolated probe.
        var prefabs = Enumerable.Range(1, 4).Select(i => AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Weapons/P_Gun_Tier" + i + ".prefab")).ToArray();
        string directory = Path.GetFullPath("Temp/FirstPersonPolishCheck");
        Directory.CreateDirectory(directory);
        var report = new StringBuilder("First-person polish live check\n");
        int failures = 0, frames = 0, previousFrame = -1;
        Action<bool, string> check = (ok, text) => { report.AppendLine((ok ? "PASS " : "FAIL ") + text); if (!ok) failures++; };
        int stage = 0, tier = 0, savedAmmo = 0;
        float stageStart = Time.time;
        double wallStart = EditorApplication.timeSinceStartup;
        Vector3 beforeFire = Vector3.zero, beforeReload = Vector3.zero, movementStart = Vector3.zero;
        Vector3 handBeforeReload = Vector3.zero;
        Quaternion initialCamera = Camera.main.transform.rotation;
        float maxWalkCameraAngle = 0f, maxGaitMotion = 0f;
        bool respawned = false;
        Action onRespawn = () => respawned = true;
        respawn.Respawned += onRespawn;
        holder.Equip(prefabs[0]);
        EditorApplication.CallbackFunction tick = null;
        Action cleanup = () => {
            EditorApplication.update -= tick;
            respawn.Respawned -= onRespawn;
            if (wave != null) wave.enabled = waveEnabled;
            if (input != null && originalDevices.Length > 0) input.SwitchCurrentControlScheme(originalScheme, originalDevices);
            if (input != null) input.neverAutoSwitchControlSchemes = originalAutoSwitch;
            if (keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse.added) InputSystem.RemoveDevice(mouse);
            InputSystem.settings = originalInputSettings;
            if (holder != null && originalWeapon != null) holder.Equip(originalWeapon.Data.Prefab);
            var floorObj=GameObject.Find("FP_Check_Floor");if(floorObj!=null)UnityEngine.Object.Destroy(floorObj);
        };
        Action<int> next = value => { stage = value; stageStart = Time.time; };
        tick = () => {
            try
            {
                if (!EditorApplication.isPlaying || holder == null) throw new Exception("Play mode ended during check.");
                if (GameManager.Instance.IsPaused) GameManager.Instance.Resume();
                if (EditorApplication.timeSinceStartup - wallStart > 100) throw new Exception("Timed out waiting for gameplay frames.");
                if (previousFrame == Time.frameCount) return;
                previousFrame = Time.frameCount; frames++;
                float elapsed = Time.time - stageStart;
                WeaponBase weapon = holder.CurrentWeapon;
                if (stage < 20 && health.CurrentHP < health.MaxHP) health.ResetHealth();
                switch (stage)
                {
                    case 0:
                        if (elapsed < 0.7f) break;
                        check(holder.HandSocket.GetComponent<FirstPersonWeaponMotion>() != null, "Tier " + (tier+1) + " motion attached");
                        check(weapon.GetComponentsInParent<Camera>().Length == 1, "Tier " + (tier+1) + " uses existing camera");
                        check(weapon.GetComponentsInChildren<Renderer>().Any(r => r.enabled && !r.forceRenderingOff), "Tier " + (tier+1) + " renderers visible");
                        var arms = holder.GetComponentInChildren<FirstPersonArms>();
                        check(arms != null && arms.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r => r.enabled), "Tier " + (tier+1) + " rigged hands and sleeves visible");
                        check(arms.RightHand.name == "hand.R" && arms.GetComponent<Animator>() != null, "Supplied PSX rig and animation player are active");
                        Vector3 handTarget = weapon.transform.TransformPoint(new Vector3(0.015f,-0.065f,-0.075f));
                        check(Vector3.Distance(arms.RightHand.position,handTarget)<0.025f,"Tier " + (tier+1) + " right hand holds grip");
                        ScreenCapture.CaptureScreenshot(Path.Combine(directory, "Tier" + (tier+1) + "-Hip.png"));
                        var cam = Camera.main;
                        var ray = (Ray)typeof(PlayerShooting).GetMethod("AimRay", BindingFlags.Instance|BindingFlags.NonPublic).Invoke(shooting, new object[]{cam});
                        Ray expected = cam.ViewportPointToRay(new Vector3(0.5f,0.5f));
                        check(Vector3.Distance(ray.origin, expected.origin) < 0.00001f && Vector3.Angle(ray.direction, expected.direction) < 0.001f,
                            "Tier " + (tier+1) + " exact camera-centre shooting ray");
                        beforeFire = holder.HandSocket.localPosition;
                        int ammo = weapon.AmmoInMagazine;
                        check(shooting.TryShoot() && weapon.AmmoInMagazine == ammo-1, "Tier " + (tier+1) + " shot consumes exactly one round");
                        check(holder.HandSocket.localPosition == beforeFire, "Shot event queues recoil until LateUpdate");
                        if(tier == 0) savedAmmo = weapon.AmmoInMagazine;
                        next(1); break;
                    case 1:
                        if(elapsed < 0.04f) break;
                        check(Vector3.Distance(holder.HandSocket.localPosition,beforeFire) > 0.001f, "Tier " + (tier+1) + " visible weapon recoil");
                        weapon.StartReload(); beforeReload = holder.HandSocket.localPosition;
                        handBeforeReload = holder.HandSocket.InverseTransformPoint(holder.GetComponentInChildren<FirstPersonArms>().LeftHand.position);
                        next(2); break;
                    case 2:
                        if(elapsed < weapon.Data.ReloadTime * 0.45f) break;
                        check(weapon.IsReloading && Vector3.Distance(holder.HandSocket.localPosition,beforeReload) > 0.04f,
                            "Tier " + (tier+1) + " reload presentation follows weapon timer");
                        check(Vector3.Distance(handBeforeReload,holder.HandSocket.InverseTransformPoint(holder.GetComponentInChildren<FirstPersonArms>().LeftHand.position))>0.08f,
                            "Tier " + (tier+1) + " support hand leaves gun for reload");
                        ScreenCapture.CaptureScreenshot(Path.Combine(directory,"Tier" + (tier+1) + "-Reload.png"));
                        next(3); break;
                    case 3:
                        if(weapon.IsReloading) break;
                        check(weapon.AmmoInMagazine == weapon.Data.MagazineSize, "Tier " + (tier+1) + " reload restores correct magazine");
                        InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Right));
                        next(4); break;
                    case 4:
                        if(elapsed < 0.65f) break;
                        check(rig.IsAiming && rig.AimBlend > 0.99f, "Tier " + (tier+1) + " aim blend completes");
                        report.AppendLine("Aim input: "+input.actions.FindAction("Aim").IsPressed()+" devices="+string.Join(",",input.devices.Select(d=>d.name))+" state="+GameManager.Instance.CurrentState);
                        if(weapon.Data.HasScope) check(rig.IsScoped && weapon.GetComponentsInChildren<Renderer>().All(r => r.forceRenderingOff),
                            "Scoped weapon hidden behind scope overlay");
                        if(weapon.Data.HasScope) check(holder.GetComponentInChildren<FirstPersonArms>().GetComponentsInChildren<SkinnedMeshRenderer>().All(r=>!r.enabled),"Arms hidden in scope");
                        else check(weapon.GetComponentsInChildren<Renderer>().Any(r => !r.forceRenderingOff),"Non-scoped aim keeps gun visible");
                        if(!weapon.Data.HasScope)
                        {
                            var aimedArms = holder.GetComponentInChildren<FirstPersonArms>();
                            Vector3 support = (Vector3)typeof(FirstPersonArms).GetMethod("SupportPoint", BindingFlags.Instance|BindingFlags.NonPublic).Invoke(aimedArms,null);
                            float supportError = Vector3.Distance(aimedArms.LeftHand.position,support);
                            float gripError = Vector3.Distance(aimedArms.RightHand.position,weapon.transform.TransformPoint(new Vector3(0.015f,-0.065f,-0.075f)));
                            check(supportError<0.035f && gripError<0.025f,"Tier "+(tier+1)+" aimed hands reach both grips (left="+supportError.ToString("F4")+", right="+gripError.ToString("F4")+")");
                            Ray reticle = Camera.main.ViewportPointToRay(new Vector3(0.5f,0.5f));
                            string blocked = string.Join(",",weapon.GetComponentsInChildren<Renderer>().Concat(aimedArms.GetComponentsInChildren<Renderer>())
                                .Where(r=>r.enabled && !r.forceRenderingOff && BlocksReticle(r,reticle)).Select(r=>r.name));
                            check(blocked.Length==0,"Tier "+(tier+1)+" aimed reticle clear of weapon and arms "+blocked);
                        }
                        ScreenCapture.CaptureScreenshot(Path.Combine(directory,"Tier" + (tier+1) + "-Aim.png"));
                        next(41); break;
                    case 41:
                        if(elapsed < 0.12f) break; // Let the screenshot render while aim is still held.
                        InputSystem.QueueStateEvent(mouse,new MouseState()); next(5); break;
                    case 5:
                        if(elapsed < 0.5f) break;
                        check(weapon.GetComponentsInChildren<Renderer>().Any(r => !r.forceRenderingOff),"Tier " + (tier+1) + " visible after leaving aim");
                        if(++tier < 4) { holder.Equip(prefabs[tier]); next(0); }
                        else {
                            holder.Equip(prefabs[0]);
                            check(ReferenceEquals(originalWeapon,holder.CurrentWeapon),"Returning to pistol reuses original cached WeaponBase");
                            check(holder.GetComponentsInChildren<WeaponBase>(true).Length == 4,"Exactly four cached weapons; no duplicate viewmodels");
                            holder.CurrentWeapon.TryFire(); savedAmmo=holder.CurrentWeapon.AmmoInMagazine;
                            holder.Equip(prefabs[1]); holder.Equip(prefabs[0]);
                            check(holder.CurrentWeapon.AmmoInMagazine==savedAmmo,"Switching preserves magazine ammo");
                            // Move to a temporary, clear runtime test floor; no scene asset is saved.
                            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name="FP_Check_Floor";
                            floor.transform.position=new Vector3(1500,-0.5f,1500); floor.transform.localScale=new Vector3(100,1,100);
                            var cc=holder.GetComponent<CharacterController>();cc.enabled=false;
                            holder.transform.position=new Vector3(1500,0.1f,1500);cc.enabled=true;
                            rig.ResetViewTo(Quaternion.identity); movementStart=holder.transform.position;
                            next(10);
                        }
                        break;
                    case 10:
                        if(elapsed<0.6f)break;
                        initialCamera=Camera.main.transform.rotation;
                        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));next(11);break;
                    case 11:
                        maxWalkCameraAngle=Mathf.Max(maxWalkCameraAngle,Quaternion.Angle(initialCamera,Camera.main.transform.rotation));
                        maxGaitMotion=Mathf.Max(maxGaitMotion,Vector3.Distance(holder.HandSocket.localPosition,new Vector3(0.30f,-0.24f,0.45f)));
                        if(elapsed<1.1f)break;
                        check(player.CurrentSpeed>4.4f && Vector3.Distance(movementStart,holder.transform.position)>3f,"Walk responds at original 4.5 m/s");
                        check(maxWalkCameraAngle<0.1f,"Weapon gait does not shake the aiming camera");
                        check(maxGaitMotion>0.002f,"Walking animates the weapon");
                        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W,Key.LeftShift));next(12);break;
                    case 12:
                        if(elapsed<0.8f)break;
                        check(player.CurrentSpeed>7.4f,"Sprint retains original 7.5 m/s");
                        check(holder.HandSocket.localPosition.y < -0.26f,"Sprint lowers weapon smoothly");
                        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Space));next(13);break;
                    case 13:
                        if(elapsed<0.1f)break;
                        InputSystem.QueueStateEvent(keyboard,new KeyboardState());
                        if(elapsed<1.4f)break;
                        check(player.IsGrounded,"Jump and landing complete with presentation active");
                        next(20);break;
                    case 20:
                        holder.GetComponent<PlayerShield>()?.Deactivate();
                        health.TakeDamage(health.MaxHP*2,WeaponRarity.Legendary);
                        next(21);break;
                    case 21:
                        if(elapsed<3.5f)break;
                        check(respawned && health.IsAlive && player.enabled,"Death and respawn restore the player");
                        check(Mathf.Abs(Mathf.DeltaAngle(Camera.main.transform.eulerAngles.x,0))<0.1f,"Respawn view resets level");
                        check(holder.HandSocket.GetComponent<FirstPersonWeaponMotion>().enabled,"Weapon animation survives respawn");
                        check(GameManager.Instance.TotalWaves==4,"Four waves preserved");
                        check(new SerializedObject(rig).FindProperty("pitchLimits").vector2Value.x==-75f,"75-degree upward limit preserved");
                        report.AppendLine("Advancing frames: "+frames+"; failures: "+failures);
                        File.WriteAllText(Path.Combine(directory,"Report.txt"),report.ToString());
                        cleanup();break;
                }
            }
            catch(Exception e) { report.AppendLine("ERROR "+e);File.WriteAllText(Path.Combine(directory,"Report.txt"),report.ToString());cleanup(); }
        };
        EditorApplication.update += tick;
        return "Started all-four-weapon fire/reload/aim/scope/switch/walk/sprint/jump/respawn check. Report: "+directory;
    }

    private static bool BlocksReticle(Renderer renderer, Ray ray)
    {
        Mesh baked = null;
        Mesh mesh = null;
        if(renderer is SkinnedMeshRenderer skin) { baked=new Mesh();skin.BakeMesh(baked);mesh=baked; }
        else { var filter=renderer.GetComponent<MeshFilter>();if(filter!=null)mesh=filter.sharedMesh; }
        if(mesh==null) return false;
        var temporary = new GameObject("FP_AimRayCheck");
        try
        {
            temporary.transform.SetPositionAndRotation(renderer.transform.position,renderer.transform.rotation);
            temporary.transform.localScale=renderer.transform.lossyScale;
            var collider=temporary.AddComponent<MeshCollider>();collider.sharedMesh=mesh;
            Physics.SyncTransforms();
            return collider.Raycast(ray,out _,3f);
        }
        finally { UnityEngine.Object.DestroyImmediate(temporary);if(baked!=null)UnityEngine.Object.DestroyImmediate(baked); }
    }
}
