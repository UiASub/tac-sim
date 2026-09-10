using UnityEngine;
using UnityEngine.InputSystem;

namespace TacSim
{
    public sealed class PilotInput : MonoBehaviour
    {
        public RovVehicle vehicle;
        public PilotView view;
        public Light[] headlights;
        public bool ExternalControl { get; set; }

        void Update()
        {
            if (ExternalControl) return;
            Vector3 move = Vector3.zero;
            Vector3 turn = Vector3.zero;
            Keyboard k = Keyboard.current;
            if (k != null)
            {
                move = new Vector3(Axis(k.dKey.isPressed, k.aKey.isPressed),
                    Axis(k.spaceKey.isPressed, k.leftCtrlKey.isPressed),
                    Axis(k.wKey.isPressed, k.sKey.isPressed));
                turn = new Vector3(Axis(k.downArrowKey.isPressed, k.upArrowKey.isPressed),
                    Axis(k.eKey.isPressed, k.qKey.isPressed),
                    Axis(k.leftArrowKey.isPressed, k.rightArrowKey.isPressed));
                if (k.rKey.wasPressedThisFrame) vehicle.ResetVehicle();
                if (k.escapeKey.wasPressedThisFrame) vehicle.SetArmed(!vehicle.Armed);
                if (k.vKey.wasPressedThisFrame) view.CycleCamera();
                if (k.lKey.wasPressedThisFrame) ToggleLights();
            }
            Gamepad g = Gamepad.current;
            if (g != null)
            {
                Vector2 left = g.leftStick.ReadValue();
                Vector2 right = g.rightStick.ReadValue();
                move += new Vector3(left.x, g.rightTrigger.ReadValue() - g.leftTrigger.ReadValue(), left.y);
                turn += new Vector3(-right.y, right.x, -g.dpad.ReadValue().x);
                if (g.buttonSouth.wasPressedThisFrame) view.CycleCamera();
                if (g.startButton.wasPressedThisFrame) vehicle.ResetVehicle();
                if (g.buttonEast.wasPressedThisFrame) vehicle.SetArmed(!vehicle.Armed);
                if (g.buttonNorth.wasPressedThisFrame) ToggleLights();
            }
            vehicle.SetCommand(move, turn);
        }

        static float Axis(bool positive, bool negative) => (positive ? 1 : 0) - (negative ? 1 : 0);
        void ToggleLights() { foreach (Light light in headlights) light.enabled = !light.enabled; }
        void OnApplicationFocus(bool focused) { if (!focused) vehicle.SetArmed(false); }
        void OnDisable() { if (vehicle != null && vehicle.Body != null) vehicle.SetCommand(Vector3.zero, Vector3.zero); }
    }
}
