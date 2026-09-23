// Assets/TAC/Pipeline/Scripts/ArUcoTextureGenerator.cs
using System.Collections.Generic;
using UnityEngine;

namespace TAC.Pipeline
{
    /// Builds the printable "card": white quiet zone (OUTERMOST pixels are white),
    /// then the 1-module black frame, then the payload bits.
    /// Texture is always square, texture size is always an exact multiple of the module
    /// count and every module is an integer number of pixels -> markers stay SQUARE and crisp.
    public static class ArUcoTextureGenerator
    {
        public static int SnapSize(int requestedPixels, int totalModules)
        {
            int modulePx = Mathf.Max(2, requestedPixels / Mathf.Max(1, totalModules));
            return modulePx * Mathf.Max(1, totalModules);
        }

        public static Color32[] RenderPixels(ulong code, int bits, int quietModules,
                                             Color32 ink, Color32 paper, float jitter = 0f,
                                             System.Random rng = null)
        {
            int total = bits + 2 + 2 * quietModules;
            int px = SnapSize(256, total);
            int modulePx = px / total;
            return RenderPixels(code, bits, quietModules, px, modulePx, ink, paper, jitter, rng);
        }

        public static Color32[] RenderPixels(ulong code, int bits, int quietModules, int size,
                                             int modulePx, Color32 ink, Color32 paper,
                                             float jitter = 0f, System.Random rng = null)
        {
            int total = bits + 2 + 2 * quietModules;
            var outPix = new Color32[size * size];
            ink.a = 255; paper.a = 255;

            // tiny paper tint variation mimics printed card / wet-lab wear.
            if (rng == null) rng = new System.Random((int)(code & 0x7FFFFFFF));
            float j = Mathf.Clamp01(jitter);

            for (int y = 0; y < size; y++)
            {
                int my = (size - 1 - y) / modulePx;            // 0 = top module row
                for (int x = 0; x < size; x++)
                {
                    int mx = x / modulePx;                    // 0 = left module column

                    Color32 c = paper;

                    bool inQuiet = mx < quietModules || my < quietModules ||
                                   mx >= total - quietModules || my >= total - quietModules;

                    if (!inQuiet)
                    {
                        int i = my - quietModules;            // 0 .. bits+1
                        int k = mx - quietModules;

                        bool isFrame = i == 0 || j2(i) == bits + 1 || k == 0 || k == bits + 1;
                        if (isFrame) c = ink;
                        else if (ArUcoCodeGenerator.GetBit(code, bits, i - 1, k - 1)) c = ink;
                    }

                    if (j > 0f)
                    {
                        float n = (float)rng.NextDouble() * 2f - 1f;
                        float f = 1f + n * j * 0.10f;
                        c = new Color32(
                            (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * f), 0, 255),
                            (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * f), 0, 255),
                            (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * f), 0, 255), (byte)255);
                    }

                    outPix[y * size + x] = c;
                }
            }
            return outPix;
        }

        static int j2(int v) { return v; } // readability helper (frame test symmetry)

        public static Texture2D CreateCardTexture(ulong code, int bits, int quietModules,
                                                  int requestedPixels, Color32 ink, Color32 paper,
                                                  bool crisp, float printJitter = 0.15f)
        {
            int total = bits + 2 + 2 * quietModules;
            int size = SnapSize(Mathf.Max(32, requestedPixels), total);
            int modulePx = size / total;

            var pixels = RenderPixels(code, bits, quietModules, size, modulePx, ink, paper, printJitter);

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "ArUcoCard_Proc",
                wrapMode = TextureWrapMode.Clamp,
                // Point keeps module edges razor sharp for detection (no grey pixels to threshold).
                filterMode = crisp ? FilterMode.Point : FilterMode.Bilinear,
                anisoLevel = 8,
                hideFlags = HideFlags.DontSave
            };
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        public static Dictionary<int, Texture2D> CreateCardSet(Dictionary<int, ulong> codes, int bits,
                                                               int quietModules, int requestedPixels,
                                                               Color32 ink, Color32 paper, bool crisp,
                                                               float printJitter = 0.15f)
        {
            var set = new Dictionary<int, Texture2D>(codes.Count);
            foreach (var kvp in codes)
            {
                var t = CreateCardTexture(kvp.Value, bits, quietModules, requestedPixels, ink, paper, crisp, printJitter);
                t.name = $"ArUcoCard_{kvp.Key}";
                set[kvp.Key] = t;
            }
            return set;
        }
    }
}
