using System.IO;
using System.Linq;
using UnityEditor;

namespace TacSim.Editor
{
    public static class FarmStickProbe
    {
        [InitializeOnLoadMethod] static void Init() { EditorApplication.delayCall+=Dump; }
        [MenuItem("TAC/FarmStick/Inspect device")]
        static void Dump()
        {
            File.WriteAllText("Temp/farmstick-device.txt",FarmStickInput.DeviceDescription());
        }
    }
}

