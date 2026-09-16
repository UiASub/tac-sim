using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace TacSim
{
    // Opt-in localhost API for training and automation. One JSON request and response per line.
    public sealed class AutomationServer : MonoBehaviour
    {
        const int DefaultPort = 8765;
        const float CommandTimeout = 0.5f;
        readonly ConcurrentQueue<PendingRequest> requests = new();
        TcpListener listener;
        Thread serverThread;
        volatile bool running;
        volatile bool clientConnected;
        RovVehicle vehicle;
        PilotInput input;
        PilotView view;
        Camera pilotCamera;
        float lastCommandTime;
        bool ownedControls;
        int port;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (!TryGetPort(Environment.GetCommandLineArgs(), out int requestedPort)) return;
            PilotInput pilot = FindFirstObjectByType<PilotInput>();
            if (pilot == null)
            {
                Debug.LogError("TAC_AUTOMATION_FAILED: PilotInput was not found");
                return;
            }
            AutomationServer server = pilot.gameObject.AddComponent<AutomationServer>();
            server.port = requestedPort;
        }

        public static bool TryGetPort(string[] args, out int port)
        {
            port = DefaultPort;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != "-automationPort") continue;
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out int parsed)
                    && parsed is > 0 and <= 65535)
                {
                    port = parsed;
                    return true;
                }
                return false;
            }
            return false;
        }

        void Start()
        {
            input = GetComponent<PilotInput>();
            vehicle = input.vehicle;
            view = input.view;
            pilotCamera = view.pilotCamera;
            Application.runInBackground = true;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start(1);
            }
            catch (SocketException)
            {
                Debug.LogError($"TAC_AUTOMATION_FAILED: could not listen on 127.0.0.1:{port}");
                enabled = false;
                return;
            }
            running = true;
            serverThread = new Thread(ServerLoop) { IsBackground = true, Name = "TAC automation server" };
            serverThread.Start();
            Debug.Log($"TAC_AUTOMATION_READY: 127.0.0.1:{port}");
        }

        void Update()
        {
            if (clientConnected && !ownedControls)
            {
                ownedControls = true;
                input.ExternalControl = true;
                vehicle.SetCommand(Vector3.zero, Vector3.zero);
                lastCommandTime = Time.unscaledTime;
            }
            else if (!clientConnected && ownedControls)
            {
                ReleaseControls();
            }

            if (ownedControls && Time.unscaledTime - lastCommandTime > CommandTimeout)
                vehicle.SetCommand(Vector3.zero, Vector3.zero);

            if (requests.TryDequeue(out PendingRequest pending)) Process(pending);
        }

        void Process(PendingRequest pending)
        {
            try
            {
                AutomationRequest request = JsonUtility.FromJson<AutomationRequest>(pending.Json);
                if (request == null) throw new ArgumentException("request must be a JSON object");
                string action = string.IsNullOrWhiteSpace(request.action) ? "observe" : request.action.ToLowerInvariant();
                string error = ApplyAction(action, request);
                pending.Response = JsonUtility.ToJson(BuildResponse(request, error));
            }
            catch (Exception exception)
            {
                pending.Response = JsonUtility.ToJson(new AutomationResponse
                {
                    ok = false,
                    error = exception.Message
                });
            }
            finally
            {
                pending.Ready.Set();
            }
        }

        string ApplyAction(string action, AutomationRequest request)
        {
            switch (action)
            {
                case "observe":
                    break;
                case "command":
                    vehicle.SetCommand(new Vector3(request.sway, request.heave, request.surge),
                        new Vector3(0, request.yaw, 0));
                    lastCommandTime = Time.unscaledTime;
                    break;
                case "reset":
                    vehicle.ResetVehicle();
                    lastCommandTime = Time.unscaledTime;
                    break;
                case "arm":
                    vehicle.SetArmed(true);
                    lastCommandTime = Time.unscaledTime;
                    break;
                case "disarm":
                case "stop":
                    vehicle.SetArmed(false);
                    lastCommandTime = Time.unscaledTime;
                    break;
                default:
                    return $"unknown action '{action}'";
            }

            if (!string.IsNullOrWhiteSpace(request.camera))
            {
                int mode = request.camera.ToLowerInvariant() switch
                {
                    "chase" => 0,
                    "forward" => 1,
                    "downward" => 2,
                    _ => -1
                };
                if (mode < 0) return $"unknown camera '{request.camera}'";
                view.SetMode(mode);
            }
            return null;
        }

        AutomationResponse BuildResponse(AutomationRequest request, string error)
        {
            Rigidbody body = vehicle.Body;
            var response = new AutomationResponse
            {
                ok = error == null,
                error = error,
                request_id = request.request_id,
                sim_time = Time.timeAsDouble,
                fixed_time = Time.fixedTimeAsDouble,
                armed = vehicle.Armed,
                position = Vec3.From(body.position),
                rotation = Quat.From(body.rotation),
                linear_velocity = Vec3.From(body.linearVelocity),
                angular_velocity = Vec3.From(body.angularVelocity),
                depth = vehicle.Depth,
                throttle = vehicle.Throttle,
                is_colliding = vehicle.IsColliding,
                collision_force = vehicle.CollisionForce,
                peak_collision_force = vehicle.PeakCollisionForce,
                camera = new[] { "chase", "forward", "downward" }[view.Mode]
            };
            if (request.capture)
            {
                response.image_width = Mathf.Clamp(request.image_width > 0 ? request.image_width : 320, 64, 1280);
                response.image_height = Mathf.Clamp(request.image_height > 0 ? request.image_height : 180, 64, 720);
                response.image_jpeg_base64 = CaptureJpeg(response.image_width, response.image_height,
                    Mathf.Clamp(request.jpeg_quality > 0 ? request.jpeg_quality : 75, 20, 95));
            }
            return response;
        }

        string CaptureJpeg(int width, int height, int quality)
        {
            RenderTexture previousTarget = pilotCamera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                pilotCamera.targetTexture = target;
                pilotCamera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply(false);
                return Convert.ToBase64String(image.EncodeToJPG(quality));
            }
            finally
            {
                pilotCamera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                Destroy(image);
            }
        }

        void ServerLoop()
        {
            try
            {
                while (running)
                {
                    using TcpClient client = listener.AcceptTcpClient();
                    client.NoDelay = true;
                    clientConnected = true;
                    try
                    {
                        using NetworkStream stream = client.GetStream();
                        using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, true);
                        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                        while (running && client.Connected)
                        {
                            string line = reader.ReadLine();
                            if (line == null) break;
                            var pending = new PendingRequest(line);
                            requests.Enqueue(pending);
                            if (!pending.Ready.Wait(TimeSpan.FromSeconds(10)))
                                writer.WriteLine("{\"ok\":false,\"error\":\"simulation response timed out\"}");
                            else
                                writer.WriteLine(pending.Response);
                        }
                    }
                    finally
                    {
                        clientConnected = false;
                    }
                }
            }
            catch (SocketException exception)
            {
                if (running) Debug.LogException(exception);
            }
            catch (Exception exception)
            {
                if (running) Debug.LogException(exception);
            }
        }

        void ReleaseControls()
        {
            vehicle.SetCommand(Vector3.zero, Vector3.zero);
            input.ExternalControl = false;
            ownedControls = false;
        }

        void OnDestroy()
        {
            running = false;
            clientConnected = false;
            listener?.Stop();
            if (ownedControls) ReleaseControls();
        }

        sealed class PendingRequest
        {
            public readonly string Json;
            public readonly ManualResetEventSlim Ready = new(false);
            public string Response;
            public PendingRequest(string json) => Json = json;
        }

        [Serializable]
        sealed class AutomationRequest
        {
            public int request_id;
            public string action;
            public float surge;
            public float sway;
            public float heave;
            public float yaw;
            public string camera;
            public bool capture;
            public int image_width;
            public int image_height;
            public int jpeg_quality;
        }

        [Serializable]
        sealed class AutomationResponse
        {
            public bool ok;
            public string error;
            public int request_id;
            public double sim_time;
            public double fixed_time;
            public bool armed;
            public Vec3 position;
            public Quat rotation;
            public Vec3 linear_velocity;
            public Vec3 angular_velocity;
            public float depth;
            public float throttle;
            public bool is_colliding;
            public float collision_force;
            public float peak_collision_force;
            public string camera;
            public int image_width;
            public int image_height;
            public string image_jpeg_base64;
        }

        [Serializable]
        struct Vec3
        {
            public float x, y, z;
            public static Vec3 From(Vector3 value) => new() { x = value.x, y = value.y, z = value.z };
        }

        [Serializable]
        struct Quat
        {
            public float x, y, z, w;
            public static Quat From(Quaternion value) => new() { x = value.x, y = value.y, z = value.z, w = value.w };
        }
    }
}
