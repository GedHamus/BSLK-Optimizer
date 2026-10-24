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
        public const string Suffix = "BELKA";
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
            public bool mergeTextures = true;
            public bool mergeMaterials = true;
            public bool mergeMeshes = true;
            public bool removeUnusedShapesAndBones = true;
            public bool keepMmdShapes = true;
            public bool removeHiddenBody = true;
            // Worn clothing that still has a menu toggle counts as cover; taking it off in game shows a gap.
            public bool hiddenUnderToggledClothing = true;
            // PhysBones whose colliders are dropped: "path#index" keys from BSLKClubInsights.PhysBones.
            public readonly HashSet<string> stripColliders = new HashSet<string>();
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
                context.avatar.name = source.name + " [" + Suffix + "]";
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
                // Duplicate textures first: materials that differ only by duplicate files then merge too.
                if (options.mergeTextures) BSLKClubMaterials.MergeDuplicateTextures(context, options.protectedTextures);
                if (options.mergeMaterials) BSLKClubMaterials.MergeIdenticalMaterials(context);
                HashSet<Renderer> locked = LockedRenderers(context, out HashSet<Renderer> toggleable);
                // Body under worn clothing is cut only when deleting disabled items was not enough.
                if (options.removeHiddenBody && TriangleTotal(context.avatar) > TriangleLimit)
                    BSLKClubOcclusion.RemoveHiddenTriangles(context.avatar, locked, renderer => options.hiddenUnderToggledClothing || !toggleable.Contains(renderer),
                        mesh => SaveMesh(context, mesh), context.notes,
                        LiveShapes(context.avatar, context.copies.Values.OfType<AnimationClip>().Distinct(), options.hiddenUnderToggledClothing));
                if (options.mergeMeshes) BSLKClubGeometry.MergeClothing(context.avatar, locked, ClubClips(context), mesh => SaveMesh(context, mesh), context.notes);
                if (options.mergeMaterials)
                    context.notes.Add("Объединено слотов с одинаковым материалом: " + BSLKClubGeometry.MergeDuplicateSlots(context.avatar, locked, mesh => SaveMesh(context, mesh)) + ".");
                if (options.removeUnusedShapesAndBones) RemoveUnusedShapesAndBones(context, options);
                StripColliders(context, options.stripColliders);
                if (options.optimizeTextures) BSLKClubTextures.Optimize(context, options);
                RestoreReadability(context, source);

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
                RemoveUnusedCopies(context, prefabPath);
                context.notes.Add("Исходный аватар не изменён. Копия и её ассеты: " + context.folder);
                context.notes.Add("Сравните исходник и Club, включая лицо, одежду и мимику. Перед загрузкой проверьте итог в VRChat SDK.");
                string report = "BSLK Optimizer " + BSLKOptimizer.Version + " • по любым вопросам пишите " + BSLKOptimizer.Contact +
                    " • Discord BELKA&STRELKA: " + BSLKOptimizer.Discord + "\n\nДО\n" + before + "\n\nПОСЛЕ\n" + Analyze(context.avatar) +
                    "\n\nИЗМЕНЕНИЯ И ЗАМЕЧАНИЯ\n" + string.Join("\n", context.notes) +
                    "\n\nКТО ЗАНИМАЕТ ЛИМИТЫ (ПОСЛЕ)\n" + BSLKClubInsights.Format(BSLKClubInsights.Collect(context.avatar));
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

        // The asset graph is copied before outfit removal; drop what the saved prefab no longer uses
        // (materials of deleted clothing, animation-swap materials, their textures, unused model meshes).
        private static void RemoveUnusedCopies(Context c, string prefabPath)
        {
            var used = new HashSet<string>(AssetDatabase.GetDependencies(prefabPath, true));
            string[] unused = c.copiedPaths.Where(path => !used.Contains(path) && AssetDatabase.LoadMainAssetAtPath(path) != null).ToArray();
            int materials = unused.Count(path => AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(Material));
            var failed = new List<string>();
            if (unused.Length > 0) AssetDatabase.DeleteAssets(unused, failed);
            c.copiedPaths.ExceptWith(unused.Except(failed));
            c.notes.Add("Удалено неиспользуемых копий ассетов: " + (unused.Length - failed.Count) + ", из них материалов " + materials + ".");
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

        internal static IEnumerable<Object> References(Object target, Func<string, bool> skipPath = null)
        {
            using (SerializedObject serialized = new SerializedObject(target))
            {
                SerializedProperty property = serialized.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null &&
                        (skipPath == null || !skipPath(property.propertyPath)))
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
            string[] dependencies = Dependencies(roots);
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
                    // Meshes become editable copies so merging, shape cleanup and simplification can work.
                    Object copy = asset is Mesh mesh ? BSLKClubGeometry.ReadableCopy(mesh) : Object.Instantiate(asset);
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

        // Recursive dependencies, but not through scripts, plugin DLLs (SDK classes live there)
        // or shaders: their editor icons and default textures are not avatar assets.
        private static string[] Dependencies(IEnumerable<string> roots)
        {
            var result = new List<string>();
            var seen = new HashSet<string>();
            var pending = new Queue<string>(roots);
            while (pending.Count > 0)
            {
                string path = pending.Dequeue();
                if (!seen.Add(path)) continue;
                Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
                if (type == typeof(MonoScript) || AssetImporter.GetAtPath(path) is PluginImporter) continue;
                result.Add(path);
                if (type == typeof(Shader) || type == typeof(ComputeShader)) continue;
                foreach (string dependency in AssetDatabase.GetDependencies(path, false)) pending.Enqueue(dependency);
            }
            return result.ToArray();
        }

        // Points every component of the copy and every result-owned asset at the replacement objects.
        internal static void RemapAll(Context c, Dictionary<Object, Object> map)
        {
            foreach (Component component in c.avatar.GetComponentsInChildren<Component>(true))
                if (component != null) Remap(component, map);
            foreach (Object copy in c.copies.Values.Distinct())
                if (copy != null && !(copy is Texture) && !(copy is Mesh)) Remap(copy, map);
        }

        // Result meshes were made readable for editing; a renderer whose source mesh had Read/Write off
        // gets it off again, so the club avatar does not carry extra CPU mesh memory.
        private static void RestoreReadability(Context c, GameObject source)
        {
            foreach (Renderer renderer in c.avatar.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = BSLKClubGeometry.MeshOf(renderer);
                if (mesh == null || !mesh.isReadable || !c.copiedPaths.Contains(AssetDatabase.GetAssetPath(mesh))) continue;
                Transform original = source.transform.Find(Relative(renderer.transform, c));
                Renderer sourceRenderer = original != null ? original.GetComponent<Renderer>() : null;
                Mesh sourceMesh = sourceRenderer != null ? BSLKClubGeometry.MeshOf(sourceRenderer) : null;
                if (sourceMesh != null && !sourceMesh.isReadable) BSLKClubGeometry.SetReadable(mesh, false);
            }
        }

        internal static Mesh SaveMesh(Context c, Mesh mesh)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(c.folder + "/" + SafeName(mesh.name) + ".asset");
            AssetDatabase.CreateAsset(mesh, path);
            c.copiedPaths.Add(path);
            return mesh;
        }

        internal static long TriangleTotal(GameObject avatar)
        {
            return avatar.GetComponentsInChildren<Renderer>(true).Where(r => r is SkinnedMeshRenderer || r is MeshRenderer)
                .Select(BSLKClubGeometry.MeshOf).Where(m => m != null).Sum(m => (long)BSLKClubGeometry.TriangleCount(m));
        }

        private static ICollection<AnimationClip> ClubClips(Context c)
        {
            return c.copies.Values.OfType<AnimationClip>().Distinct().Where(clip => !c.nestedClips.Contains(clip)).ToList();
        }

        // Renderers whose identity, slots or visibility something else depends on: referenced by
        // components (visemes, eyelids, VRCFury actions, particle shapes), animated other than through
        // blend shapes, toggled below their clothing item, or driven by a nested Animator.
        // `toggleable` collects renderers that can be hidden at runtime (their object or any parent is
        // switched, or the renderer itself is animated); they must not count as a permanent cover.
        private static HashSet<Renderer> LockedRenderers(Context c, out HashSet<Renderer> toggleable)
        {
            return RuntimeRenderers(c.avatar, c.copies.Values.OfType<AnimationClip>().Distinct(), out toggleable);
        }

        // Clips the avatar can play: its controllers (descriptor layers, Animators) and build components.
        internal static IEnumerable<AnimationClip> AvatarClips(GameObject avatar)
        {
            return EditorUtility.CollectDependencies(new Object[] { avatar }).OfType<AnimationClip>().Distinct();
        }

        internal static HashSet<Renderer> RuntimeRenderers(GameObject avatar, IEnumerable<AnimationClip> clips, out HashSet<Renderer> toggleable)
        {
            var locked = new HashSet<Renderer>();
            toggleable = new HashSet<Renderer>();
            Transform root = avatar.transform;
            Func<Transform, string> relative = t => AnimationUtility.CalculateTransformPath(t, root);
            var animated = new HashSet<string>();
            var toggled = new HashSet<string>();
            foreach (Component component in avatar.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform || component is Renderer || component is MeshFilter) continue;
                foreach (Object reference in References(component))
                {
                    if (reference is Renderer renderer) locked.Add(renderer);
                    // Objects switched by build-time toggles (VRCFury etc.) count as animated visibility.
                    else if (reference is GameObject target && target != component.gameObject && target.transform.IsChildOf(root) && target.transform != root)
                        toggled.Add(relative(target.transform));
                }
            }
            foreach (AnimationClip clip in clips)
            {
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type == typeof(GameObject)) toggled.Add(binding.path);
                    else if (typeof(Renderer).IsAssignableFrom(binding.type) && !binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal)) animated.Add(binding.path);
                }
                foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    if (typeof(Renderer).IsAssignableFrom(binding.type)) animated.Add(binding.path);
            }
            foreach (Renderer renderer in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (animated.Contains(relative(renderer.transform))) { locked.Add(renderer); toggleable.Add(renderer); continue; }
                for (Transform t = renderer.transform; t != null && t != root; t = t.parent)
                    if (toggled.Contains(relative(t))) { toggleable.Add(renderer); break; }
                for (Transform t = renderer.transform; t != null && t != root; t = t.parent)
                {
                    bool topLevel = t.parent == root;
                    if ((!topLevel && toggled.Contains(relative(t))) || (t != renderer.transform && t.GetComponent<Animator>() != null)) { locked.Add(renderer); break; }
                }
            }
            return locked;
        }

        private static readonly string[] MmdShapes =
        {
            "あ", "い", "う", "え", "お", "ん", "まばたき", "笑い", "ウィンク", "ウィンク右", "ウィンク２", "ｳｨﾝｸ２右", "はぅ", "なごみ", "びっくり", "じと目",
            "なぬ！", "はちゅ目", "瞳小", "真面目", "困る", "にこり", "怒り", "上", "下", "前", "▲", "∧", "ω", "ω□", "はんっ！", "えー", "にやり", "ぺろっ", "てへぺろ",
            "照れ", "涙", "口角上げ", "口角下げ", "口横広げ", "歯無し上", "歯無し下", "ハイライト消", "映り込み消", "ぐるぐる", "星目", "はぁと", "キリッ", "あ２", "□"
        };

        // Keep every shape that is animated, named by any component (visemes, VRCFury, gestures),
        // used for eyelids, or a standard MMD morph; drop the rest from the copy.
        // Shape names that can change at runtime: animated by clips or named by components
        // (visemes, VRCFury toggles and gestures). Non-zero weights are checked by the callers.
        // ignoreClothingToggles: shrink keys of VRCFury clothing toggles are not live when that worn
        // clothing is treated as permanent (the body under it is cut anyway).
        internal static HashSet<string> LiveShapes(GameObject avatar, IEnumerable<AnimationClip> clips, bool ignoreClothingToggles = false)
        {
            var live = new HashSet<string>();
            foreach (AnimationClip clip in clips)
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                    if (binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal)) live.Add(binding.propertyName.Substring("blendShape.".Length));
            foreach (Component component in avatar.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform || component is Renderer) continue;
                using (var serialized = new SerializedObject(component))
                {
                    if (ignoreClothingToggles && IsClothingToggle(serialized)) continue;
                    SerializedProperty property = serialized.GetIterator();
                    while (property.Next(true))
                        if (property.propertyType == SerializedPropertyType.String && !string.IsNullOrEmpty(property.stringValue)) live.Add(property.stringValue);
                }
            }
            return live;
        }

        private static bool IsClothingToggle(SerializedObject serialized)
        {
            if (serialized.targetObject.GetType().FullName != "VF.Model.VRCFury") return false;
            SerializedProperty actions = serialized.FindProperty("content.state.actions");
            if (actions == null || !actions.isArray) return false;
            for (int i = 0; i < actions.arraySize; i++)
            {
                SerializedProperty action = actions.GetArrayElementAtIndex(i);
                if (action.propertyType == SerializedPropertyType.ManagedReference &&
                    action.managedReferenceFullTypename.EndsWith(" VF.Model.StateAction.ObjectToggleAction", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static void RemoveUnusedShapesAndBones(Context c, Options options)
        {
            HashSet<string> keep = LiveShapes(c.avatar, c.copies.Values.OfType<AnimationClip>().Distinct());
            if (options.keepMmdShapes) keep.UnionWith(MmdShapes);

            // The descriptor stores eyelid shapes by index: remember names, restore indices afterwards.
            Component descriptor = c.avatar.GetComponents<Component>().FirstOrDefault(x => x != null && x.GetType().Name == "VRCAvatarDescriptor");
            SkinnedMeshRenderer eyelids = null;
            var eyelidNames = new List<string>();
            if (descriptor != null)
                using (var serialized = new SerializedObject(descriptor))
                {
                    eyelids = serialized.FindProperty("customEyeLookSettings.eyelidsSkinnedMesh")?.objectReferenceValue as SkinnedMeshRenderer;
                    SerializedProperty indices = serialized.FindProperty("customEyeLookSettings.eyelidsBlendshapes");
                    if (eyelids != null && eyelids.sharedMesh != null && indices != null && indices.isArray)
                        for (int i = 0; i < indices.arraySize; i++)
                        {
                            int index = indices.GetArrayElementAtIndex(i).intValue;
                            string name = index >= 0 && index < eyelids.sharedMesh.blendShapeCount ? eyelids.sharedMesh.GetBlendShapeName(index) : null;
                            eyelidNames.Add(name);
                            if (name != null) keep.Add(name);
                        }
                }
            int shapes = BSLKClubGeometry.RemoveBlendShapes(c.avatar, keep, mesh => SaveMesh(c, mesh));
            if (descriptor != null && eyelids != null && eyelidNames.Count > 0)
                using (var serialized = new SerializedObject(descriptor))
                {
                    SerializedProperty indices = serialized.FindProperty("customEyeLookSettings.eyelidsBlendshapes");
                    for (int i = 0; i < eyelidNames.Count; i++)
                        indices.GetArrayElementAtIndex(i).intValue = eyelidNames[i] == null ? -1 : eyelids.sharedMesh.GetBlendShapeIndex(eyelidNames[i]);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            BoneReport bones = BSLKOptimizer.RemoveUnusedBones(c.avatar, mesh => SaveMesh(c, mesh));
            c.notes.Add("Удалено неиспользуемых blend shapes: " + shapes + "; привязок костей без веса: " + bones.bonesRemoved +
                (bones.skippedNotReadable > 0 ? "; мешей без Read/Write пропущено: " + bones.skippedNotReadable : "") + ".");
        }

        private static void StripColliders(Context c, HashSet<string> keys)
        {
            if (keys.Count == 0) return;
            int stripped = 0;
            foreach (var entry in BSLKClubInsights.PhysBones(c.avatar))
            {
                if (!keys.Contains(entry.key)) continue;
                using (var serialized = new SerializedObject(entry.physBone))
                {
                    SerializedProperty colliders = serialized.FindProperty("colliders");
                    if (colliders == null || !colliders.isArray || colliders.arraySize == 0) continue;
                    colliders.ClearArray();
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    stripped++;
                }
            }
            c.notes.Add("PhysBone: по вашему выбору отключены коллизии у " + stripped + " цепочек.");
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

        internal static string Relative(Transform transform, Context c)
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
                string typeName = component.GetType().FullName;
                bool physBone = typeName == "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone";
                if (physBone) ProtectPhysBoneChain(component, c, protectedTransforms);
                // An ignored transform is excluded from the chain; listing it does not make it necessary.
                // A VRCFury toggle action aimed at disabled clothing does not make that clothing necessary
                // either; such actions are removed together with their targets.
                Func<string, bool> skip = physBone ? (Func<string, bool>)(path => path.StartsWith("ignoreTransforms", StringComparison.Ordinal))
                    : typeName == "VF.Model.VRCFury" ? path => path.Contains("actions") : (Func<string, bool>)null;
                foreach (Object reference in References(component, skip))
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
            int preserved = 0;
            var removals = new List<Transform>();
            foreach (Transform t in c.avatar.GetComponentsInChildren<Transform>(true).Reverse())
            {
                if (t == c.avatar.transform || t.gameObject.activeInHierarchy) continue;
                if (protectedTransforms.Contains(t)) { preserved++; continue; }
                removals.Add(t);
            }
            // Build-time actions aimed at deleted objects would otherwise be left with missing targets.
            BSLKClubAnimation.RemoveActionsForDeleted(c, new HashSet<GameObject>(removals.Select(t => t.gameObject)));
            foreach (Transform t in removals)
            {
                c.removedPaths.Add(Relative(t, c));
                Object.DestroyImmediate(t.gameObject);
            }
            c.notes.Add("Удалено выключенных объектов: " + removals.Count + "; сохранено зависимостей: " + preserved + ".");
            if (preserved > 0) c.notes.Add("Сохранённые выключенные зависимости требуют просмотра: они могут включать служебные объекты или связанные вещи.");
        }

        // An active PhysBone needs its chain, not every disabled item parented to that chain:
        // a hair PhysBone rooted at Head must not preserve disabled hats or glasses.
        private static void ProtectPhysBoneChain(Component physBone, Context c, HashSet<Transform> protectedTransforms)
        {
            using (var serialized = new SerializedObject(physBone))
            {
                Transform physicsRoot = serialized.FindProperty("rootTransform")?.objectReferenceValue as Transform;
                if (physicsRoot == null) physicsRoot = physBone.transform;
                if (!physicsRoot.IsChildOf(c.avatar.transform)) return;
                var ignored = new HashSet<Transform>();
                SerializedProperty ignore = serialized.FindProperty("ignoreTransforms");
                if (ignore != null && ignore.isArray)
                    for (int i = 0; i < ignore.arraySize; i++)
                        if (ignore.GetArrayElementAtIndex(i).objectReferenceValue is Transform t) ignored.Add(t);
                var pending = new Stack<Transform>();
                pending.Push(physicsRoot);
                while (pending.Count > 0)
                {
                    Transform bone = pending.Pop();
                    if (ignored.Contains(bone)) continue;
                    // A disabled subtree with renderers is a wardrobe item, not a bone chain.
                    if (!bone.gameObject.activeInHierarchy && bone.GetComponentsInChildren<Renderer>(true).Length > 0) continue;
                    for (Transform t = bone; t != null && t.IsChildOf(c.avatar.transform); t = t.parent)
                        if (!protectedTransforms.Add(t)) break; // ancestors are always added together
                    foreach (Transform child in bone) pending.Push(child);
                }
            }
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
            int changed = 0, curves = 0;
            for (int i = 0; i < systems.Length; i++)
            {
                var main = systems[i].main;
                if (main.maxParticles == limits[i]) continue;
                main.maxParticles = limits[i];
                changed++;
            }
            // Unity 2022.3 does not list maxParticles as animatable; a hand-written
            // "InitialModule.maxNumParticles" binding is dead data and is removed from the copy.
            foreach (AnimationClip clip in c.copies.Values.OfType<AnimationClip>().Distinct())
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                    if (binding.type == typeof(ParticleSystem) && binding.propertyName.EndsWith("maxNumParticles", StringComparison.Ordinal))
                    {
                        AnimationUtility.SetEditorCurve(clip, binding, null);
                        curves++;
                    }
            c.notes.Add("Ограничено систем частиц: " + changed + "; удалено анимаций maxParticles: " + curves + ". Общий maxParticles ≤ " + ParticleLimit + ".");
        }

        private static void CleanPhysBoneLinks(Context c)
        {
            int removed = 0;
            foreach (Component component in c.avatar.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.GetType().FullName != "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone") continue;
                using (var serialized = new SerializedObject(component))
                {
                    removed += RemoveEmptyAndRepeated(serialized.FindProperty("colliders"));
                    // Ignored transforms of deleted wardrobe leave missing entries behind.
                    RemoveEmptyAndRepeated(serialized.FindProperty("ignoreTransforms"));
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            c.notes.Add("PhysBone: убрано пустых/повторных связей с коллайдерами: " + removed + ". Рабочие столкновения сохранены.");
        }

        private static int RemoveEmptyAndRepeated(SerializedProperty list)
        {
            if (list == null || !list.isArray) return 0;
            int removed = 0;
            var seen = new HashSet<Object>();
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                if (entry.propertyType != SerializedPropertyType.ObjectReference) continue;
                Object reference = entry.objectReferenceValue;
                if (reference != null && seen.Add(reference)) continue;
                entry.objectReferenceValue = null;
                list.DeleteArrayElementAtIndex(i);
                removed++;
            }
            return removed;
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

        // Only textures bound to shader texture properties of reachable materials (renderers,
        // animation swaps, build components). Script/menu icons and stale material slots are
        // not GPU texture memory of the avatar.
        internal static HashSet<Texture> Textures(GameObject avatar)
        {
            var textures = new HashSet<Texture>();
            foreach (Material material in EditorUtility.CollectDependencies(new Object[] { avatar }).OfType<Material>())
            {
                Shader shader = material.shader;
                if (shader == null) continue;
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    if (shader.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
                    Texture texture = material.GetTexture(shader.GetPropertyNameId(i));
                    if (texture != null) textures.Add(texture);
                }
            }
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

namespace BSLK.Optimizer
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    // What occupies each club limit, largest first, with the object to select in the editor.
    internal static class BSLKClubInsights
    {
        internal sealed class Item
        {
            public string label;
            public double value;
            public Object target;
        }

        internal sealed class Section
        {
            public string title, unit;
            public double total, limit;
            public readonly List<Item> items = new List<Item>();
        }

        internal sealed class PhysBoneEntry
        {
            public string key, label;
            public Component physBone;
            public int transforms, colliders;
            public long Checks { get { return (long)transforms * colliders; } }
        }

        internal static List<Section> Collect(GameObject avatar, int top = 8)
        {
            var sections = new List<Section>();
            if (avatar == null) return sections;
            Transform root = avatar.transform;
            Func<Component, string> path = component => component.transform == root ? avatar.name : AnimationUtility.CalculateTransformPath(component.transform, root);

            var triangles = new Section { title = "Треугольники", unit = "", limit = BSLKClubOptimizer.TriangleLimit };
            var slots = new Section { title = "Material slots", unit = "", limit = BSLKClubOptimizer.SlotLimit };
            foreach (Renderer renderer in avatar.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = renderer is SkinnedMeshRenderer || renderer is MeshRenderer ? BSLKClubGeometry.MeshOf(renderer) : null;
                if (mesh != null) triangles.items.Add(new Item { label = path(renderer), value = BSLKClubGeometry.TriangleCount(mesh), target = renderer.gameObject });
                if (renderer.sharedMaterials.Length > 0) slots.items.Add(new Item { label = path(renderer), value = renderer.sharedMaterials.Length, target = renderer.gameObject });
            }
            var textures = new Section { title = "Память текстур 2D", unit = " MiB", limit = BSLKClubOptimizer.TextureLimit / 1048576.0 };
            foreach (Texture2D texture in BSLKClubOptimizer.Textures(avatar).OfType<Texture2D>())
                textures.items.Add(new Item { label = texture.name + " (" + texture.width + "×" + texture.height + ", " + texture.format + ")", value = BSLKClubTextures.Bytes(texture) / 1048576.0, target = texture });
            var particles = new Section { title = "Частицы (maxParticles)", unit = "", limit = BSLKClubOptimizer.ParticleLimit };
            foreach (ParticleSystem system in avatar.GetComponentsInChildren<ParticleSystem>(true))
                particles.items.Add(new Item { label = path(system), value = system.main.maxParticles, target = system.gameObject });
            var collisions = new Section { title = "PhysBone-коллизии (оценка: кости × коллайдеры)", unit = "", limit = BSLKClubOptimizer.CollisionLimit };
            foreach (PhysBoneEntry entry in PhysBones(avatar))
                if (entry.Checks > 0) collisions.items.Add(new Item { label = entry.label, value = entry.Checks, target = entry.physBone.gameObject });

            foreach (Section section in new[] { triangles, slots, textures, particles, collisions })
            {
                section.total = section.items.Sum(i => i.value);
                List<Item> sorted = section.items.OrderByDescending(i => i.value).Take(top).ToList();
                section.items.Clear();
                section.items.AddRange(sorted);
                sections.Add(section);
            }
            return sections;
        }

        internal static string Format(List<Section> sections)
        {
            var text = new StringBuilder();
            foreach (Section section in sections)
            {
                text.AppendLine(section.title + ": " + Number(section.total) + section.unit + " / " + Number(section.limit) + section.unit);
                foreach (Item item in section.items)
                    text.AppendLine("  " + Number(item.value) + section.unit + " (" + (section.total > 0 ? (item.value * 100 / section.total).ToString("F0") : "0") + "%) — " + item.label);
            }
            return text.ToString();
        }

        internal static string Number(double value)
        {
            return Math.Abs(value - Math.Round(value)) < 1e-9 ? ((long)Math.Round(value)).ToString() : value.ToString("F1");
        }

        // Estimate per PhysBone: transforms that simulate (ignore list excluded) × assigned colliders.
        internal static List<PhysBoneEntry> PhysBones(GameObject avatar)
        {
            var result = new List<PhysBoneEntry>();
            Transform root = avatar.transform;
            foreach (Transform owner in avatar.GetComponentsInChildren<Transform>(true))
            {
                Component[] bones = owner.GetComponents<Component>().Where(x => x != null && x.GetType().FullName == "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone").ToArray();
                for (int i = 0; i < bones.Length; i++)
                {
                    string ownerPath = owner == root ? "" : AnimationUtility.CalculateTransformPath(owner, root);
                    var entry = new PhysBoneEntry { key = ownerPath + "#" + i, label = (ownerPath == "" ? avatar.name : ownerPath) + (bones.Length > 1 ? " #" + (i + 1) : ""), physBone = bones[i] };
                    using (var serialized = new SerializedObject(bones[i]))
                    {
                        Transform chain = serialized.FindProperty("rootTransform")?.objectReferenceValue as Transform;
                        if (chain == null) chain = owner;
                        var ignored = new HashSet<Transform>();
                        SerializedProperty ignore = serialized.FindProperty("ignoreTransforms");
                        if (ignore != null && ignore.isArray)
                            for (int k = 0; k < ignore.arraySize; k++)
                                if (ignore.GetArrayElementAtIndex(k).objectReferenceValue is Transform t) ignored.Add(t);
                        var pending = new Stack<Transform>();
                        pending.Push(chain);
                        while (pending.Count > 0)
                        {
                            Transform t = pending.Pop();
                            // Disabled transforms do not simulate; this matches the SDK count on test avatars.
                            if (ignored.Contains(t) || !t.gameObject.activeInHierarchy) continue;
                            entry.transforms++;
                            foreach (Transform child in t) pending.Push(child);
                        }
                        var colliders = new HashSet<Object>();
                        SerializedProperty list = serialized.FindProperty("colliders");
                        if (list != null && list.isArray)
                            for (int k = 0; k < list.arraySize; k++)
                            {
                                Object collider = list.GetArrayElementAtIndex(k).objectReferenceValue;
                                if (collider != null) colliders.Add(collider);
                            }
                        entry.colliders = colliders.Count;
                    }
                    result.Add(entry);
                }
            }
            return result;
        }

        // Pick the shortest colliding chains (earrings, tassels, small accessories) first until the
        // estimate fits the limit. Long chains such as hair or skirts are chosen last.
        internal static HashSet<string> Suggest(List<PhysBoneEntry> entries, long limit)
        {
            var chosen = new HashSet<string>();
            long total = entries.Sum(e => e.Checks);
            foreach (PhysBoneEntry entry in entries.Where(e => e.Checks > 0).OrderBy(e => e.transforms).ThenBy(e => e.Checks))
            {
                if (total <= limit) break;
                chosen.Add(entry.key);
                total -= entry.Checks;
            }
            return chosen;
        }
    }
}
