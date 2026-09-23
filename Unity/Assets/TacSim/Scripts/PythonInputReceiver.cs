using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace TacSim
{
    [RequireComponent(typeof(PilotInput))]
    public class PythonInputReceiver : MonoBehaviour
    {
        public int port = 5005;
        private UdpClient udpClient;
        private PilotInput pilotInput;

        private Vector3 moveCmd;
        private Vector3 turnCmd;
        private string pendingAction;

        [Serializable]
        private class CommandPacket
        {
            public float moveX, moveY, moveZ;
            public float turnY;
            public string action;
        }

        private void Awake()
        {
            pilotInput = GetComponent<PilotInput>();
        }

        private void OnEnable()
        {
            pilotInput.ExternalControl = true;
            try
            {
                udpClient = new UdpClient(port);
                udpClient.BeginReceive(OnReceive, null);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PythonBridge] Socket error: {e.Message}");
            }
        }

        private void OnReceive(IAsyncResult ar)
        {
            try
            {
                IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] bytes = udpClient.EndReceive(ar, ref endpoint);
                string json = Encoding.UTF8.GetString(bytes);
                CommandPacket p = JsonUtility.FromJson<CommandPacket>(json);

                lock (this)
                {
                    moveCmd = new Vector3(p.moveX, p.moveY, p.moveZ);
                    turnCmd = new Vector3(0, p.turnY, 0);
                    if (!string.IsNullOrEmpty(p.action)) pendingAction = p.action;
                }
            }
            catch { }

            udpClient?.BeginReceive(OnReceive, null);
        }

        private void Update()
        {
            lock (this)
            {
                if (pilotInput.vehicle != null)
                {
                    pilotInput.vehicle.SetCommand(moveCmd, turnCmd);
                }

                if (!string.IsNullOrEmpty(pendingAction))
                {
                    ExecuteAction(pendingAction);
                    pendingAction = null;
                }
            }
        }

        private void ExecuteAction(string action)
        {
            switch (action)
            {
                case "reset": pilotInput.vehicle?.ResetVehicle(); break;
                case "arm": pilotInput.vehicle?.SetArmed(true); break;
                case "disarm": pilotInput.vehicle?.SetArmed(false); break;
                case "cycle_cam": pilotInput.view?.CycleCamera(); break;
                case "toggle_lights": pilotInput.Invoke("ToggleLights", 0); break;
            }
        }

        private void OnDisable()
        {
            if (pilotInput != null) pilotInput.ExternalControl = false;
            udpClient?.Close();
        }
    }
}
