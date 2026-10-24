using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using BSLK.Optimizer;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// Copy this file into Assets/Editor of an isolated Unity 2022.3 test project.
// Run: Unity -batchmode -projectPath <project> -executeMethod BSLKClubOcclusionRegression.Run -logFile <log>
public static class BSLKClubOcclusionRegression
{
    private static int checks;
    private static string fixture;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
        checks++;
        Debug.Log("PASS: " + message);
    }

    public static void Run()
    {
        try
        {
            fixture = "Assets/ClubTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(fixture));
            Pose();
            CoveredTorso();
            ToggleableCover();
            LockedAndSeeThrough();
            Performance();
            Debug.Log("BSLK OCCLUSION REGRESSION PASSED: " + checks + " assertions");
            File.WriteAllText("test-results.txt", "PASSED: " + checks + " assertions");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            File.WriteAllText("test-results.txt", "FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }

    // ---- fixture -------------------------------------------------------------------------------

    private const float ShrinkLow = 1.1f, ShrinkHigh = 1.2f;

    private sealed class Rig
    {
        public GameObject root;
        public Transform hips, chest;
        public SkinnedMeshRenderer body, jacket;
        public Mesh bodyMesh;
    }

    // Avatar-space cylinder (axis Y) stored in the renderer's local space; weights blend Hips -> Chest by height.
    private static Mesh Cylinder(string name, float radius, float y0, float y1, int segments, int rings, bool caps, float localScale, bool shrink, int submeshSplitRing)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        for (int r = 0; r <= rings; r++)
            for (int s = 0; s < segments; s++)
            {
                float angle = s * Mathf.PI * 2f / segments, y = Mathf.Lerp(y0, y1, (float)r / rings);
                vertices.Add(new Vector3(Mathf.Sin(angle) * radius, y, Mathf.Cos(angle) * radius));
                uvs.Add(new Vector2((float)s / segments, (float)r / rings));
            }
        var upper = new List<int>();
        var lower = new List<int>();
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = r * segments + s, b = r * segments + (s + 1) % segments, c = a + segments, d = b + segments;
                // Clockwise seen from outside: Unity front faces point outward.
                List<int> list = r < submeshSplitRing ? lower : upper;
                list.AddRange(new[] { a, b, c, b, d, c });
            }
        if (caps)
        {
            int bottom = vertices.Count;
            vertices.Add(new Vector3(0, y0, 0)); uvs.Add(Vector2.zero);
            int top = vertices.Count;
            vertices.Add(new Vector3(0, y1, 0)); uvs.Add(Vector2.one);
            int last = rings * segments;
            for (int s = 0; s < segments; s++)
            {
                int n = (s + 1) % segments;
                upper.AddRange(new[] { top, last + s, last + n });
                upper.AddRange(new[] { bottom, n, s });
            }
        }
        var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(vertices.Select(v => v / localScale).ToList());
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(upper, 0);
        mesh.SetTriangles(lower, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.boneWeights = vertices.Select(v =>
        {
            float chest = Mathf.Clamp01((v.y - 1.0f) / 0.2f);
            return new BoneWeight { boneIndex0 = 0, weight0 = 1 - chest, boneIndex1 = 1, weight1 = chest };
        }).ToArray();
        if (shrink)
        {
            Vector3[] normals = mesh.normals;
            var deltas = new Vector3[vertices.Count];
            for (int v = 0; v < vertices.Count; v++)
                if (InShrink(vertices[v])) deltas[v] = -normals[v] * 0.01f / localScale;
            mesh.AddBlendShapeFrame("Shrink", 100, deltas, null, null);
            mesh.AddBlendShapeFrame("Zero", 100, new Vector3[vertices.Count], null, null);
        }
        return mesh;
    }

    private static bool InShrink(Vector3 v)
    {
        return v.y >= ShrinkLow - 1e-4f && v.y <= ShrinkHigh + 1e-4f && v.z > 0 && Mathf.Abs(v.x) <= v.z;
    }

    private static Material MakeMaterial(string name, bool transparent = false)
    {
        var material = new Material(Shader.Find("Standard")) { name = name };
        if (transparent) material.renderQueue = (int)RenderQueue.Transparent;
        AssetDatabase.CreateAsset(material, fixture + "/" + name + ".mat");
        return material;
    }

    private static SkinnedMeshRenderer Skin(string name, Transform parent, Mesh mesh, Transform hips, Transform chest, float localScale, Material[] materials)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * localScale;
        var renderer = go.AddComponent<SkinnedMeshRenderer>();
        mesh.bindposes = new[] { hips.worldToLocalMatrix * go.transform.localToWorldMatrix, chest.worldToLocalMatrix * go.transform.localToWorldMatrix };
        renderer.bones = new[] { hips, chest };
        renderer.rootBone = hips;
        renderer.sharedMesh = mesh;
        renderer.sharedMaterials = materials;
        return renderer;
    }

    private static Rig Build(string name, int segments, int rings, bool unreadableBody, bool transparentJacket = false)
    {
        var rig = new Rig { root = new GameObject(name) };
        var armature = new GameObject("Armature").transform;
        armature.SetParent(rig.root.transform, false);
        rig.hips = new GameObject("Hips").transform;
        rig.hips.SetParent(armature, false);
        rig.hips.localPosition = new Vector3(0, 0.9f, 0);
        rig.chest = new GameObject("Chest").transform;
        rig.chest.SetParent(rig.hips, false);
        rig.chest.localPosition = new Vector3(0, 0.3f, 0);
        // Body: open head and legs; jacket covers 0.9..1.4 with a 2 cm gap.
        Mesh body = Cylinder(name + "Body", 0.15f, 0f, 1.8f, segments, rings, true, 2f, true, (int)(rings * 1.15f / 1.8f));
        Mesh jacket = Cylinder(name + "Jacket", 0.17f, 0.9f, 1.4f, segments * 2, rings, false, 1f, false, 0);
        if (unreadableBody)
        {
            AssetDatabase.CreateAsset(body, fixture + "/" + name + "Body.asset");
            AssetDatabase.CreateAsset(jacket, fixture + "/" + name + "Jacket.asset");
        }
        Material skin = MakeMaterial(name + "Skin"), skin2 = MakeMaterial(name + "Skin2"), cloth = MakeMaterial(name + "Cloth", transparentJacket);
        rig.body = Skin("Body", rig.root.transform, body, rig.hips, rig.chest, 2f, new[] { skin, skin2 });
        rig.jacket = Skin("Jacket", rig.root.transform, jacket, rig.hips, rig.chest, 1f, new[] { cloth, cloth });
        if (unreadableBody)
        {
            BSLKClubGeometry.SetReadable(body, false);
            AssetDatabase.SaveAssets();
        }
        rig.bodyMesh = body;
        // Pose after binding, then move the whole avatar: baking must follow bones, rotation and scale.
        rig.chest.localRotation = Quaternion.Euler(10f, 0f, 0f);
        rig.root.transform.SetPositionAndRotation(new Vector3(5f, 0.2f, 3f), Quaternion.Euler(0f, 30f, 0f));
        rig.root.transform.localScale = Vector3.one * 1.2f;
        rig.body.SetBlendShapeWeight(0, 25f);
        return rig;
    }

    private static Func<Mesh, Mesh> Save(List<Mesh> saved)
    {
        return mesh =>
        {
            AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(fixture + "/" + mesh.name + ".asset"));
            saved.Add(mesh);
            return mesh;
        };
    }

    private static string Snapshot(Mesh mesh)
    {
        Mesh copy = mesh.isReadable ? mesh : BSLKClubGeometry.ReadableCopy(mesh);
        string text = mesh.subMeshCount + "|" + mesh.vertexCount + "|" + string.Join(",", Enumerable.Range(0, copy.subMeshCount).Select(s => string.Join(" ", copy.GetIndices(s))));
        if (copy != mesh) Object.DestroyImmediate(copy);
        return text;
    }

    private static string Key(int a, int b, int c)
    {
        var x = new[] { a, b, c };
        Array.Sort(x);
        return x[0] + "," + x[1] + "," + x[2];
    }

    // ---- tests ---------------------------------------------------------------------------------

    private static void Pose()
    {
        Rig rig = Build("Pose", 24, 30, false);
        Mesh mesh = rig.body.sharedMesh;
        Vector3[] world = BSLKClubOcclusion.WorldVertices(rig.body, mesh);
        Vector3[] local = mesh.vertices;
        BoneWeight[] weights = mesh.boneWeights;
        Matrix4x4[] bind = mesh.bindposes;
        Transform[] bones = rig.body.bones;
        float error = 0;
        for (int v = 0; v < local.Length; v++)
        {
            BoneWeight w = weights[v];
            Vector3 expected = (bones[w.boneIndex0].localToWorldMatrix * bind[w.boneIndex0]).MultiplyPoint3x4(local[v]) * w.weight0 +
                (bones[w.boneIndex1].localToWorldMatrix * bind[w.boneIndex1]).MultiplyPoint3x4(local[v]) * w.weight1;
            // Shrink is at 25%: those vertices are displaced by design; compare the rest.
            if (mesh.GetBlendShapeFrameCount(0) > 0 && rig.body.GetBlendShapeWeight(0) > 0 && InShrink(local[v] * 2f)) continue;
            error = Mathf.Max(error, (world[v] - expected).magnitude);
        }
        Check(error < 1e-4f, "Baked pose matches manual skinning with rotated, scaled avatar and scaled renderer (max error " + error + ")");
        Object.DestroyImmediate(rig.root);
    }

    private static void CoveredTorso()
    {
        Rig source = Build("Covered", 32, 90, true);
        Mesh original = source.bodyMesh;
        Check(!original.isReadable, "Fixture body mesh has Read/Write disabled");
        string before = Snapshot(original), jacketBefore = Snapshot(source.jacket.sharedMesh);
        GameObject copy = Object.Instantiate(source.root);
        SkinnedMeshRenderer body = copy.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
        SkinnedMeshRenderer jacket = copy.transform.Find("Jacket").GetComponent<SkinnedMeshRenderer>();
        var saved = new List<Mesh>();
        var notes = new List<string>();
        var timer = Stopwatch.StartNew();
        int removed = BSLKClubOcclusion.RemoveHiddenTriangles(copy, new HashSet<Renderer>(), r => true, Save(saved), notes);
        Debug.Log("Occlusion small case: " + timer.ElapsedMilliseconds + " ms, notes: " + string.Join(" | ", notes));

        Mesh result = body.sharedMesh;
        int originalTriangles = BSLKClubGeometry.TriangleCount(original);
        Check(removed > 0 && result != original && saved.Contains(result) && EditorUtility.IsPersistent(result), "Covered body triangles removed into a new saved mesh");
        Check(BSLKClubGeometry.TriangleCount(result) == originalTriangles - removed, "Returned count equals triangle difference");
        Check(source.body.sharedMesh == original && Snapshot(original) == before && !original.isReadable, "Source body mesh asset unchanged");
        Check(Snapshot(source.jacket.sharedMesh) == jacketBefore, "Jacket (cover) mesh unchanged");
        Check(notes.Count == 1 && notes[0].Contains(removed.ToString()), "One note with the removed count");
        Match shapes = Regex.Match(notes[0], @"шейпкеями: (\d+)");
        Check(shapes.Success && int.Parse(shapes.Groups[1].Value) > 0, "Note counts covered triangles kept for blend shapes");

        // Classify every original triangle by avatar-space position.
        Mesh readable = BSLKClubGeometry.ReadableCopy(original);
        Vector3[] v = readable.vertices.Select(x => x * 2f).ToArray();
        var kept = new HashSet<string>();
        for (int s = 0; s < result.subMeshCount; s++)
        {
            int[] indices = result.GetIndices(s);
            for (int i = 0; i < indices.Length; i += 3) kept.Add(s + ":" + Key(indices[i], indices[i + 1], indices[i + 2]));
        }
        int outside = 0, outsideKept = 0, shrinkTris = 0, shrinkKept = 0, core = 0, coreRemoved = 0, removedOutsideJacket = 0;
        for (int s = 0; s < readable.subMeshCount; s++)
        {
            int[] indices = readable.GetIndices(s);
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 a = v[indices[i]], b = v[indices[i + 1]], c = v[indices[i + 2]], centre = (a + b + c) / 3f;
                bool isKept = kept.Contains(s + ":" + Key(indices[i], indices[i + 1], indices[i + 2]));
                if (centre.y < 0.85f || centre.y > 1.45f) { outside++; if (isKept) outsideKept++; }
                if (!isKept && (centre.y < 0.9f || centre.y > 1.4f)) removedOutsideJacket++;
                if (InShrink(a) || InShrink(b) || InShrink(c)) { shrinkTris++; if (isKept) shrinkKept++; }
                // Core: well inside the jacket and away from the shrink region's safety ring.
                bool nearShrink = centre.y > ShrinkLow - 0.05f && centre.y < ShrinkHigh + 0.05f && centre.z > -0.05f;
                if (centre.y > 1.0f && centre.y < 1.3f && !nearShrink) { core++; if (!isKept) coreRemoved++; }
            }
        }
        Object.DestroyImmediate(readable);
        Check(outside > 0 && outsideKept == outside, "Uncovered head, legs and caps all kept (" + outside + ")");
        Check(removedOutsideJacket == 0, "Nothing removed beyond the jacket's span");
        Check(shrinkTris > 0 && shrinkKept == shrinkTris, "Triangles with blend shape deltas kept (" + shrinkTris + ")");
        Check(core > 0 && coreRemoved >= core * 0.95f, "Covered core removed (" + coreRemoved + "/" + core + ")");
        Check(removed > 400 && removed < 2400, "Removed count within expected bounds (" + removed + " of " + originalTriangles + ")");

        // Validity: only index buffers changed.
        Mesh check = BSLKClubGeometry.ReadableCopy(original);
        Check(result.subMeshCount == 2 && result.vertexCount == check.vertexCount, "Submesh and vertex counts preserved");
        Check(Enumerable.Range(0, 2).All(s => result.GetIndexCount(s) < check.GetIndexCount(s)), "Both submeshes lost covered triangles");
        Check(Enumerable.Range(0, 2).All(s => result.GetIndices(s).All(i => i >= 0 && i < result.vertexCount)), "Indices in range");
        Check(result.GetBonesPerVertex().SequenceEqual(check.GetBonesPerVertex()) && result.GetAllBoneWeights().SequenceEqual(check.GetAllBoneWeights()), "Bone weights preserved");
        Check(result.bindposes.SequenceEqual(check.bindposes), "Bind poses preserved");
        Check(result.vertices.SequenceEqual(check.vertices) && result.normals.SequenceEqual(check.normals) && result.uv.SequenceEqual(check.uv) && result.tangents.SequenceEqual(check.tangents), "Vertex attributes preserved");
        var d1 = new Vector3[result.vertexCount];
        var d2 = new Vector3[result.vertexCount];
        result.GetBlendShapeFrameVertices(0, 0, d1, null, null);
        check.GetBlendShapeFrameVertices(0, 0, d2, null, null);
        Check(result.blendShapeCount == 2 && result.GetBlendShapeName(0) == "Shrink" && d1.SequenceEqual(d2), "Blend shapes preserved");
        Check(Mathf.Approximately(body.GetBlendShapeWeight(0), 25f), "Blend shape weight kept on the renderer");
        Check(jacket.sharedMesh == source.jacket.sharedMesh, "Cover renderer keeps its mesh");
        Object.DestroyImmediate(check);
        Object.DestroyImmediate(copy);
        Object.DestroyImmediate(source.root);
    }

    private static void ToggleableCover()
    {
        Rig rig = Build("Toggle", 32, 90, false);
        Mesh original = rig.body.sharedMesh;
        string before = Snapshot(original);
        var notes = new List<string>();
        int removed = BSLKClubOcclusion.RemoveHiddenTriangles(rig.root, new HashSet<Renderer>(), r => r.name != "Jacket", Save(new List<Mesh>()), notes);
        Check(removed == 0 && rig.body.sharedMesh == original && Snapshot(original) == before, "Toggleable cover: nothing removed, mesh untouched");
        Match match = Regex.Match(notes.Single(), @"можно было бы убрать ещё (\d+)");
        Check(match.Success && int.Parse(match.Groups[1].Value) > 400, "Note reports triangles kept under toggleable clothing (" + (match.Success ? match.Groups[1].Value : "?") + ")");
        notes.Clear();
        Check(BSLKClubOcclusion.RemoveHiddenTriangles(rig.root, new HashSet<Renderer>(), r => false, Save(new List<Mesh>()), notes) == 0 &&
            rig.body.sharedMesh == original, "occluderAllowed false for all: nothing removed");
        Object.DestroyImmediate(rig.root);
    }

    private static void LockedAndSeeThrough()
    {
        Rig rig = Build("Locked", 24, 60, false);
        Mesh body = rig.body.sharedMesh, jacket = rig.jacket.sharedMesh;
        var notes = new List<string>();
        int removed = BSLKClubOcclusion.RemoveHiddenTriangles(rig.root, new HashSet<Renderer> { rig.body }, r => true, Save(new List<Mesh>()), notes);
        // The jacket is a candidate too, but nothing covers it.
        Check(removed == 0 && rig.body.sharedMesh == body && rig.jacket.sharedMesh == jacket, "Locked body is never cut");
        Object.DestroyImmediate(rig.root);

        rig = Build("Glass", 24, 60, false, true);
        body = rig.body.sharedMesh;
        removed = BSLKClubOcclusion.RemoveHiddenTriangles(rig.root, new HashSet<Renderer>(), r => true, Save(new List<Mesh>()), notes);
        Check(removed == 0 && rig.body.sharedMesh == body, "Transparent clothing does not count as cover");
        Object.DestroyImmediate(rig.root);
    }

    private static void Performance()
    {
        // ~100k body triangles (incl. caps) under ~200k clothing triangles.
        Rig rig = Build("Perf", 256, 196, false);
        int bodyTriangles = BSLKClubGeometry.TriangleCount(rig.body.sharedMesh), clothTriangles = BSLKClubGeometry.TriangleCount(rig.jacket.sharedMesh);
        var notes = new List<string>();
        var timer = Stopwatch.StartNew();
        int removed = BSLKClubOcclusion.RemoveHiddenTriangles(rig.root, new HashSet<Renderer>(), r => true, Save(new List<Mesh>()), notes);
        timer.Stop();
        Debug.Log("Occlusion performance: body " + bodyTriangles + ", clothing " + clothTriangles + ", removed " + removed + ", " + timer.ElapsedMilliseconds + " ms. " + notes.Single());
        Check(bodyTriangles >= 100000 && clothTriangles >= 200000 && removed > 10000, "Large case removes covered triangles (" + removed + ")");
        Check(timer.Elapsed.TotalSeconds < 90, "Large case finishes in under 90 s (" + timer.ElapsedMilliseconds + " ms)");
        Object.DestroyImmediate(rig.root);
    }
}
