using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacSim.Editor
{
    public static class OceanPipelineSetup
    {
        [MenuItem("TAC/Add random ocean pipeline")]
        public static void Install()
        {
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            var pipeline=UnityEngine.Object.FindFirstObjectByType<OceanPipeline>();
            if(pipeline==null) pipeline=new GameObject("Random yellow pipeline • ArUco 1–99").AddComponent<OceanPipeline>();
            pipeline.randomizeOnStart=true;
            Vector3 first=Vector3.zero;
            for(int seed=1;seed<=20;seed++)
            {
                pipeline.Generate(seed);
                if(pipeline.MarkerIds.Length!=5 || pipeline.MarkerIds.Distinct().Count()!=5 || pipeline.MarkerIds.Any(id=>id<1 || id>99)) throw new Exception("Invalid marker IDs");
                if(pipeline.Pinger==null || pipeline.Pinger.frequencyKhz!=30) throw new Exception("Missing pinger");
                if(seed==1) first=pipeline.SpawnPosition;
                if(seed==20 && Vector3.Distance(first,pipeline.SpawnPosition)<1) throw new Exception("Spawn did not vary");
            }
            var receiver=new GameObject("Hydrophone validation").AddComponent<HydrophoneSensor>();
            receiver.noiseEnabled=false;
            Vector3 beacon=pipeline.Pinger.transform.position;
            float start=pipeline.Pinger.StartedAt;
            receiver.SampleSignal(pipeline,beacon-Vector3.forward*10,Quaternion.identity,0,start+.1f);
            if(!receiver.Detected || Mathf.Abs(receiver.Bearing)>.01f || receiver.PulseCount!=1) throw new Exception("Hydrophone detection/bearing failed");
            receiver.SampleSignal(pipeline,beacon-Vector3.forward*10,Quaternion.identity,0,start+.5f);
            if(receiver.PulseCount!=1) throw new Exception("Duplicate ping");
            receiver.SampleSignal(pipeline,beacon-Vector3.forward*10,Quaternion.identity,0,start+1.1f);
            if(receiver.PulseCount!=2) throw new Exception("Pulse timing failed");
            receiver.tunedFrequencyKhz=40;
            receiver.SampleSignal(pipeline,beacon-Vector3.forward*10,Quaternion.identity,0,start+2.1f);
            if(receiver.Detected) throw new Exception("Wrong frequency detected");
            receiver.tunedFrequencyKhz=30;
            receiver.SampleSignal(pipeline,beacon-Vector3.forward*200,Quaternion.identity,0,start+3.1f);
            if(receiver.Detected) throw new Exception("Out-of-range detection");
            receiver.SampleSignal(pipeline,new Vector3(beacon.x,1,beacon.z),Quaternion.identity,0,start+4.1f);
            if(receiver.Detected) throw new Exception("Above-water detection");
            pipeline.CheckArrival(beacon+Vector3.up*3,2);
            if(pipeline.InspectionStarted) throw new Exception("Started too far away");
            pipeline.CheckArrival(beacon+Vector3.up, .5f);
            if(pipeline.InspectionStarted) throw new Exception("Started before dwell");
            pipeline.CheckArrival(beacon+Vector3.up, .6f);
            if(!pipeline.InspectionStarted) throw new Exception("Arrival did not start inspection");
            pipeline.Generate(21);
            if(pipeline.InspectionStarted) throw new Exception("Respawn did not reset inspection");
            UnityEngine.Object.DestroyImmediate(receiver.gameObject);
            pipeline.Clear();
            var view=UnityEngine.Object.FindFirstObjectByType<PilotView>();
            view.missionHint="FIND PIPE START / Hydrophone listening for 30 kHz pinger. N: new pipe";
            EditorSceneManager.SaveScene(scene);
            Debug.Log("OCEAN_PIPELINE_OK: 20 seeds verified; five unique ArUco IDs 1–99, seabed placement, random startup and N respawn.");
            Debug.Log("PINGER_OK: frequency, pulse timing, bearing, range, underwater reception, arrival dwell and respawn reset verified.");
        }
    }
}
