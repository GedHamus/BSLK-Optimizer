using System;
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
            fixture = "Assets/ClubTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(fixture));
            ParticleBudget();
            PixelError();
            Slots();
            SharedBones();
            CopyAndOutfit();
            Cancellation();
            AdaptiveTextures();
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
        MeshObject(root, "Hidden", material).SetActive(false);
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
        controller.layers[1].stateMachine.AddAnyStateTransition(state).AddCondition(AnimatorConditionMode.If, 0, "Clothes");
        root.AddComponent<Animator>().runtimeAnimatorController = controller;
        var ps = root.AddComponent<ParticleSystem>(); var main = ps.main; main.maxParticles = 9000;
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
        Check(result.avatar.GetComponent<ParticleSystem>().main.maxParticles == 2000, "Club particle limit applied");
        Check(File.ReadAllText(fixture + "/Outfit.controller") == originalController && File.ReadAllText(fixture + "/outfit.anim") == originalClip, "Original controller/clip bytes unchanged");
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(result.folder + "/Club.prefab") != null, "Club prefab persisted");
        bool refused = false;
        try { BSLKClubOptimizer.Create(result.avatar, new BSLKClubOptimizer.Options()); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "Cannot repeatedly degrade generated avatar");
        Object.DestroyImmediate(root); Object.DestroyImmediate(result.avatar);
    }

    private static Texture2D MakeTexture(string name, bool checker)
    {
        var texture = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[1024 * 1024];
        for (int y = 0; y < 1024; y++) for (int x = 0; x < 1024; x++)
            pixels[y * 1024 + x] = checker ? ((x + y) % 2 == 0 ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255)) : new Color32(128, 64, 32, 255);
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
        MeshObject(root, "Flat", MakeMaterial("FlatMat", flat));
        MeshObject(root, "Detail", MakeMaterial("DetailMat", detail));
        MeshObject(root, "Protected", MakeMaterial("ProtectedMat", protect));
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
        Check(flat.width == 1024 && File.ReadAllText(AssetDatabase.GetAssetPath(flat) + ".meta") == originalMeta, "Source texture importer unchanged");
        Object.DestroyImmediate(root); Object.DestroyImmediate(result.avatar);
    }
}
