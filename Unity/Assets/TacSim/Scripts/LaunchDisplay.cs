using System;
using UnityEngine;

namespace TacSim
{
    public static class LaunchDisplay
    {
        const int MaximumDefaultWidth = 1280;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
#if !UNITY_EDITOR
            string[] args = Environment.GetCommandLineArgs();
            if (!ShouldUseDefaultResolution(args)) return;
            Vector2Int resolution = DefaultResolution(Display.main.systemWidth, Display.main.systemHeight);
            if (resolution.x <= 0 || resolution.y <= 0) return;
            Screen.SetResolution(resolution.x, resolution.y, FullScreenMode.FullScreenWindow);
            Debug.Log($"TAC_DISPLAY: fullscreen render resolution {resolution.x}x{resolution.y}");
#endif
        }

        public static bool ShouldUseDefaultResolution(string[] args)
        {
            bool windowed = false;
            bool explicitSize = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-screen-fullscreen" && i + 1 < args.Length)
                    windowed = args[i + 1] == "0";
                if (args[i] is "-screen-width" or "-screen-height") explicitSize = true;
            }
            return !windowed && !explicitSize;
        }

        public static Vector2Int DefaultResolution(int desktopWidth, int desktopHeight)
        {
            if (desktopWidth <= 0 || desktopHeight <= 0) return Vector2Int.zero;
            int width = Mathf.Min(desktopWidth, MaximumDefaultWidth);
            int height = Mathf.RoundToInt(width * (float)desktopHeight / desktopWidth);
            return new Vector2Int(width, height);
        }
    }
}
