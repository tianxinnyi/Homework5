using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public sealed class GlobalTransformDemo : MonoBehaviour
{
    private enum Deformation { None, Taper, Twist, Bend, FFD }

    private int LatticeSize = 3;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private readonly List<Vector3> controlPoints = new List<Vector3>();
    private LatticeView latticeView;
    private Bounds sourceBounds;
    private Vector3[] sourceVertices;
    private MeshFilter modelFilter;
    private Mesh modelMesh;
    private Material modelMaterial;
    private string objPath = "Assets/cc.obj";
    private string status = "就绪";
    private Deformation deformation;
    private float strength = 0.55f;
    private int selectedPoint;
    private bool showGrid = true;
    private Vector2 panelScroll;
    private float panelWidth;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartDemo()
    {
        if (FindObjectOfType<GlobalTransformDemo>() != null) return;
        new GameObject("Global Transform Demo").AddComponent<GlobalTransformDemo>();
    }

    private void Start()
    {
        SetupCameraAndLight();
        var model = new GameObject("Imported OBJ");
        modelFilter = model.AddComponent<MeshFilter>();
        var renderer = model.AddComponent<MeshRenderer>();
        modelMaterial = new Material(Shader.Find("Standard"));
        modelMaterial.color = new Color(0.18f, 0.62f, 0.72f);
        renderer.sharedMaterial = modelMaterial;
        LoadObj(Path.Combine(Application.streamingAssetsPath, "cc.obj"));
    }

    private void SetupCameraAndLight()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            camera = cameraObject.AddComponent<Camera>();
        }
        UpdateCameraOrbit();
        camera.fieldOfView = 42f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.10f, 0.13f, 0.16f);
        if (FindObjectOfType<Light>() == null)
        {
            var lightObject = new GameObject("Key Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.35f;
            lightObject.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
        }
    }

    private void LoadObj(string path)
    {
        try
        {
            ObjData data = ParseObj(File.ReadAllLines(path));
            if (data.vertices.Count == 0 || data.triangles.Count < 3) throw new InvalidDataException("OBJ 文件必须包含顶点和面。");
            Vector3 min = data.vertices[0], max = data.vertices[0];
            foreach (Vector3 v in data.vertices) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
            Vector3 center = (min + max) * 0.5f;
            float extent = Mathf.Max(max.x - min.x, max.y - min.y, max.z - min.z);
            if (extent < 0.00001f) throw new InvalidDataException("OBJ 包围盒尺寸无效。");
            Vector3[] normalizedVertices = data.vertices.ToArray();
            for (int i = 0; i < normalizedVertices.Length; i++) normalizedVertices[i] = (normalizedVertices[i] - center) * (2f / extent);
            int subdivision = data.triangles.Count < 3000 ? 8 : 1;
            Subdivide(normalizedVertices, data.triangles, subdivision, out sourceVertices, out int[] denseTriangles);
            if (modelMesh != null) Destroy(modelMesh);
            modelMesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            if (sourceVertices.Length > 65535) modelMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            modelMesh.vertices = sourceVertices;
            modelMesh.triangles = denseTriangles;
            modelMesh.RecalculateNormals();
            modelMesh.RecalculateBounds();
            modelFilter.sharedMesh = modelMesh;
            sourceBounds = modelMesh.bounds;
            InitializeControlPoints();
            if (latticeView == null) latticeView = gameObject.AddComponent<LatticeView>();
            ApplyDeformation();
            objPath = path;
            status = "已加载：" + Path.GetFileName(path) + "  |  顶点数：" + sourceVertices.Length;
        }
        catch (Exception e) { status = "加载失败：" + e.Message; }
    }

    private sealed class ObjData
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<int> triangles = new List<int>();
    }

    private static ObjData ParseObj(string[] lines)
    {
        var data = new ObjData();
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            int comment = line.IndexOf('#');
            if (comment >= 0) line = line.Substring(0, comment).Trim();
            if (line.Length == 0) continue;
            string[] fields = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (fields[0] == "v" && fields.Length >= 4)
                data.vertices.Add(new Vector3(ParseFloat(fields[1]), ParseFloat(fields[2]), ParseFloat(fields[3])));
            else if (fields[0] == "f" && fields.Length >= 4)
            {
                var face = new List<int>();
                for (int i = 1; i < fields.Length; i++)
                {
                    string indexText = fields[i].Split('/')[0];
                    int index = int.Parse(indexText, CultureInfo.InvariantCulture);
                    int resolved = index > 0 ? index - 1 : data.vertices.Count + index;
                    if (resolved < 0 || resolved >= data.vertices.Count) throw new InvalidDataException("面索引超出顶点列表范围。");
                    face.Add(resolved);
                }
                for (int i = 1; i < face.Count - 1; i++)
                { data.triangles.Add(face[0]); data.triangles.Add(face[i]); data.triangles.Add(face[i + 1]); }
            }
        }
        return data;
    }

    private static float ParseFloat(string value)
    {
        float parsed = float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (float.IsNaN(parsed) || float.IsInfinity(parsed)) throw new InvalidDataException("顶点坐标不是有效数值。");
        return parsed;
    }

    private static void Subdivide(Vector3[] vertices, List<int> triangles, int steps, out Vector3[] resultVertices, out int[] resultTriangles)
    {
        var denseVertices = new List<Vector3>();
        var denseTriangles = new List<int>();
        for (int t = 0; t < triangles.Count; t += 3)
        {
            Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
            int[,] indices = new int[steps + 1, steps + 1];
            for (int i = 0; i <= steps; i++) for (int j = 0; j <= steps - i; j++)
            {
                indices[i, j] = denseVertices.Count;
                denseVertices.Add(a + (b - a) * (i / (float)steps) + (c - a) * (j / (float)steps));
            }
            for (int i = 0; i < steps; i++) for (int j = 0; j < steps - i; j++)
            {
                denseTriangles.Add(indices[i, j]); denseTriangles.Add(indices[i + 1, j]); denseTriangles.Add(indices[i, j + 1]);
                if (i + j < steps - 1)
                { denseTriangles.Add(indices[i + 1, j]); denseTriangles.Add(indices[i + 1, j + 1]); denseTriangles.Add(indices[i, j + 1]); }
            }
        }
        resultVertices = denseVertices.ToArray();
        resultTriangles = denseTriangles.ToArray();
    }

    private void InitializeControlPoints()
    {
        controlPoints.Clear();
        for (int z = 0; z < LatticeSize; z++)
            for (int y = 0; y < LatticeSize; y++)
                for (int x = 0; x < LatticeSize; x++)
                    controlPoints.Add(sourceBounds.min + Vector3.Scale(sourceBounds.size, new Vector3(x, y, z) / (LatticeSize - 1)));
        selectedPoint = controlPoints.Count / 2;
    }

    private void ApplyDeformation()
    {
        if (modelMesh == null || sourceVertices == null) return;
        var result = new Vector3[sourceVertices.Length];
        for (int i = 0; i < result.Length; i++)
        {
            Vector3 p = sourceVertices[i];
            float h = BoundsParameter(p.y, sourceBounds.min.y, sourceBounds.size.y);
            switch (deformation)
            {
                case Deformation.Taper:
                    float scale = Mathf.Max(0.12f, 1f - strength * h);
                    p.x *= scale; p.z *= scale; break;
                case Deformation.Twist:
                    float angle = strength * Mathf.PI * 2f * h;
                    float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
                    float x = p.x * c - p.z * s, z = p.x * s + p.z * c;
                    p.x = x; p.z = z; break;
                case Deformation.Bend:
                    float curvature = strength * 1.2f;
                    if (Mathf.Abs(curvature) > 0.0001f)
                    {
                        float theta = curvature * (p.y - sourceBounds.min.y);
                        float radius = 1f / curvature;
                        p = new Vector3(radius - (radius - p.x) * Mathf.Cos(theta), sourceBounds.min.y + (radius - p.x) * Mathf.Sin(theta), p.z);
                    }
                    break;
                case Deformation.FFD:
                    p = EvaluateFFD(p); break;
            }
            result[i] = p;
        }
        modelMesh.vertices = result;
        modelMesh.RecalculateNormals();
        modelMesh.RecalculateBounds();
    }

    private Vector3 EvaluateFFD(Vector3 p)
    {
        float u = BoundsParameter(p.x, sourceBounds.min.x, sourceBounds.size.x);
        float v = BoundsParameter(p.y, sourceBounds.min.y, sourceBounds.size.y);
        float w = BoundsParameter(p.z, sourceBounds.min.z, sourceBounds.size.z);
        Vector3 result = Vector3.zero;
        for (int z = 0; z < LatticeSize; z++)
            for (int y = 0; y < LatticeSize; y++)
                for (int x = 0; x < LatticeSize; x++)
                {
                    float weight = Bernstein(LatticeSize - 1, x, u) * Bernstein(LatticeSize - 1, y, v) * Bernstein(LatticeSize - 1, z, w);
                    result += controlPoints[PointIndex(x, y, z)] * weight;
                }
        return result;
    }

    private int PointIndex(int x, int y, int z) { return z * LatticeSize * LatticeSize + y * LatticeSize + x; }
    private static float BoundsParameter(float value, float min, float size) { return size < 0.00001f ? 0.5f : Mathf.Clamp01((value - min) / size); }
    private static float Bernstein(int degree, int i, float t)
    {
        float choose = 1f;
        for (int k = 1; k <= i; k++) choose *= (degree - k + 1f) / k;
        return choose * Mathf.Pow(t, i) * Mathf.Pow(1f - t, degree - i);
    }

    private void OnGUI()
    {
        GUI.skin.label.wordWrap = true;
        panelWidth = Mathf.Clamp(Screen.width * 0.31f, 300f, 420f);
        panelWidth = Mathf.Min(panelWidth, Mathf.Max(220f, Screen.width - 240f));
        GUILayout.BeginArea(new Rect(14, 14, panelWidth, Screen.height - 28), GUI.skin.box);
        panelScroll = GUILayout.BeginScrollView(panelScroll);
        GUILayout.Label("全局变形实验室", TitleStyle());
        GUILayout.Label("OBJ 导入  |  网格变形", GUI.skin.box);
        GUILayout.Space(8);
        GUILayout.Label("OBJ 文件路径");
        objPath = GUILayout.TextField(objPath, GUILayout.Height(28));
        if (GUILayout.Button("加载 OBJ", GUILayout.Height(34)))
        {
            string path = objPath;
            if (!Path.IsPathRooted(path)) path = Path.Combine(Directory.GetCurrentDirectory(), path);
            LoadObj(path);
        }
#if UNITY_EDITOR
        if (GUILayout.Button("浏览 OBJ 文件...", GUILayout.Height(26)))
        {
            string picked = UnityEditor.EditorUtility.OpenFilePanel("导入 OBJ 文件", "", "obj");
            if (!string.IsNullOrEmpty(picked)) LoadObj(picked);
        }
#endif
        GUILayout.Space(10);
        GUILayout.Label("变形模式", GUI.skin.box);
        GUILayout.BeginHorizontal(); ModeButton("复原", Deformation.None); ModeButton("锥化", Deformation.Taper); GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(); ModeButton("扭曲", Deformation.Twist); ModeButton("弯曲", Deformation.Bend); GUILayout.EndHorizontal();
        ModeButton("FFD 控制格", Deformation.FFD);
        if (deformation != Deformation.None && deformation != Deformation.FFD)
        {
            float value = LabeledSlider("变形强度", strength, 0f, 0.95f, "0.00");
            if (!Mathf.Approximately(value, strength)) { strength = value; ApplyDeformation(); }
        }
        if (deformation == Deformation.FFD && controlPoints.Count > 0)
        {
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Grid n = " + LatticeSize);
            for (int n = 2; n <= 5; n++)
                if (GUILayout.Button(n.ToString())) { LatticeSize = n; InitializeControlPoints(); ApplyDeformation(); }
            showGrid = GUILayout.Toggle(showGrid, "Grid"); GUILayout.EndHorizontal();
            GUILayout.Label("修改 n 会重置控制点。");
            selectedPoint = Mathf.Clamp(Mathf.RoundToInt(TrackSlider("点编号", selectedPoint, 0, controlPoints.Count - 1, "0")), 0, controlPoints.Count - 1);
            Vector3 cp = controlPoints[selectedPoint];
            GUILayout.Label("控制点 " + selectedPoint + "  （x、y、z）");
            Vector3 edited = new Vector3(AxisSlider("X", cp.x), AxisSlider("Y", cp.y), AxisSlider("Z", cp.z));
            if (edited != cp) { controlPoints[selectedPoint] = edited; ApplyDeformation(); }
            if (GUILayout.Button("重置控制格")) { InitializeControlPoints(); ApplyDeformation(); }
        }
        GUILayout.FlexibleSpace();
        GUILayout.Label(status);
        GUILayout.Label("拖动鼠标旋转视角  |  滚轮缩放", GUI.skin.box);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        HandleOrbit();
    }

    private void LateUpdate()
    {
        Camera camera = Camera.main;
        if (camera != null)
        {
            float panel = Mathf.Clamp01((panelWidth + 28f) / Mathf.Max(Screen.width, 1));
            camera.rect = new Rect(panel, 0f, 1f - panel, 1f);
        }
        if (latticeView != null) latticeView.Display(controlPoints, LatticeSize, selectedPoint, showGrid && deformation == Deformation.FFD);
    }

    private float AxisSlider(string axis, float value)
    {
        return LabeledSlider(axis, value, -2f, 2f, "0.00");
    }

    private float LabeledSlider(string label, float value, float min, float max, string format)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(76));
        float next = TrackSlider(null, value, min, max, format);
        GUILayout.EndHorizontal();
        return next;
    }

    private float TrackSlider(string label, float value, float min, float max, string format)
    {
        GUILayout.BeginHorizontal();
        if (!string.IsNullOrEmpty(label)) GUILayout.Label(label, GUILayout.Width(76));
        Rect rect = GUILayoutUtility.GetRect(80f, 26f, GUILayout.ExpandWidth(true));
        Rect valueRect = GUILayoutUtility.GetRect(42f, 24f, GUILayout.Width(42f));
        float t = max > min ? Mathf.Clamp01((value - min) / (max - min)) : 0f;
        float left = rect.x + 8f, right = rect.xMax - 8f, centerY = rect.center.y;
        float knobX = Mathf.Lerp(left, right, t);
        Rect track = new Rect(left, centerY - 3f, Mathf.Max(1f, right - left), 6f);
        Rect fill = new Rect(track.x, track.y, Mathf.Max(0f, knobX - track.x), track.height);
        GUI.color = new Color(0.10f, 0.17f, 0.20f);
        GUI.DrawTexture(track, Texture2D.whiteTexture);
        GUI.color = new Color(0.18f, 0.78f, 0.78f);
        GUI.DrawTexture(fill, Texture2D.whiteTexture);
        GUI.color = new Color(0.12f, 0.24f, 0.27f);
        GUI.DrawTexture(new Rect(knobX - 9f, centerY - 9f, 18f, 18f), Texture2D.whiteTexture);
        GUI.color = new Color(0.38f, 0.95f, 0.90f);
        GUI.DrawTexture(new Rect(knobX - 6f, centerY - 6f, 12f, 12f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(valueRect, value.ToString(format, Invariant), ValueStyle());

        Event e = Event.current;
        int controlId = GUIUtility.GetControlID(FocusType.Passive, rect);
        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && rect.Contains(e.mousePosition))
        {
            GUIUtility.hotControl = controlId;
            float position = Mathf.Clamp01((e.mousePosition.x - left) / Mathf.Max(1f, right - left));
            value = Mathf.Lerp(min, max, position);
            if (e.type == EventType.MouseDown) e.Use();
            if (e.type == EventType.MouseDrag) e.Use();
        }
        else if (e.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
        {
            GUIUtility.hotControl = 0;
            e.Use();
        }
        GUILayout.EndHorizontal();
        return value;
    }

    private GUIStyle ValueStyle()
    {
        return new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleRight,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.58f, 0.90f, 0.88f) }
        };
    }

    private void ModeButton(string label, Deformation mode)
    {
        bool active = deformation == mode;
        Color old = GUI.backgroundColor;
        if (active) GUI.backgroundColor = new Color(0.30f, 0.75f, 0.78f);
        if (GUILayout.Button(label, GUILayout.Height(34))) { deformation = mode; ApplyDeformation(); }
        GUI.backgroundColor = old;
    }

    private GUIStyle TitleStyle()
    {
        return new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.40f, 0.88f, 0.88f) } };
    }

    private float orbitYaw = 38f, orbitPitch = 24f, orbitDistance = 7.5f;
    private void HandleOrbit()
    {
        if (Event.current.type == EventType.MouseDrag && Event.current.button == 0 && Event.current.mousePosition.x > panelWidth + 28f)
        { orbitYaw += Event.current.delta.x * 0.5f; orbitPitch = Mathf.Clamp(orbitPitch - Event.current.delta.y * 0.5f, -75f, 75f); UpdateCameraOrbit(); }
        if (Event.current.type == EventType.ScrollWheel && Event.current.mousePosition.x > panelWidth + 28f)
        { orbitDistance = Mathf.Clamp(orbitDistance + Event.current.delta.y * 0.15f, 3f, 18f); UpdateCameraOrbit(); }
    }

    private void UpdateCameraOrbit()
    {
        Camera camera = Camera.main;
        if (camera == null) return;
        Quaternion rotation = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
        camera.transform.position = rotation * new Vector3(0f, 0f, -orbitDistance);
        camera.transform.LookAt(Vector3.zero);
    }

    private void OnDrawGizmos()
    {
        if (!showGrid || deformation != Deformation.FFD || controlPoints.Count != LatticeSize * LatticeSize * LatticeSize) return;
        Gizmos.color = deformation == Deformation.FFD ? new Color(0.35f, 0.9f, 0.9f, 0.75f) : new Color(0.4f, 0.6f, 0.7f, 0.4f);
        for (int z = 0; z < LatticeSize; z++) for (int y = 0; y < LatticeSize; y++) for (int x = 0; x < LatticeSize; x++)
        {
            Vector3 p = controlPoints[PointIndex(x, y, z)];
            if (x + 1 < LatticeSize) Gizmos.DrawLine(p, controlPoints[PointIndex(x + 1, y, z)]);
            if (y + 1 < LatticeSize) Gizmos.DrawLine(p, controlPoints[PointIndex(x, y + 1, z)]);
            if (z + 1 < LatticeSize) Gizmos.DrawLine(p, controlPoints[PointIndex(x, y, z + 1)]);
            Gizmos.color = PointIndex(x, y, z) == selectedPoint && deformation == Deformation.FFD ? Color.yellow : new Color(0.35f, 0.9f, 0.9f, 0.85f);
            Gizmos.DrawSphere(p, 0.045f);
            Gizmos.color = new Color(0.35f, 0.9f, 0.9f, 0.75f);
        }
    }

    private void OnDestroy()
    {
        if (modelMesh != null) Destroy(modelMesh);
        if (modelMaterial != null) Destroy(modelMaterial);
    }
}
