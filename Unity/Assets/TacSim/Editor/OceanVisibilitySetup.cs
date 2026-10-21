using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TacSim.Editor
{
    public static class OceanVisibilitySetup
    {
        public static void Install()
        {
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            var water=UnityEngine.Object.FindFirstObjectByType<OceanExplorer>();
            water.daylight=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Single(l=>l.type==LightType.Directional);
            water.lightAttenuationPerMetre=.12f;
            foreach(float visibility in new[]{1f,2f,5f})
            {
                water.SetVisibility(visibility);water.ApplyWaterAtDepth(10);
                float transmission=Mathf.Exp(-Mathf.Pow(RenderSettings.fogDensity*visibility,2));
                if(Mathf.Abs(transmission-.05f)>.002f) throw new Exception("Visibility attenuation mismatch");
            }
            water.ApplyWaterAtDepth(0);float surface=water.daylight.intensity;
            water.ApplyWaterAtDepth(10);float shallow=water.daylight.intensity;
            water.ApplyWaterAtDepth(30);float deep=water.daylight.intensity;
            if(!(surface>shallow && shallow>deep && deep<surface*.05f)) throw new Exception("Depth dimming failed");
            water.ApplyWaterAtDepth(-1);
            if(RenderSettings.fog || Mathf.Abs(water.daylight.intensity-surface)>.001f) throw new Exception("Surface recovery failed");
            water.SetVisibility(2);
            water.ApplyWaterAtDepth(-UnityEngine.Object.FindFirstObjectByType<RovVehicle>().transform.position.y);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("OCEAN_VISIBILITY_OK: 1/2/5 m fog presets, monotonic depth lighting, surface recovery; default 2 m.");
        }
    }
}
