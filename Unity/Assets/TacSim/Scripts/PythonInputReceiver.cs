using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace TacSim
{
    // Command-only UDP bridge (see pipe/TacSimPipe.py). It takes control only while packets arrive and
    // hands control back to the local pilot after CommandTimeout without a packet. While the TCP
    // AutomationServer owns the vehicle, UDP packets are ignored.
    [RequireComponent(typeof(PilotInput))]
    public class PythonInputReceiver : MonoBehaviour
    {
        const float CommandTimeout = 0.5f;
        public int port = 5005;
        private UdpClient udpClient;
        private PilotInput pilotInput;
        private readonly object gate = new();

        private Vector3 moveCmd;
        private Vector3 turnCmd;
        private string pendingAction;
        private bool packetReceived;
        private float lastPacketTime = float.NegativeInfinity;
        private bool ownsControl;

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
            try
            {
                udpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                udpClient.BeginReceive(OnReceive, null);
                Debug.Log($"[PythonBridge] Listening on UDP 127.0.0.1:{port}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[PythonBridge] Socket error on port {port}: {e.Message}");
            }
        }

        private void OnReceive(IAsyncResult ar)
        {
            UdpClient client = udpClient;
            if (client == null) return;
            try
            {
                IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] bytes = client.EndReceive(ar, ref endpoint);
                CommandPacket p = JsonUtility.FromJson<CommandPacket>(Encoding.UTF8.GetString(bytes));
                lock (gate)
                {
                    moveCmd = new Vector3(p.moveX, p.moveY, p.moveZ);
                    turnCmd = new Vector3(0, p.turnY, 0);
                    if (!string.IsNullOrEmpty(p.action)) pendingAction = p.action;
                    packetReceived = true;
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PythonBridge] Ignored packet: {e.Message}");
            }

            try { client.BeginReceive(OnReceive, null); }
            catch (ObjectDisposedException) { }
        }

        private void Update()
        {
            if (pilotInput.vehicle == null) return;
            bool automationActive = TryGetComponent(out AutomationServer server) && server.HasClient;
            Vector3 move, turn;
            string action;
            lock (gate)
            {
                if (packetReceived) lastPacketTime = Time.unscaledTime;
                packetReceived = false;
                move = moveCmd;
                turn = turnCmd;
                action = pendingAction;
                pendingAction = null;
            }
            if (automationActive) return;

            bool fresh = Time.unscaledTime - lastPacketTime <= CommandTimeout;
            if (fresh)
            {
                ownsControl = true;
                pilotInput.ExternalControl = true;
                pilotInput.vehicle.SetCommand(move, turn);
                if (!string.IsNullOrEmpty(action)) ExecuteAction(action);
            }
            else if (ownsControl)
            {
                // Stale or stopped client: neutral thrust and hand control back to the pilot.
                ownsControl = false;
                pilotInput.vehicle.SetCommand(Vector3.zero, Vector3.zero);
                pilotInput.ExternalControl = false;
            }
        }

        private void ExecuteAction(string action)
        {
            switch (action)
            {
                case "reset": pilotInput.vehicle.ResetVehicle(); break;
                case "arm": pilotInput.vehicle.SetArmed(true); break;
                case "disarm": pilotInput.vehicle.SetArmed(false); break;
                case "cycle_cam": pilotInput.view?.CycleCamera(); break;
                case "toggle_lights": pilotInput.ToggleLights(); break;
            }
        }

        private void OnDisable()
        {
            if (ownsControl && pilotInput != null)
            {
                pilotInput.vehicle?.SetCommand(Vector3.zero, Vector3.zero);
                pilotInput.ExternalControl = false;
            }
            ownsControl = false;
            udpClient?.Close();
            udpClient = null;
        }
    }
}
