using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BSLK.Optimizer
{
    public static class BSLKOptimizer
    {
        public const int MaxTextureSize = 2048;

        public const string Version = "1.1.1";

        private static readonly string[] PlatformNames =
        {
            "Standalone", "Android", "iPhone", "WebGL", "Windows Store Apps",
            "tvOS", "PS4", "XboxOne", "Switch", "LinuxHeadlessSimulation",
            "EmbeddedLinux", "QNX", "VisionOS"
        };

        public static int RemoveDisabledObjects(GameObject avatar, out int preserved)
        {
            preserved = 0;

            Undo.SetCurrentGroupName("BSLK-Optimizer: удаление выключенных объектов");

            int removed = 0;

            if (avatar != null)
            {
                Transform t = avatar.transform;
                for (int i = t.childCount - 1; i >= 0; i--)
                    RemoveDisabledRecursive(t.GetChild(i).gameObject, ref removed, ref preserved);
                return removed;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("[BSLK-Optimizer] Сцена не открыта. Откройте сцену и попробуйте снова.");
                return 0;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                RemoveDisabledRecursive(roots[i], ref removed, ref preserved);

            return removed;
        }

        private static void RemoveDisabledRecursive(GameObject go, ref int removed, ref int preserved)
        {
            if (go == null)
                return;

            if (!go.activeSelf)
            {
                if (ContainsDynamicBone(go))
                {
                    preserved++;
                    Debug.LogWarning("[BSLK-Optimizer] Объект «" + go.name + "» содержит PhysBone/DynamicBone и сохранён, несмотря на выключенный Active.");
                    return;
                }

                Undo.DestroyObjectImmediate(go);
                removed++;
                return;
            }

            Transform t = go.transform;
            for (int i = t.childCount - 1; i >= 0; i--)
                RemoveDisabledRecursive(t.GetChild(i).gameObject, ref removed, ref preserved);
        }

        private static bool ContainsDynamicBone(GameObject go)
        {
            if (HasDynamicBoneComponent(go))
                return true;

            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                if (ContainsDynamicBone(t.GetChild(i).gameObject))
                    return true;
            }
            return false;
        }

        private static bool HasDynamicBoneComponent(GameObject go)
        {
            if (go == null)
                return false;

            Component[] components = go.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                    continue;

                string typeName = components[i].GetType().Name;
                if (typeName.Contains("PhysBone") || typeName.Contains("DynamicBone"))
                    return true;
            }
            return false;
        }

        public static int RemoveLights(GameObject avatar)
        {
            List<Light> lights = new List<Light>();

            if (avatar != null)
            {
                avatar.GetComponentsInChildren(true, lights);
            }
            else
            {
                Scene scene = SceneManager.GetActiveScene();
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    Debug.LogError("[BSLK-Optimizer] Сцена не открыта. Откройте сцену и попробуйте снова.");
                    return 0;
                }

                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                    roots[i].GetComponentsInChildren(true, lights);
            }

            Undo.SetCurrentGroupName("BSLK-Optimizer: удаление источников света");
            for (int i = 0; i < lights.Count; i++)
                Undo.DestroyObjectImmediate(lights[i]);

            return lights.Count;
        }

        public static CameraAudioReport RemoveCamerasAndAudioListeners(GameObject avatar)
        {
            CameraAudioReport report = new CameraAudioReport();
            List<Camera> cameras = new List<Camera>();
            List<AudioListener> listeners = new List<AudioListener>();

            if (avatar != null)
            {
                avatar.GetComponentsInChildren(true, cameras);
                avatar.GetComponentsInChildren(true, listeners);
            }
            else
            {
                Scene scene = SceneManager.GetActiveScene();
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    Debug.LogError("[BSLK-Optimizer] Сцена не открыта. Откройте сцену и попробуйте снова.");
                    return report;
                }

                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    roots[i].GetComponentsInChildren(true, cameras);
                    roots[i].GetComponentsInChildren(true, listeners);
                }
            }

            Undo.SetCurrentGroupName("BSLK-Optimizer: удаление Camera и AudioListener");
            for (int i = 0; i < cameras.Count; i++)
                Undo.DestroyObjectImmediate(cameras[i]);
            for (int i = 0; i < listeners.Count; i++)
                Undo.DestroyObjectImmediate(listeners[i]);

            report.cameras = cameras.Count;
            report.audioListeners = listeners.Count;
            return report;
        }

        public static int RemoveReflectionProbes(GameObject avatar)
        {
            List<ReflectionProbe> probes = new List<ReflectionProbe>();

            if (avatar != null)
            {
                avatar.GetComponentsInChildren(true, probes);
            }
            else
            {
                Scene scene = SceneManager.GetActiveScene();
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    Debug.LogError("[BSLK-Optimizer] Сцена не открыта. Откройте сцену и попробуйте снова.");
                    return 0;
                }

                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                    roots[i].GetComponentsInChildren(true, probes);
            }

            Undo.SetCurrentGroupName("BSLK-Optimizer: удаление Reflection Probe");
            for (int i = 0; i < probes.Count; i++)
                Undo.DestroyObjectImmediate(probes[i]);

            return probes.Count;
        }

        public static int RemoveEmptyMaterialSlots(GameObject avatar)
        {
            List<Renderer> renderers = new List<Renderer>();
            if (!TryCollectRenderers(avatar, renderers))
                return 0;

            Undo.SetCurrentGroupName("BSLK-Optimizer: удаление пустых слотов материалов");

            int cleaned = 0;
            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i] == null)
                    continue;

                Material[] materials = renderers[i].sharedMaterials;
                if (materials == null || materials.Length == 0)
                    continue;

                int nullCount = 0;
                for (int m = 0; m < materials.Length; m++)
                {
                    if (materials[m] == null)
                        nullCount++;
                }

                if (nullCount == 0)
                    continue;

                Material[] newMaterials = new Material[materials.Length - nullCount];
                int w = 0;
                for (int m = 0; m < materials.Length; m++)
                {
                    if (materials[m] != null)
                        newMaterials[w++] = materials[m];
                }

                Undo.RecordObject(renderers[i], "BSLK-Optimizer: удаление пустых слотов материалов");
                renderers[i].sharedMaterials = newMaterials;
                cleaned += nullCount;
            }

            return cleaned;
        }

        public static TextureCleanupReport RemoveUnusedTexturesFromMaterials(GameObject avatar)
        {
            TextureCleanupReport report = new TextureCleanupReport();

            List<Renderer> renderers = new List<Renderer>();
            if (!TryCollectRenderers(avatar, renderers))
                return report;

            HashSet<Material> materials = new HashSet<Material>();
            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i] == null)
                    continue;

                Material[] assigned = renderers[i].sharedMaterials;
                for (int m = 0; m < assigned.Length; m++)
                {
                    if (assigned[m] != null)
                        materials.Add(assigned[m]);
                }
            }

            List<Material> materialList = new List<Material>(materials);
            for (int i = 0; i < materialList.Count; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar(
                        "BSLK-Optimizer",
                        string.Format("Очистка материалов... {0}/{1}", i + 1, materialList.Count),
                        (float)(i + 1) / materialList.Count))
                    break;

                Material material = materialList[i];
                if (material.shader == null)
                    continue;

                string[] shaderProperties = material.GetTexturePropertyNames();
                HashSet<string> shaderNames = new HashSet<string>(shaderProperties);

                SerializedObject serialized = new SerializedObject(material);
                SerializedProperty saved = serialized.FindProperty("m_SavedProperties");
                if (saved == null)
                {
                    serialized.Dispose();
                    continue;
                }

                SerializedProperty texEnvs = saved.FindPropertyRelative("m_TexEnvs");
                if (texEnvs == null)
                {
                    serialized.Dispose();
                    continue;
                }

                bool changed = false;
                for (int t = texEnvs.arraySize - 1; t >= 0; t--)
                {
                    SerializedProperty element = texEnvs.GetArrayElementAtIndex(t);
                    SerializedProperty nameProp = element.FindPropertyRelative("first");
                    SerializedProperty second = element.FindPropertyRelative("second");
                    SerializedProperty textureProp = second != null ? second.FindPropertyRelative("m_Texture") : null;

                    if (nameProp == null || textureProp == null)
                        continue;

                    if (shaderNames.Contains(nameProp.stringValue))
                        continue;

                    if (textureProp.objectReferenceValue == null)
                        continue;

                    textureProp.objectReferenceValue = null;
                    texEnvs.DeleteArrayElementAtIndex(t);
                    report.texturesRemoved++;
                    changed = true;
                }

                if (changed)
                {
                    Undo.RecordObject(material, "BSLK-Optimizer: удаление неиспользуемых текстур");
                    serialized.ApplyModifiedProperties();
                    EditorUtility.SetDirty(material);
                    report.materials++;
                }

                serialized.Dispose();
            }

            EditorUtility.ClearProgressBar();
            return report;
        }

        public static BlendShapeReport RemoveUnusedBlendShapes(GameObject avatar)
        {
            BlendShapeReport report = new BlendShapeReport();

            List<SkinnedMeshRenderer> renderers = new List<SkinnedMeshRenderer>();
            if (!TryCollectSkinnedRenderers(avatar, renderers))
                return report;

            HashSet<Mesh> meshes = new HashSet<Mesh>();
            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i] != null && renderers[i].sharedMesh != null)
                    meshes.Add(renderers[i].sharedMesh);
            }

            HashSet<string> usedShapes = CollectUsedBlendShapeNames();

            List<Mesh> meshList = new List<Mesh>(meshes);
            for (int i = 0; i < meshList.Count; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar(
                        "BSLK-Optimizer",
                        string.Format("Обработка Blend Shapes... {0}/{1}", i + 1, meshList.Count),
                        (float)(i + 1) / meshList.Count))
                    break;

                Mesh mesh = meshList[i];
                if (!mesh.isReadable)
                {
                    report.skipped++;
                    Debug.LogWarning("[BSLK-Optimizer] Меш «" + mesh.name + "» не доступен для чтения, Blend Shapes не удалены.");
                    continue;
                }

                int shapeCount = mesh.blendShapeCount;
                int removeCount = 0;
                for (int s = 0; s < shapeCount; s++)
                {
                    if (!usedShapes.Contains(mesh.GetBlendShapeName(s)))
                        removeCount++;
                }

                if (removeCount == 0 || removeCount == shapeCount)
                    continue;

                int vertexCount = mesh.vertexCount;
                List<BlendShapeData> keep = new List<BlendShapeData>();
                for (int s = 0; s < shapeCount; s++)
                {
                    string name = mesh.GetBlendShapeName(s);
                    if (usedShapes.Contains(name))
                    {
                        BlendShapeData data = new BlendShapeData();
                        data.name = name;

                        int frameCount = mesh.GetBlendShapeFrameCount(s);
                        for (int f = 0; f < frameCount; f++)
                        {
                            Vector3[] deltaVertices = new Vector3[vertexCount];
                            Vector3[] deltaNormals = new Vector3[vertexCount];
                            Vector3[] deltaTangents = new Vector3[vertexCount];
                            mesh.GetBlendShapeFrameVertices(s, f, deltaVertices, deltaNormals, deltaTangents);
                            data.frames.Add(new BlendShapeFrame(mesh.GetBlendShapeFrameWeight(s, f), deltaVertices, deltaNormals, deltaTangents));
                        }

                        keep.Add(data);
                    }
                }

                mesh.ClearBlendShapes();
                for (int s = 0; s < keep.Count; s++)
                {
                    for (int f = 0; f < keep[s].frames.Count; f++)
                    {
                        mesh.AddBlendShapeFrame(keep[s].name, keep[s].frames[f].weight,
                            keep[s].frames[f].deltaVertices, keep[s].frames[f].deltaNormals, keep[s].frames[f].deltaTangents);
                    }
                }

                EditorUtility.SetDirty(mesh);
                report.meshes++;
                report.shapesRemoved += removeCount;
            }

            EditorUtility.ClearProgressBar();
            return report;
        }

        private static HashSet<string> CollectUsedBlendShapeNames()
        {
            HashSet<string> names = new HashSet<string>();
            string[] guids = AssetDatabase.FindAssets("t:AnimationClip");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                    continue;

                EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
                for (int b = 0; b < bindings.Length; b++)
                {
                    if (bindings[b].type != typeof(SkinnedMeshRenderer))
                        continue;

                    string propertyName = bindings[b].propertyName;
                    if (propertyName.StartsWith("blendShape."))
                        names.Add(propertyName.Substring("blendShape.".Length));
                }
            }
            return names;
        }

        private class BlendShapeFrame
        {
            public float weight;
            public Vector3[] deltaVertices;
            public Vector3[] deltaNormals;
            public Vector3[] deltaTangents;

            public BlendShapeFrame(float weight, Vector3[] deltaVertices, Vector3[] deltaNormals, Vector3[] deltaTangents)
            {
                this.weight = weight;
                this.deltaVertices = deltaVertices;
                this.deltaNormals = deltaNormals;
                this.deltaTangents = deltaTangents;
            }
        }

        private class BlendShapeData
        {
            public string name;
            public List<BlendShapeFrame> frames = new List<BlendShapeFrame>();
        }

        public static MeshReport OptimizeMeshes(GameObject avatar)
        {
            MeshReport report = new MeshReport();

            List<Renderer> renderers = new List<Renderer>();
            if (!TryCollectRenderers(avatar, renderers))
                return report;

            HashSet<Mesh> meshes = new HashSet<Mesh>();
            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i] == null)
                    continue;

                MeshFilter filter = renderers[i].GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    meshes.Add(filter.sharedMesh);

                SkinnedMeshRenderer skinned = renderers[i] as SkinnedMeshRenderer;
                if (skinned != null && skinned.sharedMesh != null)
                    meshes.Add(skinned.sharedMesh);
            }

            List<Mesh> meshList = new List<Mesh>(meshes);
            report.meshes = meshList.Count;

            for (int i = 0; i < meshList.Count; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar(
                        "BSLK-Optimizer",
                        string.Format("Оптимизация мешей... {0}/{1}", i + 1, meshList.Count),
                        (float)(i + 1) / meshList.Count))
                    break;

                try
                {
                    meshList[i].Optimize();
                    EditorUtility.SetDirty(meshList[i]);
                    report.optimized++;
                }
                catch (System.Exception e)
                {
                    report.skipped++;
                    Debug.LogWarning("[BSLK-Optimizer] Не удалось оптимизировать меш «" + meshList[i].name + "»: " + e.Message);
                }
            }

            EditorUtility.ClearProgressBar();
            return report;
        }

        public static BoneReport RemoveUnusedBones(GameObject avatar)
        {
            BoneReport report = new BoneReport();

            List<SkinnedMeshRenderer> renderers = new List<SkinnedMeshRenderer>();
            if (!TryCollectSkinnedRenderers(avatar, renderers))
                return report;

            HashSet<Mesh> processedMeshes = new HashSet<Mesh>();

            for (int i = 0; i < renderers.Count; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar(
                        "BSLK-Optimizer",
                        string.Format("Обработка костей... {0}/{1}", i + 1, renderers.Count),
                        (float)(i + 1) / renderers.Count))
                    break;

                SkinnedMeshRenderer renderer = renderers[i];
                Mesh mesh = renderer.sharedMesh;
                Transform[] bones = renderer.bones;

                if (mesh == null || bones == null || bones.Length == 0)
                    continue;

                if (processedMeshes.Contains(mesh))
                    continue;

                if (!mesh.isReadable)
                {
                    report.skippedNotReadable++;
                    Debug.LogWarning("[BSLK-Optimizer] Меш «" + mesh.name + "» не доступен для чтения (Read/Write выключен), кости не удалены.");
                    continue;
                }

                if (mesh.bindposes == null || mesh.bindposes.Length != bones.Length)
                {
                    report.skippedMismatch++;
                    Debug.LogWarning("[BSLK-Optimizer] Меш «" + mesh.name + "»: количество bindposes не совпадает с костями, пропущен.");
                    continue;
                }

                processedMeshes.Add(mesh);

                bool[] used = CollectUsedBones(mesh, bones.Length);
                if (used == null)
                {
                    report.skippedMismatch++;
                    Debug.LogWarning("[BSLK-Optimizer] Меш «" + mesh.name + "» не содержит данных скиннинга, пропущен.");
                    continue;
                }

                MarkAncestorsAsUsed(used, bones, renderer.rootBone);
                MarkPhysBoneBonesAsUsed(used, bones);

                bool hasUnused = false;
                for (int b = 0; b < used.Length; b++)
                {
                    if (!used[b])
                    {
                        hasUnused = true;
                        break;
                    }
                }

                if (!hasUnused)
                    continue;

                int[] remap = new int[used.Length];
                List<Transform> newBones = new List<Transform>();
                List<Matrix4x4> newBindposes = new List<Matrix4x4>();
                for (int b = 0; b < used.Length; b++)
                {
                    if (used[b])
                    {
                        remap[b] = newBones.Count;
                        newBones.Add(bones[b]);
                        newBindposes.Add(mesh.bindposes[b]);
                    }
                    else
                    {
                        remap[b] = -1;
                    }
                }

                NativeArray<byte> bonesPerVertex = mesh.GetBonesPerVertex();
                NativeArray<BoneWeight1> weights = mesh.GetAllBoneWeights();

                NativeArray<byte> newBonesPerVertex = new NativeArray<byte>(bonesPerVertex, Allocator.Temp);
                NativeArray<BoneWeight1> newWeights = new NativeArray<BoneWeight1>(weights.Length, Allocator.Temp);
                for (int w = 0; w < weights.Length; w++)
                {
                    BoneWeight1 bw1 = weights[w];
                    bw1.boneIndex = RemapBoneIndex(bw1.boneIndex, remap);
                    newWeights[w] = bw1;
                }

                Undo.RecordObject(renderer, "BSLK-Optimizer: удаление неиспользуемых костей");

                mesh.SetBoneWeights(newBonesPerVertex, newWeights);
                newBonesPerVertex.Dispose();
                newWeights.Dispose();

                mesh.bindposes = newBindposes.ToArray();
                renderer.bones = newBones.ToArray();

                int rootIdx = renderer.rootBone != null ? FindBoneIndex(renderer.rootBone, bones) : -1;
                if (rootIdx >= 0 && !used[rootIdx])
                    renderer.rootBone = newBones.Count > 0 ? newBones[0] : null;

                EditorUtility.SetDirty(mesh);

                report.renderers++;
                report.bonesRemoved += bones.Length - newBones.Count;
            }

            EditorUtility.ClearProgressBar();
            return report;
        }

        private static bool TryCollectSkinnedRenderers(GameObject avatar, List<SkinnedMeshRenderer> renderers)
        {
            if (avatar != null)
            {
                avatar.GetComponentsInChildren(true, renderers);
                return true;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("[BSLK-Optimizer] Сцена не открыта. Откройте сцену и попробуйте снова.");
                return false;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                roots[i].GetComponentsInChildren(true, renderers);

            return true;
        }

        private static bool TryCollectRenderers(GameObject avatar, List<Renderer> renderers)
        {
            if (avatar != null)
            {
                avatar.GetComponentsInChildren(true, renderers);
                return true;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("[BSLK-Optimizer] Сцена не открыта. Откройте сцену и попробуйте снова.");
                return false;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                roots[i].GetComponentsInChildren(true, renderers);

            return true;
        }

        private static bool[] CollectUsedBones(Mesh mesh, int boneCount)
        {
            NativeArray<byte> bonesPerVertex = mesh.GetBonesPerVertex();
            if (bonesPerVertex.Length == 0)
                return null;

            bool[] used = new bool[boneCount];
            NativeArray<BoneWeight1> weights = mesh.GetAllBoneWeights();
            for (int w = 0; w < weights.Length; w++)
                MarkWeight(used, weights[w].boneIndex, weights[w].weight);

            return used;
        }

        private static void MarkWeight(bool[] used, int index, float weight)
        {
            if (weight > 0f && index >= 0 && index < used.Length)
                used[index] = true;
        }

        private static void MarkAncestorsAsUsed(bool[] used, Transform[] bones, Transform rootBone)
        {
            bool[] original = (bool[])used.Clone();

            for (int b = 0; b < bones.Length; b++)
            {
                if (!original[b])
                    continue;

                Transform t = bones[b] != null ? bones[b].parent : null;
                while (t != null)
                {
                    int idx = FindBoneIndex(t, bones);
                    if (idx >= 0)
                        used[idx] = true;
                    t = t.parent;
                }
            }

            if (rootBone != null)
            {
                int idx = FindBoneIndex(rootBone, bones);
                if (idx >= 0)
                    used[idx] = true;
            }
        }

        private static void MarkPhysBoneBonesAsUsed(bool[] used, Transform[] bones)
        {
            for (int b = 0; b < bones.Length; b++)
            {
                if (bones[b] != null && HasDynamicBoneComponent(bones[b].gameObject))
                    used[b] = true;
            }
        }

        private static int FindBoneIndex(Transform bone, Transform[] bones)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == bone)
                    return i;
            }
            return -1;
        }

        private static int RemapBoneIndex(int index, int[] remap)
        {
            if (index < 0 || index >= remap.Length)
                return index;
            int mapped = remap[index];
            return mapped >= 0 ? mapped : index;
        }

        public static TextureReport ProcessTextures(bool clampSize, bool disableCrunch, GameObject avatar)
        {
            TextureReport report = new TextureReport();
            string[] paths = CollectTexturePaths(avatar);

            for (int i = 0; i < paths.Length; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar(
                        "BSLK-Optimizer",
                        string.Format("Обработка текстур... {0}/{1}", i + 1, paths.Length),
                        (float)(i + 1) / paths.Length))
                    break;

                string path = paths[i];

                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;

                report.total++;
                bool changed = false;

                if (clampSize && importer.maxTextureSize > MaxTextureSize)
                {
                    importer.maxTextureSize = MaxTextureSize;
                    changed = true;
                }

                if (disableCrunch && importer.crunchedCompression)
                {
                    importer.crunchedCompression = false;
                    changed = true;
                }

                for (int p = 0; p < PlatformNames.Length; p++)
                {
                    TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(PlatformNames[p]);
                    if (!settings.overridden)
                        continue;

                    bool platformChanged = false;

                    if (clampSize && settings.maxTextureSize > MaxTextureSize)
                    {
                        settings.maxTextureSize = MaxTextureSize;
                        platformChanged = true;
                    }

                    if (disableCrunch && settings.crunchedCompression)
                    {
                        settings.crunchedCompression = false;
                        platformChanged = true;
                    }

                    if (disableCrunch && TryUnCrunchFormat(settings.format, out TextureImporterFormat format))
                    {
                        settings.format = format;
                        platformChanged = true;
                    }

                    if (platformChanged)
                    {
                        importer.SetPlatformTextureSettings(settings);
                        changed = true;
                    }
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                    report.changed++;
                }
            }

            EditorUtility.ClearProgressBar();
            return report;
        }

        private static string[] CollectTexturePaths(GameObject avatar)
        {
            if (avatar != null)
                return CollectAvatarTexturePaths(avatar);

            string[] guids = AssetDatabase.FindAssets("t:Texture");
            List<string> list = new List<string>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!path.StartsWith("Packages/"))
                    list.Add(path);
            }
            return list.ToArray();
        }

        public static TextureReport DisableTextureReadWrite(GameObject avatar)
        {
            TextureReport report = new TextureReport();
            string[] paths = CollectTexturePaths(avatar);

            for (int i = 0; i < paths.Length; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar(
                        "BSLK-Optimizer",
                        string.Format("Отключение Read/Write... {0}/{1}", i + 1, paths.Length),
                        (float)(i + 1) / paths.Length))
                    break;

                string path = paths[i];
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;

                report.total++;
                if (!importer.isReadable)
                    continue;

                importer.isReadable = false;
                importer.SaveAndReimport();
                report.changed++;
            }

            EditorUtility.ClearProgressBar();
            return report;
        }

        private static readonly Dictionary<TextureImporterFormat, TextureImporterFormat> RgbFormatPairs =
            new Dictionary<TextureImporterFormat, TextureImporterFormat>
            {
                { TextureImporterFormat.RGBA32, TextureImporterFormat.RGB24 },
                { TextureImporterFormat.ARGB32, TextureImporterFormat.RGB24 },
                { TextureImporterFormat.DXT5, TextureImporterFormat.DXT1 },
                { TextureImporterFormat.DXT5Crunched, TextureImporterFormat.DXT1Crunched },
                { TextureImporterFormat.ATC_RGBA8, TextureImporterFormat.ATC_RGB4 },
                { TextureImporterFormat.PVRTC_RGBA2, TextureImporterFormat.PVRTC_RGB2 },
                { TextureImporterFormat.PVRTC_RGBA4, TextureImporterFormat.PVRTC_RGB4 }
            };

        private static bool TryToRgbFormat(TextureImporterFormat format, out TextureImporterFormat result)
        {
            if (RgbFormatPairs.TryGetValue(format, out result))
                return true;

            string name = format.ToString();
            if (name.Contains("RGBA"))
            {
                string[] candidates =
                {
                    name.Replace("RGBA", "RGB"),
                    name.Replace("RGBA8", "RGB4"),
                    name.Replace("RGBA", "RGB4")
                };
                for (int c = 0; c < candidates.Length; c++)
                {
                    if (System.Enum.TryParse(candidates[c], out result))
                        return true;
                }
            }

            result = format;
            return false;
        }

        public static TextureReport ConvertTexturesToRgbWithoutAlpha(GameObject avatar)
        {
            TextureReport report = new TextureReport();
            string[] paths = CollectTexturePaths(avatar);

            for (int i = 0; i < paths.Length; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar(
                        "BSLK-Optimizer",
                        string.Format("Анализ альфа-канала... {0}/{1}", i + 1, paths.Length),
                        (float)(i + 1) / paths.Length))
                    break;

                string path = paths[i];
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;

                if (importer.textureType == TextureImporterType.NormalMap)
                    continue;

                report.total++;

                bool wasReadable = importer.isReadable;
                if (!wasReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                }

                bool converted = false;

                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null)
                {
                    try
                    {
                        Color32[] pixels = texture.GetPixels32();
                        bool hasAlpha = false;
                        for (int px = 0; px < pixels.Length; px++)
                        {
                            if (pixels[px].a < 255)
                            {
                                hasAlpha = true;
                                break;
                            }
                        }

                        if (!hasAlpha)
                        {
                            TextureImporterPlatformSettings defaultSettings = importer.GetDefaultPlatformTextureSettings();
                            if (TryToRgbFormat(defaultSettings.format, out TextureImporterFormat rgbFormat) &&
                                rgbFormat != defaultSettings.format)
                            {
                                defaultSettings.format = rgbFormat;
                                importer.SetPlatformTextureSettings(defaultSettings);
                                converted = true;
                            }

                            for (int p = 0; p < PlatformNames.Length; p++)
                            {
                                TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(PlatformNames[p]);
                                if (!settings.overridden)
                                    continue;
                                if (TryToRgbFormat(settings.format, out TextureImporterFormat platformRgb) &&
                                    platformRgb != settings.format)
                                {
                                    settings.format = platformRgb;
                                    importer.SetPlatformTextureSettings(settings);
                                    converted = true;
                                }
                            }
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning("[BSLK-Optimizer] Не удалось прочитать текстуру: " + path + " — " + e.Message);
                    }
                }

                bool needsReimport = converted;
                if (!wasReadable)
                {
                    importer.isReadable = false;
                    needsReimport = true;
                }

                if (needsReimport)
                {
                    importer.SaveAndReimport();
                    if (converted)
                        report.changed++;
                }
            }

            EditorUtility.ClearProgressBar();
            return report;
        }

        public static int RemoveMissingScripts(GameObject avatar)
        {
            List<GameObject> targets = new List<GameObject>();

            if (avatar != null)
            {
                targets.AddRange(avatar.GetComponentsInChildren<GameObject>(true));
            }
            else
            {
                Scene scene = SceneManager.GetActiveScene();
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    Debug.LogError("[BSLK-Optimizer] Сцена не открыта. Откройте сцену и попробуйте снова.");
                    return 0;
                }

                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                    targets.AddRange(roots[i].GetComponentsInChildren<GameObject>(true));
            }

            int removed = 0;
            Undo.SetCurrentGroupName("BSLK-Optimizer: удаление Missing Scripts");
            for (int i = 0; i < targets.Count; i++)
            {
                GameObject go = targets[i];
                MonoBehaviour[] components = go.GetComponents<MonoBehaviour>();
                int missing = 0;
                for (int c = 0; c < components.Length; c++)
                {
                    if (components[c] == null)
                        missing++;
                }
                if (missing == 0)
                    continue;

                Undo.RecordObject(go, "BSLK-Optimizer: удаление Missing Scripts");
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                removed += missing;
            }

            return removed;
        }

        public static string GatherReport(GameObject avatar)
        {
            List<Renderer> renderers = new List<Renderer>();

            if (avatar != null)
            {
                avatar.GetComponentsInChildren(true, renderers);
            }
            else
            {
                Scene scene = SceneManager.GetActiveScene();
                if (!scene.IsValid() || !scene.isLoaded)
                    return "Сцена не открыта.";

                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                    roots[i].GetComponentsInChildren(true, renderers);
            }

            int vertexCount = 0;
            int triangleCount = 0;
            int meshCount = 0;
            int blendShapeCount = 0;
            int boneCount = 0;
            int materialCount = 0;
            HashSet<string> texturePaths = new HashSet<string>();

            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                Mesh mesh = null;
                SkinnedMeshRenderer smr = renderer as SkinnedMeshRenderer;
                if (smr != null)
                {
                    mesh = smr.sharedMesh;
                    boneCount += smr.bones != null ? smr.bones.Length : 0;
                }
                else
                {
                    MeshFilter mf = renderer.GetComponent<MeshFilter>();
                    if (mf != null)
                        mesh = mf.sharedMesh;
                }

                if (mesh != null)
                {
                    meshCount++;
                    vertexCount += mesh.vertexCount;
                    triangleCount += mesh.triangles.Length / 3;
                    blendShapeCount += mesh.blendShapeCount;
                }

                Material[] materials = renderer.sharedMaterials;
                for (int m = 0; m < materials.Length; m++)
                {
                    if (materials[m] == null)
                        continue;
                    materialCount++;
                    string[] names = materials[m].GetTexturePropertyNames();
                    for (int n = 0; n < names.Length; n++)
                    {
                        Texture texture = materials[m].GetTexture(names[n]);
                        if (texture == null)
                            continue;
                        string path = AssetDatabase.GetAssetPath(texture);
                        if (!string.IsNullOrEmpty(path) && !path.StartsWith("Packages/"))
                            texturePaths.Add(path);
                    }
                }
            }

            long textureBytes = 0;
            foreach (string path in texturePaths)
            {
                try
                {
                    System.IO.FileInfo info = new System.IO.FileInfo(path);
                    if (info.Exists)
                        textureBytes += info.Length;
                }
                catch (System.Exception) { }
            }

            return string.Format(
                "Рендереров: {0}\n" +
                "Мешей: {1}\n" +
                "Вершин: {2}\n" +
                "Треугольников: {3}\n" +
                "Blend Shapes: {4}\n" +
                "Костей: {5}\n" +
                "Материалов: {6}\n" +
                "Текстур: {7}\n" +
                "Размер текстур на диске: {8:0.0} МБ",
                renderers.Count,
                meshCount,
                vertexCount,
                triangleCount,
                blendShapeCount,
                boneCount,
                materialCount,
                texturePaths.Count,
                (float)textureBytes / (1024f * 1024f));
        }

        private static string[] CollectAvatarTexturePaths(GameObject avatar)
        {
            HashSet<string> paths = new HashSet<string>();
            List<Renderer> renderers = new List<Renderer>();
            avatar.GetComponentsInChildren(true, renderers);

            for (int r = 0; r < renderers.Count; r++)
            {
                Material[] materials = renderers[r].sharedMaterials;
                for (int m = 0; m < materials.Length; m++)
                {
                    if (materials[m] == null)
                        continue;

                    string[] names = materials[m].GetTexturePropertyNames();
                    for (int n = 0; n < names.Length; n++)
                    {
                        Texture texture = materials[m].GetTexture(names[n]);
                        if (texture == null)
                            continue;

                        string path = AssetDatabase.GetAssetPath(texture);
                        if (string.IsNullOrEmpty(path) || path.StartsWith("Packages/"))
                            continue;

                        paths.Add(path);
                    }
                }
            }

            return new List<string>(paths).ToArray();
        }

        private static bool TryUnCrunchFormat(TextureImporterFormat format, out TextureImporterFormat result)
        {
            string name = format.ToString();
            if (!name.EndsWith("Crunched"))
            {
                result = format;
                return false;
            }

            string baseName = name.Substring(0, name.Length - "Crunched".Length);
            if (System.Enum.TryParse(baseName, out result))
                return true;

            result = format;
            return false;
        }

        public static GameObject PrepareAvatarCopy(GameObject avatar)
        {
            if (avatar == null)
                return null;

            if (avatar.name.EndsWith("BSLK-Optimized"))
                return avatar;

            string copyName = avatar.name + " BSLK-Optimized";
            string outDir = "Assets/BSLK-Optimizer/Output/" + copyName;

            if (AssetDatabase.IsValidFolder(outDir))
                AssetDatabase.DeleteAsset(outDir);
            System.IO.Directory.CreateDirectory(outDir);
            AssetDatabase.Refresh();

            GameObject copy = Object.Instantiate(avatar);
            copy.name = copyName;
            copy.transform.SetParent(avatar.transform.parent, false);
            copy.transform.SetPositionAndRotation(avatar.transform.position, avatar.transform.rotation);
            copy.transform.localScale = avatar.transform.localScale;
            Undo.RegisterCreatedObjectUndo(copy, "BSLK-Optimizer: копия аватара");

            Dictionary<string, string> copiedAssets = new Dictionary<string, string>();

            string GetUniquePath(string desiredName, string extension)
            {
                string path = outDir + "/" + desiredName + extension;
                int n = 1;
                while (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
                {
                    path = outDir + "/" + desiredName + " " + n + extension;
                    n++;
                }
                return path;
            }

            string DuplicateAsset(string sourcePath, string desiredName, string extension)
            {
                if (copiedAssets.TryGetValue(sourcePath, out string existing))
                    return existing;
                string dest = GetUniquePath(desiredName, extension);
                AssetDatabase.CopyAsset(sourcePath, dest);
                copiedAssets[sourcePath] = dest;
                return dest;
            }

            foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null)
                        continue;
                    string matPath = AssetDatabase.GetAssetPath(mats[i]);
                    if (string.IsNullOrEmpty(matPath))
                        continue;
                    string newMatPath = DuplicateAsset(matPath, mats[i].name + " BSLK-Optimized", ".mat");
                    Material matCopy = AssetDatabase.LoadAssetAtPath<Material>(newMatPath);
                    if (matCopy == null)
                        continue;

                    foreach (string prop in matCopy.GetTexturePropertyNames())
                    {
                        Texture tex = matCopy.GetTexture(prop);
                        if (tex == null)
                            continue;
                        string texPath = AssetDatabase.GetAssetPath(tex);
                        if (string.IsNullOrEmpty(texPath))
                            continue;
                        string ext = System.IO.Path.GetExtension(texPath);
                        string newTexPath = DuplicateAsset(texPath, tex.name + " BSLK-Optimized", ext);
                        matCopy.SetTexture(prop, AssetDatabase.LoadAssetAtPath<Texture>(newTexPath));
                    }
                    EditorUtility.SetDirty(matCopy);
                    mats[i] = matCopy;
                }
                r.sharedMaterials = mats;
            }

            foreach (MeshFilter mf in copy.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null)
                    continue;
                string meshPath = AssetDatabase.GetAssetPath(mesh);
                if (string.IsNullOrEmpty(meshPath))
                    continue;
                string newMeshPath = DuplicateAsset(meshPath, mesh.name + " BSLK-Optimized", ".asset");
                mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(newMeshPath);
            }

            foreach (SkinnedMeshRenderer smr in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = smr.sharedMesh;
                if (mesh == null)
                    continue;
                string meshPath = AssetDatabase.GetAssetPath(mesh);
                if (string.IsNullOrEmpty(meshPath))
                    continue;
                string newMeshPath = DuplicateAsset(meshPath, mesh.name + " BSLK-Optimized", ".asset");
                smr.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(newMeshPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = copy;
            return copy;
        }
    }

    public struct CameraAudioReport
    {
        public int cameras;
        public int audioListeners;
    }

    public struct MeshReport
    {
        public int meshes;
        public int optimized;
        public int skipped;
    }

    public struct BlendShapeReport
    {
        public int meshes;
        public int shapesRemoved;
        public int skipped;
    }

    public struct TextureCleanupReport
    {
        public int materials;
        public int texturesRemoved;
    }

    public struct TextureReport
    {
        public int total;
        public int changed;
    }

    public struct BoneReport
    {
        public int renderers;
        public int bonesRemoved;
        public int skippedNotReadable;
        public int skippedMismatch;
    }
}