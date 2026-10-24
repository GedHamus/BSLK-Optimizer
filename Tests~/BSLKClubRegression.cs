using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using BSLK.Optimizer;
using Object = UnityEngine.Object;

// Copy this file into Assets/Editor of an isolated Unity 2022.3 test project.
// Run: Unity -batchmode -projectPath <project> -executeMethod BSLKClubRegression.Run -logFile <log>
public static class BSLKClubRegression
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
            // VRChat projects use Linear color space; texture checks must hold there.
            if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;
            fixture = "Assets/ClubTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(fixture));
            ParticleBudget();
            PixelError();
            Slots();
            ShaderTextures();
            SharedBones();
            CopyAndOutfit();
            Cancellation();
            AdaptiveTextures();
            MergeAndDeduplicate();
            Simplification();
            ImportedModel();
            SimplifyAndRestore();
            Debug.Log("BSLK REGRESSION PASSED: " + checks + " assertions");
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

    private static void ParticleBudget()
    {
        var random = new System.Random(123);
        for (int trial = 0; trial < 100; trial++)
        {
            int[] original = Enumerable.Range(0, random.Next(0, 50)).Select(_ => random.Next(0, 100000)).ToArray();
            int[] result = BSLKClubOptimizer.AllocateParticleBudget(original, 2000);
            if (result.Sum() != Math.Min(2000L, original.Sum(x => (long)x)) || result.Where((x, i) => x < 0 || x > original[i]).Any())
                throw new Exception("Particle allocation invariant failed");
        }
        Check(true, "Particle budget: 100 randomized cases, exact sum and per-system bounds");
        Check(BSLKClubOptimizer.AllocateParticleBudget(new[] { int.MaxValue, int.MaxValue }, 2000).Sum() == 2000, "Particle budget cannot overflow Int32");
    }

    private static void PixelError()
    {
        Color32[] reference = { new Color32(0, 0, 0, 255), new Color32(255, 255, 255, 255) };
        Check(BSLKClubTextures.Error(reference, reference, out float damage) == 0 && damage == 0, "Identical image has zero error");
        Color32[] candidate = { reference[0], new Color32(255, 255, 255, 0) };
        Check(BSLKClubTextures.Error(reference, candidate, out damage) > 0 && damage == 0.5f, "Alpha changes count as damage");
    }

    private static Mesh MakeMesh(string name)
    {
        var mesh = new Mesh { name = name };
        mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
        mesh.triangles = new[] { 0, 1, 2 };
        return mesh;
    }

    private static Material MakeMaterial(string name, Texture texture = null)
    {
        var material = new Material(Shader.Find("Standard")) { name = name, mainTexture = texture };
        AssetDatabase.CreateAsset(material, fixture + "/" + name + ".mat");
        return material;
    }

    private static void Slots()
    {
        GameObject go = new GameObject("Slots");
        go.AddComponent<MeshFilter>().sharedMesh = MakeMesh("slots");
        var renderer = go.AddComponent<MeshRenderer>();
        Material material = MakeMaterial("SlotsMaterial");
        renderer.sharedMaterials = new[] { material, null, material, null };
        Check(BSLKOptimizer.RemoveEmptyMaterialSlots(go) == 1, "Only trailing material slots removed");
        Check(renderer.sharedMaterials.Length == 3 && renderer.sharedMaterials[1] == null, "Interior slot index preserved");
        Object.DestroyImmediate(go);
    }

    private static void ShaderTextures()
    {
        GameObject go = new GameObject("ShaderTextures");
        go.AddComponent<MeshFilter>().sharedMesh = MakeMesh("shaderTextures");
        Texture2D used = MakeTexture("UsedSlot", false), stale = MakeTexture("StaleSlot", false);
        Material material = MakeMaterial("StaleMaterial", used);
        material.SetTexture("_StaleTex", stale);
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        var textures = BSLKClubOptimizer.Textures(go);
        Check(textures.Contains(used) && !textures.Contains(stale), "Texture budget counts shader properties only, not stale material slots");
        Object.DestroyImmediate(go);
    }

    private static void SharedBones()
    {
        GameObject root = new GameObject("SharedBones");
        Mesh mesh = MakeMesh("SharedSkin");
        mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity, Matrix4x4.identity };
        mesh.boneWeights = new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1 }, new BoneWeight { boneIndex0 = 2, weight0 = 1 }, new BoneWeight { boneIndex0 = 0, weight0 = 1 } };
        AssetDatabase.CreateAsset(mesh, fixture + "/skin.asset");
        var renderers = new SkinnedMeshRenderer[2];
        var expected = new Transform[2][];
        for (int i = 0; i < 2; i++)
        {
            var go = new GameObject("Skin" + i); go.transform.SetParent(root.transform);
            renderers[i] = go.AddComponent<SkinnedMeshRenderer>();
            expected[i] = Enumerable.Range(0, 3).Select(j => { var bone = new GameObject("Bone" + i + "_" + j); bone.transform.SetParent(root.transform); return bone.transform; }).ToArray();
            renderers[i].sharedMesh = mesh;
            renderers[i].bones = expected[i];
            renderers[i].rootBone = expected[i][0];
        }
        BSLKOptimizer.RemoveUnusedBones(root);
        Check(renderers.All(r => r.bones.Length == 2), "Unused bindings removed from both renderers");
        Check(renderers[0].bones[1] == expected[0][2] && renderers[1].bones[1] == expected[1][2], "Shared mesh does not cross-wire different skeletons");
        Check(mesh.bindposes.Length == 3 && renderers[0].sharedMesh != renderers[1].sharedMesh, "Source skin unchanged; renderer meshes isolated");
        Object.DestroyImmediate(root);
    }

    private static GameObject MeshObject(GameObject root, string name, Material material)
    {
        var go = new GameObject(name); go.transform.SetParent(root.transform);
        go.AddComponent<MeshFilter>().sharedMesh = MakeMesh(name);
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    private static void CopyAndOutfit()
    {
        var root = new GameObject("Wardrobe");
        var material = MakeMaterial("WardrobeMaterial");
        MeshObject(root, "Shirt", material);
        MeshObject(root, "Hidden", MakeMaterial("HiddenMaterial")).SetActive(false);
        AnimationClip clip = new AnimationClip { name = "Outfit" };
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Shirt", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 1, 0));
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Hidden", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 1, 1));
        AssetDatabase.CreateAsset(clip, fixture + "/outfit.anim");
        var gesture = new AnimationClip { name = "Gesture" };
        AnimationUtility.SetEditorCurve(gesture, EditorCurveBinding.FloatCurve("Shirt", typeof(SkinnedMeshRenderer), "blendShape.Smile"), AnimationCurve.Constant(0, 1, 50));
        AssetDatabase.CreateAsset(gesture, fixture + "/gesture.anim");
        var controller = AnimatorController.CreateAnimatorControllerAtPath(fixture + "/Outfit.controller");
        controller.layers[0].stateMachine.AddState("Gesture").motion = gesture;
        controller.AddLayer("Clothes");
        controller.AddParameter("Clothes", AnimatorControllerParameterType.Bool);
        AnimatorState state = controller.layers[1].stateMachine.AddState("Toggle");
        state.motion = clip;
        state.writeDefaultValues = false;
        controller.layers[1].stateMachine.AddAnyStateTransition(state).AddCondition(AnimatorConditionMode.If, 0, "Clothes");
        root.AddComponent<Animator>().runtimeAnimatorController = controller;
        var ps = root.AddComponent<ParticleSystem>(); var main = ps.main; main.maxParticles = 9000;
        // Not animatable in Unity 2022.3, but such dead bindings must not survive in the club copy.
        EditorCurveBinding particleBinding = EditorCurveBinding.FloatCurve("", typeof(ParticleSystem), "InitialModule.maxNumParticles");
        var particleClip = new AnimationClip { name = "Particles" };
        AnimationUtility.SetEditorCurve(particleClip, particleBinding, AnimationCurve.Constant(0, 1, 9000));
        AssetDatabase.CreateAsset(particleClip, fixture + "/particles.anim");
        controller.layers[0].stateMachine.AddState("Particles").motion = particleClip;
        root.AddComponent<Light>();
        AssetDatabase.SaveAssets();
        string originalController = File.ReadAllText(fixture + "/Outfit.controller");
        string originalClip = File.ReadAllText(fixture + "/outfit.anim");
        string originalMaterial = File.ReadAllText(fixture + "/WardrobeMaterial.mat");
        material.color = Color.green; EditorUtility.SetDirty(material);
        BSLKClubOptimizer.Result result = BSLKClubOptimizer.Create(root, new BSLKClubOptimizer.Options { optimizeTextures = false });
        Check(root.transform.Find("Hidden") != null && root.GetComponent<Light>() != null, "Source hierarchy not modified");
        Check(result.avatar.transform.Find("Hidden") == null && result.avatar.GetComponent<Light>() == null, "Club removes disabled wardrobe and lights");
        Check(result.avatar.transform.Find("Shirt").GetComponent<MeshRenderer>().sharedMaterial != material, "Material asset copied");
        Check(result.avatar.transform.Find("Shirt").GetComponent<MeshRenderer>().sharedMaterial.color == Color.green &&
            File.ReadAllText(fixture + "/WardrobeMaterial.mat") == originalMaterial, "Visible unsaved material edits copied without saving source");
        Check(AssetDatabase.Contains(result.avatar.transform.Find("Shirt").GetComponent<MeshFilter>().sharedMesh), "Transient mesh persisted to an asset");
        var copiedController = (AnimatorController)result.avatar.GetComponent<Animator>().runtimeAnimatorController;
        Check(copiedController != controller, "Animator controller copied");
        Check(copiedController.layers.Length == 2 && copiedController.layers[1].stateMachine.anyStateTransitions.Length == 0, "Clothing layer neutralized without renumbering");
        Check(AnimationUtility.GetCurveBindings(copiedController.layers[0].stateMachine.states[0].state.motion as AnimationClip).Length == 1, "Gesture curve retained");
        Check(!copiedController.layers[1].stateMachine.defaultState.writeDefaultValues, "Neutralized layer keeps Write Defaults OFF");
        var copiedParticles = (AnimationClip)copiedController.layers[0].stateMachine.states.First(s => s.state.name == "Particles").state.motion;
        Check(copiedParticles != particleClip && AnimationUtility.GetCurveBindings(copiedParticles).Length == 0 &&
            AnimationUtility.GetCurveBindings(particleClip).Length == 1, "maxParticles animation removed from copy only");
        Check(result.avatar.GetComponent<ParticleSystem>().main.maxParticles == 2000, "Club particle limit applied");
        Check(File.ReadAllText(fixture + "/Outfit.controller") == originalController && File.ReadAllText(fixture + "/outfit.anim") == originalClip, "Original controller/clip bytes unchanged");
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(result.folder + "/Club.prefab") != null, "Club prefab persisted");
        Check(result.avatar.name == "Wardrobe [BELKA]", "Generated avatar carries the BELKA suffix");
        Check(Directory.GetFiles(result.folder, "HiddenMaterial*").Length == 0 && Directory.GetFiles(result.folder, "WardrobeMaterial*.mat").Length == 1,
            "Materials of deleted clothing are not left in the result folder");
        bool refused = false;
        try { BSLKClubOptimizer.Create(result.avatar, new BSLKClubOptimizer.Options()); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "Cannot repeatedly degrade generated avatar");
        Object.DestroyImmediate(root); Object.DestroyImmediate(result.avatar);
    }

    private static Mesh MakeSkin(string name, int quads, Transform bone, params string[] shapes)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (int q = 0; q < quads; q++)
        {
            int b = vertices.Count;
            vertices.AddRange(new[] { new Vector3(q, 0, 0), new Vector3(q + 1, 0, 0), new Vector3(q, 1, 0), new Vector3(q + 1, 1, 0) });
            triangles.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.boneWeights = vertices.Select(_ => new BoneWeight { boneIndex0 = 0, weight0 = 1 }).ToArray();
        mesh.bindposes = new[] { bone.worldToLocalMatrix };
        foreach (string shape in shapes) mesh.AddBlendShapeFrame(shape, 100, vertices.Select(_ => Vector3.up).ToArray(), null, null);
        AssetDatabase.CreateAsset(mesh, fixture + "/" + name + ".asset");
        return mesh;
    }

    private static SkinnedMeshRenderer Skin(Transform parent, string name, Mesh mesh, Transform bone, Material material)
    {
        var go = new GameObject(name); go.transform.SetParent(parent);
        var renderer = go.AddComponent<SkinnedMeshRenderer>();
        renderer.sharedMesh = mesh; renderer.bones = new[] { bone }; renderer.rootBone = bone; renderer.sharedMaterial = material;
        return renderer;
    }

    private static void MergeAndDeduplicate()
    {
        var root = new GameObject("Merge");
        var hips = new GameObject("Hips").transform; hips.SetParent(root.transform);
        var jacket = new GameObject("Jacket").transform; jacket.SetParent(root.transform);
        Material cloth = MakeMaterial("Cloth"), clothTwin = MakeMaterial("ClothTwin");
        SkinnedMeshRenderer body = Skin(jacket, "Body", MakeSkin("JacketBody", 4, hips, "Used", "Unused", "Weighted"), hips, cloth);
        body.SetBlendShapeWeight(2, 30);
        Skin(jacket, "Zipper", MakeSkin("JacketZipper", 1, hips, "Open"), hips, clothTwin);
        Texture2D a = MakeTexture("TwinA", false), b = MakeTexture("TwinB", false);
        GameObject propA = MeshObject(root, "PropA", MakeMaterial("PropMatA", a)), propB = MeshObject(root, "PropB", MakeMaterial("PropMatB", b));
        var multiMesh = new Mesh { name = "Multi", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one }, subMeshCount = 2 };
        multiMesh.SetTriangles(new[] { 0, 1, 2 }, 0); multiMesh.SetTriangles(new[] { 1, 3, 2 }, 1);
        GameObject multi = MeshObject(root, "Multi", cloth);
        multi.GetComponent<MeshFilter>().sharedMesh = multiMesh;
        multi.GetComponent<MeshRenderer>().sharedMaterials = new[] { cloth, clothTwin };
        var clip = new AnimationClip { name = "Shapes" };
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Jacket/Body", typeof(SkinnedMeshRenderer), "blendShape.Used"), AnimationCurve.Constant(0, 1, 100));
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Jacket/Zipper", typeof(SkinnedMeshRenderer), "blendShape.Open"), AnimationCurve.Constant(0, 1, 100));
        AssetDatabase.CreateAsset(clip, fixture + "/shapes.anim");
        var controller = AnimatorController.CreateAnimatorControllerAtPath(fixture + "/Shapes.controller");
        controller.layers[0].stateMachine.AddState("Shapes").motion = clip;
        root.AddComponent<Animator>().runtimeAnimatorController = controller;
        AssetDatabase.SaveAssets();

        var result = BSLKClubOptimizer.Create(root, new BSLKClubOptimizer.Options { optimizeTextures = false });
        Transform copy = result.avatar.transform;
        var skins = copy.Find("Jacket").GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Check(skins.Length == 1 && skins[0].sharedMaterials.Length == 1 && BSLKClubGeometry.TriangleCount(skins[0].sharedMesh) == 10,
            "Jacket meshes merged into one renderer and one slot, triangles preserved");
        Mesh merged = skins[0].sharedMesh;
        Check(merged.GetBlendShapeIndex("Used") >= 0 && merged.GetBlendShapeIndex("Open") >= 0 && merged.GetBlendShapeIndex("Unused") < 0 &&
            skins[0].GetBlendShapeWeight(merged.GetBlendShapeIndex("Weighted")) == 30, "Animated and weighted shapes kept, unused shape removed");
        var copiedClip = (AnimationClip)((AnimatorController)result.avatar.GetComponent<Animator>().runtimeAnimatorController).layers[0].stateMachine.states[0].state.motion;
        Check(AnimationUtility.GetCurveBindings(copiedClip).Count(x => x.path == "Jacket/Body") == 2 && AnimationUtility.GetCurveBindings(copiedClip).All(x => x.path != "Jacket/Zipper"),
            "Blend shape animation follows the merged renderer");
        Check(copy.Find("PropA").GetComponent<MeshRenderer>().sharedMaterial == copy.Find("PropB").GetComponent<MeshRenderer>().sharedMaterial &&
            Directory.GetFiles(result.folder, "Twin*.png").Length == 1, "Identical textures and materials collapse to one copy");
        Check(copy.Find("Multi").GetComponent<MeshRenderer>().sharedMaterials.Length == 1, "Slots with identical materials merged");
        Check(root.transform.Find("Jacket").GetComponentsInChildren<SkinnedMeshRenderer>().Length == 2 && body.sharedMesh.blendShapeCount == 3 &&
            root.transform.Find("Multi").GetComponent<MeshRenderer>().sharedMaterials.Length == 2, "Source meshes and slots unchanged");
        var triangles = BSLKClubInsights.Collect(result.avatar).First(x => x.title == "Треугольники");
        Check(triangles.items[0].label == "Jacket/Body" && triangles.total == 14, "Limit report names the largest contributor");
        Object.DestroyImmediate(root); Object.DestroyImmediate(result.avatar);
    }

    private static void Simplification()
    {
        const int size = 30;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (int y = 0; y <= size; y++) for (int x = 0; x <= size; x++) vertices.Add(new Vector3(x, Mathf.Sin(x * 0.2f) * 0.1f, y));
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            int i = y * (size + 1) + x;
            triangles.AddRange(new[] { i, i + size + 1, i + 1, i + 1, i + size + 1, i + size + 2 });
        }
        var mesh = new Mesh { name = "Grid" };
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals();
        mesh.AddBlendShapeFrame("Wave", 100, vertices.Select(v => Vector3.up).ToArray(), null, null);
        Mesh simple = BSLKClubGeometry.Simplify(mesh, 0.3f, false);
        int before = BSLKClubGeometry.TriangleCount(mesh), after = BSLKClubGeometry.TriangleCount(simple);
        Check(after <= before * 0.5f && after > 0 && simple.vertexCount < mesh.vertexCount && simple.blendShapeCount == 1 &&
            Mathf.Abs(simple.bounds.size.x - mesh.bounds.size.x) < 0.01f && Mathf.Abs(simple.bounds.size.z - mesh.bounds.size.z) < 0.01f, "Simplification: " + before + " → " + after + " triangles, border and shapes kept");
        // A model imported without Read/Write, like a typical source avatar FBX.
        var obj = new System.Text.StringBuilder();
        foreach (Vector3 v in vertices) obj.AppendLine("v " + v.x.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + v.y.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + v.z.ToString(System.Globalization.CultureInfo.InvariantCulture));
        for (int t = 0; t < triangles.Count; t += 3) obj.AppendLine("f " + (triangles[t] + 1) + " " + (triangles[t + 1] + 1) + " " + (triangles[t + 2] + 1));
        string modelPath = fixture + "/Grid.obj";
        File.WriteAllText(modelPath, obj.ToString());
        AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport);
        var modelImporter = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        modelImporter.isReadable = false; modelImporter.meshOptimizationFlags = 0; modelImporter.SaveAndReimport();
        Mesh imported = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Mesh>().First();
        int importedBefore = BSLKClubGeometry.TriangleCount(imported);
        Mesh importedSimple = BSLKClubGeometry.Simplify(imported, 0.3f, false);
        Check(!imported.isReadable && BSLKClubGeometry.TriangleCount(importedSimple) <= importedBefore * 0.5f && BSLKClubGeometry.TriangleCount(imported) == importedBefore,
            "Model without Read/Write simplified into a new mesh, source untouched");
        Mesh lockedShapes = BSLKClubGeometry.Simplify(mesh, 0.3f, true);
        // Shape keys on every vertex (typical body) must not freeze the mesh; shapes that move
        // neighbours differently must still limit it.
        Check(BSLKClubGeometry.TriangleCount(lockedShapes) < before * 0.5f, "Uniform shape keys on all vertices do not block simplification");
        Mesh uneven = Object.Instantiate(mesh);
        uneven.ClearBlendShapes();
        uneven.AddBlendShapeFrame("Ripple", 100, vertices.Select((v, i) => Vector3.up * (((int)v.x % 2) * 2 + (int)v.z % 2)).ToArray(), null, null);
        Check(BSLKClubGeometry.TriangleCount(BSLKClubGeometry.Simplify(uneven, 0.3f, true)) > after, "Shapes that move neighbours differently keep that geometry");
    }

    // Source models usually have Read/Write off: club operations must still work on the copy,
    // and the copy must end up with Read/Write off again.
    private static void ImportedModel()
    {
        string path = fixture + "/TwoParts.obj";
        File.WriteAllText(path, "v 0 0 0\nv 1 0 0\nv 0 1 0\nv 1 1 0\nusemtl A\nf 1 2 3\nusemtl B\nf 2 4 3\n");
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.isReadable = false; importer.SaveAndReimport();
        string meta = File.ReadAllText(path + ".meta");
        Mesh model = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().First();
        var root = new GameObject("Imported");
        GameObject part = MeshObject(root, "Part", MakeMaterial("PartA"));
        part.GetComponent<MeshFilter>().sharedMesh = model;
        Mesh editable = BSLKClubGeometry.ReadableCopy(model);
        Check(!model.isReadable && editable.isReadable && editable.vertexCount == model.vertexCount && BSLKClubGeometry.Editable(editable),
            "Model without Read/Write yields an editable copy for club operations");
        Object.DestroyImmediate(editable);
        var result = BSLKClubOptimizer.Create(root, new BSLKClubOptimizer.Options { optimizeTextures = false });
        Mesh copy = result.avatar.transform.Find("Part").GetComponent<MeshFilter>().sharedMesh;
        Check(copy != model && BSLKClubGeometry.TriangleCount(copy) == 2 && !copy.isReadable && AssetDatabase.GetAssetPath(copy).StartsWith(result.folder),
            "Club copy of the model is intact and keeps Read/Write off like the source");
        Check(File.ReadAllText(path + ".meta") == meta && !model.isReadable, "Source model and importer unchanged");
        Object.DestroyImmediate(root); Object.DestroyImmediate(result.avatar);
    }

    // The window simplifies several scene meshes and restores them all, through the remembered originals.
    private static void SimplifyAndRestore()
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
        var window = ScriptableObject.CreateInstance<BSLKOptimizerWindow>();
        var root = new GameObject("SimplifyScene");
        var meshes = new List<Mesh>();
        var renderers = new List<MeshRenderer>();
        for (int i = 0; i < 2; i++)
        {
            GameObject go = MeshObject(root, "Part" + i, MakeMaterial("SimplifyMat" + i));
            Mesh grid = MakeGrid(20);
            AssetDatabase.CreateAsset(grid, fixture + "/grid" + i + ".asset");
            go.GetComponent<MeshFilter>().sharedMesh = grid;
            meshes.Add(grid);
            renderers.Add(go.GetComponent<MeshRenderer>());
            typeof(BSLKOptimizerWindow).GetField("simplifyTarget", flags).SetValue(window, go.GetComponent<MeshRenderer>());
            typeof(BSLKOptimizerWindow).GetMethod("Simplify", flags).Invoke(window, null);
        }
        var found = (List<Renderer>)typeof(BSLKOptimizerWindow).GetMethod("FindSimplified", flags).Invoke(null, null);
        Check(found.Count == 2 && renderers.All(r => r.GetComponent<MeshFilter>().sharedMesh != meshes[renderers.IndexOf(r)]) &&
            AssetDatabase.GetAssetPath(renderers[0].GetComponent<MeshFilter>().sharedMesh).StartsWith(BSLKClubOptimizer.OutputRoot + "/Simplified/"),
            "Two scene meshes simplified into new assets");
        typeof(BSLKOptimizerWindow).GetMethod("RestoreAll", flags).Invoke(window, null);
        Check(renderers.All(r => r.GetComponent<MeshFilter>().sharedMesh == meshes[renderers.IndexOf(r)]) && meshes.All(m => BSLKClubGeometry.TriangleCount(m) == 800),
            "All simplified meshes restored to their unchanged originals");
        // Automatic plan toward a triangle budget, then restore everything.
        typeof(BSLKOptimizerWindow).GetField("avatar", flags).SetValue(window, root);
        typeof(BSLKOptimizerWindow).GetField("autoBudget", flags).SetValue(window, 1000);
        object[] args = { root, 1000, null };
        var plan = (System.Collections.IList)typeof(BSLKOptimizerWindow).GetMethod("Plan", flags).Invoke(window, args);
        typeof(BSLKOptimizerWindow).GetField("autoPlan", flags).SetValue(window, plan);
        typeof(BSLKOptimizerWindow).GetMethod("ApplyPlan", flags).Invoke(window, null);
        int total = renderers.Sum(r => BSLKClubGeometry.TriangleCount(r.GetComponent<MeshFilter>().sharedMesh));
        Check(plan.Count == 2 && total < 1600 * 0.8f, "Automatic plan reduced 1600 triangles toward 1000: " + total + " (" + args[2] + ")");
        typeof(BSLKOptimizerWindow).GetMethod("RestoreAll", flags).Invoke(window, null);
        Check(renderers.All(r => r.GetComponent<MeshFilter>().sharedMesh == meshes[renderers.IndexOf(r)]), "Automatic simplification restored");

        // Vertices skinned to protected bones (head, hands) are never collapsed.
        Mesh skinned = MakeGrid(10);
        skinned.boneWeights = skinned.vertices.Select(_ => new BoneWeight { boneIndex0 = 0, weight0 = 1 }).ToArray();
        skinned.bindposes = new[] { Matrix4x4.identity };
        Check(BSLKClubGeometry.TriangleCount(BSLKClubGeometry.Simplify(skinned, 0.3f, false, new[] { 0 })) == 200 &&
            BSLKClubGeometry.TriangleCount(BSLKClubGeometry.Simplify(skinned, 0.3f, false)) < 200, "Protected bones keep their geometry");
        Object.DestroyImmediate(root); Object.DestroyImmediate(window);
    }

    private static Mesh MakeGrid(int size)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (int y = 0; y <= size; y++) for (int x = 0; x <= size; x++) vertices.Add(new Vector3(x, Mathf.Sin(x * 0.3f) * 0.2f, y));
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            int i = y * (size + 1) + x;
            triangles.AddRange(new[] { i, i + size + 1, i + 1, i + 1, i + size + 1, i + size + 2 });
        }
        var mesh = new Mesh { name = "Grid" };
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals();
        return mesh;
    }

    private static Texture2D MakeTexture(string name, bool checker, bool darkNoise = false, byte alpha = 255)
    {
        var texture = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[1024 * 1024];
        var random = new System.Random(7);
        for (int y = 0; y < 1024; y++) for (int x = 0; x < 1024; x++)
        {
            byte v = (byte)random.Next(0, 41);
            pixels[y * 1024 + x] = darkNoise ? new Color32(v, v, v, 255) :
                checker ? ((x + y) % 2 == 0 ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255)) : new Color32(128, 64, 32, alpha);
        }
        texture.SetPixels32(pixels); texture.Apply();
        string path = fixture + "/" + name + ".png";
        File.WriteAllBytes(path, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static void Cancellation()
    {
        var source = new GameObject("..");
        int folders = AssetDatabase.GetSubFolders(BSLKClubOptimizer.OutputRoot).Length;
        var token = new System.Threading.CancellationTokenSource(); token.Cancel();
        bool cancelled = false;
        try { BSLKClubOptimizer.Create(source, new BSLKClubOptimizer.Options { cancellationToken = token.Token }); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && source != null && AssetDatabase.GetSubFolders(BSLKClubOptimizer.OutputRoot).Length == folders, "Cancellation removes only incomplete generated folder, keeps source");
        Object.DestroyImmediate(source); token.Dispose();
    }

    private static void AdaptiveTextures()
    {
        GameObject root = new GameObject("Textures");
        Texture2D flat = MakeTexture("Flat", false), detail = MakeTexture("Detail", true), protect = MakeTexture("Protected", false);
        Texture2D dark = MakeTexture("DarkDetail", false, true);
        Texture2D translucent = MakeTexture("Translucent", false, false, 128);
        Color32 sample = BSLKClubTextures.Readback(flat, 1024, 1024)[0];
        Check(Math.Abs(sample.r - 128) <= 2 && Math.Abs(sample.g - 64) <= 2 && Math.Abs(sample.b - 32) <= 2,
            "Readback compares sRGB values in a Linear project: " + sample);
        MeshObject(root, "Flat", MakeMaterial("FlatMat", flat));
        MeshObject(root, "Detail", MakeMaterial("DetailMat", detail));
        MeshObject(root, "Protected", MakeMaterial("ProtectedMat", protect));
        MeshObject(root, "Dark", MakeMaterial("DarkMat", dark));
        MeshObject(root, "Translucent", MakeMaterial("TranslucentMat", translucent));
        var options = new BSLKClubOptimizer.Options { minimumTextureSize = 256 };
        options.protectedTextures.Add(protect);
        string originalMeta = File.ReadAllText(AssetDatabase.GetAssetPath(flat) + ".meta");
        var result = BSLKClubOptimizer.Create(root, options);
        Texture flatCopy = result.avatar.transform.Find("Flat").GetComponent<MeshRenderer>().sharedMaterial.mainTexture;
        Texture detailCopy = result.avatar.transform.Find("Detail").GetComponent<MeshRenderer>().sharedMaterial.mainTexture;
        Texture protectCopy = result.avatar.transform.Find("Protected").GetComponent<MeshRenderer>().sharedMaterial.mainTexture;
        Check(flatCopy != flat && flatCopy.width == 256, "Flat texture reduced below budget to chosen minimum");
        Check(detailCopy.width == 1024, "High-frequency detail rejects destructive resize");
        Check(protectCopy.width == 1024, "Protected texture retains resolution");
        Texture darkCopy = result.avatar.transform.Find("Dark").GetComponent<MeshRenderer>().sharedMaterial.mainTexture;
        Check(darkCopy.width == 1024, "Dark detail is judged perceptually and is not downscaled");
        Texture2D translucentCopy = (Texture2D)result.avatar.transform.Find("Translucent").GetComponent<MeshRenderer>().sharedMaterial.mainTexture;
        Check(((Texture2D)flatCopy).format == TextureFormat.DXT1 && translucentCopy.format != TextureFormat.DXT1,
            "Opaque texture drops its unused alpha (DXT1); a texture with alpha keeps it (" + ((Texture2D)flatCopy).format + ", " + translucentCopy.format + ")");
        Check(flat.width == 1024 && File.ReadAllText(AssetDatabase.GetAssetPath(flat) + ".meta") == originalMeta, "Source texture importer unchanged");
        Object.DestroyImmediate(root); Object.DestroyImmediate(result.avatar);
    }
}
