// Assets/TacSim/PipeGenerator/Scripts/PipelineSurfaceStyle.cs
using System.Collections.Generic;
using UnityEngine;

namespace TAC.Pipeline
{
    public enum PipelinePart { Pipe, Joint, EndCap, Pinger, MarkerCard, ClearFrame }

    /// All colours / textures / shaders of the generated pipeline in one asset.
    [CreateAssetMenu(menuName = "TAC Challenge/Pipeline Surface Style", fileName = "PipelineSurfaceStyle")]
    public class PipelineSurfaceStyle : ScriptableObject
    {
        [Header("Palette (booklet defaults)")]
        [ColorUsage(false, true)] public Color pipeColour   = new Color32(255, 224, 0, 255); // §3.2.3 YELLOW
        [ColorUsage(false, true)] public Color pipeAccent   = new Color32(196, 165, 18, 255);
        [ColorUsage(false, true)] public Color jointColour  = new Color32(214, 180, 22, 255);
        [ColorUsage(false, true)] public Color endCapColour = new Color32(178, 150, 18, 255);
        [ColorUsage(false, true)] public Color pingerColour = new Color32(38, 40, 44, 255);
        [ColorUsage(false, true)] public Color markerInk    = Color.black;
        [ColorUsage(false, true)] public Color markerPaper  = Color.white;   // outermost part of the card
        [ColorUsage(false, true)] public Color clearPlastic = new Color(1f, 1f, 1f, 0.10f);

        [Header("Overrides (null -> procedural duct texture)")]
        public Texture2D pipeColourMap;
        [Tooltip("R channel = where pipeAccent shows (separate texture to drive colour variation).")]
        public Texture2D pipeColourMask;
        public Texture2D pipeNormalMap;
        public Shader pipeShaderOverride;
        public Shader markerShaderOverride;      // Unlit keeps ArUco contrast readable
        public Vector2 pipeTextureScale = Vector2.one;

        [Header("Surface response")]
        [Range(0f, 1f)] public float pipeMetallic = 0.06f;
        [Range(0f, 1f)] public float pipeSmoothness = 0.55f;
        [Range(0f, 1f)] public float jointMetallic = 0.15f;
        [Range(0f, 1f)] public float jointSmoothness = 0.45f;
        [Range(0f, 1f)] public float accentAmount = 0.85f;

        [Header("Procedural duct texture")]
        public bool generateProceduralTexture = true;
        public int proceduralSize = 256;
        [Range(2, 48)] public int circumferentialBands = 16;
        [Range(0f, 1f)] public float grimeAmount = 0.35f;
        [Range(0f, 1f)] public float spiralSeam = 0.5f;

        readonly Dictionary<PipelinePart, Material> _cache = new Dictionary<PipelinePart, Material>();
        readonly Dictionary<PipelinePart, Texture2D> _genTex = new Dictionary<PipelinePart, Texture2D>();
        Texture2D _markerPaperTex;

        // ------------------------------------------------------------------ public API

        public Material GetMaterial(PipelinePart part)
        {
            Material m;
            if (_cache.TryGetValue(part, out m) && m != null) return m;
            m = BuildMaterial(part);
            _cache[part] = m;
            return m;
        }

        /// Call after changing colours/masks/shaders at runtime.
        public void RefreshMaterials()
        {
            foreach (var kv in _cache)
                if (kv.Value != null) ApplyTo(kv.Value, kv.Key);
        }

        public void Release()
        {
            foreach (var kv in _cache) if (kv.Value != null) DestroyUnityObject(kv.Value);
            foreach (var kv in _genTex) if (kv.Value != null) DestroyUnityObject(kv.Value);
            if (_markerPaperTex != null) DestroyUnityObject(_markerPaperTex);
            _cache.Clear(); _genTex.Clear(); _markerPaperTex = null;
        }

        static void DestroyUnityObject(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        // ------------------------------------------------------------------ material build

        Material BuildMaterial(PipelinePart part)
        {
            Shader sh;
            if (part == PipelinePart.MarkerCard)
                sh = markerShaderOverride != null ? markerShaderOverride : ResolveShader(true, false);
            else if (part == PipelinePart.ClearFrame)
                sh = pipeShaderOverride != null ? pipeShaderOverride : ResolveShader(false, true);
            else
                sh = pipeShaderOverride != null ? pipeShaderOverride : ResolveShader(false, false);

            var m = new Material(sh)
            {
                name = "TAC_Pipeline_" + part,
                hideFlags = HideFlags.DontSave
            };
            ApplyTo(m, part);
            return m;
        }

        void ApplyTo(Material m, PipelinePart part)
        {
            switch (part)
            {
                case PipelinePart.MarkerCard:
                    var card = MarkerPaperTex;
                    SetTex(m, "_MainTex", card); SetTex(m, "_BaseMap", card);
                    SetColour(m, "_Color", Color.white); SetColour(m, "_BaseColor", Color.white);
                    SetTex(m, "_EmissionMap", card); SetColour(m, "_EmissionColor", Color.white * 0.12f);
                    break;

                case PipelinePart.ClearFrame:
                    SetTex(m, "_MainTex", null); SetTex(m, "_BaseMap", null);
                    SetColour(m, "_Color", clearPlastic); SetColour(m, "_BaseColor", clearPlastic);
                    SetFloat(m, "_Surface", 1f); SetFloat(m, "_BlendMode", 2f);
                    SetFloat(m, "_SrcBlend", 5f); SetFloat(m, "_DstBlend", 10f);
                    SetFloat(m, "_ZWrite", 0f);
                    SetFloat(m, "_Cull", 0f);                 // double sided thin plastic
                    SetFloat(m, "_CullMode", 0f);
                    SetFloat(m, "_Glossiness", 0.9f); SetFloat(m, "_Smoothness", 0.9f);
                    SetFloat(m, "_Metallic", 0f);
                    m.renderQueue = 3000;
                    break;

                case PipelinePart.Pinger:
                    Paint(m, pingerColour, pingerColour, null, null, 0.25f, 0.35f, Vector2.one);
                    break;

                case PipelinePart.EndCap:
                    Paint(m, endCapColour, endCapColour, null, null, 0.10f, 0.50f, Vector2.one);
                    break;

                case PipelinePart.Joint:
                    Paint(m, jointColour, jointColour, pipeColourMap, pipeColourMask,
                          jointMetallic, jointSmoothness, pipeTextureScale);
                    SetTex(m, "_BumpMap", pipeNormalMap);
                    SetFloat(m, "_BumpScale", 1f);
                    break;

                default: // Pipe
                    Texture2D detail = pipeColourMap != null ? pipeColourMap
                                     : (generateProceduralTexture ? ProceduralDuct() : null);
                    Paint(m, pipeColour, pipeAccent, detail, pipeColourMask,
                          pipeMetallic, pipeSmoothness, pipeTextureScale);
                    Texture2D nrm = pipeNormalMap != null ? pipeNormalMap
                                    : (generateProceduralTexture ? ProceduralNormal() : null);
                    SetTex(m, "_BumpMap", nrm);
                    SetFloat(m, "_BumpScale", 1f);
                    if (nrm != null)
                    {
                        if (m.HasProperty("_PixelSplitFlag")) m.SetFloat("_PixelSplitFlag", 0f);
                        m.EnableKeyword("_NORMALMAP");
                    }
                    break;
            }
        }

        /// Texture is multiplied by the shader base colour, so the accent colour is folded into the
        /// (mask-driven) texture as accent/base -> changing pipeColour stays a 1-line change.
        void Paint(Material m, Color baseC, Color accent, Texture2D detail, Texture2D mask,
                   float metallic, float smoothness, Vector2 scale)
        {
            Color colour = baseC;
            if (detail != null && mask != null && accentAmount > 0.001f)
            {
                detail = Compose(detail, mask, baseC, accent, accentAmount);
                colour = Color.white;                 // colours are baked into the composed map
            }

            SetColour(m, "_Color", colour); SetColour(m, "_BaseColor", colour);
            SetTex(m, "_MainTex", detail);  SetTex(m, "_BaseMap", detail);
            if (detail != null) SetVec4(m, "_MainTex_ST", new Vector4(scale.x, scale.y, 0f, 0f));
            SetFloat(m, "_Metallic", metallic);
            SetFloat(m, "_Glossiness", smoothness); SetFloat(m, "_Smoothness", smoothness);
        }

        Texture2D MarkerPaperTex
        {
            get
            {
                if (_markerPaperTex != null) return _markerPaperTex;
                const int s = 4;
                _markerPaperTex = new Texture2D(s, s, TextureFormat.RGBA32, false)
                {
                    name = "TAC_WhiteCard",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave
                };
                var px = new Color32[s * s];
                var w = markerPaper; w.a = 255;
                for (int i = 0; i < px.Length; i++) px[i] = w;
                _markerPaperTex.SetPixels32(px);
                _markerPaperTex.Apply();
                return _markerPaperTex;
            }
        }

        // ------------------------------------------------------------------ procedural textures

        Texture2D ProceduralDuct()
        {
            Texture2D cached;
            if (_genTex.TryGetValue(PipelinePart.Pipe, out cached) && cached != null) return cached;

            int n = Mathf.Max(32, proceduralSize);
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                name = "TAC_DuctDetail",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                float v = y / (float)n;                 // around the circumference
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n;             // along the pipe
                    float bands = 0.5f + 0.5f * Mathf.Sin(v * Mathf.PI * 2f * circumferentialBands);
                    float grime = Fbm(u * 6f, v * 6f, 11);
                    float seam  = Mathf.Exp(-40f * Mathf.Abs(Frac(u * 3f - v * 1.5f) - 0.5f)) * spiralSeam;

                    float shade = 1f - grimeAmount * 0.35f * grime - 0.18f * spiralSeam * seam + 0.06f * bands;
                    shade = Mathf.Clamp(shade, 0.55f, 1.05f);

                    byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(shade * 255), 0, 255);
                    px[y * n + x] = new Color32(b, b, b, 255);
                }
            }
            tex.SetPixels32(px); tex.Apply();
            _genTex[PipelinePart.Pipe] = tex;
            return tex;
        }

        Texture2D ProceduralNormal()
        {
            Texture2D cached;
            if (_genTex.TryGetValue(PipelinePart.Joint, out cached) && cached != null) return cached;

            int n = Mathf.Max(32, proceduralSize / 2);
            var h = new float[n, n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float bands = 0.5f + 0.5f * Mathf.Sin(v * Mathf.PI * 2f * circumferentialBands);
                h[x, y] = bands * 0.6f + Fbm(u * 10f, v * 10f, 7) * 0.4f;
            }

            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                name = "TAC_DuctNormal",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            var px = new Color32[n * n];
            const float strength = 2.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float hl = h[Wrap(x - 1, n), y], hr = h[Wrap(x + 1, n), y];
                float hd = h[x, Wrap(y - 1, n)], hu = h[x, Wrap(y + 1, n)];
                Vector3 nrm = new Vector3((hl - hr) * strength, (hd - hu) * strength, 1f).normalized;
                px[y * n + x] = new Color32(
                    (byte)Mathf.RoundToInt((nrm.x * 0.5f + 0.5f) * 255),
                    (byte)Mathf.RoundToInt((nrm.y * 0.5f + 0.5f) * 255),
                    (byte)Mathf.RoundToInt((nrm.z * 0.5f + 0.5f) * 255), (byte)255);
            }
            tex.SetPixels32(px); tex.Apply();
            _genTex[PipelinePart.Joint] = tex;
            return tex;
        }

        Texture2D Compose(Texture2D detail, Texture2D mask, Color baseC, Color accent, float amount)
        {
            var d = ReadableCopy(detail);
            var m = ReadableCopy(mask);
            if (d == null || m == null) return d;

            int w = d.width, h = d.height;

            Color32 ratio = new Color32(255, 255, 255, 255);
            if (baseC.maxColorComponent > 0.001f)
            {
                ratio = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(255f * accent.r / baseC.r), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(255f * accent.g / baseC.g), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(255f * accent.b / baseC.b), 0, 255), (byte)255);
            }

            var outT = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "TAC_DuctComposed",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };

            Color32[] dp = d.GetPixels32();
            Color32[] mp = m.GetPixels32();
            for (int i = 0; i < dp.Length; i++)
            {
                int sx = (i % w) * m.width / w;
                int sy = (i / w) * m.height / h;
                float t = mp[sy * m.width + sx].r / 255f * Mathf.Clamp01(amount);

                float r = dp[i].r / 255f, g = dp[i].g / 255f, b = dp[i].b / 255f;
                dp[i] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(r, r * ratio.r / 255f, t) * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(g, g * ratio.g / 255f, t) * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(b, b * ratio.b / 255f, t) * 255f), 0, 255),
                    (byte)255);
            }
            outT.SetPixels32(dp); outT.Apply();
            return outT;
        }

        static Texture2D ReadableCopy(Texture2D src)
        {
            if (src == null) return null;
            if (src.isReadable) return src;

            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontSave
            };
            copy.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        static int Wrap(int x, int n) { return ((x % n) + n) % n; }
        static float Frac(float v) { return v - Mathf.Floor(v); }

        static float Fbm(float x, float y, int seed)
        {
            float sum = 0f, amp = 0.5f, freq = 1f;
            for (int o = 0; o < 4; o++)
            {
                sum += amp * Noise(x * freq, y * freq, seed + o);
                amp *= 0.5f; freq *= 2f;
            }
            return sum;
        }

        static float Noise(float x, float y, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            float u = xf * xf * (3f - 2f * xf), v = yf * yf * (3f - 2f * yf);
            float a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed);
            float c = Hash(xi, yi + 1, seed), e = Hash(xi + 1, yi + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, e, u), v);
        }

        static float Hash(int x, int y, int s)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)s * 1274126177u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFFu) / (float)0xFFFFFF;
            }
        }

        // ------------------------------------------------------------------ shader plumbing

        static Shader _opaque, _unlit, _transparent;

        static Shader ResolveShader(bool wantUnlit, bool wantTransparent)
        {
            if (wantTransparent && _transparent != null) return _transparent;
            if (wantUnlit && _unlit != null) return _unlit;
            if (!wantUnlit && !wantTransparent && _opaque != null) return _opaque;

            string[] candidates = wantTransparent
                ? new[] { "Universal Render Pipeline/Lit", "Standard" }
                : wantUnlit
                    ? new[] { "Universal Render Pipeline/Unlit", "HDRP/Unlit", "Unlit/Texture",
                              "Unlit/Color", "Standard" }
                    : new[] { "Universal Render Pipeline/Lit", "HDRP/Lit", "Standard",
                              "Standard (Specular setup)", "Diffuse" };

            Shader found = null;
            foreach (string name in candidates)
            {
                found = Shader.Find(name);
                if (found != null) break;
            }
            if (found == null)
            {
                Debug.LogWarning("[PipelineSurfaceStyle] No usable shader found. Assign a shader override " +
                                 "on this asset, and add the built-in shaders to Always Included Shaders.");
                found = Shader.Find("UI/Default");
            }

            if (wantTransparent) _transparent = found;
            else if (wantUnlit) _unlit = found;
            else _opaque = found;
            return found;
        }

        static void SetColour(Material m, string p, Color v)   { if (m.HasProperty(p)) m.SetColor(p, v); }
        static void SetFloat(Material m, string p, float v)    { if (m.HasProperty(p)) m.SetFloat(p, v); }
        static void SetVec2(Material m, string p, Vector2 v)   { if (m.HasProperty(p)) m.SetVector(p, v); }
        static void SetVec4(Material m, string p, Vector4 v)   { if (m.HasProperty(p)) m.SetVector(p, v); }
        static void SetTex(Material m, string p, Texture t)    { if (m.HasProperty(p)) m.SetTexture(p, t); }
    }
}
