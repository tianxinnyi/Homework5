using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;

public static class SmokeCheck
{
    public static void Main()
    {
        Type demo = typeof(GlobalTransformDemo);
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        object parsed = demo.GetMethod("ParseObj", flags).Invoke(null, new object[] { File.ReadAllLines("Assets/cc.obj") });
        Type dataType = parsed.GetType();
        var vertices = (System.Collections.Generic.List<Vector3>)dataType.GetField("vertices").GetValue(parsed);
        var triangles = (System.Collections.Generic.List<int>)dataType.GetField("triangles").GetValue(parsed);
        if (vertices.Count != 5 || triangles.Count != 18) throw new Exception("OBJ parser count mismatch");
        object[] args = { vertices.ToArray(), triangles, 8, null, null };
        demo.GetMethod("Subdivide", flags).Invoke(null, args);
        var dense = (Vector3[])args[3]; var indices = (int[])args[4];
        if (dense.Length != 270 || indices.Length != 1152) throw new Exception("Subdivision count mismatch");
        foreach (int i in indices) if (i < 0 || i >= dense.Length) throw new Exception("Invalid triangle index");
        MethodInfo bernstein = demo.GetMethod("Bernstein", flags);
        for (int n = 2; n <= 5; n++) for (int sample = 0; sample <= 20; sample++)
        {
            float t = sample / 20f, sum = 0, reconstructed = 0;
            for (int i = 0; i < n; i++)
            {
                float weight = (float)bernstein.Invoke(null, new object[] { n - 1, i, t });
                sum += weight; reconstructed += weight * i / (n - 1f);
            }
            if (Math.Abs(sum - 1) > 0.00001 || Math.Abs(reconstructed - t) > 0.00001) throw new Exception("FFD identity failure");
        }
        Console.WriteLine("PASS: cc.obj parser (5 vertices, 6 triangles), subdivision (270 vertices, 384 triangles), valid indices, Bernstein partition and identity for n=2..5.");
    }
}
