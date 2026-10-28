using UnityEngine;
using UnityEngine.InputSystem;

namespace TacSim
{
    // Optional, scene-local assistance for the public demonstration.
    [DefaultExecutionOrder(210)]
    public sealed class EasyValveAssist : MonoBehaviour
    {
        public TeknologidagenDive dive;
        public float connectRange = 1.6f;
        public TacValve Connected { get; private set; }
        public TacValve Candidate { get; private set; }
        Vector3 mount;
        Quaternion armRotation;
        RigidbodyConstraints constraints;
        LineRenderer extension;
        bool ready;
        public Vector3 Socket(TacValve v) => v.transform.TransformPoint(new Vector3(0,.04f,0));

        void Start()
        {
            mount=dive.Arm.transform.localPosition;
            armRotation=dive.Arm.transform.localRotation;
            dive.vehicle.VehicleReset+=Release;
            var go=new GameObject("Assisted arm extension"); go.transform.SetParent(transform,false);
            extension=go.AddComponent<LineRenderer>(); extension.positionCount=2;
            extension.startWidth=extension.endWidth=.035f;
            extension.sharedMaterial=TacMaterials.Lit("Assisted arm steel",new Color(.32f,.36f,.38f),.7f);
            extension.enabled=false;
            ready=true;
        }
        public bool CanConnect(TacValve valve)
        {
            if(valve==null || !dive.armEnabled || !dive.vehicle.Armed) return false;
            Vector3 origin=dive.vehicle.transform.TransformPoint(mount), target=Socket(valve);
            Vector3 delta=target-origin;
            if(delta.magnitude>connectRange || Vector3.Dot(origin-target,valve.transform.up)<.025f) return false;
            // Do not attach through the deck, the rear wall, rocks or other obstacles.
            foreach(var hit in Physics.RaycastAll(origin,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                if(!hit.transform.IsChildOf(dive.vehicle.transform) && !hit.transform.IsChildOf(valve.transform)) return false;
            return true;
        }
        public bool Connect(TacValve valve)
        {
            if(Connected!=null || !CanConnect(valve)) return false;
            Connected=valve;
            constraints=dive.vehicle.Body.constraints;
            dive.vehicle.Body.linearVelocity=Vector3.zero; dive.vehicle.Body.angularVelocity=Vector3.zero;
            dive.vehicle.Body.constraints=RigidbodyConstraints.FreezeAll;
            dive.Arm.enabled=false;
            PoseArm();
            return true;
        }
        public void Turn(float direction,float seconds)
        {
            if(Connected==null || !dive.vehicle.Armed) return;
            float angle=Mathf.Clamp(Connected.angle+Mathf.Clamp(direction,-1,1)*35*seconds,0,90);
            // Use the same valve coupling as the manual arm, in small increments to preserve grip.
            for(float remaining=Mathf.Abs(angle-Connected.angle);remaining>.001f;remaining=Mathf.Abs(angle-Connected.angle))
            {
                float next=Mathf.MoveTowards(Connected.angle,angle,5);
                Vector3 slot=Connected.transform.TransformDirection(Quaternion.Euler(0,next,0)*Vector3.forward);
                if(!Connected.Drive(Socket(Connected),-Connected.transform.up,slot)) break;
            }
            PoseArm();
        }
        public void Release()
        {
            if(Connected==null) return;
            Connected.Drive(Socket(Connected)+Connected.transform.up*10,-Connected.transform.up,Connected.transform.forward);
            Connected=null;
            if(dive.vehicle.Body!=null) dive.vehicle.Body.constraints=constraints;
            dive.Arm.transform.localPosition=mount; dive.Arm.transform.localRotation=armRotation;
            dive.Arm.enabled=true;
            extension.enabled=false;
        }
        void Update()
        {
            if(!ready) return;
            if(Connected!=null && (!dive.armEnabled || !dive.vehicle.Armed)) Release();
            Candidate=null;
            float best=float.MaxValue;
            foreach(var v in dive.Valves)
            {
                float distance=Vector3.Distance(dive.vehicle.transform.TransformPoint(mount),Socket(v));
                if(distance<best && CanConnect(v)) { Candidate=v; best=distance; }
            }
            var k=Keyboard.current;
            if(k==null || !Application.isFocused) return;
            if(k.fKey.wasPressedThisFrame) { if(Connected!=null) Release(); else Connect(Candidate); }
            if(Connected!=null) Turn((k.xKey.isPressed?1:0)-(k.zKey.isPressed?1:0),Time.deltaTime);
        }
        void PoseArm()
        {
            var v=Connected;
            var root=dive.Arm.transform;
            root.Find("Tilt joint").localRotation=Quaternion.identity;
            root.Find("Tilt joint/Wrist").localRotation=Quaternion.identity;
            Vector3 forward=-v.transform.up;
            Vector3 slot=v.transform.TransformDirection(Quaternion.Euler(0,v.angle,0)*Vector3.forward);
            root.rotation=Quaternion.LookRotation(forward,Vector3.Cross(forward,slot));
            root.position=Socket(v)-forward*TacArm.Reach;
            extension.enabled=true;
            extension.SetPosition(0,dive.vehicle.transform.TransformPoint(mount));
            extension.SetPosition(1,root.position);
        }
        void OnDisable() { if(ready) Release(); }
        void OnDestroy() { if(dive!=null && dive.vehicle!=null) dive.vehicle.VehicleReset-=Release; }
    }
}
