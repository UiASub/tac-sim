using UnityEngine;
using UnityEngine.InputSystem;

namespace TacSim
{
    public sealed class PilotInput : MonoBehaviour
    {
        public RovVehicle vehicle;
        public PilotView view;
        public Light[] headlights;
        [Range(0.1f, 1)] public float precisionScale = 0.35f;
        public bool ExternalControl { get; set; }

        void Update()
        {
            if (ExternalControl) return;
            Vector3 move = Vector3.zero;
            Vector3 turn = Vector3.zero;
            bool precision = false;
            Keyboard k = Keyboard.current;
            if (k != null)
            {
                move = new Vector3(Axis(k.dKey.isPressed, k.aKey.isPressed),
                    Axis(k.spaceKey.isPressed, k.leftCtrlKey.isPressed),
                    Axis(k.wKey.isPressed, k.sKey.isPressed));
                turn = new Vector3(0, Axis(k.eKey.isPressed, k.qKey.isPressed), 0);
                precision = k.leftShiftKey.isPressed || k.rightShiftKey.isPressed;
                if (k.rKey.wasPressedThisFrame) vehicle.ResetVehicle();
                if (k.escapeKey.wasPressedThisFrame) vehicle.SetArmed(!vehicle.Armed);
                if (k.vKey.wasPressedThisFrame) view.CycleCamera();
                if (k.lKey.wasPressedThisFrame) ToggleLights();
                if (k.mKey.wasPressedThisFrame) vehicle.GetComponent<RovVisuals>()?.Toggle();
                if (view.appearance != null)
                {
                    if (k.hKey.wasPressedThisFrame) view.appearance.MenuOpen = !view.appearance.MenuOpen;
                    if (k.fKey.wasPressedThisFrame) view.appearance.CyclePreset();
                    if (k.pKey.wasPressedThisFrame) view.appearance.SetFilters(!view.appearance.FiltersEnabled);
                }
            }
            Gamepad g = Gamepad.current;
            if (g != null)
            {
                Vector2 left = g.leftStick.ReadValue();
                Vector2 right = g.rightStick.ReadValue();
                move += new Vector3(left.x, g.rightTrigger.ReadValue() - g.leftTrigger.ReadValue(), left.y);
                turn += new Vector3(0, right.x, 0);
                precision |= g.leftShoulder.isPressed;
                if (g.buttonSouth.wasPressedThisFrame) view.CycleCamera();
                if (g.startButton.wasPressedThisFrame) vehicle.ResetVehicle();
                if (g.buttonEast.wasPressedThisFrame) vehicle.SetArmed(!vehicle.Armed);
                if (g.buttonNorth.wasPressedThisFrame) ToggleLights();
                if (g.selectButton.wasPressedThisFrame) vehicle.GetComponent<RovVisuals>()?.Toggle();
                if (g.rightShoulder.wasPressedThisFrame && view.appearance != null) view.appearance.CyclePreset();
            }
            if (precision)
            {
                move *= precisionScale;
                turn *= precisionScale;
            }
            vehicle.SetCommand(move, turn);
        }

        static float Axis(bool positive, bool negative) => (positive ? 1 : 0) - (negative ? 1 : 0);
        void ToggleLights() { foreach (Light light in headlights) light.enabled = !light.enabled; }
        void OnApplicationFocus(bool focused)
        {
            // The opt-in smoke runner owns commands while ExternalControl is active.
            // Switching desktop focus must not inject a pilot command into that run.
            if (!focused && !ExternalControl) vehicle.SetArmed(false);
        }
        void OnDisable() { if (vehicle != null && vehicle.Body != null) vehicle.SetCommand(Vector3.zero, Vector3.zero); }
    }
}
