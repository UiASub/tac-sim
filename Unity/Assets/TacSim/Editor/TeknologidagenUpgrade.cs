using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TacSim.Editor
{
    // Turns the built 30 m lake into an 80 × 80 m fjord basin: a seabed sloping from 10 to 15 m with
    // undulations and mounds, boulders, eelgrass meadows, sugar kelp, pebbles and marine snow. Also splits
    // the imported wall valve into body and handle so the real handle can turn. Safe to run again.
    public static class TeknologidagenUpgrade
    {
        const string ScenePath = "Assets/teknologidagen_scene.unity";
        const string Art = "Assets/TacSim/Art/Teknologidagen";
        const string Generated = Art + "/Generated";
        const string Split = "Assets/TacSim/Art/Structure/Split";
        const string Model = "Assets/TacSim/Art/Structure/UnderwaterStructure.fbx";
        // The arena is ±38 m; beyond it the floor rises steeply towards the surface.
        public const float Half = 38;
        static readonly string[] OldParts = { "Seabed -10m", "Shore west", "Shore east", "Shore north", "Shore south",
            "Water surface 0m", "Suspended sediment", "Environment" };

        // The generated meshes and textures are not in Git. When the scene is open without them (a fresh
        // clone), rebuild them; the generator is seeded, so everyone gets the same lake.
        [InitializeOnLoadMethod]
        static void WatchScenes()
        {
            EditorSceneManager.sceneOpened -= Opened;
            EditorSceneManager.sceneOpened += Opened;
            EditorApplication.delayCall += () => Opened(SceneManager.GetActiveScene(), OpenSceneMode.Single);
        }

        static void Opened(Scene scene, OpenSceneMode mode)
        {
            if (scene.path != ScenePath || Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var dive = Object.FindFirstObjectByType<TeknologidagenDive>();
            if (dive == null) return;
            bool missing = dive.wallValveBody == null || dive.wallValveHandle == null
                || !(dive.seabed is MeshCollider floor) || floor.sharedMesh == null;
            if (!missing) return;
            Debug.Log("Teknologidagen: the generated lake assets are missing (they are not in Git); rebuilding them.");
            EditorApplication.delayCall += Run;
        }

        [MenuItem("TAC/Teknologidagen/Upgrade lake (80x80, real valve, seagrass)")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            var dive = Object.FindFirstObjectByType<TeknologidagenDive>();
            if (dive == null) throw new InvalidOperationException("Build the lake first: TAC/Teknologidagen/Build lake scene.");
            var log = new StringBuilder();
            Apply(dive, log);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/teknologidagen-result.txt", "UPGRADED\n" + log);
            Debug.Log("TEKNOLOGIDAGEN_UPGRADED\n" + log);
        }

        public static void Apply(TeknologidagenDive dive, StringBuilder log)
        {
            foreach (Transform child in dive.transform.Cast<Transform>().ToArray())
                if (OldParts.Contains(child.name) || child.name.StartsWith("Shore rock ")) Object.DestroyImmediate(child.gameObject);
            if (AssetDatabase.IsValidFolder(Generated)) AssetDatabase.DeleteAsset(Generated);
            AssetDatabase.CreateFolder(Art, "Generated");
            (dive.wallValveBody, dive.wallValveHandle) = SplitValve(log);
            // The valve body collides without the T-bar, which gets its own hinged colliders at runtime.
            var part = dive.structure.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == TeknologidagenDive.WallValvePart);
            var bodyCollider = part != null ? part.GetComponent<MeshCollider>() : null;
            if (bodyCollider != null) bodyCollider.sharedMesh = dive.wallValveBody;
            var env = new GameObject("Environment").transform;
            env.SetParent(dive.transform, false);
            var rng = new System.Random(2026);
            dive.seabed = Seabed(env, log);
            Boundary(env);
            var meshes = Enumerable.Range(0, 6).Select(RockMesh).ToArray();
            var rock = Lit("Fjord rock", RockTexture(), .12f);
            var rocks = Boulders(env, meshes, rock, rng, log);
            Eelgrass(env, rng, log);
            Kelp(env, rocks, rng, log);
            Pebbles(env, meshes, rock, rng, log);
            Surface(env);
            dive.sediment = Snow(env);
            dive.arenaHalfSize = Half;
            EditorUtility.SetDirty(dive);
        }

        // Plateau at about 10 m on the near half, a gentle slope to about 15 m on the far side, broad
        // undulations, small bumps and a few mounds and hollows. Not flat anywhere.
        public static float Floor(float x, float z)
        {
            float slope = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-2, 32, z));
            float h = -10 - 5 * slope;
            h += (Mathf.PerlinNoise(x * .045f + 11, z * .045f + 3) - .5f) * 1.4f;
            h += (Mathf.PerlinNoise(x * .18f + 5, z * .18f + 19) - .5f) * .35f;
            h += .9f * Mathf.Exp(-((x + 18) * (x + 18) + (z - 12) * (z - 12)) / 40f);
            h -= .7f * Mathf.Exp(-((x - 20) * (x - 20) + (z + 22) * (z + 22)) / 60f);
            h += .5f * Mathf.Exp(-((x - 8) * (x - 8) + (z - 22) * (z - 22)) / 25f);
            float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - Half;
            if (edge > 0) h += edge * edge * .35f;
            return Mathf.Min(h, -1.5f);
        }

        // ---------------------------------------------------------------- valve

        // The STEP valve is a single fused shell: the T-bar is unioned into the rim ring and the hub. Split it
        // by shape instead. Measured on the 240 mm valve (scaled by its actual radius): the bar lies 16–47.5 mm
        // below the open face, 25 mm wide, reaching into the rim notches at 96 mm; below it a 120 × 25 mm
        // stem plate runs down to the round hub at 86.8 mm. Everything else, hub included, stays put.
        static (Mesh body, Mesh handle) SplitValve(StringBuilder log)
        {
            if (!AssetDatabase.IsValidFolder(Split)) AssetDatabase.CreateFolder("Assets/TacSim/Art/Structure", "Split");
            string bodyPath = Split + "/Wall valve body.asset", handlePath = Split + "/Wall valve handle.asset";
            var existingBody = AssetDatabase.LoadAssetAtPath<Mesh>(bodyPath);
            var existingHandle = AssetDatabase.LoadAssetAtPath<Mesh>(handlePath);
            if (existingBody != null && existingHandle != null)
            {
                log.AppendLine("Valve: reused split meshes in " + Split);
                return (existingBody, existingHandle);
            }
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            if (!importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
                log.AppendLine("Valve: enabled Read/Write on the structure model to split the valve.");
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            var part = model.GetComponentsInChildren<Transform>(true).First(t => t.name == TeknologidagenDive.WallValvePart);
            var source = part.GetComponent<MeshFilter>().sharedMesh;
            var v = source.vertices;
            var normals = source.normals;
            var uv = source.uv;
            var tri = source.triangles;
            if (v.Length == 0 || tri.Length == 0) throw new InvalidOperationException("The valve mesh could not be read.");

            Bounds bounds = source.bounds;
            int axis = bounds.size.x <= bounds.size.y && bounds.size.x <= bounds.size.z ? 0 : bounds.size.y <= bounds.size.z ? 1 : 2;
            Vector3 axisVector = Vector3.zero;
            axisVector[axis] = 1;
            float depth = bounds.size[axis], outer = bounds.size[(axis + 1) % 3] * .5f, k = outer / .12f;
            Vector3 Flat(Vector3 p) { var d = p - bounds.center; d[axis] = 0; return d; }
            // The open face is the end with nothing near the axis: the bucket is empty above the T-bar.
            Vector3 low = bounds.center - axisVector * depth * .5f;
            int nearLow = 0, nearHigh = 0;
            foreach (var p in v)
            {
                if (Flat(p).magnitude >= .4f * outer) continue;
                float a = Vector3.Dot(p - low, axisVector);
                if (a < .1f * depth) nearLow++;
                else if (a > .9f * depth) nearHigh++;
            }
            Vector3 inward = nearHigh < nearLow ? -axisVector : axisVector;
            Vector3 face = nearHigh < nearLow ? low + axisVector * depth : low;
            float Depth(Vector3 p) => Vector3.Dot(p - face, inward);
            // Bar direction from points that can only be bar: inside the empty bucket, at bar depth.
            float sxx = 0, sxy = 0, syy = 0;
            int samples = 0;
            Vector3 e1 = Vector3.zero, e2 = Vector3.zero;
            e1[(axis + 1) % 3] = 1;
            e2[(axis + 2) % 3] = 1;
            foreach (var p in v)
            {
                float d = Depth(p), r = Flat(p).magnitude;
                if (d < .022f * k || d > .044f * k || r < .03f * k || r > .058f * k) continue;
                float x = Vector3.Dot(Flat(p), e1), y = Vector3.Dot(Flat(p), e2);
                sxx += x * x; sxy += x * y; syy += y * y; samples++;
            }
            float theta = .5f * Mathf.Atan2(2 * sxy, sxx - syy);
            Vector3 bar = e1 * Mathf.Cos(theta) + e2 * Mathf.Sin(theta), across = Vector3.Cross(axisVector, bar);
            bool IsHandle(int t)
            {
                Vector3 c = (v[tri[t]] + v[tri[t + 1]] + v[tri[t + 2]]) / 3, q = Flat(c);
                float d = Depth(c), along = Mathf.Abs(Vector3.Dot(q, bar)), side = Mathf.Abs(Vector3.Dot(q, across));
                bool crossbar = d >= .016f * k && d <= .0475f * k && along <= .097f * k;
                bool stem = d > .0475f * k && d <= .0868f * k && along <= .064f * k;
                return side <= .0145f * k && (crossbar || stem);
            }
            int handleTris = 0;
            for (int t = 0; t < tri.Length; t += 3) if (IsHandle(t)) handleTris++;
            log.AppendLine($"Valve mesh: {v.Length} vertices, {tri.Length / 3} triangles, axis {"XYZ"[axis]}, outer radius {outer:F3}, " +
                $"open face at the {(nearHigh < nearLow ? "high" : "low")} end, {samples} bar samples");
            if (samples < 100 || handleTris < 1000 || handleTris > tri.Length / 3 / 4)
                throw new InvalidOperationException($"Could not isolate the valve handle ({samples} bar samples, {handleTris} triangles).\n{log}");
            var body = Subset(v, normals, uv, tri, t => !IsHandle(t), "Wall valve body");
            var handle = Subset(v, normals, uv, tri, IsHandle, "Wall valve handle");
            AssetDatabase.CreateAsset(body, bodyPath);
            AssetDatabase.CreateAsset(handle, handlePath);
            log.AppendLine($"Valve: handle = {handleTris} triangles, size {handle.bounds.size:F3}");
            return (body, handle);
        }

        static Mesh Subset(Vector3[] v, Vector3[] n, Vector2[] uv, int[] tri, Func<int, bool> keep, string name)
        {
            var map = new Dictionary<int, int>();
            var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nuv = new List<Vector2>(); var nt = new List<int>();
            for (int t = 0; t < tri.Length; t += 3)
            {
                if (!keep(t)) continue;
                for (int k = 0; k < 3; k++)
                {
                    int i = tri[t + k];
                    if (!map.TryGetValue(i, out int j))
                    {
                        j = nv.Count; map[i] = j; nv.Add(v[i]);
                        if (n.Length == v.Length) nn.Add(n[i]);
                        if (uv.Length == v.Length) nuv.Add(uv[i]);
                    }
                    nt.Add(j);
                }
            }
            var mesh = new Mesh { name = name, indexFormat = nv.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(nv);
            if (nuv.Count == nv.Count) mesh.SetUVs(0, nuv);
            mesh.SetTriangles(nt, 0);
            if (nn.Count == nv.Count) mesh.SetNormals(nn); else mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------------------------------------------------------------- seabed

        static Collider Seabed(Transform env, StringBuilder log)
        {
            const float extent = 46, step = .5f;
            int n = Mathf.RoundToInt(extent * 2 / step);
            var v = new Vector3[(n + 1) * (n + 1)];
            var uv = new Vector2[v.Length];
            var t = new int[n * n * 6];
            for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
            {
                int i = z * (n + 1) + x;
                float wx = -extent + x * step, wz = -extent + z * step;
                v[i] = new Vector3(wx, Floor(wx, wz), wz);
                uv[i] = new Vector2(wx, wz) / 4f;
                if (x < n && z < n)
                {
                    int k = (z * n + x) * 6;
                    t[k] = i; t[k + 1] = i + n + 1; t[k + 2] = i + 1;
                    t[k + 3] = i + 1; t[k + 4] = i + n + 1; t[k + 5] = i + n + 2;
                }
            }
            var mesh = new Mesh { name = "Fjord seabed", vertices = v, uv = uv, triangles = t };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, Generated + "/Fjord seabed.asset");
            var go = new GameObject("Seabed • 80 x 80 m • 10–15 m");
            go.transform.SetParent(env, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Lit("Fjord seabed", SiltTexture(), .08f);
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            log.AppendLine($"Seabed: {v.Length} vertices, depth {-Floor(0, -20):F1} m (plateau) to {-Floor(0, 34):F1} m (far side)");
            return collider;
        }

        // Invisible walls just beyond the rim, where the floor reaches towards the surface.
        static void Boundary(Transform env)
        {
            var root = new GameObject("Arena boundary (invisible)").transform;
            root.SetParent(env, false);
            var walls = new[]
            {
                (new Vector3(-46.5f, -10, 0), new Vector3(1, 30, 94)), (new Vector3(46.5f, -10, 0), new Vector3(1, 30, 94)),
                (new Vector3(0, -10, -46.5f), new Vector3(94, 30, 1)), (new Vector3(0, -10, 46.5f), new Vector3(94, 30, 1)),
            };
            foreach (var (position, size) in walls)
            {
                var wall = new GameObject("Wall");
                wall.transform.SetParent(root, false);
                wall.transform.localPosition = position;
                wall.AddComponent<BoxCollider>().size = size;
            }
        }

        // ---------------------------------------------------------------- rocks

        static List<(Vector3 top, float size)> Boulders(Transform env, Mesh[] meshes, Material material, System.Random rng, StringBuilder log)
        {
            var parent = new GameObject("Boulders").transform;
            parent.SetParent(env, false);
            var result = new List<(Vector3, float)>();
            // Scattered stones plus a few glacial clusters, as on a moraine fjord floor.
            var clusters = new[] { new Vector2(-26, 18), new Vector2(22, 8), new Vector2(-6, -33), new Vector2(28, -26), new Vector2(4, 30) };
            for (int i = 0; i < 150; i++)
            {
                Vector2 xz;
                float size;
                if (i < 90)
                {
                    xz = clusters[i % clusters.Length] + new Vector2(R(rng, -6, 6), R(rng, -6, 6));
                    size = R(rng, .3f, 1.6f);
                }
                else
                {
                    xz = new Vector2(R(rng, -36, 36), R(rng, -36, 36));
                    size = R(rng, .25f, 1.1f);
                }
                if (rng.NextDouble() < .06) size *= 2.2f;
                var mesh = meshes[rng.Next(meshes.Length)];
                var go = new GameObject("Rock " + i);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = new Vector3(xz.x, Floor(xz.x, xz.y) - size * .12f, xz.y);
                go.transform.localRotation = Quaternion.Euler(R(rng, -8, 8), R(rng, 0, 360), R(rng, -8, 8));
                go.transform.localScale = Vector3.one * size;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                result.Add((go.transform.position + Vector3.up * (mesh.bounds.max.y * size * .85f), size));
            }
            log.AppendLine($"Boulders: {result.Count}");
            return result;
        }

        static Mesh RockMesh(int index)
        {
            const int lat = 10, lon = 18;
            var rng = new System.Random(100 + index);
            float o1 = R(rng, 0, 100), o2 = R(rng, 0, 100), sx = R(rng, .8f, 1.3f), sz = R(rng, .8f, 1.3f);
            Vector3 Point(Vector3 dir)
            {
                float r = 1 + .7f * (Noise3(dir * 1.2f, o1) - .5f) * 2 + .25f * (Noise3(dir * 3.5f, o2) - .5f) * 2;
                var p = dir * r;
                p.x *= sx; p.z *= sz; p.y *= .7f;
                // Flat underside, as a stone settled into the silt.
                if (p.y < -.2f) p.y = -.2f + (p.y + .2f) * .25f;
                return p;
            }
            var v = new List<Vector3> { Point(Vector3.up) };
            for (int i = 1; i < lat; i++)
            {
                float theta = Mathf.PI * i / lat;
                for (int j = 0; j < lon; j++)
                {
                    float phi = 2 * Mathf.PI * j / lon;
                    v.Add(Point(new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi))));
                }
            }
            v.Add(Point(Vector3.down));
            int Ring(int i, int j) => 1 + (i - 1) * lon + j % lon;
            var t = new List<int>();
            for (int j = 0; j < lon; j++) Outward(t, v, 0, Ring(1, j), Ring(1, j + 1));
            for (int i = 1; i < lat - 1; i++)
            for (int j = 0; j < lon; j++)
            {
                Outward(t, v, Ring(i, j), Ring(i + 1, j), Ring(i, j + 1));
                Outward(t, v, Ring(i, j + 1), Ring(i + 1, j), Ring(i + 1, j + 1));
            }
            for (int j = 0; j < lon; j++) Outward(t, v, v.Count - 1, Ring(lat - 1, j + 1), Ring(lat - 1, j));
            var mesh = new Mesh { name = "Rock " + index };
            mesh.SetVertices(v);
            mesh.SetUVs(0, v.Select(p => new Vector2(p.x + p.z, p.y) * 1.5f).ToList());
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, $"{Generated}/Rock {index}.asset");
            return mesh;
        }

        // Unity's front face is the one Cross(b - a, c - a) points at: make that the outside of the stone.
        static void Outward(List<int> t, List<Vector3> v, int a, int b, int c)
        {
            if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), v[a] + v[b] + v[c]) < 0) (b, c) = (c, b);
            t.Add(a); t.Add(b); t.Add(c);
        }

        static float Noise3(Vector3 p, float o) =>
            (Mathf.PerlinNoise(p.x + o, p.y + o) + Mathf.PerlinNoise(p.y + o, p.z + o) + Mathf.PerlinNoise(p.z + o, p.x + o)) / 3;

        static void Pebbles(Transform env, Mesh[] meshes, Material material, System.Random rng, StringBuilder log)
        {
            var combine = new List<CombineInstance>();
            for (int i = 0; i < 900; i++)
            {
                var p = new Vector2(R(rng, -37, 37), R(rng, -37, 37));
                float size = R(rng, .04f, .22f);
                combine.Add(new CombineInstance
                {
                    mesh = meshes[rng.Next(meshes.Length)],
                    transform = Matrix4x4.TRS(new Vector3(p.x, Floor(p.x, p.y) - size * .15f, p.y),
                        Quaternion.Euler(R(rng, -20, 20), R(rng, 0, 360), R(rng, -20, 20)), Vector3.one * size),
                });
            }
            var mesh = new Mesh { name = "Pebbles", indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(combine.ToArray(), true, true);
            AssetDatabase.CreateAsset(mesh, Generated + "/Pebbles.asset");
            var go = new GameObject("Pebbles");
            go.transform.SetParent(env, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            log.AppendLine($"Pebbles: {combine.Count}");
        }

        // ---------------------------------------------------------------- vegetation

        // Eelgrass (Zostera marina) meadows on the shallower plateau; single shoots in between.
        static void Eelgrass(Transform env, System.Random rng, StringBuilder log)
        {
            var material = Grass("Eelgrass", new Color(.15f, .23f, .07f), new Color(.30f, .29f, .13f), .12f, .7f);
            var parent = new GameObject("Eelgrass meadows").transform;
            parent.SetParent(env, false);
            int meadows = 0, blades = 0;
            for (int i = 0; i < 28; i++)
            {
                var centre = new Vector2(R(rng, -34, 34), R(rng, -34, 12));
                if (Floor(centre.x, centre.y) < -12.5f) continue;
                float radius = R(rng, 2, 6.5f);
                var builder = new RibbonBuilder();
                // Eelgrass grows in dense meadows: about 8 shoots per square metre here, thinning at the edge.
                int clumps = Mathf.RoundToInt(Mathf.PI * radius * radius * 8);
                for (int k = 0; k < clumps; k++)
                {
                    float a = R(rng, 0, Mathf.PI * 2), d = radius * Mathf.Sqrt((float)rng.NextDouble());
                    // Sparser towards the edge of the meadow.
                    if (rng.NextDouble() < d / radius * .7f) continue;
                    var p = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                    Shoot(builder, rng, new Vector3(p.x, Floor(p.x, p.y) - .02f, p.y), rng.Next(5, 11));
                }
                blades += builder.Blades;
                builder.Save(parent, "Meadow " + i, material, $"{Generated}/Meadow {i}.asset");
                meadows++;
            }
            var scattered = new RibbonBuilder();
            for (int k = 0; k < 500; k++)
            {
                var p = new Vector2(R(rng, -36, 36), R(rng, -36, 36));
                if (Floor(p.x, p.y) < -13) continue;
                Shoot(scattered, rng, new Vector3(p.x, Floor(p.x, p.y) - .02f, p.y), rng.Next(2, 5));
            }
            blades += scattered.Blades;
            scattered.Save(parent, "Scattered shoots", material, Generated + "/Scattered shoots.asset");
            log.AppendLine($"Eelgrass: {meadows} meadows, {blades} blades");
        }

        static void Shoot(RibbonBuilder builder, System.Random rng, Vector3 position, int count)
        {
            float yaw = R(rng, 0, 360);
            for (int i = 0; i < count; i++)
                builder.Blade(position + new Vector3(R(rng, -.04f, .04f), 0, R(rng, -.04f, .04f)), R(rng, .35f, 1f), R(rng, .012f, .02f),
                    yaw + R(rng, -40, 40), R(rng, .05f, .3f), R(rng, .05f, .18f), .5f, 0, 0, 4, Tint(rng, .25f), (float)rng.NextDouble());
        }

        // Sugar kelp (Saccharina latissima) on the larger stones: a short stipe and a long ruffled blade.
        static void Kelp(Transform env, List<(Vector3 top, float size)> rocks, System.Random rng, StringBuilder log)
        {
            var material = Grass("Sugar kelp", new Color(.24f, .18f, .06f), new Color(.40f, .31f, .12f), .2f, .45f);
            var builder = new RibbonBuilder();
            foreach (var (top, size) in rocks)
            {
                if (size < .7f || rng.NextDouble() > .35) continue;
                int blades = rng.Next(1, 4);
                for (int i = 0; i < blades; i++)
                    builder.Blade(top + new Vector3(R(rng, -.1f, .1f) * size, -.03f, R(rng, -.1f, .1f) * size), R(rng, 1, 2.2f), R(rng, .09f, .18f),
                        R(rng, 0, 360), R(rng, .35f, .6f), R(rng, .12f, .25f), .55f, .18f, .12f, 9, Tint(rng, .15f), (float)rng.NextDouble());
            }
            builder.Save(env, "Sugar kelp", material, Generated + "/Sugar kelp.asset");
            log.AppendLine($"Sugar kelp: {builder.Blades} blades");
        }

        static Color Tint(System.Random rng, float brown)
        {
            float bright = R(rng, .8f, 1.15f);
            return Color.Lerp(Color.white, new Color(1.2f, .95f, .7f), (float)rng.NextDouble() * brown * 2) * bright;
        }

        sealed class RibbonBuilder
        {
            readonly List<Vector3> v = new(), n = new();
            readonly List<Vector2> uv = new();
            readonly List<Color> c = new();
            readonly List<int> t = new();
            public int Blades;

            // A ribbon from base to tip that leans and curls over; uv.y = height, colour.a = sway phase.
            public void Blade(Vector3 position, float length, float width, float yaw, float lean, float curl,
                float tipWidth, float ruffle, float stipe, int segments, Color tint, float phase)
            {
                Blades++;
                Vector3 leanDirection = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                Vector3 side = Quaternion.Euler(0, yaw + 90, 0) * Vector3.forward;
                Vector3 direction = Vector3.Slerp(Vector3.up, leanDirection, lean).normalized;
                int start = v.Count;
                for (int s = 0; s <= segments; s++)
                {
                    float h = s / (float)segments;
                    float w = width * Mathf.Lerp(1, tipWidth, h) * (s == segments ? .25f : 1) * (1 + ruffle * Mathf.Sin(h * 37 + phase * 20));
                    if (stipe > 0) w *= Mathf.Lerp(.12f, 1, Mathf.SmoothStep(0, 1, h / stipe));
                    Vector3 normal = Vector3.Cross(direction, side).normalized;
                    v.Add(position - side * w * .5f); v.Add(position + side * w * .5f);
                    n.Add(normal); n.Add(normal);
                    uv.Add(new Vector2(0, h)); uv.Add(new Vector2(1, h));
                    var colour = new Color(tint.r, tint.g, tint.b, phase);
                    c.Add(colour); c.Add(colour);
                    if (s < segments)
                    {
                        int i = start + s * 2;
                        t.Add(i); t.Add(i + 2); t.Add(i + 1);
                        t.Add(i + 1); t.Add(i + 2); t.Add(i + 3);
                    }
                    direction = Vector3.Slerp(direction, leanDirection, curl).normalized;
                    position += direction * (length / segments);
                }
            }

            public void Save(Transform parent, string name, Material material, string path)
            {
                if (v.Count == 0) return;
                var mesh = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(v);
                mesh.SetNormals(n);
                mesh.SetUVs(0, uv);
                mesh.SetColors(c);
                mesh.SetTriangles(t, 0);
                mesh.RecalculateBounds();
                // The sway leaves the rest pose; keep the edges of a meadow from being culled.
                var bounds = mesh.bounds;
                bounds.Expand(.6f);
                mesh.bounds = bounds;
                AssetDatabase.CreateAsset(mesh, path);
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // ---------------------------------------------------------------- water

        static void Surface(Transform env)
        {
            var surface = GameObject.CreatePrimitive(PrimitiveType.Plane);
            surface.name = "Water surface 0 m";
            surface.transform.SetParent(env, false);
            surface.transform.localScale = new Vector3(12, 1, 12);
            Object.DestroyImmediate(surface.GetComponent<Collider>());
            surface.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Art + "/Lake surface.mat");
        }

        // Fine marine snow around the camera; the dive script sets its rate and brightness per water type.
        static ParticleSystem Snow(Transform env)
        {
            var go = new GameObject("Suspended sediment • follows camera");
            go.transform.SetParent(env, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.prewarm = true; main.duration = 10;
            main.startLifetime = 14; main.startSpeed = .02f;
            main.startSize = new ParticleSystem.MinMaxCurve(.008f, .05f);
            main.startColor = new Color(.55f, .57f, .47f, .5f);
            main.maxParticles = 3000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = 120;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(14, 9, 14);
            var noise = ps.noise;
            noise.enabled = true; noise.strength = .05f; noise.frequency = .2f; noise.scrollSpeed = .05f;
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Art + "/Sediment.mat");
            return ps;
        }

        // ---------------------------------------------------------------- materials and textures

        static Material Lit(string name, Texture2D texture, float smoothness)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", texture);
            material.mainTexture = texture;
            material.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(material, $"{Generated}/{name}.mat");
            return material;
        }

        static Material Grass(string name, Color baseColor, Color tip, float sway, float speed)
        {
            var shader = Shader.Find("TacSim/Seagrass");
            if (shader == null) throw new InvalidOperationException("TacSim/Seagrass shader is missing or failed to compile.");
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", baseColor);
            material.SetColor("_TipColor", tip);
            material.SetFloat("_Sway", sway);
            material.SetFloat("_Speed", speed);
            AssetDatabase.CreateAsset(material, $"{Generated}/{name}.mat");
            return material;
        }

        // Grey-brown fjord mud with flocculent patches, faint ripples, burrow holes and shell grit. Tiles seamlessly.
        static Texture2D SiltTexture()
        {
            const int size = 512;
            var rng = new System.Random(7);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float broad = Tile(u, v, 3, 10), mid = Tile(u, v, 9, 40), fine = Tile(u, v, 40, 80);
                float ripple = Mathf.Sin((v * 10 + Tile(u, v, 4, 60) * 1.6f) * Mathf.PI * 2) * .5f + .5f;
                float shade = .78f + .22f * broad + .12f * (mid - .5f) + .10f * (fine - .5f) + .05f * ripple;
                var mud = Color.Lerp(new Color(.24f, .23f, .17f), new Color(.36f, .34f, .26f), broad) * shade;
                mud.a = 1;
                px[y * size + x] = mud;
            }
            for (int i = 0; i < 220; i++) Dot(px, size, rng, 1.5f, 3, new Color(.07f, .07f, .05f), .45f);
            for (int i = 0; i < 350; i++) Dot(px, size, rng, 1, 2, new Color(.62f, .60f, .52f), .3f);
            return SaveTexture(px, size, "Fjord silt");
        }

        // Granite with dark cracks, olive algal film and pink crustose coralline patches.
        static Texture2D RockTexture()
        {
            const int size = 256;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                var c = Color.Lerp(new Color(.25f, .24f, .22f), new Color(.36f, .34f, .30f), Tile(u, v, 6, 3));
                c *= .9f + .2f * Tile(u, v, 30, 5);
                float crack = Tile(u, v, 14, 9);
                if (crack < .32f) c *= Mathf.Lerp(.55f, 1, crack / .32f);
                c = Color.Lerp(c, new Color(.20f, .24f, .11f), Mathf.SmoothStep(.55f, .7f, Tile(u, v, 4, 21)) * .8f);
                c = Color.Lerp(c, new Color(.58f, .40f, .42f), Mathf.SmoothStep(.62f, .75f, Tile(u, v, 5, 33)) * .7f);
                c.a = 1;
                px[y * size + x] = c;
            }
            return SaveTexture(px, size, "Fjord rock");
        }

        static Texture2D SaveTexture(Color[] px, int size, string name)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = name, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4,
            };
            texture.SetPixels(px);
            texture.Apply(true);
            AssetDatabase.CreateAsset(texture, $"{Generated}/{name}.asset");
            return texture;
        }

        // Perlin noise blended across the tile edges so the texture repeats without seams.
        static float Tile(float u, float v, float frequency, float offset)
        {
            float x = u * frequency + offset, y = v * frequency + offset;
            float a = Mathf.PerlinNoise(x, y), b = Mathf.PerlinNoise(x - frequency, y);
            float c = Mathf.PerlinNoise(x, y - frequency), d = Mathf.PerlinNoise(x - frequency, y - frequency);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        static void Dot(Color[] px, int size, System.Random rng, float minRadius, float maxRadius, Color colour, float strength)
        {
            float cx = R(rng, 0, size), cy = R(rng, 0, size), radius = R(rng, minRadius, maxRadius);
            int reach = Mathf.CeilToInt(radius);
            for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                float d = Mathf.Sqrt(dx * dx + dy * dy) / radius;
                if (d > 1) continue;
                int x = ((int)cx + dx + size) % size, y = ((int)cy + dy + size) % size;
                px[y * size + x] = Color.Lerp(px[y * size + x], colour, (1 - d * d) * strength);
            }
        }

        static float R(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}
