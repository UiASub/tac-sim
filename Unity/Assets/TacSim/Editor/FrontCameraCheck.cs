using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace TacSim.Editor
{
    public static class FrontCameraCheck
    {
        [InitializeOnLoadMethod] static void Init() { EditorApplication.update -= Poll; EditorApplication.update += Poll; }
        static void Poll()
        {
            const string request="Temp/front-camera.request";
            if(!File.Exists(request)||EditorApplication.isCompiling||EditorApplication.isUpdating) return;
            string action=File.ReadAllText(request).Trim(); File.Delete(request);
            var dive=Object.FindFirstObjectByType<TeknologidagenDive>();
            if(action=="assist-test") { TestAssist(dive); return; }
            var camera=dive.diveCamera;
            File.WriteAllText("Temp/front-camera.txt", "Playing="+Application.isPlaying+"\n"+string.Join("\n",dive.vehicle.GetComponentsInChildren<Renderer>(true).Select(r=>r.name+" enabled="+r.enabled+" active="+r.gameObject.activeInHierarchy+" local="+dive.vehicle.transform.InverseTransformPoint(r.bounds.center)+" size="+r.bounds.size)));
            if(!Application.isPlaying) return;
            camera.GetComponent<PilotView>().SetMode(1);
            EditorApplication.delayCall+=()=> {
                var rt=new RenderTexture(1000,650,24); var old=RenderTexture.active; var target=camera.targetTexture;
                camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt;
                var image=new Texture2D(1000,650,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,1000,650),0,0); image.Apply();
                File.WriteAllBytes("Temp/front-camera.png",image.EncodeToPNG());
                camera.targetTexture=target; RenderTexture.active=old; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(image);
            };
        }
        static void TestAssist(TeknologidagenDive dive)
        {
            var assist=dive.GetComponent<EasyValveAssist>();
            var body=dive.vehicle.Body;
            var position=body.position; var rotation=body.rotation; var oldConstraints=body.constraints;
            var angles=dive.Valves.Select(v=>v.angle).ToArray();
            try
            {
                if(dive.diveCamera.GetComponent<PilotView>().enabled) throw new System.Exception("Old HUD still enabled");
                for(int i=0;i<dive.Valves.Length;i++)
                {
                    var valve=dive.Valves[i];
                    var mount=dive.Arm.transform.localPosition;
                    Quaternion facing=Mathf.Abs(valve.transform.up.y)>.9f ? Quaternion.identity : Quaternion.LookRotation(-valve.transform.up);
                    Vector3 origin=assist.Socket(valve)+valve.transform.up*.8f;
                    body.position=origin-facing*mount; body.rotation=facing;
                    dive.vehicle.transform.SetPositionAndRotation(body.position,facing); Physics.SyncTransforms();
                    if(!assist.Connect(valve)) throw new System.Exception("Failed nearby connect: "+valve.name);
                    assist.Turn(1,3);
                    if(valve.angle<89 || !valve.Completed) throw new System.Exception("Open failed");
                    assist.Turn(-1,3);
                    if(valve.angle>1) throw new System.Exception("Close failed");
                    assist.Release();
                    if(body.constraints!=oldConstraints || !dive.Arm.enabled) throw new System.Exception("Release did not restore movement");
                    body.position+=valve.transform.up*5; dive.vehicle.transform.position=body.position; Physics.SyncTransforms();
                    if(assist.Connect(valve)) throw new System.Exception("Remote connect accepted");
                }
                File.WriteAllText("Temp/assist-test.txt","PASS: compact HUD; nearby connect, open, close, release and out-of-range rejection for every valve.");
            }
            catch(System.Exception e) { File.WriteAllText("Temp/assist-test.txt",e.ToString()); }
            finally
            {
                assist.Release(); body.position=position; body.rotation=rotation; dive.vehicle.transform.SetPositionAndRotation(position,rotation);
                body.linearVelocity=Vector3.zero; body.angularVelocity=Vector3.zero;
                for(int i=0;i<angles.Length;i++) dive.Valves[i].SetAngle(angles[i]);
                Physics.SyncTransforms();
            }
        }
    }
}
