// Assets/TAC/Pipeline/Editor/PipelineToolWindow.cs
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TAC.Pipeline;
using UnityEditor;
using UnityEngine;

public class PipelineToolWindow : EditorWindow
{
    GameObject _prefab;
    DefaultAsset _folder;
    ProceduralPipelineGenerator _target;

    [MenuItem("Tools/TAC Challenge/Pipeline Generator")]
    static void Open()
    {
        var w = GetWindow<PipelineToolWindow>("TAC Pipeline");
        w.minSize = new Vector2(360f, 360f);
    }

    void OnEnable()
    {
        if (Selection.activeGameObject != null)
            _target = Selection.activeGameObject.GetComponent<ProceduralPipelineGenerator>();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("1) Create the prefab", EditorStyles.boldLabel);
        _folder = (DefaultAsset)EditorGUILayout.ObjectField("Folder", _folder, typeof(DefaultAsset), false);
        if (GUILayout.Button("Create TAC Pipeline Prefab")) CreatePrefab();
        EditorGUILayout.HelpBox("Prefab = empty GameObject + ProceduralPipelineGenerator (+ surface style). " +
                                "Geometry and markers are generated at Awake / on demand.", MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("2) Drive a scene instance", EditorStyles.boldLabel);
        _target = (ProceduralPipelineGenerator)EditorGUILayout.ObjectField("Generator", _target,
                    typeof(ProceduralPipelineGenerator), true);

        if (_target != null)
        {
            EditorGUI.BeginChangeCheck();
            _target.orthogonalLayout = EditorGUILayout.Toggle("Orthogonal 90° elbows", _target.orthogonalLayout);
            _target.selfAvoidPath    = EditorGUILayout.Toggle("Self-avoid path", _target.selfAvoidPath);
            if (EditorGUI.EndChangeCheck()) { _target.Regenerate(); EditorUtility.SetDirty(_target); }
        }

        using (new EditorGUI.DisabledScope(_target == null))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Generate"))  { _target.Regenerate(); EditorUtility.SetDirty(_target); }
            if (GUILayout.Button("Randomize")) { _target.Randomize();  EditorUtility.SetDirty(_target); }
            if (GUILayout.Button("Fit volume to path")) { _target.FitVolumeToPath(); EditorUtility.SetDirty(_target); }
            EditorGUILayout.EndHorizontal();

            if (_target.PlacedMarkerCount > 0)
            {
                EditorGUILayout.SelectableLabel("Sequence from pinger: " + _target.SequenceFromPingerString(),
                    EditorStyles.textField, GUILayout.Height(18f));
            }
            if (GUILayout.Button("Export ArUco PNGs + ground truth CSV")) ExportMarkers();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("3) Booklet limits (§3.2)", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("max total length", "10 m");
        EditorGUILayout.LabelField("diameter", "200 mm");
        EditorGUILayout.LabelField("markers", "4..10, IDs 1..99, unique, >= 0.2 m apart, horizontal, random yaw");
        EditorGUILayout.LabelField("joints", "elbows only, 0 .. 90 deg, straight duct between, constant depth");
    }

    // ------------------------------------------------------------------- prefab
    void CreatePrefab()
    {
        string root = _folder != null ? AssetDatabase.GetAssetPath(_folder) : "Assets";
        if (!root.StartsWith("Assets")) root = "Assets";
        string dir = root + "/TAC/Pipeline";
        EnsureFolder(dir);

        var styleAsset = AssetDatabase.LoadAssetAtPath<PipelineSurfaceStyle>(dir + "/PipelineSurfaceStyle.asset");
        if (styleAsset == null)
        {
            styleAsset = ScriptableObject.CreateInstance<PipelineSurfaceStyle>();
            AssetDatabase.CreateAsset(styleAsset, dir + "/PipelineSurfaceStyle.asset");
        }

        var go = new GameObject("TAC_Pipeline_Procedural");
        var gen = go.AddComponent<ProceduralPipelineGenerator>();
        gen.style = styleAsset;
        gen.generateOnAwake = true;
        gen.generateInEditMode = false;                 // was 'runInEditMode' -> does not exist
        gen.seed = (uint)Random.Range(1, int.MaxValue);
        gen.totalLength = 7.5f;
        gen.pipeDiameter = 0.2f;
        gen.markerCount = 6;
        gen.markerSize = 0.15f;
        gen.markerBits = 4;
        gen.quietZoneModules = 1;

        // straight-run layout: 90° elbows only, compact fittings, no self intersection
        gen.orthogonalLayout = true;
        gen.elbowRadius = 0.18f;
        gen.minStraightBetweenElbows = 0.35f;
        gen.selfAvoidPath = true;
        gen.selfClearancePipeRadii = 2.6f;

        gen.useVolume = true;
        gen.volumeCenter = new Vector3(0f, 0f, 0f);
        gen.volumeSize = new Vector3(6f, 1.2f, 6f);

        string path = dir + "/TAC_Pipeline_Procedural.prefab";
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        AssetDatabase.Refresh();

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<GameObject>(path));
        Selection.activeGameObject = instance;
        EditorGUIUtility.PingObject(instance);
        _target = instance.GetComponent<ProceduralPipelineGenerator>();
        if (_target != null) _target.Regenerate();     // edit-mode preview

        _prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Debug.Log($"[TAC Pipeline] Prefab created at {path}");
    }

    static void EnsureFolder(string path)
    {
        path = path.Replace('\\', '/');
        if (AssetDatabase.IsValidFolder(path)) return;
        string[] parts = path.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }

    // ------------------------------------------------------------------- export
    void ExportMarkers()
    {
        string folder = EditorUtility.SaveFolderPanel("Export ArUco cards",
            Application.dataPath + "/TAC/Exports", "");
        if (string.IsNullOrEmpty(folder)) return;

        int seedDirIdx = folder.IndexOf("ArUco_");
        string tag = seedDirIdx >= 0 ? folder.Substring(seedDirIdx) : "seed_" + _target.seed;
        string dir = Path.Combine(folder, tag);
        Directory.CreateDirectory(dir);

        var csv = new StringBuilder();
        csv.AppendLine("indexFromPinger,markerId,arcLength_m,localX,localY,localZ,yawDeg,bits,codeHex,texture");

        foreach (var m in _target.Markers)
        {
            if (m.cardTexture != null)
            {
                byte[] png = m.cardTexture.EncodeToPNG();
                if (png != null)
                {
                    string pngPath = Path.Combine(dir, $"marker_{m.markerId:00}.png");
                    File.WriteAllBytes(pngPath, pngPath != null ? png : png);   // keep linters quiet
                    File.WriteAllBytes(pngPath, png);
                }
            }
            csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2:0.####},{3:0.####},{4:0.####},{5:0.####},{6:0.##},{7},{8},marker_{9:00}.png",
                m.indexFromPinger, m.markerId, m.arcLength,
                m.localPositionOnPath.x, m.localPositionOnPath.y, m.localPositionOnPath.z,
                m.yawDegrees, m.bits, "0x" + m.code.ToString("X16"), m.markerId));
        }

        csv.AppendLine();
        csv.AppendLine("# total_length_m," + _target.PathLength.ToString("0.####", CultureInfo.InvariantCulture));
        csv.AppendLine("# joints," + _target.JointCount);
        csv.AppendLine("# diameter_m," + _target.pipeDiameter.ToString("0.###", CultureInfo.InvariantCulture));
        csv.AppendLine("# sequence_from_pinger," + _target.SequenceFromPingerString());

        File.WriteAllText(Path.Combine(dir, "ground_truth.csv"), csv.ToString());
        AssetDatabase.Refresh();
        EditorUtility.RevealInFinder(dir);
        Debug.Log($"[TAC Pipeline] Exported {_target.PlacedMarkerCount} cards -> {dir}");
    }
}
#endif
