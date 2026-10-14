using System;
using UnityEngine;

namespace TacSim
{
    // JSON shapes for the automation API's sensor block. Field names follow each vendor's own
    // output where one exists (Water Linked DVL velocity report, VectorNav register names).
    [Serializable]
    public struct SensorVector
    {
        public float x, y, z;
        public static SensorVector From(Vector3 value) => new() { x = value.x, y = value.y, z = value.z };
    }

    [Serializable]
    public sealed class ImuReading
    {
        public double time;
        public string model;
        public SensorVector acceleration;
        public SensorVector angular_rate;
        public SensorVector magnetic;
        public float yaw, pitch, roll;
        public float[] quaternion;
        public float temperature;
        public float pressure_kpa;
    }

    [Serializable]
    public sealed class DvlTransducer
    {
        public int id;
        public float velocity, distance, rssi, nsd;
        public bool beam_valid;
    }

    [Serializable]
    public sealed class DvlPosition
    {
        public float x, y, z, std, roll, pitch, yaw;
    }

    [Serializable]
    public sealed class DvlReading
    {
        public double time;
        public string model;
        public float vx, vy, vz, fom, altitude, time_ms;
        public bool velocity_valid;
        public int status;
        public DvlTransducer[] transducers;
        public DvlPosition dead_reckoning;
    }

    [Serializable]
    public sealed class PressureReading
    {
        public double time;
        public string model;
        public float pressure_mbar, temperature_c, depth_m, surface_pressure_mbar;
    }

    [Serializable]
    public sealed class SonarReading
    {
        public double time;
        public string model;
        public int frequency_khz, beams, bins;
        public float horizontal_aperture, vertical_aperture, range, min_range, ping_rate;
        public float[] beam_ranges;
        public string image_base64;
    }

    [Serializable]
    public sealed class SensorReadings
    {
        public int seed;
        public bool noise;
        public ImuReading imu;
        public DvlReading dvl;
        public PressureReading pressure;
        public SonarReading sonar;
    }

    [Serializable]
    public sealed class CameraImage
    {
        public string name;
        public string model;
        public int width, height;
        public double time;
        public string jpeg_base64;
    }

    [Serializable]
    public sealed class SensorInfo
    {
        public string name;
        public string kind;
        public string model;
        // Mount pose relative to the vehicle origin: FRD metres and roll/pitch/yaw degrees.
        public SensorVector mount_position;
        public SensorVector mount_rpy;
        public float rate;
        // Cameras only: equidistant fisheye intrinsics at the native resolution.
        public int native_width, native_height;
        public float focal_px, image_circle, port_refraction, latency;
        public SensorVector field_of_view;
    }

    public static class SensorTelemetry
    {
        public static readonly string[] CameraNames = { "explorehd", "fisheye" };

        public static FisheyeCameraSensor FindCamera(SensorSuite suite, string name)
        {
            return name?.ToLowerInvariant() switch
            {
                "explorehd" or "forward" => suite.ForwardCamera,
                "fisheye" or "imx307" or "downward" => suite.DownwardCamera,
                _ => null
            };
        }

        public static SensorReadings Read(SensorSuite suite, bool sonarImage)
        {
            var readings = new SensorReadings { seed = suite.Seed, noise = suite.NoiseEnabled };
            if (suite.Imu is { } imu)
            {
                Vector4 q = imu.AttitudeQuaternion;
                readings.imu = new ImuReading
                {
                    time = imu.SampleTime,
                    model = imu.model,
                    acceleration = SensorVector.From(imu.Acceleration),
                    angular_rate = SensorVector.From(imu.AngularRate),
                    magnetic = SensorVector.From(imu.MagneticField),
                    yaw = imu.YawPitchRoll.x,
                    pitch = imu.YawPitchRoll.y,
                    roll = imu.YawPitchRoll.z,
                    quaternion = new[] { q.x, q.y, q.z, q.w },
                    temperature = imu.Temperature,
                    pressure_kpa = imu.EnclosurePressure
                };
            }
            if (suite.Dvl is { } dvl)
            {
                var transducers = new DvlTransducer[dvl.Beams.Length];
                for (int i = 0; i < transducers.Length; i++)
                {
                    DopplerVelocityLog.Beam beam = dvl.Beams[i];
                    transducers[i] = new DvlTransducer
                    {
                        id = beam.id, velocity = beam.velocity, distance = beam.distance,
                        rssi = beam.rssi, nsd = beam.nsd, beam_valid = beam.valid
                    };
                }
                readings.dvl = new DvlReading
                {
                    time = dvl.SampleTime,
                    model = dvl.model,
                    vx = dvl.Velocity.x, vy = dvl.Velocity.y, vz = dvl.Velocity.z,
                    fom = dvl.FigureOfMerit,
                    altitude = dvl.Altitude,
                    time_ms = dvl.PingInterval * 1000,
                    velocity_valid = dvl.VelocityValid,
                    status = dvl.Status,
                    transducers = transducers,
                    dead_reckoning = new DvlPosition
                    {
                        x = dvl.DeadReckoning.x, y = dvl.DeadReckoning.y, z = dvl.DeadReckoning.z,
                        std = dvl.DeadReckoningStd,
                        roll = dvl.Orientation.x, pitch = dvl.Orientation.y, yaw = dvl.Orientation.z
                    }
                };
            }
            if (suite.Pressure is { } pressure)
            {
                readings.pressure = new PressureReading
                {
                    time = pressure.SampleTime,
                    model = pressure.model,
                    pressure_mbar = pressure.Pressure,
                    temperature_c = pressure.Temperature,
                    depth_m = pressure.Depth,
                    surface_pressure_mbar = pressure.SurfacePressure
                };
            }
            if (suite.Sonar is { } sonar)
            {
                readings.sonar = new SonarReading
                {
                    time = sonar.SampleTime,
                    model = sonar.model,
                    frequency_khz = sonar.FrequencyKhz,
                    beams = sonar.beamCount,
                    bins = sonar.rangeBins,
                    horizontal_aperture = sonar.HorizontalAperture,
                    vertical_aperture = sonar.verticalAperture,
                    range = sonar.range,
                    min_range = sonar.minRange,
                    ping_rate = sonar.EffectivePingRate,
                    beam_ranges = sonar.BeamRanges,
                    image_base64 = sonarImage && sonar.Image != null ? Convert.ToBase64String(sonar.Image) : null
                };
            }
            return readings;
        }

        public static SensorInfo[] Describe(SensorSuite suite)
        {
            var info = new SensorInfo[suite.Sensors.Count];
            for (int i = 0; i < info.Length; i++)
            {
                RovSensor sensor = suite.Sensors[i];
                Vector3 ypr = SensorFrames.YawPitchRoll(sensor.transform.localRotation);
                info[i] = new SensorInfo
                {
                    name = sensor.name,
                    kind = sensor.Kind,
                    model = sensor.model,
                    mount_position = SensorVector.From(SensorFrames.ToFrd(sensor.transform.localPosition)),
                    mount_rpy = SensorVector.From(new Vector3(ypr.z, ypr.y, Mathf.DeltaAngle(0, ypr.x)))
                };
                switch (sensor)
                {
                    case FisheyeCameraSensor camera:
                        info[i].name = camera == suite.ForwardCamera ? "explorehd" : "fisheye";
                        info[i].rate = camera.frameRate;
                        info[i].native_width = camera.nativeWidth;
                        info[i].native_height = camera.nativeHeight;
                        info[i].focal_px = camera.FocalNative;
                        info[i].image_circle = camera.imageCircle;
                        info[i].port_refraction = camera.portRefraction;
                        info[i].latency = camera.latency;
                        info[i].field_of_view = SensorVector.From(camera.FieldOfView);
                        break;
                    case DopplerVelocityLog dvl: info[i].rate = dvl.PingRate; break;
                    case PressureSensor pressure: info[i].rate = pressure.rate; break;
                    case InertialSensor imu: info[i].rate = imu.rate; break;
                    case ImagingSonar sonar: info[i].rate = sonar.EffectivePingRate; break;
                }
            }
            return info;
        }
    }
}
