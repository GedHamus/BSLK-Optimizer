using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BSLK.Optimizer
{
    public class BSLKOptimizerWindow : EditorWindow
    {
        [SerializeField] private GameObject avatar;
        [SerializeField] private bool singleOutfit = true;
        [SerializeField] private bool optimizeTextures = true;
        [SerializeField] private bool limitParticles = true;
        [SerializeField] private int minimumTextureSize = 512;
        [SerializeField] private float textureError = 0.02f;
        [SerializeField] private bool mergeTextures = true;
        [SerializeField] private bool mergeMaterials = true;
        [SerializeField] private bool mergeMeshes = true;
        [SerializeField] private bool removeUnusedShapesAndBones = true;
        [SerializeField] private bool keepMmdShapes = true;
        [SerializeField] private List<Texture> protectedTextures = new List<Texture>();
        [SerializeField] private List<string> stripColliders = new List<string>();
        [SerializeField] private string report = "";
        [SerializeField] private GameObject lastSource;
        [SerializeField] private GameObject lastResult;
        [SerializeField] private Renderer simplifyTarget;
        [SerializeField] private float simplifyRatio = 0.5f;
        [SerializeField] private bool simplifyLockShapes = true;
        [SerializeField] private Mesh simplifyOriginal;
        [SerializeField] private Mesh simplifyResult;
        private Vector2 scroll;
        private Vector2 reportScroll;
        private bool showTextureOptions;
        private bool showGeometryOptions;
        private bool showPhysBones;
        private bool showInsights = true;
        private bool showSimplify;
        private List<BSLKClubInsights.Section> insights;
        private List<BSLKClubInsights.PhysBoneEntry> physBones;
        private bool updating;
        private string updateStatus = "";

        [MenuItem("BSLK-Optimizer/BSLK-Optimizer")]
        private static void Open()
        {
            GetWindow<BSLKOptimizerWindow>("BSLK • Club").minSize = new Vector2(500, 580);
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Клубная версия аватара", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("500K треугольников • 50 слотов • 150 MiB текстур\n2000 частиц • 1500 PhysBone Collision Checks\nБез SPS, DPS и источников света", MessageType.Info);
            GameObject selected = (GameObject)EditorGUILayout.ObjectField("Исходный аватар", avatar, typeof(GameObject), true);
            if (selected != avatar) { avatar = selected; physBones = null; stripColliders.Clear(); }
            EditorGUILayout.HelpBox("В сцене включите нужную одежду и выключите остальные вещи. Изменения будут сохранены в отдельную копию и отдельные ассеты. Исходник останется без изменений.", MessageType.None);
            singleOutfit = EditorGUILayout.ToggleLeft("Один образ: удалить выключенное и закрепить одежду", singleOutfit);
            optimizeTextures = EditorGUILayout.ToggleLeft("Адаптивно уменьшать текстуры, даже ниже 150 MiB", optimizeTextures);
            limitParticles = EditorGUILayout.ToggleLeft("Распределить общий бюджет 2000 частиц", limitParticles);
            TextureOptions();
            GeometryOptions();
            PhysBoneOptions();
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(avatar == null || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Проверить лимиты без изменений"))
                {
                    report = BSLKClubOptimizer.Analyze(avatar);
                    insights = BSLKClubInsights.Collect(avatar);
                }
                // Long operations run after this GUI pass so progress bars and refreshes cannot break layout.
                if (GUILayout.Button("Создать клубную версию", GUILayout.Height(38))) EditorApplication.delayCall += Run;
            }
            if (lastResult != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Сравнение в сцене", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Показать исходник")) ShowComparison(false);
                if (GUILayout.Button("Показать " + BSLKClubOptimizer.Suffix)) ShowComparison(true);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.HelpBox("Обе версии сохранены. Эти кнопки только переключают их видимость в сцене; действие можно отменить через Undo.", MessageType.None);
                // VRCFury saves all modified project assets during a build, exactly as on upload.
                if (GUILayout.Button("Проверить после сборки (VRCFury и другие build-плагины)") &&
                    EditorUtility.DisplayDialog("BSLK-Optimizer", "Build-плагины будут запущены на временной копии, как при загрузке. VRCFury при этом сохраняет все несохранённые изменения ассетов проекта. Продолжить?", "Проверить", "Отмена"))
                    EditorApplication.delayCall += BuildCheck;
                SimplifyTool();
            }
            Insights();
            if (!string.IsNullOrEmpty(report))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Отчёт", EditorStyles.boldLabel);
                // The report has its own scroller so long statistics do not stretch the window.
                reportScroll = EditorGUILayout.BeginScrollView(reportScroll, GUILayout.Height(300));
                EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Версия " + BSLKOptimizer.Version + " • автор GedHamus", EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(updating))
                if (GUILayout.Button("Проверить обновления"))
                {
                    updating = true;
                    BSLKUpdater.CheckForUpdates(BSLKUpdater.Repo, status => { updateStatus = status; Repaint(); }, () => { updating = false; Repaint(); });
                }
            if (!string.IsNullOrEmpty(updateStatus)) EditorGUILayout.HelpBox(updateStatus, MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private void TextureOptions()
        {
            showTextureOptions = EditorGUILayout.Foldout(showTextureOptions, "Качество и защищённые текстуры", true);
            if (!showTextureOptions) return;
            minimumTextureSize = EditorGUILayout.IntPopup("Минимальный размер", minimumTextureSize,
                new[] { "256", "512", "1024" }, new[] { 256, 512, 1024 });
            int quality = textureError < 0.015f ? 0 : textureError < 0.03f ? 1 : 2;
            quality = EditorGUILayout.Popup("Сжатие", quality, new[] { "Бережное", "Сбалансированное", "Сильное" });
            textureError = new[] { 0.01f, 0.02f, 0.035f }[quality];
            EditorGUILayout.HelpBox("Бережный режим лучше сохраняет детали. Сравнение по пикселям не гарантирует незаметность изменений. Normal maps, маски, linear/HDR и текстуры >4096 защищены автоматически. Добавьте сюда лицо, глаза, надписи и другие важные текстуры.", MessageType.None);
            for (int i = 0; i < protectedTextures.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                protectedTextures[i] = (Texture)EditorGUILayout.ObjectField(protectedTextures[i], typeof(Texture), false);
                if (GUILayout.Button("×", GUILayout.Width(25))) { protectedTextures.RemoveAt(i); i--; }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Добавить исключение")) protectedTextures.Add(null);
        }

        private void GeometryOptions()
        {
            showGeometryOptions = EditorGUILayout.Foldout(showGeometryOptions, "Меши, материалы и blend shapes", true);
            if (!showGeometryOptions) return;
            mergeTextures = EditorGUILayout.ToggleLeft("Объединять одинаковые по содержимому текстуры", mergeTextures);
            mergeMaterials = EditorGUILayout.ToggleLeft("Объединять одинаковые материалы и их слоты", mergeMaterials);
            mergeMeshes = EditorGUILayout.ToggleLeft("Объединять меши одной вещи в один рендерер", mergeMeshes);
            removeUnusedShapesAndBones = EditorGUILayout.ToggleLeft("Удалять неиспользуемые blend shapes и привязки костей", removeUnusedShapesAndBones);
            using (new EditorGUI.DisabledScope(!removeUnusedShapesAndBones))
                keepMmdShapes = EditorGUILayout.ToggleLeft("    Сохранять MMD-ключи лица", keepMmdShapes);
            EditorGUILayout.HelpBox("Не трогаются меши, на которые ссылаются компоненты (лицо, visemes, VRCFury), рендереры с анимацией материалов или включения и всё под вложенными Animator. Сохраняются blend shapes из анимаций, компонентов и век.", MessageType.None);
        }

        private void PhysBoneOptions()
        {
            showPhysBones = EditorGUILayout.Foldout(showPhysBones, "PhysBone-коллизии" + (stripColliders.Count > 0 ? " (отключить у " + stripColliders.Count + ")" : ""), true);
            if (!showPhysBones) return;
            using (new EditorGUI.DisabledScope(avatar == null))
                if (GUILayout.Button("Рассчитать по исходнику")) physBones = BSLKClubInsights.PhysBones(avatar);
            if (physBones == null) return;
            List<BSLKClubInsights.PhysBoneEntry> colliding = physBones.Where(e => e.Checks > 0).OrderByDescending(e => e.Checks).ToList();
            long total = colliding.Sum(e => e.Checks), kept = colliding.Where(e => !stripColliders.Contains(e.key)).Sum(e => e.Checks);
            EditorGUILayout.HelpBox("Оценка: " + total + " → " + kept + " / " + BSLKClubOptimizer.CollisionLimit +
                ". Отмеченным цепочкам коллизии отключаются только в клубной копии. Точное значение даёт статистика SDK в отчёте.", kept > BSLKClubOptimizer.CollisionLimit ? MessageType.Warning : MessageType.None);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Предложить автоматически")) { stripColliders.Clear(); stripColliders.AddRange(BSLKClubInsights.Suggest(physBones, BSLKClubOptimizer.CollisionLimit)); }
            if (GUILayout.Button("Снять все")) stripColliders.Clear();
            EditorGUILayout.EndHorizontal();
            foreach (BSLKClubInsights.PhysBoneEntry entry in colliding)
            {
                EditorGUILayout.BeginHorizontal();
                bool strip = stripColliders.Contains(entry.key);
                bool now = EditorGUILayout.ToggleLeft(entry.label, strip);
                if (now != strip) { if (now) stripColliders.Add(entry.key); else stripColliders.Remove(entry.key); }
                GUILayout.Label(entry.transforms + " × " + entry.colliders + " = " + entry.Checks, GUILayout.Width(130));
                if (GUILayout.Button("Выбрать", GUILayout.Width(70)) && entry.physBone != null) Select(entry.physBone.gameObject);
                EditorGUILayout.EndHorizontal();
            }
        }

        private void Insights()
        {
            if (insights == null || insights.Count == 0) return;
            EditorGUILayout.Space();
            showInsights = EditorGUILayout.Foldout(showInsights, "Кто занимает лимиты", true, EditorStyles.foldoutHeader);
            if (!showInsights) return;
            foreach (BSLKClubInsights.Section section in insights)
            {
                EditorGUILayout.LabelField(section.title + ": " + BSLKClubInsights.Number(section.total) + section.unit + " / " + BSLKClubInsights.Number(section.limit) + section.unit,
                    section.total > section.limit ? EditorStyles.boldLabel : EditorStyles.label);
                foreach (BSLKClubInsights.Item item in section.items)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(BSLKClubInsights.Number(item.value) + section.unit, GUILayout.Width(90));
                    GUILayout.Label(item.label, EditorStyles.miniLabel, GUILayout.MinWidth(100));
                    if (GUILayout.Button("Выбрать", GUILayout.Width(70)) && item.target != null) Select(item.target);
                    EditorGUILayout.EndHorizontal();
                }
            }
        }

        private void SimplifyTool()
        {
            showSimplify = EditorGUILayout.Foldout(showSimplify, "Упрощение геометрии (вручную, только в " + BSLKClubOptimizer.Suffix + ")", true);
            if (!showSimplify) return;
            Renderer target = (Renderer)EditorGUILayout.ObjectField("Меш в копии", simplifyTarget, typeof(Renderer), true);
            if (target != simplifyTarget) { simplifyTarget = target; simplifyOriginal = null; simplifyResult = null; }
            if (simplifyTarget != null && !simplifyTarget.transform.IsChildOf(lastResult.transform))
            {
                EditorGUILayout.HelpBox("Выберите рендерер внутри созданной копии " + lastResult.name + ". Исходник не упрощается.", MessageType.Warning);
                return;
            }
            simplifyRatio = EditorGUILayout.Slider("Оставить треугольников, %", simplifyRatio * 100, 10, 95) / 100;
            simplifyLockShapes = EditorGUILayout.ToggleLeft("Не трогать вершины blend shapes (лицо, мимика, shrink)", simplifyLockShapes);
            Mesh current = simplifyTarget != null ? BSLKClubGeometry.MeshOf(simplifyTarget) : null;
            if (current != null) EditorGUILayout.LabelField("Сейчас треугольников: " + BSLKClubGeometry.TriangleCount(current));
            using (new EditorGUI.DisabledScope(current == null))
                if (GUILayout.Button("Упростить")) Simplify();
            if (simplifyOriginal == null || simplifyResult == null) return;
            EditorGUILayout.LabelField("Оригинал " + BSLKClubGeometry.TriangleCount(simplifyOriginal) + " → упрощённый " + BSLKClubGeometry.TriangleCount(simplifyResult));
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Показать оригинал")) SetMesh(simplifyOriginal);
            if (GUILayout.Button("Показать упрощённый")) SetMesh(simplifyResult);
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Сохранить текущий вариант в prefab")) SaveSimplified();
        }

        private void Simplify()
        {
            Mesh current = BSLKClubGeometry.MeshOf(simplifyTarget);
            if (simplifyOriginal == null || (current != simplifyOriginal && current != simplifyResult)) simplifyOriginal = current;
            string folder = ResultFolder();
            try
            {
                Mesh mesh = BSLKClubGeometry.Simplify(simplifyOriginal, simplifyRatio, simplifyLockShapes);
                AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + mesh.name + ".asset"));
                simplifyResult = mesh;
                SetMesh(mesh);
            }
            catch (Exception exception) { EditorUtility.DisplayDialog("BSLK-Optimizer", "Не удалось упростить: " + exception.Message, "OK"); }
        }

        private string ResultFolder()
        {
            string prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(lastResult);
            if (string.IsNullOrEmpty(prefab) || !prefab.StartsWith(BSLKClubOptimizer.OutputRoot + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("Копия не связана с prefab в папке результатов.");
            return Path.GetDirectoryName(prefab).Replace('\\', '/');
        }

        private void SetMesh(Mesh mesh)
        {
            Object holder = simplifyTarget is SkinnedMeshRenderer ? (Object)simplifyTarget : simplifyTarget.GetComponent<MeshFilter>();
            Undo.RecordObject(holder, "Упрощение геометрии");
            BSLKClubGeometry.Assign(simplifyTarget, mesh);
        }

        private void SaveSimplified()
        {
            Object holder = simplifyTarget is SkinnedMeshRenderer ? (Object)simplifyTarget : simplifyTarget.GetComponent<MeshFilter>();
            string prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(lastResult);
            ResultFolder();
            PrefabUtility.ApplyObjectOverride(holder, prefab, InteractionMode.UserAction);
            report += "\n\nУпрощение: " + simplifyTarget.name + " — " + BSLKClubGeometry.TriangleCount(BSLKClubGeometry.MeshOf(simplifyTarget)) + " треугольников сохранено в " + prefab + ".";
            insights = BSLKClubInsights.Collect(lastResult);
        }

        private void BuildCheck()
        {
            GameObject target = lastResult != null ? lastResult : avatar;
            if (target == null) return;
            report += "\n\nПРОВЕРКА ПОСЛЕ СБОРКИ (" + target.name + ")\n" +
                (BSLKClubSdk.TryAnalyzeBuilt(target, out string built, out string reason) ? built : "НЕ ПРОВЕРЕНО: " + reason);
            EditorUtility.ClearProgressBar();
            Repaint();
        }

        private static void Select(Object target)
        {
            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
        }

        private void Run()
        {
            if (avatar == null) return;
            var options = new BSLKClubOptimizer.Options
            {
                singleOutfit = singleOutfit, optimizeTextures = optimizeTextures,
                limitParticles = limitParticles, minimumTextureSize = minimumTextureSize, textureError = textureError,
                mergeTextures = mergeTextures, mergeMaterials = mergeMaterials, mergeMeshes = mergeMeshes,
                removeUnusedShapesAndBones = removeUnusedShapesAndBones, keepMmdShapes = keepMmdShapes
            };
            foreach (Texture texture in protectedTextures) if (texture != null) options.protectedTextures.Add(texture);
            options.stripColliders.UnionWith(stripColliders);
            try
            {
                BSLKClubOptimizer.Result result = BSLKClubOptimizer.Create(avatar, options);
                lastSource = avatar;
                lastResult = result.avatar;
                report = result.report;
                insights = BSLKClubInsights.Collect(lastResult);
                simplifyTarget = null; simplifyOriginal = null; simplifyResult = null;
                lastResult.SetActive(false);
                Debug.Log("[BSLK-Optimizer]\n" + report, lastResult);
            }
            catch (OperationCanceledException exception) { report = exception.Message; }
            catch (Exception exception)
            {
                report = "Клубная копия не создана: " + exception.Message;
                Debug.LogException(exception);
            }
            finally { EditorUtility.ClearProgressBar(); Repaint(); }
        }

        private void ShowComparison(bool club)
        {
            if (lastSource != null)
            {
                Undo.RecordObject(lastSource, "Сравнить клубный аватар");
                lastSource.SetActive(!club);
            }
            if (lastResult != null)
            {
                Undo.RecordObject(lastResult, "Сравнить клубный аватар");
                lastResult.SetActive(club);
                Selection.activeGameObject = club ? lastResult : lastSource;
            }
        }
    }
}
