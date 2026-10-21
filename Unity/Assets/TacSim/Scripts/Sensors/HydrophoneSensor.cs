using System.Text;
using UnityEngine;

namespace TacSim
{
    // Directional hydrophone-array approximation: bearing and relative level, no passive range estimate.
    // Omits multipath/refraction and acoustic shadows; silt does not block acoustic detection.
    public sealed class HydrophoneSensor : RovSensor
    {
        public float tunedFrequencyKhz = 30;
        public float bandwidthKhz = 1;
        public bool Detected { get; private set; }
        public float Bearing { get; private set; }
        public float Elevation { get; private set; }
        public float Level { get; private set; }
        public int PulseCount { get; private set; }
        public override string Kind => "hydrophone";
        public float LastPingTime { get; private set; } = -1;
        PipelinePinger source;
        int lastPulse = -1;
        public override void ResetState()
        {
            base.ResetState(); source=null; lastPulse=-1; Detected=false; PulseCount=0; LastPingTime=-1;
            Bearing=Elevation=Level=0;
        }
        void FixedUpdate()
        {
            if(vehicle==null || !SampleDue(20)) return;
            var pipeline=FindFirstObjectByType<OceanPipeline>();
            SampleSignal(pipeline,MountPosition,MountRotation,vehicle.waterSurface,Time.time);
        }
        public void SampleSignal(OceanPipeline pipeline,Vector3 position,Quaternion rotation,float waterSurface,float now)
        {
            var next=pipeline!=null ? pipeline.Pinger : null;
            if(next!=source) {ResetState();source=next;}
            SampleTime=now;
            if(source==null || !source.isActiveAndEnabled) {Detected=false;return;}
            Vector3 delta=source.transform.position-position;
            float distance=delta.magnitude;
            if(position.y>=waterSurface || source.transform.position.y>=waterSurface ||
                distance>source.detectionRange || Mathf.Abs(source.frequencyKhz-tunedFrequencyKhz)>bandwidthKhz*.5f)
            {Detected=false;Level=0;return;}
            int pulse=source.ReceivedPulse(now,distance);
            if(pulse>=0 && pulse>lastPulse)
            {
                lastPulse=pulse;PulseCount++;LastPingTime=now;Detected=true;
                Vector3 local=Quaternion.Inverse(rotation)*delta;
                Bearing=Mathf.DeltaAngle(0,Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg+Gauss(1));
                Elevation=Mathf.Atan2(local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg+Gauss(1);
                Level=Mathf.Clamp01(1/(1+distance*distance/100)+Gauss(.005f));
            }
            if(now-LastPingTime>source.intervalSeconds*2.5f) Detected=false;
        }
        public override void AppendTelemetry(StringBuilder text)
        {
            text.AppendLine("HYDROPHONE / simulated directional array");
            text.AppendLine(Detected ? $"  {tunedFrequencyKhz:F0} kHz  bearing {Bearing:+0.0;-0.0;0} deg  elevation {Elevation:F1} deg  level {Level:P0}  ping {PulseCount}" : "  listening / no matching underwater signal");
        }
    }
}
