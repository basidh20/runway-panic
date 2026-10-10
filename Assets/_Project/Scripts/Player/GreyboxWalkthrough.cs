using UnityEngine;
using UnityEngine.InputSystem;

namespace RunwayPanic.Greybox
{
    /// <summary>Temporary first-person scale/collision preview, replace with S2's player prefab.</summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class GreyboxWalkthrough : MonoBehaviour
    {
        public Camera walkCamera;
        public Camera overviewCamera;
        public float walkSpeed = 5f;
        public float sprintSpeed = 9f;
        public float mouseSensitivity = 0.10f;

        private CharacterController controller;
        private bool walking;
        private float verticalVelocity;
        private float pitch;
        private float ignoreLookUntil;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            SetWalking(false);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.tabKey.wasPressedThisFrame) SetWalking(!walking);
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (!walking) return;

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && mouse.position.ReadValue().y < Screen.height - 160)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked && Time.unscaledTime > ignoreLookUntil)
            {
                Vector2 look = mouse.delta.ReadValue() * mouseSensitivity;
                transform.Rotate(0, look.x, 0);
                pitch = Mathf.Clamp(pitch - look.y, -80, 80);
                walkCamera.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            }

            if (keyboard.digit1Key.wasPressedThisFrame) Teleport(new Vector3(0, 0.12f, -24), 0);
            if (keyboard.digit2Key.wasPressedThisFrame) Teleport(new Vector3(-13, 0.12f, 12), -90);
            if (keyboard.digit3Key.wasPressedThisFrame) Teleport(new Vector3(13, 0.12f, -12), 90);

            Vector2 input = new Vector2(
                (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
            input = Vector2.ClampMagnitude(input, 1);
            float speed = keyboard.leftShiftKey.isPressed ? sprintSpeed : walkSpeed;
            if (controller.isGrounded && verticalVelocity < 0) verticalVelocity = -2;
            if (controller.isGrounded && keyboard.spaceKey.wasPressedThisFrame) verticalVelocity = Mathf.Sqrt(2 * 20 * 1.0f);
            verticalVelocity -= 20 * Time.deltaTime;
            controller.Move((transform.right * input.x * speed + transform.forward * input.y * speed + Vector3.up * verticalVelocity) * Time.deltaTime);
            if (transform.position.y < -10) Teleport(new Vector3(0, 0.12f, -24), 0);
        }

        private void Teleport(Vector3 position, float yaw)
        {
            controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            controller.enabled = true;
            verticalVelocity = 0;
            pitch = 0;
            ignoreLookUntil = Time.unscaledTime + 0.15f;
            walkCamera.transform.localRotation = Quaternion.identity;
        }

        private void SetWalking(bool value)
        {
            walking = value;
            ignoreLookUntil = Time.unscaledTime + 0.15f;
            walkCamera.enabled = value;
            overviewCamera.enabled = !value;
            walkCamera.GetComponent<AudioListener>().enabled = value;
            overviewCamera.GetComponent<AudioListener>().enabled = !value;
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !value;
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnGUI()
        {
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
                bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            }
            titleStyle.normal.textColor = Color.white;
            bodyStyle.normal.textColor = Color.white;
            titleStyle.fontSize = 20;
            bodyStyle.fontSize = 13;
            GUI.color = Color.white;
            GUI.contentColor = Color.white;
            GUI.Box(new Rect(16, 16, 460, 142), GUIContent.none);
            GUI.Label(new Rect(30, 25, 430, 30), "RUNWAY PANIC  /  AIRPORT GREYBOX", titleStyle);
            GUI.Label(new Rect(30, 58, 425, 45), walking
                ? "WASD move  |  Mouse look  |  Shift sprint  |  Space jump\n1 runway  |  2 Shelter A entrance  |  3 Shelter B entrance"
                : "100 x 80 m arena  |  60 x 14 m runway  |  2 open shelters\nTemporary layout preview. Gameplay and AI come later.", bodyStyle);
            if (GUI.Button(new Rect(30, 112, 155, 28), walking ? "Tab: overview" : "Tab: walk airport")) SetWalking(!walking);
            GUI.Label(new Rect(200, 117, 255, 25), walking ? "Esc releases mouse" : "Click the Game view to focus", bodyStyle);
        }
    }
}
