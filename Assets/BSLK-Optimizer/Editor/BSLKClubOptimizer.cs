using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BSLK.Optimizer
{
    // Editor-only: the source avatar and its asset graph are never modified.
    public static class BSLKClubOptimizer
    {
        public const string OutputRoot = "Assets/BSLK-Optimizer-Generated";
        public const long TriangleLimit = 500000;
        public const int SlotLimit = 50, ParticleLimit = 2000, CollisionLimit = 1500;
        public const long TextureLimit = 150L * 1024 * 1024;

        public sealed class Options
        {
            public bool singleOutfit = true;
            public bool optimizeTextures = true;
            public bool limitParticles = true;
            public int minimumTextureSize = 512;
            public float textureError = 0.02f;
            public System.Threading.CancellationToken cancellationToken;
            public readonly HashSet<Texture> protectedTextures = new HashSet<Texture>();
        }

        public sealed class Result
        {
            public GameObject avatar;
            public string folder;
            public string report;
        }

        internal sealed class Context
        {
            public GameObject avatar;
            public string folder;
            public bool ownsFolder;
            public readonly Dictionary<Object, Object> copies = new Dictionary<Object, Object>();
            public readonly HashSet<string> copiedPaths = new HashSet<string>();
            public readonly List<string> notes = new List<string>();
            public readonly HashSet<string> removedPaths = new HashSet<string>();
            public readonly HashSet<string> fixedVisibility = new HashSet<string>();
            public readonly HashSet<string> rendererPaths = new HashSet<string>();
            public readonly HashSet<AnimationClip> nestedClips = new HashSet<AnimationClip>();
            public readonly HashSet<AnimationClip> modifiedClips = new HashSet<AnimationClip>();
        }

        public static Result Create(GameObject source, Options options)
        {
            if (source == null || EditorUtility.IsPersistent(source) || !source.scene.IsValid())
                throw new InvalidOperationException("Выберите корень аватара в открытой сцене.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode())
                throw new InvalidOperationException("Выйдите из Play Mode и режима предпросмотра анимации.");
            if (!source.activeInHierarchy)
                throw new InvalidOperationException("Перед обработкой включите корень аватара.");
            string sourcePrefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(source);
            if (!string.IsNullOrEmpty(sourcePrefab) && sourcePrefab.StartsWith(OutputRoot + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("Выберите исходный аватар, а не результат оптимизации.");
            if (options.minimumTextureSize < 32 || options.textureError <= 0 || options.textureError > 0.1f)
                throw new ArgumentOutOfRangeException(nameof(options), "Неверные настройки качества текстур.");

            Context context = new Context();
            string before = Analyze(source);
            try
            {
                if (!AssetDatabase.IsValidFolder(OutputRoot))
                    AssetDatabase.CreateFolder("Assets", "BSLK-Optimizer-Generated");
                context.folder = AssetDatabase.GenerateUniqueAssetPath(OutputRoot + "/" + SafeName(source.name));
                string folderGuid = AssetDatabase.CreateFolder(OutputRoot, Path.GetFileName(context.folder));
                if (string.IsNullOrEmpty(folderGuid)) throw new IOException("Не удалось создать папку результата.");
                context.ownsFolder = true;
                context.avatar = Object.Instantiate(source, source.transform.parent);
                context.avatar.name = source.name + " [Club]";
                if (PrefabUtility.IsPartOfPrefabInstance(context.avatar))
                    PrefabUtility.UnpackPrefabInstance(context.avatar, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                options.cancellationToken.ThrowIfCancellationRequested();

                // Copy references before modifying anything: includes animation material swaps and SDK assets.
                CopyAssetGraph(context);
                RemoveForbidden(context);
                if (options.singleOutfit)
                {
                    CaptureOutfit(context);
                    BSLKClubAnimation.FreezeBuildToggles(context);
                    RemoveDisabled(context);
                    FreezeClips(context);
                    BSLKClubAnimation.PruneEmptyLayersAndMenus(context);
                }
                BSLKOptimizer.RemoveMissingScripts(context.avatar);
                BSLKOptimizer.RemoveLights(context.avatar);
                BSLKOptimizer.RemoveCamerasAndAudioListeners(context.avatar);
                BSLKOptimizer.RemoveReflectionProbes(context.avatar);
                BSLKOptimizer.RemoveEmptyMaterialSlots(context.avatar);
                CleanPhysBoneLinks(context);
                if (options.limitParticles) LimitParticles(context);
                if (options.optimizeTextures) BSLKClubTextures.Optimize(context, options);

                // Persist all data to a prefab; a successful result never relies on transient mesh instances.
                foreach (string path in context.copiedPaths)
                {
                    Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                    if (asset != null) AssetDatabase.SaveAssetIfDirty(asset);
                }
                string prefabPath = context.folder + "/Club.prefab";
                if (PrefabUtility.SaveAsPrefabAssetAndConnect(context.avatar, prefabPath, InteractionMode.AutomatedAction) == null)
                    throw new InvalidOperationException("Не удалось сохранить клубный prefab.");
                Undo.RegisterCreatedObjectUndo(context.avatar, "Создать клубный аватар");
                context.notes.Add("Исходный аватар не изменён. Копия и её ассеты: " + context.folder);
                context.notes.Add("Сравните исходник и Club, включая лицо, одежду и мимику. Перед загрузкой проверьте итог в VRChat SDK.");
                string report = "ДО\n" + before + "\n\nПОСЛЕ\n" + Analyze(context.avatar) +
                    "\n\nИЗМЕНЕНИЯ И ЗАМЕЧАНИЯ\n" + string.Join("\n", context.notes);
                File.WriteAllText(context.folder + "/Report.txt", report);
                AssetDatabase.Refresh();
                Selection.activeGameObject = context.avatar;
                return new Result { avatar = context.avatar, folder = context.folder, report = report };
            }
            catch
            {
                if (context.avatar != null) Object.DestroyImmediate(context.avatar);
                // Only the unique directory created by this invocation may be removed.
                string allowedRoot = Path.GetFullPath(OutputRoot) + Path.DirectorySeparatorChar;
                if (context.ownsFolder && !string.IsNullOrEmpty(context.folder) &&
                    Path.GetFullPath(context.folder).StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
                    AssetDatabase.DeleteAsset(context.folder);
                throw;
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        internal static void Progress(string message, float progress)
        {
            if (EditorUtility.DisplayCancelableProgressBar("Клубный аватар", message, progress))
                throw new OperationCanceledException("Операция отменена; незавершённая копия удалена.");
        }

        private static string SafeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim().Trim('.').Trim();
            if (name.Length > 80) name = name.Substring(0, 80);
            return string.IsNullOrWhiteSpace(name) ? "Avatar" : name;
        }

        private static IEnumerable<Object> References(Object target)
        {
            using (SerializedObject serialized = new SerializedObject(target))
            {
                SerializedProperty property = serialized.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null)
                        yield return property.objectReferenceValue;
            }
        }

        private static bool Copyable(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".mat" || extension == ".anim" || extension == ".controller" ||
                extension == ".overridecontroller" || extension == ".asset" ||
                AssetImporter.GetAtPath(path) is TextureImporter;
        }

        private static void CopyAssetGraph(Context c)
        {
            Component[] components = c.avatar.GetComponentsInChildren<Component>(true);
            var roots = new HashSet<string>();
            var liveReferences = new Queue<Object>();
            foreach (Component component in components)
            {
                if (component == null) continue;
                foreach (Object reference in References(component))
                {
                    liveReferences.Enqueue(reference);
                    string path = AssetDatabase.GetAssetPath(reference);
                    if (!string.IsNullOrEmpty(path)) roots.Add(path);
                }
            }
            // Scene-created meshes/materials are not returned by AssetDatabase.GetDependencies.
            var visited = new HashSet<Object>();
            while (liveReferences.Count > 0)
            {
                Object asset = liveReferences.Dequeue();
                if (!visited.Add(asset)) continue;
                if (asset == null) continue;
                string livePath = AssetDatabase.GetAssetPath(asset);
                if (!string.IsNullOrEmpty(livePath))
                {
                    roots.Add(livePath);
                    // Walk live serialized references as well as disk dependencies. A controller
                    // may contain a newly added state/clip that has not been saved yet.
                    if (Copyable(livePath) && !(asset is Texture) && !(asset is Mesh))
                        foreach (Object dependency in References(asset)) liveReferences.Enqueue(dependency);
                    continue;
                }
                if (asset is RuntimeAnimatorController)
                    throw new InvalidOperationException("Сохраните временный AnimatorController как ассет перед оптимизацией.");
                if (!(asset is Mesh) && !(asset is Material) && !(asset is AnimationClip) && !(asset is ScriptableObject) && !(asset is Texture)) continue;
                Object copy = Object.Instantiate(asset);
                copy.name = asset.name;
                copy.hideFlags = HideFlags.None;
                string extension = asset is Material ? ".mat" : asset is AnimationClip ? ".anim" : ".asset";
                string destination = AssetDatabase.GenerateUniqueAssetPath(c.folder + "/" + SafeName(asset.name) + extension);
                AssetDatabase.CreateAsset(copy, destination);
                c.copiedPaths.Add(destination);
                c.copies[asset] = copy;
                foreach (Object dependency in References(asset))
                {
                    liveReferences.Enqueue(dependency);
                    string path = AssetDatabase.GetAssetPath(dependency);
                    if (!string.IsNullOrEmpty(path)) roots.Add(path);
                }
            }
            string[] dependencies = AssetDatabase.GetDependencies(roots.ToArray(), true);
            for (int i = 0; i < dependencies.Length; i++)
            {
                string path = dependencies[i];
                Progress("Копирование ассетов: " + Path.GetFileName(path), (float)i / Math.Max(1, dependencies.Length));
                if (!Copyable(path)) continue;
                string destination = AssetDatabase.GenerateUniqueAssetPath(c.folder + "/" + Path.GetFileName(path));
                // AssetDatabase.CopyAsset flushes dirty source assets in Unity 2022.3.
                // Copy bytes and importer settings with a fresh GUID, then remap references.
                string physicalPath = PhysicalPath(path);
                File.Copy(physicalPath, destination);
                string metadata = File.ReadAllText(physicalPath + ".meta");
                var guidPattern = new System.Text.RegularExpressions.Regex(@"(?m)^guid: [a-fA-F0-9]{32}\r?$");
                if (!guidPattern.IsMatch(metadata)) throw new IOException("Не найден GUID: " + path);
                File.WriteAllText(destination + ".meta", guidPattern.Replace(metadata, "guid: " + Guid.NewGuid().ToString("N"), 1));
                AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
                c.copiedPaths.Add(destination);
                Object[] oldAssets = AssetDatabase.LoadAllAssetsAtPath(path);
                Object[] newAssets = AssetDatabase.LoadAllAssetsAtPath(destination);
                var byId = new Dictionary<long, Object>();
                foreach (Object asset in newAssets)
                    if (asset != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long id))
                        byId[id] = asset;
                foreach (Object asset in oldAssets)
                {
                    if (asset == null) continue;
                    Object copy = null;
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long id)) byId.TryGetValue(id, out copy);
                    if (copy == null || copy.GetType() != asset.GetType())
                    {
                        if (AssetImporter.GetAtPath(path) is TextureImporter)
                            throw new IOException("Не удалось сопоставить sub-asset текстуры: " + path + " / " + asset.name);
                        copy = Object.Instantiate(asset);
                        copy.name = asset.name;
                        AssetDatabase.AddObjectToAsset(copy, destination);
                    }
                    c.copies[asset] = copy;
                    // CopyAsset reads the disk file. Preserve unsaved material/animation edits
                    // already visible in the editor without saving the user's source assets.
                    if (!(asset is Texture))
                    {
                        EditorUtility.CopySerialized(asset, copy);
                        EditorUtility.SetDirty(copy);
                    }
                }
            }
            // Imported models stay intact. Extract their meshes/clips/materials as standalone assets.
            foreach (string path in dependencies)
            {
                if (!(AssetImporter.GetAtPath(path) is ModelImporter)) continue;
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (!(asset is Mesh) && !(asset is AnimationClip) && !(asset is Material)) continue;
                    Object copy = Object.Instantiate(asset);
                    copy.name = asset.name;
                    copy.hideFlags = HideFlags.None;
                    string ext = asset is AnimationClip ? ".anim" : asset is Material ? ".mat" : ".asset";
                    string destination = AssetDatabase.GenerateUniqueAssetPath(c.folder + "/" + SafeName(asset.name) + ext);
                    AssetDatabase.CreateAsset(copy, destination);
                    c.copiedPaths.Add(destination);
                    c.copies[asset] = copy;
                }
            }
            foreach (Component component in components) if (component != null) Remap(component, c.copies);
            foreach (Object copy in c.copies.Values.Distinct())
                if (!(copy is Texture) && !(copy is Mesh)) Remap(copy, c.copies);
        }

        private static void Remap(Object target, Dictionary<Object, Object> copies)
        {
            using (SerializedObject serialized = new SerializedObject(target))
            {
                SerializedProperty property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    Object reference = property.objectReferenceValue;
                    if (reference != null && copies.TryGetValue(reference, out Object copy)) property.objectReferenceValue = copy;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static string PhysicalPath(string assetPath)
        {
            if (File.Exists(assetPath)) return assetPath;
            UnityEditor.PackageManager.PackageInfo package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
            if (package == null) throw new FileNotFoundException("Не найден ассет " + assetPath);
            return Path.Combine(package.resolvedPath, assetPath.Substring(package.assetPath.Length).TrimStart('/'));
        }

        private static string Relative(Transform transform, Context c)
        {
            return AnimationUtility.CalculateTransformPath(transform, c.avatar.transform);
        }

        private static void CaptureOutfit(Context c)
        {
            foreach (Animator animator in c.avatar.GetComponentsInChildren<Animator>(true))
                if (animator.transform != c.avatar.transform && animator.runtimeAnimatorController != null)
                    foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips) c.nestedClips.Add(clip);
            if (c.nestedClips.Count > 0) c.notes.Add("Вложенные Animator: их клипы сохранены, поскольку пути привязок относительны другому корню. Проверьте выбранный образ в этих объектах.");
            foreach (Renderer renderer in c.avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                c.rendererPaths.Add(Relative(renderer.transform, c));
                for (Transform t = renderer.transform; t != null && t != c.avatar.transform; t = t.parent)
                    c.fixedVisibility.Add(Relative(t, c));
            }
        }

        private static void RemoveDisabled(Context c)
        {
            // Preserve bones and referenced service objects of retained components, not whole disabled wardrobes.
            var protectedTransforms = new HashSet<Transform>();
            var pending = new Queue<Component>(c.avatar.GetComponentsInChildren<Component>(true)
                .Where(component => component != null && component.gameObject.activeInHierarchy && !(component is Transform)));
            var visited = new HashSet<Component>();
            while (pending.Count > 0)
            {
                Component component = pending.Dequeue();
                if (component == null || !visited.Add(component)) continue;
                if (component.GetType().FullName == "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone")
                {
                    using (var serialized = new SerializedObject(component))
                    {
                        Transform physicsRoot = serialized.FindProperty("rootTransform")?.objectReferenceValue as Transform;
                        if (physicsRoot == null) physicsRoot = component.transform;
                        if (physicsRoot.IsChildOf(c.avatar.transform))
                            foreach (Transform bone in physicsRoot.GetComponentsInChildren<Transform>(true)) protectedTransforms.Add(bone);
                    }
                }
                foreach (Object reference in References(component))
                {
                    // GameObject references in clothing toggles do not make that wardrobe necessary.
                    Transform t = (reference as Component)?.transform;
                    if (reference is Component dependency && !(dependency is Transform) && t != null && t.IsChildOf(c.avatar.transform))
                        pending.Enqueue(dependency);
                    while (t != null && t.IsChildOf(c.avatar.transform))
                    {
                        protectedTransforms.Add(t);
                        t = t.parent;
                    }
                }
            }
            int removed = 0, preserved = 0;
            foreach (Transform t in c.avatar.GetComponentsInChildren<Transform>(true).Reverse())
            {
                if (t == c.avatar.transform || t == null || t.gameObject.activeInHierarchy) continue;
                if (protectedTransforms.Contains(t)) { preserved++; continue; }
                c.removedPaths.Add(Relative(t, c));
                Object.DestroyImmediate(t.gameObject);
                removed++;
            }
            c.notes.Add("Удалено выключенных объектов: " + removed + "; сохранено зависимостей: " + preserved + ".");
            if (preserved > 0) c.notes.Add("Сохранённые выключенные зависимости требуют просмотра: они могут включать служебные объекты или связанные вещи.");
        }

        private static void FreezeClips(Context c)
        {
            int removed = 0;
            foreach (AnimationClip clip in c.copies.Values.OfType<AnimationClip>().Distinct())
            {
                if (c.nestedClips.Contains(clip)) continue;
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    bool missing = c.removedPaths.Contains(binding.path);
                    bool visibility = binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive" && c.fixedVisibility.Contains(binding.path);
                    bool rendererState = typeof(Renderer).IsAssignableFrom(binding.type) && binding.propertyName == "m_Enabled" && c.rendererPaths.Contains(binding.path);
                    if (!missing && !visibility && !rendererState) continue;
                    AnimationUtility.SetEditorCurve(clip, binding, null);
                    c.modifiedClips.Add(clip);
                    removed++;
                }
                foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    bool materialSwap = c.rendererPaths.Contains(binding.path) && binding.propertyName.StartsWith("m_Materials", StringComparison.Ordinal);
                    if (!c.removedPaths.Contains(binding.path) && !materialSwap) continue;
                    AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                    c.modifiedClips.Add(clip);
                    removed++;
                }
            }
            c.notes.Add("Удалено анимационных привязок скрытых объектов, видимости одежды и замены материалов: " + removed + ".");
            c.notes.Add("Смешанные переключатели с жестами и другими функциями сохраняются.");
        }

        internal static bool ForbiddenComponent(Component component)
        {
            if (component == null) return false;
            string name = component.GetType().Name;
            if (name == "VRCFuryHapticPlug" || name == "VRCFuryHapticSocket" || name == "VRCFurySpsOptions" || name == "VRCFurySpsGreenScreenFix") return true;
            if (component.GetType().FullName == "VF.Model.VRCFury")
            {
                using (var serialized = new SerializedObject(component))
                {
                    SerializedProperty content = serialized.FindProperty("content");
                    SerializedProperty legacy = serialized.FindProperty("config.features");
                    return content != null && content.propertyType == SerializedPropertyType.ManagedReference &&
                        content.managedReferenceFullTypename.EndsWith(" VF.Model.Feature.SpsOptions", StringComparison.Ordinal) &&
                        (legacy == null || legacy.arraySize == 0);
                }
            }
            return false;
        }

        private static void RemoveForbidden(Context c)
        {
            int removed = 0;
            foreach (Component component in c.avatar.GetComponentsInChildren<Component>(true))
            {
                if (!ForbiddenComponent(component)) continue;
                Object.DestroyImmediate(component);
                removed++;
            }
            c.notes.Add("Удалено распознанных компонентов SPS: " + removed + ". Источники света, камеры и Reflection Probes удалены.");
            // Unknown shader variants must not be silently replaced with an unrelated shader.
            foreach (Material material in c.copies.Values.OfType<Material>().Distinct())
            {
                if (material.HasProperty("_TPS_Enabled") && material.GetFloat("_TPS_Enabled") != 0)
                {
                    material.SetFloat("_TPS_Enabled", 0);
                    c.notes.Add("Отключён TPS в материале " + material.name + ".");
                }
                if (material.shader != null && material.shader.name.IndexOf("Raliv", StringComparison.OrdinalIgnoreCase) >= 0)
                    c.notes.Add("ТРЕБУЕТ ИСПРАВЛЕНИЯ: DPS-шейдер " + material.name + ". Замените его с сохранением внешнего вида.");
            }
        }

        private static void LimitParticles(Context c)
        {
            ParticleSystem[] systems = c.avatar.GetComponentsInChildren<ParticleSystem>(true);
            int[] limits = AllocateParticleBudget(systems.Select(s => s.main.maxParticles).ToArray(), ParticleLimit);
            int changed = 0;
            for (int i = 0; i < systems.Length; i++)
            {
                var main = systems[i].main;
                if (main.maxParticles == limits[i]) continue;
                main.maxParticles = limits[i];
                changed++;
            }
            foreach (AnimationClip clip in c.copies.Values.OfType<AnimationClip>().Distinct())
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                    if (binding.type == typeof(ParticleSystem) && binding.propertyName == "maxNumParticles")
                        AnimationUtility.SetEditorCurve(clip, binding, null);
            c.notes.Add("Ограничено систем частиц: " + changed + ". Общий maxParticles ≤ " + ParticleLimit + ".");
        }

        private static void CleanPhysBoneLinks(Context c)
        {
            int removed = 0;
            foreach (Component component in c.avatar.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.GetType().FullName != "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone") continue;
                using (var serialized = new SerializedObject(component))
                {
                    SerializedProperty colliders = serialized.FindProperty("colliders");
                    if (colliders == null || !colliders.isArray) continue;
                    var seen = new HashSet<Object>();
                    for (int i = colliders.arraySize - 1; i >= 0; i--)
                    {
                        SerializedProperty entry = colliders.GetArrayElementAtIndex(i);
                        if (entry.propertyType != SerializedPropertyType.ObjectReference) continue;
                        Object collider = entry.objectReferenceValue;
                        if (collider != null && seen.Add(collider)) continue;
                        entry.objectReferenceValue = null;
                        colliders.DeleteArrayElementAtIndex(i);
                        removed++;
                    }
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            c.notes.Add("PhysBone: убрано пустых/повторных связей с коллайдерами: " + removed + ". Рабочие столкновения сохранены.");
        }

        internal static int[] AllocateParticleBudget(int[] counts, int budget)
        {
            long total = counts.Sum(x => (long)Math.Max(0, x));
            if (total <= budget) return counts.Select(x => Math.Max(0, x)).ToArray();
            int[] result = counts.Select(x => (int)((long)Math.Max(0, x) * budget / total)).ToArray();
            int remaining = budget - result.Sum();
            // Largest remainder method, stable ordering; never exceeds an original limit.
            foreach (int i in Enumerable.Range(0, counts.Length).OrderByDescending(i => (long)Math.Max(0, counts[i]) * budget % total))
            {
                if (remaining == 0) break;
                if (result[i] >= counts[i]) continue;
                result[i]++;
                remaining--;
            }
            return result;
        }

        internal static HashSet<Texture> Textures(GameObject avatar)
        {
            var textures = new HashSet<Texture>();
            foreach (Object dependency in EditorUtility.CollectDependencies(new Object[] { avatar }))
                if (dependency is Texture texture) textures.Add(texture);
            return textures;
        }

        private static string Check(long count, long limit) { return count <= limit ? "OK" : "ПРЕВЫШЕНИЕ"; }

        public static string Analyze(GameObject avatar)
        {
            if (avatar == null) return "Аватар не выбран.";
            long triangles = 0, particles = 0;
            int slots = 0, lights = 0, forbidden = 0;
            var warnings = new HashSet<string>();
            foreach (Renderer renderer in avatar.GetComponentsInChildren<Renderer>(true))
            {
                slots += renderer.sharedMaterials.Length;
                Mesh mesh = (renderer as SkinnedMeshRenderer)?.sharedMesh;
                if (mesh == null)
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null) mesh = filter.sharedMesh;
                }
                if (mesh == null) continue;
                for (int i = 0; i < mesh.subMeshCount; i++)
                    if (mesh.GetTopology(i) == MeshTopology.Triangles) triangles += (long)mesh.GetIndexCount(i) / 3;
                if (!mesh.isReadable) warnings.Add("Есть меши без Read/Write: проверьте требования VRChat SDK.");
            }
            foreach (ParticleSystem system in avatar.GetComponentsInChildren<ParticleSystem>(true)) particles += system.main.maxParticles;
            foreach (Component component in avatar.GetComponentsInChildren<Component>(true))
            {
                if (component is Light) lights++;
                if (ForbiddenComponent(component)) forbidden++;
                if (component == null) continue;
                string type = component.GetType().FullName ?? "";
                if (type.IndexOf("VRCFury", StringComparison.OrdinalIgnoreCase) >= 0 || type.IndexOf("ModularAvatar", StringComparison.OrdinalIgnoreCase) >= 0)
                    warnings.Add("Есть сборочные компоненты VRCFury/Modular Avatar: итоговые ресурсы и выбранный образ необходимо проверить после их обработки.");
            }
            long textureBytes = 0;
            bool unknownTexture = false;
            foreach (Texture texture in Textures(avatar))
            {
                if (texture is Texture2D t)
                {
                    for (int mip = 0; mip < t.mipmapCount; mip++)
                        textureBytes += (long)UnityEngine.Experimental.Rendering.GraphicsFormatUtility.ComputeMipmapSize(Math.Max(1, t.width >> mip), Math.Max(1, t.height >> mip), t.graphicsFormat);
                }
                else unknownTexture = true;
            }
            foreach (Material material in EditorUtility.CollectDependencies(new Object[] { avatar }).OfType<Material>())
            {
                if (material.shader != null && (material.shader.name.IndexOf("Raliv", StringComparison.OrdinalIgnoreCase) >= 0 || material.shader.name.IndexOf("SPS", StringComparison.OrdinalIgnoreCase) >= 0))
                { forbidden++; warnings.Add("Обнаружен возможный DPS/SPS-шейдер: " + material.name); }
                if (material.HasProperty("_TPS_Enabled") && material.GetFloat("_TPS_Enabled") > 0) forbidden++;
            }
            StringBuilder report = new StringBuilder();
            report.AppendLine("Треугольники: " + triangles + " / " + TriangleLimit + " — " + Check(triangles, TriangleLimit));
            report.AppendLine("Material Slots: " + slots + " / " + SlotLimit + " — " + Check(slots, SlotLimit));
            report.AppendLine("Текстуры 2D, оценка GPU: " + (textureBytes / 1048576.0).ToString("F1") + " / 150 MiB — " + (unknownTexture ? "НЕПОЛНЫЙ ПОДСЧЁТ" : Check(textureBytes, TextureLimit)));
            report.AppendLine("Частицы (сумма maxParticles): " + particles + " / " + ParticleLimit + " — " + Check(particles, ParticleLimit));
            if (BSLKClubSdk.TryRead(avatar, out BSLKClubSdk.Stats sdk, out string sdkReason))
            {
                report.AppendLine("PhysBone Collision Checks (SDK): " + sdk.collisions + " / " + CollisionLimit + " — " + Check(sdk.collisions, CollisionLimit));
                report.AppendLine("SDK: треугольники " + sdk.triangles + "; слоты " + sdk.slots + "; частицы " + sdk.particles +
                    "; текстуры " + sdk.textureMegabytes.ToString("F2") + " MB; свет " + sdk.lights + ".");
                if (sdk.collisions > CollisionLimit) warnings.Add("Уберите лишние связи PhysBone–Collider. Плагин не отключает физику одежды вслепую.");
                if (sdk.triangles > TriangleLimit || sdk.slots > SlotLimit || sdk.particles > ParticleLimit || sdk.textureMegabytes > 150 || sdk.lights > 0)
                    warnings.Add("По статистике SDK есть превышение правил клуба.");
            }
            else
            {
                report.AppendLine("PhysBone Collision Checks: НЕ ПРОВЕРЕНО / " + CollisionLimit);
                warnings.Add(sdkReason);
            }
            report.AppendLine("Lights: " + lights + " / 0 — " + Check(lights, 0));
            report.AppendLine("Распознанные SPS/DPS/TPS: " + forbidden + " — " + (forbidden == 0 ? "не найдены; требуется проверка сборки" : "ТРЕБУЕТ ИСПРАВЛЕНИЯ"));
            report.AppendLine("Это предварительный анализ текущей иерархии, не сертификат соответствия клуба. Платформа: " + EditorUserBuildSettings.activeBuildTarget + ".");
            if (triangles > TriangleLimit) warnings.Add("Нужно уменьшение геометрии: автоматическая decimation в этом плагине не выполняется.");
            if (slots > SlotLimit) warnings.Add("Нужно объединение материалов/мешей: несовместимые шейдеры автоматически не объединяются.");
            if (textureBytes > TextureLimit) warnings.Add("Текстуры превышают бюджет: уменьшите защищённые текстуры или настройте допустимую потерю деталей.");
            if (unknownTexture) warnings.Add("Cubemap, RenderTexture или TextureArray не включены в оценку 2D-памяти.");
            foreach (string warning in warnings) report.AppendLine(warning);
            return report.ToString();
        }
    }
}
