using System;
using System.Collections.Generic;
using UnityEngine;

namespace TacSim
{
    // The Malstrøm payload: installs each simulated sensor at its mounting pose on the vehicle,
    // owns the noise seed and resets sensor state together with the vehicle.
    public sealed class SensorSuite : MonoBehaviour
    {
        // Named "Vehicle" in TagManager; sensor cameras and acoustic rays ignore this layer.
        public const int VehicleLayer = 6;
        public const int VehicleMask = 1 << VehicleLayer;
        public static SensorSuite Instance { get; private set; }

        public RovVehicle vehicle;
        public bool TelemetryVisible { get; set; } = true;
        public bool BeamsVisible { get; set; } = true;
        public int Seed { get; private set; }
        public bool NoiseEnabled { get; private set; } = true;
        readonly List<RovSensor> sensors = new();
        public IReadOnlyList<RovSensor> Sensors => sensors;

        public FisheyeCameraSensor ForwardCamera { get; private set; }
        public FisheyeCameraSensor DownwardCamera { get; private set; }
        public DopplerVelocityLog Dvl { get; private set; }
        public PressureSensor Pressure { get; private set; }
        public InertialSensor Imu { get; private set; }
        public ImagingSonar Sonar { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            RovVehicle found = FindFirstObjectByType<RovVehicle>();
            if (found != null) Install(found);
        }

        public static SensorSuite Install(RovVehicle target)
        {
            var suite = target.GetComponent<SensorSuite>();
            if (suite == null) suite = target.gameObject.AddComponent<SensorSuite>();
            suite.Configure(target);
            return suite;
        }

        void Configure(RovVehicle target)
        {
            if (vehicle == target && sensors.Count > 0) return;
            vehicle = target;
            Instance = this;
            string[] args = Environment.GetCommandLineArgs();
            Seed = TryGetInt(args, "-sensorSeed", out int seed) ? seed : 1;
            SetLayer(vehicle.gameObject, VehicleLayer);

            // Mounting poses are relative to the 0.41 × 0.32 × 0.52 m Malstrøm hull centre.
            ForwardCamera = Mount<FisheyeCameraSensor>("exploreHD forward camera",
                new Vector3(0, 0.02f, 0.27f), Vector3.zero, camera =>
                {
                    // exploreHD 3.0: 1/2.9" Sony CMOS, 1080p30 H.264/MJPEG, 150° diagonal in water.
                    camera.model = "DeepWater Exploration exploreHD 3.0";
                    camera.mounting = "forward";
                    camera.nativeWidth = 1920;
                    camera.nativeHeight = 1080;
                    camera.frameRate = 30;
                    camera.diagonalFov = 150;
                    camera.imageCircle = 180;
                    camera.latency = 0.1f;
                    camera.sensorNoise = 0.012f;
                    camera.vignetting = 0.3f;
                });

            DownwardCamera = Mount<FisheyeCameraSensor>("IMX307 fisheye downward camera",
                new Vector3(0, -0.15f, 0.15f), new Vector3(90, 0, 0), camera =>
                {
                    // Sony IMX307 1/2.8" (2.9 µm pixels) behind a 1.5 mm 180° fisheye; the "3MP"
                    // stream is the ISP's 2304×1296 upscale, so f = 1.5 mm / 2.9 µm × 1.2 = 621 px.
                    // The 180° image circle overfills the sensor height and leaves black sides.
                    camera.model = "WGWK IMX307 3MP PoE, 1.5 mm 180°";
                    camera.mounting = "down, top = fwd";
                    camera.nativeWidth = 2304;
                    camera.nativeHeight = 1296;
                    camera.frameRate = 25;
                    camera.focalLengthPixels = 1.5f / 0.0029f * 2304 / 1920;
                    camera.imageCircle = 180;
                    // Assumes a dome port; set 1.333 for a flat port, which narrows the view to ~97°.
                    camera.portRefraction = 1;
                    // RTSP/H.265 IP cameras typically add 150-300 ms before the frame is decoded.
                    camera.latency = 0.2f;
                    camera.sensorNoise = 0.02f;
                    camera.vignetting = 0.45f;
                });

            // Transducer face below the skids, aligned with the hull (LED/X axis forward).
            Dvl = Mount<DopplerVelocityLog>("DVL A50", new Vector3(0, -0.18f, -0.05f), Vector3.zero,
                dvl => dvl.model = "Water Linked DVL A50");

            // Penetrator on the rear end cap of the electronics enclosure.
            bool bar100 = TryGetString(args, "-pressureSensor", out string variant)
                && variant.Equals("bar100", StringComparison.OrdinalIgnoreCase);
            Pressure = Mount<PressureSensor>("Bar30/Bar100 depth sensor", new Vector3(0, 0.06f, -0.24f), Vector3.zero,
                sensor => sensor.variant = bar100 ? PressureSensorVariant.Bar100 : PressureSensorVariant.Bar30);

            // Inside the electronics enclosure, axes aligned with the hull.
            Imu = Mount<InertialSensor>("VN-100 IMU", new Vector3(0, 0, 0.05f), Vector3.zero,
                imu => imu.model = "VectorNav VN-100 IMU/AHRS");

            // Front of the frame below the camera, tilted 15° down so the fan sweeps the floor ahead.
            Sonar = Mount<ImagingSonar>("Sonoptix ECHO sonar", new Vector3(0, -0.08f, 0.27f), new Vector3(15, 0, 0),
                sonar => sonar.model = "Sonoptix ECHO multibeam imaging sonar");

            vehicle.VehicleReset += ResetSensors;
            if (Array.IndexOf(args, "-noSensorNoise") >= 0) SetNoise(false);
            Debug.Log($"TAC_SENSORS_READY: {sensors.Count} sensors, seed {Seed}");
        }

        T Mount<T>(string mountName, Vector3 position, Vector3 euler, Action<T> defaults) where T : RovSensor
        {
            Transform mount = vehicle.transform.Find(mountName);
            T sensor = mount != null ? mount.GetComponent<T>() : null;
            if (sensor == null)
            {
                if (mount == null)
                {
                    mount = new GameObject(mountName).transform;
                    mount.SetParent(vehicle.transform, false);
                    mount.SetLocalPositionAndRotation(position, Quaternion.Euler(euler));
                    mount.gameObject.layer = VehicleLayer;
                }
                sensor = mount.gameObject.AddComponent<T>();
                defaults(sensor);
            }
            sensor.Initialise(vehicle, SensorSeed(Seed, sensors.Count));
            sensors.Add(sensor);
            return sensor;
        }

        // A new seed redraws every unit error (biases, scale factors) for domain randomisation.
        public void Reseed(int seed)
        {
            Seed = seed;
            for (int i = 0; i < sensors.Count; i++) sensors[i].Reseed(SensorSeed(seed, i));
        }

        public void SetNoise(bool enabled)
        {
            NoiseEnabled = enabled;
            foreach (RovSensor sensor in sensors) sensor.noiseEnabled = enabled;
        }

        public T Get<T>() where T : RovSensor
        {
            foreach (RovSensor sensor in sensors) if (sensor is T match) return match;
            return null;
        }

        void ResetSensors()
        {
            foreach (RovSensor sensor in sensors) sensor.ResetState();
        }

        static int SensorSeed(int seed, int index) => unchecked(seed * 486187739 + (index + 1) * 16777619);

        public static bool TryGetString(string[] args, string name, out string value)
        {
            int index = Array.IndexOf(args, name);
            value = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
            return value != null;
        }

        public static bool TryGetInt(string[] args, string name, out int value)
        {
            value = 0;
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out value);
        }

        public static void SetLayer(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform) SetLayer(child.gameObject, layer);
        }

        void OnDestroy()
        {
            if (vehicle != null) vehicle.VehicleReset -= ResetSensors;
            if (Instance == this) Instance = null;
        }
    }
}
