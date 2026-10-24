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

        public const string Version = "2.0.0";
        public const string Contact = "GedHamus";
        public const string Discord = "https://discord.gg/ugC6vSKUfg";

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
                    lights.AddRange(roots[i].GetComponentsInChildren<Light>(true));
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
                    cameras.AddRange(roots[i].GetComponentsInChildren<Camera>(true));
                    listeners.AddRange(roots[i].GetComponentsInChildren<AudioListener>(true));
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
                    probes.AddRange(roots[i].GetComponentsInChildren<ReflectionProbe>(true));
            }

            Undo.SetCurrentGroupName("BSLK-Optimizer: удаление Reflection Probe");
            for (int i = 0; i < probes.Count; i++)
                Undo.DestroyObjectImmediate(probes[i]);

            return probes.Count;
        }

        private static Mesh EnsureMutableMesh(GameObject avatar, Mesh mesh, Dictionary<Mesh, Mesh> cloneMap, List<Renderer> allRenderers)
        {
            if (mesh == null)
                return null;
            if (cloneMap.TryGetValue(mesh, out Mesh existingClone))
                return existingClone;

            string path = AssetDatabase.GetAssetPath(mesh);
            bool isAsset = !string.IsNullOrEmpty(path);
            // Клонируем только ассеты из проекта (FBX), чтобы не портить исходный файл.
            // Меши уже в сцене (path=="") — уже инстансы, клонировать не нужно.
            if (!isAsset)
                return mesh;

            Mesh clone = UnityEngine.Object.Instantiate(mesh);
            clone.name = mesh.name;
            SaveGeneratedMesh(clone);
            cloneMap[mesh] = clone;

            // Переназначить всем рендерерам, которые шарили этот меш
            for (int i = 0; i < allRenderers.Count; i++)
            {
                if (allRenderers[i] == null)
                    continue;
                SkinnedMeshRenderer smr = allRenderers[i] as SkinnedMeshRenderer;
                if (smr != null && smr.sharedMesh == mesh)
                {
                    Undo.RecordObject(smr, "BSLK-Optimizer: клонирование меша");
                    smr.sharedMesh = clone;
                    EditorUtility.SetDirty(smr);
                    continue;
                }
                MeshFilter mf = allRenderers[i].GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh == mesh)
                {
                    Undo.RecordObject(mf, "BSLK-Optimizer: клонирование меша");
                    mf.sharedMesh = clone;
                    EditorUtility.SetDirty(mf);
                }
            }
            // Если меш шарится вне аватара (редкий кейс) — тоже клонируем, но не переназначаем
            return clone;
        }

        private static void SaveGeneratedMesh(Mesh mesh)
        {
            const string folder = "Assets/BSLK-Optimizer-Generated";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "BSLK-Optimizer-Generated");
            AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(folder + "/Mesh.asset"));
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

                // Removing an interior slot changes the material/submesh and animation indices.
                int nullCount = 0;
                for (int m = materials.Length - 1; m >= 0 && materials[m] == null; m--)
                    nullCount++;

                if (nullCount == 0)
                    continue;

                Material[] newMaterials = new Material[materials.Length - nullCount];
                System.Array.Copy(materials, newMaterials, newMaterials.Length);

                Mesh mesh = null;
                SkinnedMeshRenderer skinned = renderers[i] as SkinnedMeshRenderer;
                if (skinned != null)
                    mesh = skinned.sharedMesh;
                else
                {
                    MeshFilter filter = renderers[i].GetComponent<MeshFilter>();
                    if (filter != null)
                        mesh = filter.sharedMesh;
                }
                if (mesh != null && newMaterials.Length < mesh.subMeshCount)
                {
                    Debug.LogWarning("[BSLK-Optimizer] Рендерер «" + renderers[i].name + "»: после удаления пустых слотов материалов (" + newMaterials.Length + ") меньше чем subMeshCount (" + mesh.subMeshCount + "), пропущен чтобы не сломать меш.");
                    continue;
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

            if (HasVRCComponent(avatar))
            {
                Debug.LogWarning("[BSLK-Optimizer] Обнаружен VRC Avatar Descriptor — удаление BlendShapes пропущено, чтобы сохранить viseme/eye blend shapes. Отключи галочку 'Удалить Blend Shapes' если хочешь форсировать.");
                EditorUtility.ClearProgressBar();
                return report;
            }

            HashSet<string> usedShapes = CollectUsedBlendShapeNames(avatar);

            List<Mesh> meshList = new List<Mesh>(meshes);

            // Для клонирования нужен полный список рендереров аватара
            List<Renderer> allRenderers = new List<Renderer>();
            TryCollectRenderers(avatar, allRenderers);
            Dictionary<Mesh, Mesh> cloneMap = new Dictionary<Mesh, Mesh>();
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

                mesh = EnsureMutableMesh(avatar, mesh, cloneMap, allRenderers);

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

                var weightsByRenderer = new Dictionary<SkinnedMeshRenderer, Dictionary<string, float>>();
                foreach (SkinnedMeshRenderer renderer in renderers)
                {
                    if (renderer == null || renderer.sharedMesh != mesh) continue;
                    var weights = new Dictionary<string, float>();
                    for (int s = 0; s < mesh.blendShapeCount; s++)
                        weights[mesh.GetBlendShapeName(s)] = renderer.GetBlendShapeWeight(s);
                    weightsByRenderer[renderer] = weights;
                    Undo.RecordObject(renderer, "BSLK-Optimizer: Blend Shapes");
                }
                Undo.RegisterCompleteObjectUndo(mesh, "BSLK-Optimizer: Blend Shapes");
                mesh.ClearBlendShapes();
                for (int s = 0; s < keep.Count; s++)
                {
                    for (int f = 0; f < keep[s].frames.Count; f++)
                    {
                        mesh.AddBlendShapeFrame(keep[s].name, keep[s].frames[f].weight,
                            keep[s].frames[f].deltaVertices, keep[s].frames[f].deltaNormals, keep[s].frames[f].deltaTangents);
                    }
                }

                foreach (var entry in weightsByRenderer)
                    for (int s = 0; s < mesh.blendShapeCount; s++)
                        entry.Key.SetBlendShapeWeight(s, entry.Value[mesh.GetBlendShapeName(s)]);
                try { mesh.RecalculateBounds(); } catch { }
                EditorUtility.SetDirty(mesh);
                report.meshes++;
                report.shapesRemoved += removeCount;
            }

            EditorUtility.ClearProgressBar();
            return report;
        }

        private static bool HasVRCComponent(GameObject avatar)
        {
            if (avatar == null)
                return false;
            Component[] comps = avatar.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null)
                    continue;
                string tn = comps[i].GetType().Name;
                if (tn.Contains("VRC") || tn.Contains("VRCAvatarDescriptor") || tn.Contains("PipelineManager"))
                    return true;
            }
            return false;
        }

        private static HashSet<string> CollectUsedBlendShapeNames(GameObject avatar)
        {
            HashSet<string> names = new HashSet<string>();
            // 1. Все AnimationClip в проекте
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
            // 2. Все AnimatorController — их клипы тоже могут содержать бленды, плюс покрывает случаи где клип — саб-ассет
            string[] ctrlGuids = AssetDatabase.FindAssets("t:AnimatorController");
            for (int i = 0; i < ctrlGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(ctrlGuids[i]);
                RuntimeAnimatorController ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
                if (ctrl == null)
                    continue;
                AnimationClip[] clips = ctrl.animationClips;
                for (int c = 0; c < clips.Length; c++)
                {
                    AnimationClip clip = clips[c];
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
            }
            // 3. Если аватар передан — собрать бленды у которых текущий вес > 0 (пользователь мог выставить в сцене)
            if (avatar != null)
            {
                SkinnedMeshRenderer[] smrs = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                for (int i = 0; i < smrs.Length; i++)
                {
                    SkinnedMeshRenderer smr = smrs[i];
                    Mesh m = smr.sharedMesh;
                    if (m == null)
                        continue;
                    for (int s = 0; s < m.blendShapeCount; s++)
                    {
                        try
                        {
                            float w = smr.GetBlendShapeWeight(s);
                            if (w != 0f)
                                names.Add(m.GetBlendShapeName(s));
                        }
                        catch { }
                    }
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

            // Собираем все скинед-меши чтобы не ломать скиннинг на втором клике
            HashSet<Mesh> skinnedMeshes = new HashSet<Mesh>();
            for (int i = 0; i < renderers.Count; i++)
            {
                SkinnedMeshRenderer smr = renderers[i] as SkinnedMeshRenderer;
                if (smr != null && smr.sharedMesh != null)
                    skinnedMeshes.Add(smr.sharedMesh);
            }

            Dictionary<Mesh, Mesh> cloneMap = new Dictionary<Mesh, Mesh>();
            for (int i = 0; i < meshList.Count; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar(
                        "BSLK-Optimizer",
                        string.Format("Оптимизация мешей... {0}/{1}", i + 1, meshList.Count),
                        (float)(i + 1) / meshList.Count))
                    break;

                Mesh mesh = meshList[i];
                if (mesh == null)
                {
                    report.skipped++;
                    continue;
                }
                if (skinnedMeshes.Contains(mesh))
                {
                    report.skipped++;
                    continue;
                }
                if (!mesh.isReadable)
                {
                    report.skipped++;
                    continue;
                }
                if (mesh.blendShapeCount > 0)
                {
                    report.skipped++;
                    continue;
                }
                if (mesh.boneWeights != null && mesh.boneWeights.Length > 0)
                {
                    report.skipped++;
                    continue;
                }

                try
                {
                    mesh = EnsureMutableMesh(avatar, mesh, cloneMap, renderers);
                    mesh.Optimize();
                    try { mesh.RecalculateBounds(); } catch { }
                    EditorUtility.SetDirty(mesh);
                    report.optimized++;
                }
                catch (System.Exception e)
                {
                    report.skipped++;
                    Debug.LogWarning("[BSLK-Optimizer] Не удалось оптимизировать меш «" + mesh.name + "»: " + e.Message);
                }
            }

            EditorUtility.ClearProgressBar();
            return report;
        }

        // `save` persists the per-renderer mesh copies; Club mode passes one that writes into the result folder.
        public static BoneReport RemoveUnusedBones(GameObject avatar, System.Action<Mesh> save = null)
        {
            BoneReport report = new BoneReport();

            List<SkinnedMeshRenderer> renderers = new List<SkinnedMeshRenderer>();
            if (!TryCollectSkinnedRenderers(avatar, renderers))
                return report;

            List<Renderer> allRenderers = new List<Renderer>();
            TryCollectRenderers(avatar, allRenderers);
            Dictionary<Mesh, Mesh> cloneMap = new Dictionary<Mesh, Mesh>();

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

                bool[] used = CollectUsedBones(mesh, bones.Length);
                if (used == null)
                {
                    report.skippedMismatch++;
                    Debug.LogWarning("[BSLK-Optimizer] Меш «" + mesh.name + "» не содержит данных скиннинга, пропущен.");
                    continue;
                }

                MarkPhysBoneBonesAsUsed(used, bones);
                MarkAncestorsAsUsed(used, bones, renderer.rootBone);

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

                // Bindings belong to the renderer, not to the shared mesh. Never copy another
                // renderer's Transform[] even when both renderers share the same source mesh.
                mesh = UnityEngine.Object.Instantiate(mesh);
                mesh.name = renderer.sharedMesh.name + "_Bones";
                (save ?? SaveGeneratedMesh)(mesh);
                Undo.RecordObject(renderer, "BSLK-Optimizer: копия меша для костей");
                renderer.sharedMesh = mesh;

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
                    int orig = bw1.boneIndex;
                    if (orig >= 0 && orig < remap.Length && remap[orig] == -1)
                    {
                        bw1.boneIndex = 0;
                        bw1.weight = 0f;
                    }
                    else
                    {
                        bw1.boneIndex = RemapBoneIndex(orig, remap);
                    }
                    newWeights[w] = bw1;
                }

                Undo.RecordObject(renderer, "BSLK-Optimizer: удаление неиспользуемых костей");

                mesh.SetBoneWeights(newBonesPerVertex, newWeights);
                newBonesPerVertex.Dispose();
                newWeights.Dispose();

                Transform[] newBonesArray = newBones.ToArray();
                mesh.bindposes = newBindposes.ToArray();
                renderer.bones = newBonesArray;

                int rootIdx = renderer.rootBone != null ? FindBoneIndex(renderer.rootBone, bones) : -1;
                if (rootIdx >= 0 && !used[rootIdx])
                    renderer.rootBone = newBones.Count > 0 ? newBones[0] : null;

                try { mesh.RecalculateBounds(); } catch { }
                EditorUtility.SetDirty(mesh);
                EditorUtility.SetDirty(renderer);

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
                renderers.AddRange(roots[i].GetComponentsInChildren<SkinnedMeshRenderer>(true));

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
                renderers.AddRange(roots[i].GetComponentsInChildren<Renderer>(true));

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
            return mapped >= 0 ? mapped : 0;
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
                { TextureImporterFormat.ETC2_RGBA8, TextureImporterFormat.ETC_RGB4 },
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
                Transform[] all = avatar.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                    targets.Add(all[i].gameObject);
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
                {
                    Transform[] all = roots[i].GetComponentsInChildren<Transform>(true);
                    for (int t = 0; t < all.Length; t++)
                        targets.Add(all[t].gameObject);
                }
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
                    renderers.AddRange(roots[i].GetComponentsInChildren<Renderer>(true));
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
                    for (int s = 0; s < mesh.subMeshCount; s++)
                        if (mesh.GetTopology(s) == MeshTopology.Triangles) triangleCount += (int)(mesh.GetIndexCount(s) / 3);
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

namespace BSLK.Optimizer
{
    using System;
    using System.Linq;
    using UnityEngine.Rendering;
    using Object = UnityEngine.Object;

    // Club-mode geometry on result-owned meshes. Every mesh written here is a new mesh handed to
    // `save`, which persists it inside the result folder; source assets are never touched.
    internal static class BSLKClubGeometry
    {
        private sealed class Frame
        {
            public float weight;
            public Vector3[] vertices, normals, tangents;
        }

        // All per-vertex data of a mesh in editable form.
        private sealed class Parts
        {
            public readonly List<Vector3> positions = new List<Vector3>(), normals = new List<Vector3>();
            public readonly List<Vector4> tangents = new List<Vector4>();
            public readonly List<Color> colors = new List<Color>();
            public readonly List<Vector4>[] uvs = Enumerable.Range(0, 8).Select(_ => new List<Vector4>()).ToArray();
            public readonly int[] uvDimensions = new int[8];
            public bool hasNormals, hasTangents, hasColors, skinned;
            public readonly List<byte> bonesPerVertex = new List<byte>();
            public readonly List<BoneWeight1> weights = new List<BoneWeight1>();
            public readonly List<Matrix4x4> bindposes = new List<Matrix4x4>();
            public readonly List<List<int>> submeshes = new List<List<int>>();
            public readonly List<string> shapeNames = new List<string>();
            public readonly Dictionary<string, List<Frame>> shapes = new Dictionary<string, List<Frame>>();
            public int Count { get { return positions.Count; } }
        }

        // Instantiate keeps a model's "no Read/Write" flag; a serialized copy carries the data in the
        // editor and can be marked readable. The source mesh and its importer are not touched.
        internal static Mesh ReadableCopy(Mesh source)
        {
            var copy = new Mesh();
            EditorUtility.CopySerialized(source, copy);
            SetReadable(copy, true);
            copy.name = source.name;
            return copy;
        }

        internal static void SetReadable(Mesh mesh, bool readable)
        {
            if (mesh.isReadable == readable) return;
            using (var serialized = new SerializedObject(mesh))
            {
                SerializedProperty flag = serialized.FindProperty("m_IsReadable");
                if (flag == null) return;
                flag.boolValue = readable;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        internal static bool Editable(Mesh mesh)
        {
            if (mesh == null || !mesh.isReadable) return false;
            for (int i = 0; i < mesh.subMeshCount; i++)
                if (mesh.GetTopology(i) != MeshTopology.Triangles) return false;
            return true;
        }

        private static void Fill<T>(List<T> list, int count, T value)
        {
            while (list.Count < count) list.Add(value);
        }

        private static Parts Read(Mesh mesh)
        {
            var p = new Parts();
            mesh.GetVertices(p.positions);
            int n = p.Count;
            mesh.GetNormals(p.normals);
            p.hasNormals = p.normals.Count == n && n > 0;
            if (!p.hasNormals) { p.normals.Clear(); Fill(p.normals, n, Vector3.zero); }
            mesh.GetTangents(p.tangents);
            p.hasTangents = p.tangents.Count == n && n > 0;
            if (!p.hasTangents) { p.tangents.Clear(); Fill(p.tangents, n, Vector4.zero); }
            mesh.GetColors(p.colors);
            p.hasColors = p.colors.Count == n && n > 0;
            if (!p.hasColors) { p.colors.Clear(); Fill(p.colors, n, Color.white); }
            for (int i = 0; i < 8; i++)
            {
                var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + i);
                p.uvDimensions[i] = mesh.HasVertexAttribute(attribute) ? mesh.GetVertexAttributeDimension(attribute) : 0;
                if (p.uvDimensions[i] > 0) mesh.GetUVs(i, p.uvs[i]);
                if (p.uvs[i].Count != n) { p.uvs[i].Clear(); Fill(p.uvs[i], n, Vector4.zero); }
            }
            var bonesPerVertex = mesh.GetBonesPerVertex();
            p.skinned = bonesPerVertex.Length == n && n > 0;
            if (p.skinned)
            {
                p.bonesPerVertex.AddRange(bonesPerVertex);
                p.weights.AddRange(mesh.GetAllBoneWeights());
            }
            else Fill(p.bonesPerVertex, n, (byte)0);
            p.bindposes.AddRange(mesh.bindposes);
            for (int s = 0; s < mesh.subMeshCount; s++) p.submeshes.Add(new List<int>(mesh.GetIndices(s)));
            for (int s = 0; s < mesh.blendShapeCount; s++)
            {
                string name = mesh.GetBlendShapeName(s);
                var frames = new List<Frame>();
                for (int f = 0; f < mesh.GetBlendShapeFrameCount(s); f++)
                {
                    var frame = new Frame { weight = mesh.GetBlendShapeFrameWeight(s, f), vertices = new Vector3[n], normals = new Vector3[n], tangents = new Vector3[n] };
                    mesh.GetBlendShapeFrameVertices(s, f, frame.vertices, frame.normals, frame.tangents);
                    frames.Add(frame);
                }
                p.shapeNames.Add(name);
                p.shapes[name] = frames;
            }
            return p;
        }

        private static Mesh Write(Parts p, string name)
        {
            var mesh = new Mesh { name = name };
            if (p.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(p.positions);
            if (p.hasNormals) mesh.SetNormals(p.normals);
            if (p.hasTangents) mesh.SetTangents(p.tangents);
            if (p.hasColors) mesh.SetColors(p.colors);
            for (int i = 0; i < 8; i++)
            {
                if (p.uvDimensions[i] == 2) mesh.SetUVs(i, p.uvs[i].Select(v => (Vector2)v).ToList());
                else if (p.uvDimensions[i] == 3) mesh.SetUVs(i, p.uvs[i].Select(v => (Vector3)v).ToList());
                else if (p.uvDimensions[i] == 4) mesh.SetUVs(i, p.uvs[i]);
            }
            if (p.skinned)
            {
                using (var bonesPerVertex = new NativeArray<byte>(p.bonesPerVertex.ToArray(), Allocator.Temp))
                using (var weights = new NativeArray<BoneWeight1>(p.weights.ToArray(), Allocator.Temp))
                    mesh.SetBoneWeights(bonesPerVertex, weights);
            }
            if (p.bindposes.Count > 0) mesh.bindposes = p.bindposes.ToArray();
            mesh.subMeshCount = p.submeshes.Count;
            for (int s = 0; s < p.submeshes.Count; s++) mesh.SetIndices(p.submeshes[s], MeshTopology.Triangles, s, false);
            foreach (string shape in p.shapeNames)
                foreach (Frame frame in p.shapes[shape])
                    mesh.AddBlendShapeFrame(shape, frame.weight, frame.vertices, frame.normals, frame.tangents);
            mesh.RecalculateBounds();
            return mesh;
        }

        internal static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        internal static void Assign(Renderer renderer, Mesh mesh)
        {
            if (renderer is SkinnedMeshRenderer skinned) skinned.sharedMesh = mesh;
            else renderer.GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        private static Transform TopLevel(Transform t, Transform root)
        {
            while (t.parent != null && t.parent != root) t = t.parent;
            return t;
        }

        // Several SkinnedMeshRenderers of one clothing item become one renderer. Skinning uses
        // per-bone bind poses, so vertices of different renderers can share one mesh unchanged.
        // Submeshes with the same material become one slot.
        internal static int MergeClothing(GameObject avatar, HashSet<Renderer> locked, ICollection<AnimationClip> clips, Func<Mesh, Mesh> save, List<string> notes)
        {
            Transform root = avatar.transform;
            var candidates = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r =>
                r.gameObject.activeInHierarchy && r.enabled && !locked.Contains(r) && r.transform != root && Editable(r.sharedMesh) &&
                r.bones.Length > 0 && r.bones.Length == r.sharedMesh.bindposes.Length && r.bones.All(b => b != null) &&
                r.sharedMesh.GetBonesPerVertex().Length == r.sharedMesh.vertexCount &&
                r.sharedMaterials.Length == r.sharedMesh.subMeshCount && r.sharedMaterials.All(m => m != null)).ToList();
            var bindings = new Dictionary<string, List<KeyValuePair<AnimationClip, EditorCurveBinding>>>();
            foreach (AnimationClip clip in clips)
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                    if (binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                    {
                        if (!bindings.TryGetValue(binding.path, out var list)) bindings[binding.path] = list = new List<KeyValuePair<AnimationClip, EditorCurveBinding>>();
                        list.Add(new KeyValuePair<AnimationClip, EditorCurveBinding>(clip, binding));
                    }
            int merged = 0, slotsBefore = 0, slotsAfter = 0;
            foreach (var group in candidates.GroupBy(r => new { top = TopLevel(r.transform, root), r.shadowCastingMode, r.receiveShadows, r.quality, r.updateWhenOffscreen, r.skinnedMotionVectors, r.gameObject.layer }))
            {
                List<SkinnedMeshRenderer> members = group.OrderByDescending(r => r.sharedMesh.vertexCount).ToList();
                if (members.Count < 2) continue;
                if (!TryMerge(members, root, bindings, save, out int before, out int after)) continue;
                merged += members.Count - 1;
                slotsBefore += before;
                slotsAfter += after;
            }
            notes.Add("Объединено мешей одежды: " + merged + " (слоты этих вещей: " + slotsBefore + " → " + slotsAfter + ").");
            return merged;
        }

        private static bool TryMerge(List<SkinnedMeshRenderer> members, Transform root, Dictionary<string, List<KeyValuePair<AnimationClip, EditorCurveBinding>>> bindings,
            Func<Mesh, Mesh> save, out int slotsBefore, out int slotsAfter)
        {
            slotsBefore = members.Sum(r => r.sharedMaterials.Length);
            slotsAfter = slotsBefore;
            SkinnedMeshRenderer target = members[0];
            var parts = members.Select(r => Read(r.sharedMesh)).ToList();
            // Different shading inputs cannot be combined without changing the look.
            if (parts.Any(p => p.hasNormals != parts[0].hasNormals || p.hasTangents != parts[0].hasTangents)) return false;
            // A shape name shared by several members must have the same frames and the same weight.
            var shapeWeights = new Dictionary<string, float>();
            var frameWeights = new Dictionary<string, float[]>();
            for (int m = 0; m < members.Count; m++)
                for (int s = 0; s < parts[m].shapeNames.Count; s++)
                {
                    string name = parts[m].shapeNames[s];
                    float weight = members[m].GetBlendShapeWeight(s);
                    float[] frames = parts[m].shapes[name].Select(f => f.weight).ToArray();
                    if (shapeWeights.TryGetValue(name, out float existing) &&
                        (existing != weight || !frameWeights[name].SequenceEqual(frames))) return false;
                    shapeWeights[name] = weight;
                    frameWeights[name] = frames;
                }
            // Two members animated through the same property in one clip cannot share a renderer.
            var animated = new HashSet<string>();
            foreach (SkinnedMeshRenderer member in members)
            {
                string path = AnimationUtility.CalculateTransformPath(member.transform, root);
                if (!bindings.TryGetValue(path, out var list)) continue;
                foreach (var entry in list)
                    if (!animated.Add(entry.Key.GetInstanceID() + "/" + entry.Value.propertyName)) return false;
            }

            var result = new Parts { hasNormals = parts[0].hasNormals, hasTangents = parts[0].hasTangents, hasColors = parts.Any(p => p.hasColors), skinned = true };
            for (int i = 0; i < 8; i++) result.uvDimensions[i] = parts.Max(p => p.uvDimensions[i]);
            var bones = new List<Transform>();
            var materials = new List<Material>();
            int total = parts.Sum(p => p.Count);
            foreach (string name in parts.SelectMany(p => p.shapeNames).Distinct())
            {
                result.shapeNames.Add(name);
                result.shapes[name] = frameWeights[name].Select(w => new Frame { weight = w, vertices = new Vector3[total], normals = new Vector3[total], tangents = new Vector3[total] }).ToList();
            }
            for (int m = 0; m < members.Count; m++)
            {
                Parts p = parts[m];
                int offset = result.Count;
                Transform[] memberBones = members[m].bones;
                var remap = new int[memberBones.Length];
                for (int b = 0; b < memberBones.Length; b++)
                {
                    int index = -1;
                    for (int k = 0; k < bones.Count; k++)
                        if (bones[k] == memberBones[b] && result.bindposes[k] == p.bindposes[b]) { index = k; break; }
                    if (index < 0)
                    {
                        index = bones.Count;
                        bones.Add(memberBones[b]);
                        result.bindposes.Add(p.bindposes[b]);
                    }
                    remap[b] = index;
                }
                result.positions.AddRange(p.positions);
                result.normals.AddRange(p.normals);
                result.tangents.AddRange(p.tangents);
                result.colors.AddRange(p.colors);
                for (int i = 0; i < 8; i++) result.uvs[i].AddRange(p.uvs[i]);
                result.bonesPerVertex.AddRange(p.bonesPerVertex);
                foreach (BoneWeight1 weight in p.weights)
                    result.weights.Add(new BoneWeight1 { boneIndex = remap[weight.boneIndex], weight = weight.weight });
                Material[] memberMaterials = members[m].sharedMaterials;
                for (int s = 0; s < p.submeshes.Count; s++)
                {
                    int slot = materials.IndexOf(memberMaterials[s]);
                    if (slot < 0) { slot = materials.Count; materials.Add(memberMaterials[s]); result.submeshes.Add(new List<int>()); }
                    result.submeshes[slot].AddRange(p.submeshes[s].Select(index => index + offset));
                }
                foreach (string name in p.shapeNames)
                    for (int f = 0; f < p.shapes[name].Count; f++)
                    {
                        Frame source = p.shapes[name][f], destination = result.shapes[name][f];
                        Array.Copy(source.vertices, 0, destination.vertices, offset, p.Count);
                        Array.Copy(source.normals, 0, destination.normals, offset, p.Count);
                        Array.Copy(source.tangents, 0, destination.tangents, offset, p.Count);
                    }
            }

            // Renderer bounds live in root-bone space; gather all members' world bounds there.
            Transform space = target.rootBone != null ? target.rootBone : target.transform;
            Bounds bounds = target.localBounds;
            foreach (SkinnedMeshRenderer member in members.Skip(1))
            {
                Bounds world = member.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = world.center + Vector3.Scale(world.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    bounds.Encapsulate(space.InverseTransformPoint(point));
                }
            }

            Mesh mesh = save(Write(result, target.sharedMesh.name + "_merged"));
            string targetPath = AnimationUtility.CalculateTransformPath(target.transform, root);
            foreach (SkinnedMeshRenderer member in members.Skip(1))
            {
                string path = AnimationUtility.CalculateTransformPath(member.transform, root);
                if (bindings.TryGetValue(path, out var list))
                    foreach (var entry in list)
                    {
                        AnimationCurve curve = AnimationUtility.GetEditorCurve(entry.Key, entry.Value);
                        AnimationUtility.SetEditorCurve(entry.Key, entry.Value, null);
                        EditorCurveBinding moved = entry.Value;
                        moved.path = targetPath;
                        AnimationUtility.SetEditorCurve(entry.Key, moved, curve);
                    }
                Object.DestroyImmediate(member);
            }
            target.sharedMesh = mesh;
            target.bones = bones.ToArray();
            target.sharedMaterials = materials.ToArray();
            target.localBounds = bounds;
            for (int s = 0; s < mesh.blendShapeCount; s++) target.SetBlendShapeWeight(s, shapeWeights[mesh.GetBlendShapeName(s)]);
            slotsAfter = materials.Count;
            return true;
        }

        // Several slots of one renderer with the same material become one submesh.
        internal static int MergeDuplicateSlots(GameObject avatar, HashSet<Renderer> locked, Func<Mesh, Mesh> save)
        {
            int removed = 0;
            foreach (Renderer renderer in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer) || locked.Contains(renderer)) continue;
                Mesh mesh = MeshOf(renderer);
                Material[] materials = renderer.sharedMaterials;
                if (!Editable(mesh) || materials.Length != mesh.subMeshCount || materials.Any(m => m == null)) continue;
                Material[] distinct = materials.Distinct().ToArray();
                if (distinct.Length == materials.Length) continue;
                Parts parts = Read(mesh);
                var submeshes = distinct.Select(m => new List<int>()).ToList();
                for (int s = 0; s < materials.Length; s++) submeshes[Array.IndexOf(distinct, materials[s])].AddRange(parts.submeshes[s]);
                parts.submeshes.Clear();
                parts.submeshes.AddRange(submeshes);
                var weights = BlendWeights(renderer as SkinnedMeshRenderer);
                Assign(renderer, save(Write(parts, mesh.name)));
                renderer.sharedMaterials = distinct;
                RestoreBlendWeights(renderer as SkinnedMeshRenderer, weights);
                removed += materials.Length - distinct.Length;
            }
            return removed;
        }

        private static Dictionary<string, float> BlendWeights(SkinnedMeshRenderer renderer)
        {
            var weights = new Dictionary<string, float>();
            if (renderer == null || renderer.sharedMesh == null) return weights;
            for (int s = 0; s < renderer.sharedMesh.blendShapeCount; s++) weights[renderer.sharedMesh.GetBlendShapeName(s)] = renderer.GetBlendShapeWeight(s);
            return weights;
        }

        private static void RestoreBlendWeights(SkinnedMeshRenderer renderer, Dictionary<string, float> weights)
        {
            if (renderer == null || renderer.sharedMesh == null) return;
            for (int s = 0; s < renderer.sharedMesh.blendShapeCount; s++)
                if (weights.TryGetValue(renderer.sharedMesh.GetBlendShapeName(s), out float weight)) renderer.SetBlendShapeWeight(s, weight);
        }

        // Shapes nobody animates, names or holds at a non-zero weight are dropped from the copy.
        internal static int RemoveBlendShapes(GameObject avatar, HashSet<string> keep, Func<Mesh, Mesh> save)
        {
            int removed = 0;
            foreach (var users in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => Editable(r.sharedMesh)).GroupBy(r => r.sharedMesh).ToList())
            {
                Mesh mesh = users.Key;
                var drop = new HashSet<string>();
                for (int s = 0; s < mesh.blendShapeCount; s++)
                {
                    string name = mesh.GetBlendShapeName(s);
                    if (!keep.Contains(name) && users.All(r => r.GetBlendShapeWeight(s) == 0)) drop.Add(name);
                }
                if (drop.Count == 0) continue;
                Parts parts = Read(mesh);
                parts.shapeNames.RemoveAll(drop.Contains);
                var weights = users.ToDictionary(r => r, BlendWeights);
                Mesh result = save(Write(parts, mesh.name));
                foreach (SkinnedMeshRenderer renderer in users)
                {
                    renderer.sharedMesh = result;
                    RestoreBlendWeights(renderer, weights[renderer]);
                }
                removed += drop.Count;
            }
            return removed;
        }

        // ---- Simplification: quadric-error half-edge collapse. A vertex only moves onto an existing
        // neighbour, so skin weights, UVs and blend shape deltas stay exact. Index-space boundaries
        // (mesh borders and UV/normal seams) are locked so no cracks open.
        // protectedBones: indices into the renderer's bone list; vertices mostly weighted to them stay.
        internal static Mesh Simplify(Mesh source, float ratio, bool lockBlendShapes, ICollection<int> protectedBones = null)
        {
            // Source-avatar meshes are often imported without Read/Write: work on an in-memory copy.
            if (source != null && !source.isReadable)
            {
                Mesh copy = ReadableCopy(source);
                try
                {
                    if (!copy.isReadable) throw new InvalidOperationException("Включите Read/Write в настройках импорта модели «" + source.name + "».");
                    return Simplify(copy, ratio, lockBlendShapes, protectedBones);
                }
                finally { Object.DestroyImmediate(copy); }
            }
            if (!Editable(source)) throw new InvalidOperationException("Меш недоступен для чтения или содержит не только треугольники.");
            Parts parts = Read(source);
            int vertexCount = parts.Count;
            var triangles = new List<int>();
            var triangleSubmesh = new List<int>();
            for (int s = 0; s < parts.submeshes.Count; s++)
                for (int i = 0; i + 2 < parts.submeshes[s].Count; i += 3)
                {
                    triangles.Add(parts.submeshes[s][i]); triangles.Add(parts.submeshes[s][i + 1]); triangles.Add(parts.submeshes[s][i + 2]);
                    triangleSubmesh.Add(s);
                }
            int triangleCount = triangleSubmesh.Count;
            int goal = Math.Max(1, (int)(triangleCount * Mathf.Clamp01(ratio)));
            var alive = Enumerable.Repeat(true, triangleCount).ToArray();
            var vertexTriangles = Enumerable.Range(0, vertexCount).Select(_ => new List<int>()).ToArray();
            for (int t = 0; t < triangleCount; t++)
                for (int k = 0; k < 3; k++) vertexTriangles[triangles[t * 3 + k]].Add(t);

            var locked = new bool[vertexCount];
            var edges = new Dictionary<long, int>();
            for (int t = 0; t < triangleCount; t++)
                for (int k = 0; k < 3; k++)
                {
                    long key = EdgeKey(triangles[t * 3 + k], triangles[t * 3 + (k + 1) % 3]);
                    edges[key] = edges.TryGetValue(key, out int count) ? count + 1 : 1;
                }
            foreach (var edge in edges)
                if (edge.Value != 2) { locked[(int)(edge.Key >> 32)] = true; locked[(int)(edge.Key & 0xffffffff)] = true; }
            if (protectedBones != null && protectedBones.Count > 0 && parts.skinned)
                for (int v = 0, w = 0; v < vertexCount; w += parts.bonesPerVertex[v], v++)
                {
                    int dominant = -1;
                    float best = 0;
                    for (int k = w; k < w + parts.bonesPerVertex[v]; k++)
                        if (parts.weights[k].weight > best) { best = parts.weights[k].weight; dominant = parts.weights[k].boneIndex; }
                    if (protectedBones.Contains(dominant)) locked[v] = true;
                }
            // Bodies often have shape keys on every vertex, so locking shaped vertices would freeze the whole
            // mesh. Instead a collapse is allowed only between vertices that every shape moves alike.
            Vector3[][] deltas = lockBlendShapes ? parts.shapes.Values.SelectMany(frames => frames).Select(frame => frame.vertices).ToArray() : null;

            var quadrics = new double[vertexCount * 10];
            for (int t = 0; t < triangleCount; t++)
            {
                Vector3 a = parts.positions[triangles[t * 3]], b = parts.positions[triangles[t * 3 + 1]], c = parts.positions[triangles[t * 3 + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                double area = normal.magnitude;
                if (area < 1e-20) continue;
                normal /= (float)area;
                double d = -Vector3.Dot(normal, a);
                double[] plane = { normal.x, normal.y, normal.z, d };
                for (int k = 0; k < 3; k++) AddQuadric(quadrics, triangles[t * 3 + k], plane, area);
            }

            var version = new int[vertexCount];
            var dead = new bool[vertexCount];
            var heap = new Heap();
            for (int v = 0; v < vertexCount; v++)
                foreach (int w in Neighbours(v, triangles, vertexTriangles, alive)) Push(heap, v, w, parts, quadrics, locked, version);
            int remaining = triangleCount;
            while (remaining > goal && heap.Count > 0)
            {
                Heap.Entry entry = heap.Pop();
                int u = entry.u, v = entry.v;
                if (dead[u] || dead[v] || version[u] != entry.versionU || version[v] != entry.versionV) continue;
                if (deltas != null && !SameShapes(deltas, parts.positions, u, v)) continue;
                if (!CanCollapse(u, v, triangles, vertexTriangles, alive, parts.positions)) continue;
                foreach (int t in vertexTriangles[u])
                {
                    if (!alive[t]) continue;
                    bool hasV = false;
                    for (int k = 0; k < 3; k++) hasV |= triangles[t * 3 + k] == v;
                    if (hasV) { alive[t] = false; remaining--; continue; }
                    for (int k = 0; k < 3; k++) if (triangles[t * 3 + k] == u) triangles[t * 3 + k] = v;
                    vertexTriangles[v].Add(t);
                }
                vertexTriangles[u].Clear();
                dead[u] = true;
                for (int q = 0; q < 10; q++) quadrics[v * 10 + q] += quadrics[u * 10 + q];
                version[v]++;
                vertexTriangles[v].RemoveAll(t => !alive[t]);
                foreach (int w in Neighbours(v, triangles, vertexTriangles, alive))
                {
                    Push(heap, v, w, parts, quadrics, locked, version);
                    Push(heap, w, v, parts, quadrics, locked, version);
                }
            }

            // Compact: keep only vertices still referenced, with every attribute and shape.
            var map = Enumerable.Repeat(-1, vertexCount).ToArray();
            var order = new List<int>();
            var submeshes = parts.submeshes.Select(_ => new List<int>()).ToList();
            for (int t = 0; t < triangleCount; t++)
            {
                if (!alive[t]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int old = triangles[t * 3 + k];
                    if (map[old] < 0) { map[old] = order.Count; order.Add(old); }
                    submeshes[triangleSubmesh[t]].Add(map[old]);
                }
            }
            var weightStart = new int[vertexCount + 1];
            for (int v = 0; v < vertexCount; v++) weightStart[v + 1] = weightStart[v] + parts.bonesPerVertex[v];
            var result = new Parts { hasNormals = parts.hasNormals, hasTangents = parts.hasTangents, hasColors = parts.hasColors, skinned = parts.skinned };
            Array.Copy(parts.uvDimensions, result.uvDimensions, 8);
            result.bindposes.AddRange(parts.bindposes);
            result.submeshes.AddRange(submeshes);
            foreach (int old in order)
            {
                result.positions.Add(parts.positions[old]);
                result.normals.Add(parts.normals[old]);
                result.tangents.Add(parts.tangents[old]);
                result.colors.Add(parts.colors[old]);
                for (int i = 0; i < 8; i++) result.uvs[i].Add(parts.uvs[i][old]);
                result.bonesPerVertex.Add(parts.bonesPerVertex[old]);
                if (parts.skinned) for (int w = weightStart[old]; w < weightStart[old + 1]; w++) result.weights.Add(parts.weights[w]);
            }
            foreach (string name in parts.shapeNames)
            {
                result.shapeNames.Add(name);
                result.shapes[name] = parts.shapes[name].Select(frame => new Frame
                {
                    weight = frame.weight,
                    vertices = order.Select(old => frame.vertices[old]).ToArray(),
                    normals = order.Select(old => frame.normals[old]).ToArray(),
                    tangents = order.Select(old => frame.tangents[old]).ToArray()
                }).ToList();
            }
            return Write(result, source.name + "_" + Mathf.RoundToInt(ratio * 100) + "pct");
        }

        internal static int TriangleCount(Mesh mesh)
        {
            long count = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
                if (mesh.GetTopology(s) == MeshTopology.Triangles) count += mesh.GetIndexCount(s) / 3;
            return (int)count;
        }

        private static long EdgeKey(int a, int b)
        {
            return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        }

        private static void AddQuadric(double[] q, int vertex, double[] p, double weight)
        {
            int o = vertex * 10;
            q[o] += weight * p[0] * p[0]; q[o + 1] += weight * p[0] * p[1]; q[o + 2] += weight * p[0] * p[2]; q[o + 3] += weight * p[0] * p[3];
            q[o + 4] += weight * p[1] * p[1]; q[o + 5] += weight * p[1] * p[2]; q[o + 6] += weight * p[1] * p[3];
            q[o + 7] += weight * p[2] * p[2]; q[o + 8] += weight * p[2] * p[3]; q[o + 9] += weight * p[3] * p[3];
        }

        private static double Cost(double[] q, int a, int b, Vector3 v)
        {
            int i = a * 10, j = b * 10;
            double x = v.x, y = v.y, z = v.z;
            double q0 = q[i] + q[j], q1 = q[i + 1] + q[j + 1], q2 = q[i + 2] + q[j + 2], q3 = q[i + 3] + q[j + 3], q4 = q[i + 4] + q[j + 4];
            double q5 = q[i + 5] + q[j + 5], q6 = q[i + 6] + q[j + 6], q7 = q[i + 7] + q[j + 7], q8 = q[i + 8] + q[j + 8], q9 = q[i + 9] + q[j + 9];
            return q0 * x * x + 2 * q1 * x * y + 2 * q2 * x * z + 2 * q3 * x + q4 * y * y + 2 * q5 * y * z + 2 * q6 * y + q7 * z * z + 2 * q8 * z + q9;
        }

        private static void Push(Heap heap, int u, int v, Parts parts, double[] quadrics, bool[] locked, int[] version)
        {
            if (locked[u]) return;
            heap.Push(new Heap.Entry { cost = Cost(quadrics, u, v, parts.positions[v]), u = u, v = v, versionU = version[u], versionV = version[v] });
        }

        private static HashSet<int> Neighbours(int v, List<int> triangles, List<int>[] vertexTriangles, bool[] alive)
        {
            var result = new HashSet<int>();
            foreach (int t in vertexTriangles[v])
                if (alive[t])
                    for (int k = 0; k < 3; k++) if (triangles[t * 3 + k] != v) result.Add(triangles[t * 3 + k]);
            return result;
        }

        // Each shape may move the two vertices apart by at most half their distance (0.25 kept only
        // ~17% on a real 120K body with 181 shape keys; 0.5 gives ~38% while expressions stay intact).
        private static bool SameShapes(Vector3[][] deltas, List<Vector3> positions, int u, int v)
        {
            float tolerance = 0.5f * Vector3.Distance(positions[u], positions[v]) + 1e-5f;
            tolerance *= tolerance;
            foreach (Vector3[] delta in deltas)
                if ((delta[u] - delta[v]).sqrMagnitude > tolerance) return false;
            return true;
        }

        // Reject collapses that fold a triangle over or pinch the surface into a non-manifold edge.
        private static bool CanCollapse(int u, int v, List<int> triangles, List<int>[] vertexTriangles, bool[] alive, List<Vector3> positions)
        {
            var shared = Neighbours(u, triangles, vertexTriangles, alive);
            shared.IntersectWith(Neighbours(v, triangles, vertexTriangles, alive));
            if (shared.Count > 2) return false;
            foreach (int t in vertexTriangles[u])
            {
                if (!alive[t]) continue;
                int a = triangles[t * 3], b = triangles[t * 3 + 1], c = triangles[t * 3 + 2];
                if (a == v || b == v || c == v) continue;
                Vector3 before = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                Vector3 pa = positions[a == u ? v : a], pb = positions[b == u ? v : b], pc = positions[c == u ? v : c];
                Vector3 after = Vector3.Cross(pb - pa, pc - pa);
                if (after.sqrMagnitude < 1e-24f || Vector3.Dot(before.normalized, after.normalized) < 0.2f) return false;
            }
            return true;
        }

        private sealed class Heap
        {
            internal struct Entry { public double cost; public int u, v, versionU, versionV; }
            private readonly List<Entry> items = new List<Entry>();
            public int Count { get { return items.Count; } }

            public void Push(Entry entry)
            {
                items.Add(entry);
                for (int i = items.Count - 1; i > 0;)
                {
                    int parent = (i - 1) / 2;
                    if (items[parent].cost <= items[i].cost) break;
                    Entry swap = items[parent]; items[parent] = items[i]; items[i] = swap;
                    i = parent;
                }
            }

            public Entry Pop()
            {
                Entry top = items[0];
                items[0] = items[items.Count - 1];
                items.RemoveAt(items.Count - 1);
                for (int i = 0; ;)
                {
                    int left = i * 2 + 1, right = left + 1, smallest = i;
                    if (left < items.Count && items[left].cost < items[smallest].cost) smallest = left;
                    if (right < items.Count && items[right].cost < items[smallest].cost) smallest = right;
                    if (smallest == i) break;
                    Entry swap = items[smallest]; items[smallest] = items[i]; items[i] = swap;
                    i = smallest;
                }
                return top;
            }
        }
    }
}

namespace BSLK.Optimizer
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEngine;
    using UnityEngine.Rendering;
    using Object = UnityEngine.Object;

    // Removes body triangles that no viewer can see because always-visible clothing covers them.
    // Works on the club copy only: new meshes are created and saved, the source meshes are never written.
    internal static class BSLKClubOcclusion
    {
        // World-space triangles of one renderer in the current pose.
        private sealed class Geometry
        {
            public Renderer renderer;
            public Mesh readable;          // the mesh itself or a readable copy we own
            public bool ownsReadable;
            public Vector3[] positions;    // world space
            public int[] triangles;        // all submeshes flattened, 3 per triangle
            public int[] triangleSubmesh;
            public int[] welded;           // vertex -> position id (UV/normal seams split vertices)
            public bool[] moved;           // per welded id: some blend shape moves it
            public bool candidate, toggleable;
            public int TriangleCount { get { return triangles.Length / 3; } }
        }

        // Direction table in a local frame where +z is the outward normal: the normal itself and two rings.
        // 70° from the normal still finds gaps at clothing edges; flatter rays only add grazing noise.
        private static readonly Vector3[] Directions = MakeDirections();

        private static Vector3[] MakeDirections()
        {
            var result = new List<Vector3> { Vector3.forward };
            float[] angles = { 35f, 70f };
            for (int ring = 0; ring < angles.Length; ring++)
                for (int i = 0; i < 8; i++)
                {
                    float polar = angles[ring] * Mathf.Deg2Rad, azimuth = (i + ring * 0.5f) * Mathf.PI / 4f;
                    result.Add(new Vector3(Mathf.Sin(polar) * Mathf.Cos(azimuth), Mathf.Sin(polar) * Mathf.Sin(azimuth), Mathf.Cos(polar)));
                }
            return result.ToArray();
        }

        // liveShapes: blend shapes that can change at runtime (animated, named by components, non-zero
        // weight). Unused shapes cannot uncover anything; null treats every shape as live.
        internal static int RemoveHiddenTriangles(GameObject avatar, HashSet<Renderer> locked, Func<Renderer, bool> occluderAllowed,
            Func<Mesh, Mesh> save, List<string> notes, ISet<string> liveShapes = null)
        {
            var geometries = new List<Geometry>();
            try
            {
                Renderer[] renderers = avatar.GetComponentsInChildren<Renderer>(false)
                    .Where(r => r.enabled && r.gameObject.activeInHierarchy && (r is SkinnedMeshRenderer || r is MeshRenderer)).ToArray();
                if (renderers.Length < 2)
                {
                    notes.Add("Скрытые под одеждой треугольники: нечего удалять (нет подходящих мешей тела и одежды).");
                    return 0;
                }
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
                for (int i = 0; i < renderers.Length; i++)
                {
                    BSLKClubOptimizer.Progress("Скрытые треугольники: поза " + renderers[i].name, 0.1f * i / renderers.Length);
                    Renderer renderer = renderers[i];
                    bool candidate = renderer is SkinnedMeshRenderer && !locked.Contains(renderer);
                    bool allowed = occluderAllowed(renderer);
                    Geometry g = Capture(renderer, bounds.center, liveShapes);
                    if (g == null) continue;
                    g.candidate = candidate;
                    // Toggleable renderers stay in the structure only to estimate what they would save.
                    g.toggleable = !allowed;
                    geometries.Add(g);
                }
                List<Geometry> candidates = geometries.Where(g => g.candidate).ToList();
                if (candidates.Count == 0 || geometries.Count < 2)
                {
                    notes.Add("Скрытые под одеждой треугольники: нечего удалять (нет подходящих мешей тела и одежды).");
                    return 0;
                }

                float height = Mathf.Max(bounds.size.y, 0.1f);
                // Covering clothing is close to the skin: farther hits (another limb, a skirt hem) move with animation.
                float reach = Mathf.Clamp(height * 0.1f, 0.03f, 0.5f), offset = height * 1e-4f;

                var bvh = new Bvh(geometries);
                bool[] noCover = bvh.Owners.Select(o => geometries[o].toggleable).ToArray();
                var tester = new Tester(bvh, reach, offset);

                // Pass 1: only always-visible covers.
                var hidden = new Dictionary<Geometry, bool[]>();
                for (int c = 0; c < candidates.Count; c++)
                    hidden[candidates[c]] = tester.Hidden(candidates[c], geometries.IndexOf(candidates[c]), noCover, null, 0.1f + 0.5f * c / candidates.Count);
                // Pass 2: a cover may itself be a candidate that loses triangles. Recheck pass-1 results while
                // ignoring every triangle pass 1 could remove, so each final result relies only on geometry that stays.
                if (candidates.Count > 1)
                {
                    bool[] ignore = (bool[])noCover.Clone();
                    for (int t = 0; t < ignore.Length; t++)
                    {
                        Geometry owner = geometries[bvh.Owners[t]];
                        if (owner.candidate && hidden[owner][bvh.Local[t]]) ignore[t] = true;
                    }
                    for (int c = 0; c < candidates.Count; c++)
                        hidden[candidates[c]] = tester.Hidden(candidates[c], geometries.IndexOf(candidates[c]), ignore, hidden[candidates[c]], 0.6f + 0.15f * c / candidates.Count);
                }

                int removed = 0, keptByShapes = 0, potential = 0;
                var changed = new List<string>();
                for (int c = 0; c < candidates.Count; c++)
                {
                    Geometry g = candidates[c];
                    bool[] cut = Dilate(g, hidden[g], out int shapes);
                    keptByShapes += shapes;
                    // Estimate only: what toggleable clothing hides as well (the full test, without the safety ring).
                    if (noCover.Any(x => x))
                    {
                        bool[] withToggles = tester.Hidden(g, geometries.IndexOf(g), null, null, 0.75f + 0.2f * c / candidates.Count, hidden[g]);
                        for (int t = 0; t < withToggles.Length; t++)
                            if (withToggles[t] && !hidden[g][t] && !TouchesMoved(g, t)) potential++;
                    }
                    int count = cut.Count(x => x);
                    if (count == 0) continue;
                    Apply(g, cut, save);
                    changed.Add(g.renderer.name + " " + g.TriangleCount + " → " + (g.TriangleCount - count));
                    removed += count;
                }
                notes.Add("Удалено треугольников тела, полностью закрытых одеждой: " + removed +
                    (changed.Count > 0 ? " (" + string.Join(", ", changed) + ")" : "") +
                    ". Оставлены треугольники с шейпкеями: " + keptByShapes +
                    "; под переключаемой одеждой можно было бы убрать ещё " + potential +
                    ", но они оставлены, чтобы при её выключении не было дыр.");
                return removed;
            }
            finally
            {
                foreach (Geometry g in geometries)
                    if (g.ownsReadable && g.readable != null) Object.DestroyImmediate(g.readable);
            }
        }

        // Positions are kept relative to the avatar centre: an avatar placed far from the origin
        // must not lose float precision in ray tests.
        private static Geometry Capture(Renderer renderer, Vector3 centre, ISet<string> liveShapes)
        {
            Mesh mesh = BSLKClubGeometry.MeshOf(renderer);
            if (mesh == null) return null;
            for (int s = 0; s < mesh.subMeshCount; s++)
                if (mesh.GetTopology(s) != MeshTopology.Triangles) return null;
            var g = new Geometry { renderer = renderer, readable = mesh };
            if (!mesh.isReadable) { g.readable = BSLKClubGeometry.ReadableCopy(mesh); g.ownsReadable = true; }
            g.positions = WorldVertices(renderer, g.readable);
            if (g.positions == null) return null;
            for (int v = 0; v < g.positions.Length; v++) g.positions[v] -= centre;

            var triangles = new List<int>();
            var submeshOf = new List<int>();
            for (int s = 0; s < g.readable.subMeshCount; s++)
            {
                int[] indices = g.readable.GetIndices(s);
                triangles.AddRange(indices);
                for (int t = 0; t < indices.Length / 3; t++) submeshOf.Add(s);
            }
            g.triangles = triangles.ToArray();
            g.triangleSubmesh = submeshOf.ToArray();

            Vector3[] local = g.readable.vertices;
            var ids = new Dictionary<Vector3, int>();
            g.welded = new int[local.Length];
            for (int v = 0; v < local.Length; v++)
            {
                if (!ids.TryGetValue(local[v], out int id)) ids[local[v]] = id = ids.Count;
                g.welded[v] = id;
            }
            g.moved = new bool[ids.Count];
            if (g.readable.blendShapeCount > 0)
            {
                var delta = new Vector3[local.Length];
                var normals = new Vector3[local.Length];
                var tangents = new Vector3[local.Length];
                var skinned = renderer as SkinnedMeshRenderer;
                for (int s = 0; s < g.readable.blendShapeCount; s++)
                {
                    if (liveShapes != null && !liveShapes.Contains(g.readable.GetBlendShapeName(s)) && (skinned == null || skinned.GetBlendShapeWeight(s) == 0)) continue;
                    for (int f = 0; f < g.readable.GetBlendShapeFrameCount(s); f++)
                    {
                        g.readable.GetBlendShapeFrameVertices(s, f, delta, normals, tangents);
                        // Shrink keys and expressions move vertices out from under the clothing: never cut them.
                        for (int v = 0; v < delta.Length; v++)
                            if (delta[v].sqrMagnitude > 1e-12f) g.moved[g.welded[v]] = true;
                    }
                }
            }
            return g;
        }

        // Current pose in world space. BakeMesh(useScale: true) returns the posed mesh in the renderer's local
        // space (verified against manual skinning), so the full localToWorldMatrix applies, including scale.
        internal static Vector3[] WorldVertices(Renderer renderer, Mesh readable)
        {
            Transform t = renderer.transform;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                Mesh original = skinned.sharedMesh;
                var weights = new float[original.blendShapeCount];
                for (int s = 0; s < weights.Length; s++) weights[s] = skinned.GetBlendShapeWeight(s);
                var baked = new Mesh();
                try
                {
                    if (original != readable) skinned.sharedMesh = readable;
                    skinned.BakeMesh(baked, true);
                }
                finally
                {
                    if (skinned.sharedMesh != original) skinned.sharedMesh = original;
                    for (int s = 0; s < weights.Length; s++) skinned.SetBlendShapeWeight(s, weights[s]);
                }
                Vector3[] vertices = baked.vertices;
                Object.DestroyImmediate(baked);
                if (vertices.Length != readable.vertexCount) return null;
                Matrix4x4 m = t.localToWorldMatrix;
                for (int v = 0; v < vertices.Length; v++) vertices[v] = m.MultiplyPoint3x4(vertices[v]);
                return vertices;
            }
            Vector3[] result = readable.vertices;
            Matrix4x4 world = t.localToWorldMatrix;
            for (int v = 0; v < result.Length; v++) result[v] = world.MultiplyPoint3x4(result[v]);
            return result;
        }

        // Vector3.normalized returns zero below 1e-5: cross products of millimetre-sized triangles are smaller.
        private static Vector3 Unit(Vector3 v)
        {
            float length = v.magnitude;
            return length > 0f ? v / length : Vector3.zero;
        }

        private static bool TouchesMoved(Geometry g, int triangle)
        {
            return g.moved[g.welded[g.triangles[triangle * 3]]] || g.moved[g.welded[g.triangles[triangle * 3 + 1]]] || g.moved[g.welded[g.triangles[triangle * 3 + 2]]];
        }

        // Safety ring: a hidden triangle sharing a position with any kept triangle stays, so sampling misses
        // and small pose changes at clothing edges do not open cracks.
        private static bool[] Dilate(Geometry g, bool[] hidden, out int keptByShapes)
        {
            keptByShapes = 0;
            int count = g.TriangleCount;
            var keep = new bool[count];
            var nearKept = new bool[g.moved.Length];
            for (int t = 0; t < count; t++)
            {
                keep[t] = !hidden[t] || TouchesMoved(g, t);
                if (hidden[t] && keep[t]) keptByShapes++;
                if (keep[t])
                    for (int k = 0; k < 3; k++) nearKept[g.welded[g.triangles[t * 3 + k]]] = true;
            }
            var cut = new bool[count];
            for (int t = 0; t < count; t++)
                cut[t] = !keep[t] && !nearKept[g.welded[g.triangles[t * 3]]] && !nearKept[g.welded[g.triangles[t * 3 + 1]]] && !nearKept[g.welded[g.triangles[t * 3 + 2]]];
            return cut;
        }

        // Only index buffers change: vertices, weights, bind poses, blend shapes and slots stay as they are.
        private static void Apply(Geometry g, bool[] cut, Func<Mesh, Mesh> save)
        {
            var skinned = (SkinnedMeshRenderer)g.renderer;
            Mesh original = skinned.sharedMesh;
            Mesh result = g.ownsReadable ? g.readable : BSLKClubGeometry.ReadableCopy(original);
            if (g.ownsReadable) g.readable = null;
            result.name = original.name;
            var submeshes = Enumerable.Range(0, result.subMeshCount).Select(_ => new List<int>()).ToList();
            for (int t = 0; t < cut.Length; t++)
                if (!cut[t])
                    for (int k = 0; k < 3; k++) submeshes[g.triangleSubmesh[t]].Add(g.triangles[t * 3 + k]);
            Bounds bounds = result.bounds;
            for (int s = 0; s < submeshes.Count; s++) result.SetIndices(submeshes[s], MeshTopology.Triangles, s, false);
            result.bounds = bounds;
            var weights = new float[original.blendShapeCount];
            for (int s = 0; s < weights.Length; s++) weights[s] = skinned.GetBlendShapeWeight(s);
            skinned.sharedMesh = save(result);
            for (int s = 0; s < weights.Length && s < result.blendShapeCount; s++) skinned.SetBlendShapeWeight(s, weights[s]);
        }

        // Visibility sampling for one candidate renderer.
        private sealed class Tester
        {
            private readonly Bvh bvh;
            private readonly float reach, offset;

            public Tester(Bvh bvh, float reach, float offset)
            {
                this.bvh = bvh;
                this.reach = reach;
                this.offset = offset;
            }

            // only == null: test every triangle; otherwise only triangles marked there. skip: triangles already known hidden.
            public bool[] Hidden(Geometry g, int owner, bool[] ignore, bool[] only, float progress, bool[] skip = null)
            {
                int count = g.TriangleCount;
                Vector3[] p = g.positions;
                int[] tri = g.triangles;
                // Smooth per-position normals so every copy of a seam vertex is sampled the same way.
                var normals = new Vector3[g.moved.Length];
                var faces = new Vector3[count];
                for (int t = 0; t < count; t++)
                {
                    Vector3 a = p[tri[t * 3]], b = p[tri[t * 3 + 1]], c = p[tri[t * 3 + 2]];
                    Vector3 n = Vector3.Cross(b - a, c - a);
                    faces[t] = n;
                    for (int k = 0; k < 3; k++) normals[g.welded[tri[t * 3 + k]]] += n;
                }
                var state = new sbyte[g.moved.Length]; // 0 unknown, 1 hidden, -1 visible
                var result = new bool[count];
                for (int t = 0; t < count; t++)
                {
                    if ((t & 2047) == 0) BSLKClubOptimizer.Progress("Скрытые треугольники: " + g.renderer.name, progress);
                    if (skip != null && skip[t]) { result[t] = true; continue; }
                    if (only != null && !only[t]) continue;
                    Vector3 face = faces[t];
                    float area = face.magnitude;
                    if (area < 1e-12f) continue;
                    face /= area;
                    bool hiddenAll = true;
                    for (int k = 0; k < 3 && hiddenAll; k++)
                    {
                        int v = tri[t * 3 + k], id = g.welded[v];
                        if (state[id] == 0) state[id] = (sbyte)(PointHidden(p[v], Unit(normals[id]), owner, ignore) ? 1 : -1);
                        hiddenAll = state[id] > 0;
                    }
                    if (!hiddenAll) continue;
                    Vector3 v0 = p[tri[t * 3]], v1 = p[tri[t * 3 + 1]], v2 = p[tri[t * 3 + 2]], centre = (v0 + v1 + v2) / 3f;
                    hiddenAll = PointHidden(centre, face, owner, ignore) && PointHidden((v0 + centre) * 0.5f, face, owner, ignore) &&
                        PointHidden((v1 + centre) * 0.5f, face, owner, ignore) && PointHidden((v2 + centre) * 0.5f, face, owner, ignore);
                    result[t] = hiddenAll;
                }
                return result;
            }

            private bool PointHidden(Vector3 point, Vector3 normal, int owner, bool[] ignore)
            {
                if (normal.sqrMagnitude < 0.5f) return false;
                Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 bitangent = Vector3.Cross(normal, tangent);
                Vector3 origin = point + normal * offset;
                foreach (Vector3 d in Directions)
                {
                    Vector3 direction = tangent * d.x + bitangent * d.y + normal * d.z;
                    if (!bvh.AnyHit(origin, direction, reach, owner, ignore)) return false;
                }
                return true;
            }
        }

        // Bounding volume hierarchy over all cover triangles with a filtered any-hit query.
        private sealed class Bvh
        {
            private const int LeafSize = 4;
            private readonly float[] tri;      // v0, edge1, edge2, normal: 12 floats per triangle
            private readonly bool[] twoSided;
            public readonly int[] Owners, Local;
            private readonly int[] order;
            private readonly float[] boxes;  // min xyz, max xyz per node
            private readonly int[] first, count, left, right;
            private readonly int[] stack = new int[256];

            public Bvh(List<Geometry> geometries)
            {
                var data = new List<float>();
                var sided = new List<bool>();
                var owners = new List<int>();
                var local = new List<int>();
                for (int o = 0; o < geometries.Count; o++)
                {
                    Geometry g = geometries[o];
                    Material[] materials = g.renderer.sharedMaterials;
                    for (int t = 0; t < g.TriangleCount; t++)
                    {
                        int s = g.triangleSubmesh[t];
                        // Missing slots do not draw; see-through materials do not hide what is behind them.
                        Material material = s < materials.Length ? materials[s] : null;
                        if (material == null || SeeThrough(material) || TouchesMoved(g, t)) continue;
                        Vector3 a = g.positions[g.triangles[t * 3]], b = g.positions[g.triangles[t * 3 + 1]], c = g.positions[g.triangles[t * 3 + 2]];
                        Vector3 e1 = b - a, e2 = c - a, n = Vector3.Cross(e1, e2);
                        if (n.sqrMagnitude < 1e-20f) continue;
                        n = Unit(n);
                        data.Add(a.x); data.Add(a.y); data.Add(a.z);
                        data.Add(e1.x); data.Add(e1.y); data.Add(e1.z);
                        data.Add(e2.x); data.Add(e2.y); data.Add(e2.z);
                        data.Add(n.x); data.Add(n.y); data.Add(n.z);
                        sided.Add(material.HasProperty("_Cull") && material.GetFloat("_Cull") == 0f);
                        owners.Add(o);
                        local.Add(t);
                    }
                }
                tri = data.ToArray();
                twoSided = sided.ToArray();
                Owners = owners.ToArray();
                Local = local.ToArray();
                order = Enumerable.Range(0, Owners.Length).ToArray();
                var centres = new float[Owners.Length * 3];
                for (int t = 0; t < Owners.Length; t++)
                    for (int k = 0; k < 3; k++)
                        centres[t * 3 + k] = tri[t * 12 + k] + (tri[t * 12 + 3 + k] + tri[t * 12 + 6 + k]) / 3f;
                var nodes = new Nodes();
                if (Owners.Length > 0) Build(0, Owners.Length, centres, nodes);
                boxes = nodes.boxes.ToArray();
                first = nodes.first.ToArray();
                count = nodes.count.ToArray();
                left = nodes.left.ToArray();
                right = nodes.right.ToArray();
            }

            private sealed class Nodes
            {
                public readonly List<float> boxes = new List<float>();
                public readonly List<int> first = new List<int>(), count = new List<int>(), left = new List<int>(), right = new List<int>();
            }

            private static bool SeeThrough(Material material)
            {
                return material.renderQueue >= (int)RenderQueue.AlphaTest || material.IsKeywordEnabled("_ALPHATEST_ON") ||
                    material.IsKeywordEnabled("_ALPHABLEND_ON") || material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON");
            }

            private int Build(int start, int length, float[] centres, Nodes nodes)
            {
                int node = nodes.count.Count;
                nodes.first.Add(start); nodes.count.Add(length); nodes.left.Add(-1); nodes.right.Add(-1);
                float[] min = { float.MaxValue, float.MaxValue, float.MaxValue }, max = { float.MinValue, float.MinValue, float.MinValue };
                float[] cmin = { float.MaxValue, float.MaxValue, float.MaxValue }, cmax = { float.MinValue, float.MinValue, float.MinValue };
                for (int i = start; i < start + length; i++)
                {
                    int t = order[i] * 12;
                    for (int k = 0; k < 3; k++)
                    {
                        float a = tri[t + k], b = a + tri[t + 3 + k], c = a + tri[t + 6 + k], centre = centres[order[i] * 3 + k];
                        min[k] = Math.Min(min[k], Math.Min(a, Math.Min(b, c)));
                        max[k] = Math.Max(max[k], Math.Max(a, Math.Max(b, c)));
                        cmin[k] = Math.Min(cmin[k], centre);
                        cmax[k] = Math.Max(cmax[k], centre);
                    }
                }
                nodes.boxes.AddRange(min);
                nodes.boxes.AddRange(max);
                if (length <= LeafSize) return node;
                int axis = 0;
                for (int k = 1; k < 3; k++) if (cmax[k] - cmin[k] > cmax[axis] - cmin[axis]) axis = k;
                float split = (cmin[axis] + cmax[axis]) * 0.5f;
                int lo = start, hi = start + length - 1;
                while (lo <= hi)
                {
                    if (centres[order[lo] * 3 + axis] < split) lo++;
                    else { int swap = order[lo]; order[lo] = order[hi]; order[hi] = swap; hi--; }
                }
                int half = lo - start;
                if (half == 0 || half == length) half = length / 2; // identical centres
                nodes.count[node] = 0;
                int l = Build(start, half, centres, nodes), r = Build(start + half, length - half, centres, nodes);
                nodes.left[node] = l;
                nodes.right[node] = r;
                return node;
            }

            public bool AnyHit(Vector3 origin, Vector3 direction, float reach, int skipOwner, bool[] ignore)
            {
                if (Owners.Length == 0) return false;
                float ox = origin.x, oy = origin.y, oz = origin.z, dx = direction.x, dy = direction.y, dz = direction.z;
                float ix = 1f / (Math.Abs(dx) > 1e-12f ? dx : 1e-12f), iy = 1f / (Math.Abs(dy) > 1e-12f ? dy : 1e-12f), iz = 1f / (Math.Abs(dz) > 1e-12f ? dz : 1e-12f);
                int sp = 0;
                stack[sp++] = 0;
                while (sp > 0)
                {
                    int node = stack[--sp], b = node * 6;
                    float t1 = (boxes[b] - ox) * ix, t2 = (boxes[b + 3] - ox) * ix;
                    float near = Math.Min(t1, t2), far = Math.Max(t1, t2);
                    t1 = (boxes[b + 1] - oy) * iy; t2 = (boxes[b + 4] - oy) * iy;
                    near = Math.Max(near, Math.Min(t1, t2)); far = Math.Min(far, Math.Max(t1, t2));
                    t1 = (boxes[b + 2] - oz) * iz; t2 = (boxes[b + 5] - oz) * iz;
                    near = Math.Max(near, Math.Min(t1, t2)); far = Math.Min(far, Math.Max(t1, t2));
                    if (far < Math.Max(near, 0f) || near > reach) continue;
                    if (left[node] >= 0)
                    {
                        if (sp + 2 > stack.Length) return false; // pathological depth: treat as visible
                        stack[sp++] = left[node];
                        stack[sp++] = right[node];
                        continue;
                    }
                    for (int i = first[node], end = first[node] + count[node]; i < end; i++)
                    {
                        int id = order[i];
                        if (Owners[id] == skipOwner || ignore != null && ignore[id]) continue;
                        int t = id * 12;
                        // A single-sided face is drawn only toward its normal; the viewer looks back along the ray.
                        if (!twoSided[id] && tri[t + 9] * dx + tri[t + 10] * dy + tri[t + 11] * dz <= 0f) continue;
                        float e1x = tri[t + 3], e1y = tri[t + 4], e1z = tri[t + 5], e2x = tri[t + 6], e2y = tri[t + 7], e2z = tri[t + 8];
                        float px = dy * e2z - dz * e2y, py = dz * e2x - dx * e2z, pz = dx * e2y - dy * e2x;
                        float det = e1x * px + e1y * py + e1z * pz;
                        if (Math.Abs(det) < 1e-14f) continue;
                        float inv = 1f / det;
                        float sx = ox - tri[t], sy = oy - tri[t + 1], sz = oz - tri[t + 2];
                        float u = (sx * px + sy * py + sz * pz) * inv;
                        // A small overlap keeps rays through shared edges and vertices from slipping between
                        // neighbouring triangles because of rounding; 0.1% of a triangle cannot hide a real gap.
                        if (u < -1e-3f || u > 1.001f) continue;
                        float qx = sy * e1z - sz * e1y, qy = sz * e1x - sx * e1z, qz = sx * e1y - sy * e1x;
                        float v = (dx * qx + dy * qy + dz * qz) * inv;
                        if (v < -1e-3f || u + v > 1.001f) continue;
                        float distance = (e2x * qx + e2y * qy + e2z * qz) * inv;
                        if (distance > 0f && distance <= reach) return true;
                    }
                }
                return false;
            }
        }
    }
}
