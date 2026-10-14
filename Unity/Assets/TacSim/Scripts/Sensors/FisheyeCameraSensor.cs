using System;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace TacSim
{
    // Equidistant fisheye camera (r = f·θ). Five 90° faces are rendered around the mount and
    // re-projected through the lens model, so field of view, image circle and corner cropping
    // follow the real optics. Frames pass through a latency buffer like a video pipeline.
    public sealed class FisheyeCameraSensor : RovSensor
    {
        [Header("Optics (in water)")]
        public int nativeWidth = 1920;
        public int nativeHeight = 1080;
        public float frameRate = 30;
        [Tooltip("Diagonal field of view in degrees; ignored when focalLengthPixels is set")]
        public float diagonalFov = 150;
        [Tooltip("Focal length in native pixels; 0 derives it from the diagonal field of view")]
        public float focalLengthPixels;
        [Tooltip("Lens image circle in degrees; a circle smaller than the sensor leaves black edges")]
        public float imageCircle = 180;
        [Tooltip("1 for a dome port or a lens specified in water; 1.333 for an in-air lens behind a flat port")]
        public float portRefraction = 1;
        public string mounting = "forward";

        [Header("Imaging pipeline")]
        [Tooltip("Glass-to-glass latency in seconds, rounded to whole frames")]
        public float latency = 0.1f;
        [Range(0, 0.2f)] public float sensorNoise = 0.015f;
        [Range(0, 1)] public float vignetting = 0.3f;
        public float gain = 1;
        [Tooltip("Width actually rendered; telemetry and the API still report the native format")]
        public int renderWidth = 960;
        public float nearClip = 0.03f;
        public float farClip = 70;

        static readonly Quaternion[] FaceRotations =
        {
            Quaternion.identity, Quaternion.Euler(0, 90, 0), Quaternion.Euler(0, -90, 0),
            Quaternion.Euler(-90, 0, 0), Quaternion.Euler(90, 0, 0)
        };
        static readonly string[] FaceProperties = { "_FaceFront", "_FaceRight", "_FaceLeft", "_FaceUp", "_FaceDown" };

        Camera faceCamera;
        readonly RenderTexture[] faces = new RenderTexture[5];
        RenderTexture[] frames;
        double[] frameTimes;
        int newest = -1;
        int frameCount;
        Material projection;
        float activeUntil = -1;
        double nextFrame;
        bool failed;

        public override string Kind => "camera";
        public int RenderHeight => Mathf.Max(1, Mathf.RoundToInt(renderWidth * (float)nativeHeight / nativeWidth));
        public float FocalNative => focalLengthPixels > 0 ? focalLengthPixels
            : 0.5f * Mathf.Sqrt(nativeWidth * nativeWidth + nativeHeight * nativeHeight) / (diagonalFov * 0.5f * Mathf.Deg2Rad);
        float FocalRender => FocalNative * renderWidth / nativeWidth;
        float MaxTheta => imageCircle * 0.5f * Mathf.Deg2Rad;
        // Snell's law at a flat port compresses the in-air field into a narrower cone in water.
        float WaterAngle(float airAngle) => Mathf.Asin(Mathf.Sin(airAngle) / Mathf.Max(1, portRefraction));
        int DelayFrames => Mathf.Max(0, Mathf.RoundToInt(latency * frameRate));
        public bool Active => Time.unscaledTime < activeUntil;

        // Horizontal, vertical and diagonal coverage in degrees, limited by the image circle.
        public Vector3 FieldOfView
        {
            get
            {
                float f = FocalNative;
                float Angle(float halfPixels)
                {
                    float air = Mathf.Min(halfPixels / f, MaxTheta);
                    return 2 * (portRefraction > 1 ? WaterAngle(Mathf.Min(air, Mathf.PI / 2)) : air) * Mathf.Rad2Deg;
                }
                return new Vector3(Angle(nativeWidth / 2f), Angle(nativeHeight / 2f),
                    Angle(0.5f * Mathf.Sqrt(nativeWidth * nativeWidth + nativeHeight * nativeHeight)));
            }
        }

        // Latest frame that has made it through the latency buffer, or null before the first frame.
        public RenderTexture Output => frameCount == 0 ? null : frames[DeliveredIndex];
        public double OutputTime => frameCount == 0 ? -1 : frameTimes[DeliveredIndex];
        int DeliveredIndex => (newest - Mathf.Min(frameCount - 1, DelayFrames) + frames.Length) % frames.Length;

        // Cameras render only while something displays or captures them.
        public void KeepActive(float seconds = 0.5f) => activeUntil = Mathf.Max(activeUntil, Time.unscaledTime + seconds);

        void LateUpdate()
        {
            if (!Active || failed) return;
            double now = Time.timeAsDouble;
            if (now + 1e-6 < nextFrame) return;
            double period = 1.0 / Mathf.Max(1, frameRate);
            nextFrame = nextFrame + period < now ? now + period : nextFrame + period;
            RenderFrame();
        }

        public override void ResetState()
        {
            base.ResetState();
            frameCount = 0;
            newest = -1;
        }

        void RenderFrame()
        {
            if (!EnsureResources()) return;
            Camera main = Camera.main;
            if (main != null) faceCamera.backgroundColor = main.backgroundColor;
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            for (int i = 0; i < faces.Length; i++)
            {
                faceCamera.transform.SetPositionAndRotation(position, rotation * FaceRotations[i]);
                faceCamera.targetTexture = faces[i];
                faceCamera.Render();
            }
            faceCamera.targetTexture = null;

            newest = (newest + 1) % frames.Length;
            frameCount = Mathf.Min(frameCount + 1, frames.Length);
            frameTimes[newest] = Time.timeAsDouble;
            SampleTime = Time.timeAsDouble;
            projection.SetVector("_Projection", new Vector4(FocalRender, MaxTheta, renderWidth, RenderHeight));
            projection.SetFloat("_PortRefraction", Mathf.Max(1, portRefraction));
            projection.SetVector("_Sensor", new Vector4(noiseEnabled ? sensorNoise : 0, vignetting,
                Noise.Uniform(0, 1000), gain));
            RenderTexture previous = RenderTexture.active;
            Graphics.SetRenderTarget(frames[newest]);
            projection.SetPass(0);
            Graphics.DrawProceduralNow(MeshTopology.Triangles, 3);
            RenderTexture.active = previous;
        }

        bool EnsureResources()
        {
            if (faceCamera != null) return true;
            Shader shader = Resources.Load<Shader>("FisheyeProjection");
            if (shader == null || !shader.isSupported)
            {
                Debug.LogError($"TAC_SENSOR_FAILED: fisheye projection shader unavailable for {model}");
                failed = true;
                return false;
            }
            projection = new Material(shader) { hideFlags = HideFlags.DontSave };

            var rig = new GameObject($"{name} render rig") { hideFlags = HideFlags.DontSave };
            rig.transform.SetParent(transform, false);
            faceCamera = rig.AddComponent<Camera>();
            faceCamera.enabled = false;
            faceCamera.fieldOfView = 90;
            faceCamera.aspect = 1;
            faceCamera.nearClipPlane = nearClip;
            faceCamera.farClipPlane = farClip;
            faceCamera.clearFlags = CameraClearFlags.SolidColor;
            faceCamera.allowHDR = false;
            faceCamera.allowMSAA = false;
            // The vehicle's own CAD geometry is excluded: it is costly and would occlude the faces.
            faceCamera.cullingMask = ~(1 << SensorSuite.VehicleLayer);
            var data = rig.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = true;

            int faceSize = Mathf.Clamp(Mathf.CeilToInt(FocalRender * Mathf.PI / 2 / 32) * 32, 128, 2048);
            for (int i = 0; i < faces.Length; i++)
            {
                faces[i] = new RenderTexture(faceSize, faceSize, 24, RenderTextureFormat.ARGB32)
                {
                    name = $"{name} face {i}",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                faces[i].Create();
                projection.SetTexture(FaceProperties[i], faces[i]);
            }
            frames = new RenderTexture[DelayFrames + 1];
            frameTimes = new double[frames.Length];
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i] = new RenderTexture(renderWidth, RenderHeight, 0, RenderTextureFormat.ARGB32)
                {
                    name = $"{name} frame {i}",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                frames[i].Create();
            }
            return true;
        }

        // JPEG of the delivered frame at the requested size; the first request renders immediately.
        public string CaptureJpeg(int width, int height, int quality, out double frameTime)
        {
            KeepActive(2);
            if (Output == null) RenderFrame();
            frameTime = OutputTime;
            if (Output == null) throw new InvalidOperationException($"{model} could not render");
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                // Match the API's pilot-camera captures, which clients flip from a bottom-left origin.
                Graphics.Blit(Output, target, new Vector2(1, -1), new Vector2(0, 1));
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply(false);
                return Convert.ToBase64String(image.EncodeToJPG(quality));
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Destroy(image);
            }
        }

        public override void AppendTelemetry(StringBuilder text)
        {
            Vector3 fov = FieldOfView;
            text.AppendLine($"{SensorOverlay.Header("CAMERA")}  {model}  ({mounting})");
            text.AppendLine($"  {nativeWidth}×{nativeHeight} @ {frameRate:0} fps   FOV {fov.x:0}°×{fov.y:0}° ({fov.z:0}° diag)"
                + (portRefraction > 1 ? "  flat port" : ""));
            string state = Active ? $"live, frame {Time.timeAsDouble - OutputTime:0.00} s old" : "idle";
            text.AppendLine($"  latency {latency * 1000:0} ms   {state}");
        }

        void OnDestroy()
        {
            foreach (RenderTexture face in faces) if (face != null) Destroy(face);
            if (frames != null) foreach (RenderTexture frame in frames) if (frame != null) Destroy(frame);
            if (projection != null) Destroy(projection);
            if (faceCamera != null) Destroy(faceCamera.gameObject);
        }
    }
}
