using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TacSim
{
    // Metre-scale yellow inspection pipe; marker convention matches the existing TAC course.
    public sealed class OceanPipeline : MonoBehaviour
    {
        public bool randomizeOnStart = true;
        public int seed = 1;
        [Range(4,10)] public int markerCount = 5;
        public float diameter = .2f;
        public float markerSize = .15f;
        public int ActiveSeed { get; private set; }
        public int[] MarkerIds { get; private set; }
        public Vector3 SpawnPosition { get; private set; }
        public PipelinePinger Pinger { get; private set; }
        public bool InspectionStarted { get; private set; }
        public event Action InspectionStartReached;
        public float arrivalRadius = 1.5f;
        public float arrivalHoldSeconds = 1;
        float arrivalTime;
        RovVehicle trackedVehicle;
        Transform generated;
        Mesh pipeMesh;
        Material yellow;
        readonly List<Vector3> centreline = new();
        void Start()
        {
            trackedVehicle=FindFirstObjectByType<RovVehicle>();
            if(trackedVehicle!=null) trackedVehicle.VehicleReset+=ResetInspection;
            Generate(randomizeOnStart ? Guid.NewGuid().GetHashCode() : seed);
        }
        public void ResetInspection() { InspectionStarted=false; arrivalTime=0; }
        public void CheckArrival(Vector3 position,float dt)
        {
            if(InspectionStarted || Pinger==null) return;
            arrivalTime=Vector3.Distance(position,Pinger.transform.position)<=arrivalRadius ? arrivalTime+dt : 0;
            if(arrivalTime>=arrivalHoldSeconds) {InspectionStarted=true;InspectionStartReached?.Invoke();}
        }
        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.nKey.wasPressedThisFrame) Generate(Guid.NewGuid().GetHashCode());
            if(trackedVehicle!=null) CheckArrival(trackedVehicle.transform.position,Time.deltaTime);
            var view=FindFirstObjectByType<PilotView>();
            var hydrophone=SensorSuite.Instance!=null ? SensorSuite.Instance.Hydrophone : null;
            if(view!=null) view.missionHint=InspectionStarted ? "PIPE START REACHED / Follow the yellow pipe and inspect ArUco markers. N: new pipe"
                : hydrophone!=null && hydrophone.Detected ? $"PINGER 30 kHz / bearing {hydrophone.Bearing:+0;-0;0} deg, elevation {hydrophone.Elevation:+0;-0;0} deg / Approach pipe start."
                : "FIND PIPE START / Hydrophone listening for 30 kHz pinger. N: new pipe";
        }
        public void Clear()
        {
            Pinger=null;ResetInspection();
            if (generated != null) { generated.gameObject.SetActive(false); Release(generated.gameObject); }
            if (pipeMesh != null) Release(pipeMesh);
            if (yellow != null) Release(yellow);
            generated = null; pipeMesh = null; yellow = null;
        }
        static void Release(UnityEngine.Object value) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        void OnDestroy() { if(trackedVehicle!=null) trackedVehicle.VehicleReset-=ResetInspection; if (pipeMesh != null) Release(pipeMesh); if (yellow != null) Release(yellow); }
        public void Generate(int value)
        {
            ActiveSeed = value;
            Clear();
            var rng = new System.Random(value);
            var bottom = new List<MeshCollider>();
            foreach (var collider in FindObjectsByType<MeshCollider>(FindObjectsSortMode.None))
                if (collider.transform.root.name.StartsWith("Seabed")) bottom.Add(collider);
            if (bottom.Count == 0) throw new InvalidOperationException("Ocean pipeline requires seabed colliders.");
            Physics.SyncTransforms();
            // The long first leg and three rounded bends follow the supplied photograph.
            Vector3[] corners = { new(0,0,-4), new(0,0,0), new(-1.4f,0,2), new(-3.5f,0,1.7f), new(-4,0,3) };
            var path = new List<Vector3> { corners[0] };
            void Line(Vector3 end) { Vector3 start=path[^1]; int n=Mathf.CeilToInt(Vector3.Distance(start,end)/.12f); for(int i=1;i<=n;i++) path.Add(Vector3.Lerp(start,end,i/(float)n)); }
            for(int c=1;c<corners.Length-1;c++)
            {
                Vector3 a=corners[c]+(corners[c-1]-corners[c]).normalized*.35f;
                Vector3 b=corners[c]+(corners[c+1]-corners[c]).normalized*.35f;
                Line(a);
                for(int j=1;j<=10;j++) {float t=j/10f;path.Add((1-t)*(1-t)*a+2*(1-t)*t*corners[c]+t*t*b);}
            }
            Line(corners[^1]);
            bool placed=false;
            var rov=FindFirstObjectByType<RovVehicle>();
            for(int attempt=0;attempt<600 && !placed;attempt++)
            {
                centreline.Clear();
                Vector3 origin=new((float)rng.NextDouble()*82-41,0,(float)rng.NextDouble()*82-41);
                Quaternion rotation=Quaternion.Euler(0,(float)rng.NextDouble()*360,0);
                placed=true;
                foreach(Vector3 local in path)
                {
                    Vector3 p=origin+rotation*local;
                    if(Mathf.Abs(p.x)>47 || Mathf.Abs(p.z)>47) {placed=false;break;}
                    var ray=new Ray(new Vector3(p.x,20,p.z),Vector3.down);
                    bool found=false; float height=float.NegativeInfinity;
                    foreach(var floor in bottom) if(floor.Raycast(ray,out var hit,150)) {found=true;height=Mathf.Max(height,hit.point.y);}
                    if(!found) {placed=false;break;}
                    p.y=height+diameter*.5f+.025f;
                    if(rov!=null && Vector3.Distance(p,rov.transform.position)<2) {placed=false;break;}
                    foreach(var obstacle in Physics.OverlapSphere(p,diameter*.7f,~0,QueryTriggerInteraction.Ignore))
                        if(!bottom.Contains(obstacle as MeshCollider)) {placed=false;break;}
                    if(!placed) break;
                    if(centreline.Count>0 && Mathf.Abs(p.y-centreline[^1].y)>.12f) {placed=false;break;}
                    centreline.Add(p);
                }
            }
            if(!placed) throw new InvalidOperationException("No clear seabed position found for pipeline.");
            SpawnPosition=centreline[0];
            generated=new GameObject("Yellow inspection pipe • seed "+value).transform;
            generated.SetParent(transform,false);
            yellow=TacMaterials.Lit("Ocean pipeline yellow",new Color(.95f,.69f,.025f),0,.45f);
            const int sides=20;
            var vertices=new Vector3[centreline.Count*sides]; var triangles=new List<int>();
            for(int i=0;i<centreline.Count;i++)
            {
                Vector3 tangent=(centreline[Mathf.Min(i+1,centreline.Count-1)]-centreline[Mathf.Max(0,i-1)]).normalized;
                Vector3 right=Vector3.Cross(Vector3.up,tangent).normalized, up=Vector3.Cross(tangent,right).normalized;
                for(int s=0;s<sides;s++)
                {
                    float angle=2*Mathf.PI*s/sides;
                    vertices[i*sides+s]=transform.InverseTransformPoint(centreline[i]+diameter*.5f*(right*Mathf.Cos(angle)+up*Mathf.Sin(angle)));
                    if(i==0) continue;
                    int a=(i-1)*sides+s,b=(i-1)*sides+(s+1)%sides,c=i*sides+s,d=i*sides+(s+1)%sides;
                    triangles.AddRange(new[]{a,b,c,b,d,c});
                }
            }
            pipeMesh=new Mesh {name="Rounded yellow pipe",vertices=vertices,triangles=triangles.ToArray()};pipeMesh.RecalculateNormals();pipeMesh.RecalculateBounds();
            generated.gameObject.AddComponent<MeshFilter>().sharedMesh=pipeMesh;
            generated.gameObject.AddComponent<MeshRenderer>().sharedMaterial=yellow;
            generated.gameObject.AddComponent<MeshCollider>().sharedMesh=pipeMesh;
            var beacon=GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beacon.name="30 kHz pinger • inspection start";
            beacon.transform.SetParent(generated,false);
            beacon.transform.position=centreline[0]+Vector3.up*.17f;
            beacon.transform.localScale=new Vector3(.07f,.09f,.07f);
            beacon.GetComponent<Renderer>().sharedMaterial=TacMaterials.Lit("Pinger dark housing",new Color(.045f,.065f,.075f),.4f,.4f);
            Pinger=beacon.AddComponent<PipelinePinger>();
            var ids=new List<int>();for(int id=1;id<=99;id++) ids.Add(id);
            for(int i=98;i>0;i--) {int j=rng.Next(i+1);(ids[i],ids[j])=(ids[j],ids[i]);}
            int count=Mathf.Clamp(markerCount,4,10); MarkerIds=ids.GetRange(0,count).ToArray();
            for(int i=0;i<count;i++)
            {
                int index=Mathf.RoundToInt((i+.5f)/count*(centreline.Count-1));
                Vector3 tangent=(centreline[index+1]-centreline[index-1]).normalized;
                Vector3 top=centreline[index]+Vector3.up*(diameter*.5f+.012f);
                ArucoMarker.Create(generated,MarkerIds[i],markerSize,transform.InverseTransformPoint(top),Quaternion.LookRotation(new Vector3(tangent.x,0,tangent.z),Vector3.up));
            }
        }
    }
}

