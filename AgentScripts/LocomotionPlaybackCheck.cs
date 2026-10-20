using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using RunwayPanic.Player;

// Opt-in live probe. No probe objects or input devices are saved into the scene.
public static class LocomotionPlaybackCheck
{
    public static string Start()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play first.");
        var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        var input = player.GetComponent<PlayerInput>();
        var originalDevices = input.devices.ToArray();
        string originalScheme = input.currentControlScheme;
        var keyboard = InputSystem.AddDevice<Keyboard>("LocomotionCheckKeyboard");
        var mouse = InputSystem.AddDevice<Mouse>("LocomotionCheckMouse");
        input.SwitchCurrentControlScheme(keyboard, mouse);
        var manager = RunwayPanic.Core.GameManager.Instance;
        if (manager != null && manager.IsPaused) manager.Resume();
        var animator = player.GetComponent<PlayerAnimator>().Animator;
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var cc = player.GetComponent<CharacterController>();
        var report = new StringBuilder("Live rendered-frame locomotion check\n");
        string directory = Path.GetFullPath("Temp/LocomotionCheck");
        Directory.CreateDirectory(directory);
        double began = EditorApplication.timeSinceStartup;
        int phase = -1, lastFrame = -1, samples = 0;
        float minRoot=999, maxRoot=-999, minHip=999, maxHip=-999, minSpeed=999,maxSpeed=-999;
        float maxCameraTurn=0; Quaternion previousCamera=Quaternion.identity;
        bool haveCamera=false, firstImage=false, secondImage=false;
        float walkStarted=0f, firstStep=-1f, walkAtSpeed=-1f, animationAtSpeed=-1f;
        float expectedWalkSpeed = new SerializedObject(player).FindProperty("walkSpeed").floatValue;
        EditorApplication.CallbackFunction tick = null;
        Action<ScriptableRenderContext,Camera> render = null;
        Action cleanup = () => {
            EditorApplication.update -= tick;
            RenderPipelineManager.endCameraRendering -= render;
            if (input != null && originalDevices.Length > 0)
                input.SwitchCurrentControlScheme(originalScheme, originalDevices);
            if (keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse.added) InputSystem.RemoveDevice(mouse);
        };
        render = (context, camera) => {
            if (camera != Camera.main || Time.frameCount == lastFrame || player == null) return;
            lastFrame=Time.frameCount;
            double elapsed=EditorApplication.timeSinceStartup-began;
            if (phase == 1)
            {
                float sinceStart = Time.time - walkStarted;
                if (firstStep < 0f && player.CurrentSpeed > 0.05f) firstStep = sinceStart;
                if (walkAtSpeed < 0f && player.CurrentSpeed >= expectedWalkSpeed * 0.9f) walkAtSpeed = sinceStart;
                if (animationAtSpeed < 0f && animator.GetFloat("Speed") >= expectedWalkSpeed * 0.9f)
                    animationAtSpeed = sinceStart;
            }
            if (elapsed < 2.5 || elapsed > 4.0) return;
            samples++;
            float root=player.transform.position.y;
            float hip=player.transform.InverseTransformPoint(hips.position).y;
            float speed=new Vector2(cc.velocity.x,cc.velocity.z).magnitude;
            minRoot=Mathf.Min(minRoot,root);maxRoot=Mathf.Max(maxRoot,root);
            minHip=Mathf.Min(minHip,hip);maxHip=Mathf.Max(maxHip,hip);
            minSpeed=Mathf.Min(minSpeed,speed);maxSpeed=Mathf.Max(maxSpeed,speed);
            if (haveCamera) maxCameraTurn=Mathf.Max(maxCameraTurn,Quaternion.Angle(previousCamera,camera.transform.rotation));
            previousCamera=camera.transform.rotation;haveCamera=true;
        };
        tick = () => {
            try {
                if (!EditorApplication.isPlaying || player == null) throw new InvalidOperationException("Play interrupted");
                double elapsed=EditorApplication.timeSinceStartup-began;
                int next=elapsed<0.4?0:elapsed<1.8?1:elapsed<4.1?2:elapsed<4.6?3:elapsed<5.4?4:5;
                if (next!=phase) {
                    phase=next;
                    if (phase == 1) walkStarted = Time.time;
                    var state=phase==1?new KeyboardState(Key.W):phase==2?new KeyboardState(Key.W,Key.LeftShift):
                        phase==4?new KeyboardState(Key.W,Key.D,Key.LeftShift):new KeyboardState();
                    InputSystem.QueueStateEvent(keyboard,state);
                }
                if(elapsed>2.8&&!firstImage){firstImage=true;ScreenCapture.CaptureScreenshot(Path.Combine(directory,"Run-A.png"));}
                if(elapsed>2.96&&!secondImage){secondImage=true;ScreenCapture.CaptureScreenshot(Path.Combine(directory,"Run-B.png"));}
                if (elapsed < 5.7) return;
                report.AppendLine("Sprint rendered frames: "+samples);
                report.AppendLine("First step after walk input (s): "+firstStep.ToString("F4"));
                report.AppendLine("90% walking speed after input (s): "+walkAtSpeed.ToString("F4"));
                report.AppendLine("90% walking animation after input (s): "+animationAtSpeed.ToString("F4"));
                report.AppendLine("Responsive startup PASS: "+(firstStep>=0f&&firstStep<=0.05f&&walkAtSpeed>=0f&&walkAtSpeed<=0.12f&&animationAtSpeed>=0f&&animationAtSpeed<=0.18f));
                report.AppendLine("Controller height range (m): "+(maxRoot-minRoot).ToString("F5"));
                report.AppendLine("Hips height range (m): "+(maxHip-minHip).ToString("F5"));
                report.AppendLine("Sprint speed min/max (m/s): "+minSpeed.ToString("F3")+" / "+maxSpeed.ToString("F3"));
                report.AppendLine("Largest camera rotation step during straight sprint (degrees): "+maxCameraTurn.ToString("F4"));
                report.AppendLine("Stopped speed (m/s): "+player.CurrentSpeed.ToString("F4"));
                report.AppendLine("Camera update: "+Camera.main.GetComponent<Unity.Cinemachine.CinemachineBrain>().UpdateMethod);
                report.AppendLine("PASS: "+(samples>10&&maxRoot-minRoot<0.02f&&maxCameraTurn<0.5f&&player.CurrentSpeed<0.05f));
                File.WriteAllText(Path.Combine(directory,"Report.txt"),report.ToString());
                cleanup();
            } catch(Exception e) {
                File.WriteAllText(Path.Combine(directory,"Report.txt"),report+e.ToString());cleanup();
            }
        };
        RenderPipelineManager.endCameraRendering += render;
        EditorApplication.update += tick;
        return "Started rendered-frame walk/sprint/stop/turn probe. Report: Temp/LocomotionCheck/Report.txt";
    }
}
