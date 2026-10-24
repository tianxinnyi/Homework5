using System.Collections.Generic;
using UnityEngine;

// Real scene geometry: the FFD cage is visible in Game view and standalone builds.
public sealed class LatticeView : MonoBehaviour
{
    private readonly List<Transform> points = new List<Transform>();
    private readonly List<LineRenderer> edges = new List<LineRenderer>();
    private Material material;

    public void Display(List<Vector3> positions, int n, int selected, bool visible)
    {
        if (material == null) material = new Material(Shader.Find("Sprites/Default"));
        while (points.Count < positions.Count)
        {
            GameObject point = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            point.name = "FFD point " + points.Count;
            point.transform.SetParent(transform, false);
            Destroy(point.GetComponent<Collider>());
            point.GetComponent<Renderer>().sharedMaterial = material;
            points.Add(point.transform);
        }
        for (int i = 0; i < points.Count; i++)
        {
            points[i].gameObject.SetActive(visible && i < positions.Count);
            if (i >= positions.Count) continue;
            points[i].localPosition = positions[i];
            points[i].localScale = Vector3.one * (i == selected ? 0.115f : 0.055f);
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", i == selected ? Color.yellow : new Color(0.2f, 0.85f, 0.9f));
            points[i].GetComponent<Renderer>().SetPropertyBlock(block);
        }
        int edge = 0;
        for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            int index = z * n * n + y * n + x;
            if (x + 1 < n) DrawEdge(edge++, positions[index], positions[index + 1], visible);
            if (y + 1 < n) DrawEdge(edge++, positions[index], positions[index + n], visible);
            if (z + 1 < n) DrawEdge(edge++, positions[index], positions[index + n * n], visible);
        }
        for (int i = edge; i < edges.Count; i++) edges[i].enabled = false;
    }

    private void DrawEdge(int index, Vector3 a, Vector3 b, bool visible)
    {
        if (index >= edges.Count)
        {
            var go = new GameObject("FFD edge");
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.positionCount = 2;
            line.useWorldSpace = false;
            line.startWidth = line.endWidth = 0.008f;
            line.startColor = line.endColor = new Color(0.2f, 0.85f, 0.9f, 0.65f);
            edges.Add(line);
        }
        edges[index].enabled = visible;
        edges[index].SetPosition(0, a);
        edges[index].SetPosition(1, b);
    }

    private void OnDestroy() { if (material != null) Destroy(material); }
}
