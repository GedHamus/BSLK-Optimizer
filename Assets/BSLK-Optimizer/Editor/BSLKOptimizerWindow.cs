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
        [SerializeField] private bool removeHiddenBody = true;
        [SerializeField] private bool hiddenUnderToggledClothing = true;
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
        [SerializeField] private int tab;
        private Vector2 scroll;
        private Vector2 reportScroll;
        private bool showTextureOptions;
        private bool showGeometryOptions;
        private List<BSLKClubInsights.Section> insights;
        private List<BSLKClubInsights.PhysBoneEntry> physBones;
        private List<Renderer> simplified;
        private List<PlanEntry> autoPlan;
        private string autoPlanNote = "";
        private string hiddenNote = "";
        [SerializeField] private int autoBudget = (int)BSLKClubOptimizer.TriangleLimit;
        [SerializeField] private bool autoProtectFaceHands = true;
        private bool updating;
        private string updateStatus = "";

        // ---- Look: BELKA&STRELKA club colours.
        private static readonly Color Orange = new Color32(255, 90, 0, 255);
        private static readonly Color Blue = new Color32(0, 163, 255, 255);
        private static readonly Color Header = new Color32(24, 25, 30, 255);
        private static readonly Color Track = new Color(0.5f, 0.5f, 0.5f, 0.18f);
        private static readonly Color Good = new Color32(64, 186, 110, 255);
        private static readonly Color Bad = new Color32(230, 70, 60, 255);
        private static readonly string[] Tabs = { "Создание", "Лимиты", "Инструменты", "О плагине" };
        private GUIStyle title, subtitle, chip, card, cardTitle, bigButton, link, small, wrap;

        [MenuItem("BSLK-Optimizer/BSLK-Optimizer")]
        private static void Open()
        {
            var window = GetWindow<BSLKOptimizerWindow>("BSLK Optimizer");
            window.minSize = new Vector2(520, 620);
        }

        private void Styles()
        {
            if (title != null) return;
            title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 20, normal = { textColor = Color.white } };
            subtitle = new GUIStyle(EditorStyles.label) { fontSize = 11, normal = { textColor = new Color(1, 1, 1, 0.7f) } };
            chip = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            card = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(12, 12, 10, 10), margin = new RectOffset(8, 8, 6, 6) };
            cardTitle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            bigButton = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold, fixedHeight = 40, normal = { textColor = Color.white }, hover = { textColor = Color.white } };
            link = new GUIStyle(EditorStyles.label) { normal = { textColor = Blue }, hover = { textColor = Orange }, fontStyle = FontStyle.Bold };
            small = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            wrap = new GUIStyle(EditorStyles.label) { wordWrap = true };
        }

        private void OnGUI()
        {
            Styles();
            DrawHeader();
            tab = GUILayout.Toolbar(tab, Tabs, GUILayout.Height(28));
            scroll = EditorGUILayout.BeginScrollView(scroll);
            GUILayout.Space(4);
            switch (tab)
            {
                case 0: CreateTab(); break;
                case 1: LimitsTab(); break;
                case 2: ToolsTab(); break;
                default: AboutTab(); break;
            }
            EditorGUILayout.EndScrollView();
            Footer();
        }

        private void DrawHeader()
        {
            Rect area = GUILayoutUtility.GetRect(position.width, 92);
            EditorGUI.DrawRect(area, Header);
            EditorGUI.DrawRect(new Rect(area.x, area.yMax - 3, area.width / 2, 3), Orange);
            EditorGUI.DrawRect(new Rect(area.x + area.width / 2, area.yMax - 3, area.width / 2, 3), Blue);
            GUI.Label(new Rect(area.x + 14, area.y + 12, area.width - 28, 28), "BSLK Optimizer", title);
            GUI.Label(new Rect(area.x + 15, area.y + 38, area.width - 28, 18), "Клубная версия аватара • BELKA&STRELKA • v" + BSLKOptimizer.Version, subtitle);
            string[] limits = { "500K треуг.", "50 слотов", "150 MiB", "2000 частиц", "1500 коллизий", "без SPS/DPS/света" };
            float x = area.x + 15;
            for (int i = 0; i < limits.Length; i++)
            {
                float width = chip.CalcSize(new GUIContent(limits[i])).x + 12;
                if (x + width > area.xMax - 8) break;
                Rect pill = new Rect(x, area.y + 60, width, 18);
                EditorGUI.DrawRect(pill, i % 2 == 0 ? new Color(Orange.r, Orange.g, Orange.b, 0.85f) : new Color(Blue.r, Blue.g, Blue.b, 0.85f));
                GUI.Label(pill, limits[i], chip);
                x += width + 5;
            }
        }

        private void Footer()
        {
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(position.width, 1), Track);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("По любым вопросам пишите " + BSLKOptimizer.Contact, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Discord BELKA&STRELKA", link)) Application.OpenURL(BSLKOptimizer.Discord);
            EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
            EditorGUILayout.EndHorizontal();
        }

        private bool BeginCard(string heading, Color accent)
        {
            EditorGUILayout.BeginVertical(card);
            Rect line = GUILayoutUtility.GetRect(10, 20);
            EditorGUI.DrawRect(new Rect(line.x - 12, line.y, 3, line.height), accent);
            GUI.Label(line, heading, cardTitle);
            return true;
        }

        private static void EndCard() { EditorGUILayout.EndVertical(); }

        private bool ColoredButton(string text, Color color)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = color * 1.4f;
            bool clicked = GUILayout.Button(text, bigButton);
            GUI.backgroundColor = previous;
            return clicked;
        }

        private void Bar(double value, double limit, string text)
        {
            Rect rect = GUILayoutUtility.GetRect(10, 20);
            EditorGUI.DrawRect(rect, Track);
            float fraction = limit > 0 ? Mathf.Clamp01((float)(value / limit)) : 0;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width * fraction, rect.height), value > limit ? Bad : fraction > 0.85f ? Orange : Good);
            GUI.Label(new Rect(rect.x + 6, rect.y + 2, rect.width - 12, rect.height), text, EditorStyles.boldLabel);
        }

        // ---- Tabs

        private void CreateTab()
        {
            BeginCard("1. Аватар", Orange);
            GameObject selected = (GameObject)EditorGUILayout.ObjectField("Исходный аватар", avatar, typeof(GameObject), true);
            if (selected != avatar) { avatar = selected; physBones = null; stripColliders.Clear(); }
            GUILayout.Label("В сцене включите нужную одежду и выключите остальное. Плагин создаст отдельную копию «[" + BSLKClubOptimizer.Suffix +
                "]» и отдельные ассеты; исходник и его файлы не меняются.", small);
            EndCard();

            BeginCard("2. Что сделать", Blue);
            singleOutfit = EditorGUILayout.ToggleLeft("Один образ: удалить выключенное и закрепить одежду", singleOutfit);
            optimizeTextures = EditorGUILayout.ToggleLeft("Адаптивно уменьшать текстуры, даже ниже 150 MiB", optimizeTextures);
            limitParticles = EditorGUILayout.ToggleLeft("Распределить общий бюджет 2000 частиц", limitParticles);
            showGeometryOptions = EditorGUILayout.Foldout(showGeometryOptions, "Меши, материалы и blend shapes", true);
            if (showGeometryOptions)
            {
                EditorGUI.indentLevel++;
                mergeTextures = EditorGUILayout.ToggleLeft("Объединять одинаковые по содержимому текстуры", mergeTextures);
                mergeMaterials = EditorGUILayout.ToggleLeft("Объединять одинаковые материалы и их слоты", mergeMaterials);
                mergeMeshes = EditorGUILayout.ToggleLeft("Объединять меши одной вещи в один рендерер", mergeMeshes);
                removeUnusedShapesAndBones = EditorGUILayout.ToggleLeft("Удалять неиспользуемые blend shapes и привязки костей", removeUnusedShapesAndBones);
                using (new EditorGUI.DisabledScope(!removeUnusedShapesAndBones))
                    keepMmdShapes = EditorGUILayout.ToggleLeft("Сохранять MMD-ключи лица", keepMmdShapes);
                removeHiddenBody = EditorGUILayout.ToggleLeft("Вырезать тело под одеждой, если после удаления выключенного всё ещё больше 500K", removeHiddenBody);
                using (new EditorGUI.DisabledScope(!removeHiddenBody))
                    hiddenUnderToggledClothing = EditorGUILayout.ToggleLeft(ToggledCoverLabel, hiddenUnderToggledClothing);
                GUILayout.Label("Лицо, visemes, всё, на что ссылаются компоненты (VRCFury и др.), рендереры с анимацией материалов или включения и объекты под вложенными Animator не объединяются.", small);
                EditorGUI.indentLevel--;
            }
            showTextureOptions = EditorGUILayout.Foldout(showTextureOptions, "Качество и защищённые текстуры", true);
            if (showTextureOptions)
            {
                EditorGUI.indentLevel++;
                minimumTextureSize = EditorGUILayout.IntPopup("Минимальный размер", minimumTextureSize, new[] { "256", "512", "1024" }, new[] { 256, 512, 1024 });
                int quality = textureError < 0.015f ? 0 : textureError < 0.03f ? 1 : 2;
                quality = EditorGUILayout.Popup("Сжатие", quality, new[] { "Бережное", "Сбалансированное", "Сильное" });
                textureError = new[] { 0.01f, 0.02f, 0.035f }[quality];
                GUILayout.Label("Normal maps, маски, linear/HDR и текстуры >4096 защищены автоматически. Добавьте лицо, глаза, надписи и другие важные текстуры.", small);
                for (int i = 0; i < protectedTextures.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    protectedTextures[i] = (Texture)EditorGUILayout.ObjectField(protectedTextures[i], typeof(Texture), false);
                    if (GUILayout.Button("×", GUILayout.Width(25))) { protectedTextures.RemoveAt(i); i--; }
                    EditorGUILayout.EndHorizontal();
                }
                if (GUILayout.Button("Добавить исключение")) protectedTextures.Add(null);
                EditorGUI.indentLevel--;
            }
            if (stripColliders.Count > 0)
                GUILayout.Label("PhysBone: коллизии будут отключены у " + stripColliders.Count + " цепочек (вкладка «Инструменты»).", small);
            EndCard();

            BeginCard("3. Запуск", Orange);
            using (new EditorGUI.DisabledScope(avatar == null || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                // Long operations run after this GUI pass so progress bars and refreshes cannot break layout.
                if (ColoredButton("Создать клубную версию", Orange)) EditorApplication.delayCall += Run;
                if (GUILayout.Button("Проверить лимиты без изменений", GUILayout.Height(26)))
                {
                    report = BSLKClubOptimizer.Analyze(avatar);
                    insights = BSLKClubInsights.Collect(avatar);
                    tab = 1;
                }
            }
            EndCard();

            if (lastResult != null)
            {
                BeginCard("Сравнение в сцене", Blue);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Показать исходник", GUILayout.Height(26))) ShowComparison(false);
                if (GUILayout.Button("Показать " + BSLKClubOptimizer.Suffix, GUILayout.Height(26))) ShowComparison(true);
                EditorGUILayout.EndHorizontal();
                if (GUILayout.Button("Открыть сравнение рядом", GUILayout.Height(26))) BSLKCompareWindow.Open(lastSource, lastResult);
                GUILayout.Label("Кнопки только переключают видимость в сцене; действие отменяется через Undo. Отчёт и лимиты — на вкладке «Лимиты».", small);
                EndCard();
            }
        }

        private void LimitsTab()
        {
            if (insights == null || insights.Count == 0)
            {
                BeginCard("Лимиты", Blue);
                GUILayout.Label("Нажмите «Проверить лимиты» или создайте клубную версию на вкладке «Создание».", wrap);
                EndCard();
            }
            else
                foreach (BSLKClubInsights.Section section in insights)
                {
                    BeginCard(section.title, section.total > section.limit ? Bad : Blue);
                    Bar(section.total, section.limit, BSLKClubInsights.Number(section.total) + section.unit + " / " + BSLKClubInsights.Number(section.limit) + section.unit);
                    foreach (BSLKClubInsights.Item item in section.items)
                    {
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Label(BSLKClubInsights.Number(item.value) + section.unit, EditorStyles.boldLabel, GUILayout.Width(90));
                        GUILayout.Label(item.label, EditorStyles.miniLabel, GUILayout.MinWidth(100));
                        if (GUILayout.Button("Выбрать", GUILayout.Width(70)) && item.target != null) Select(item.target);
                        EditorGUILayout.EndHorizontal();
                    }
                    EndCard();
                }
            if (string.IsNullOrEmpty(report)) return;
            BeginCard("Отчёт", Orange);
            // The report has its own scroller so long statistics do not stretch the window.
            reportScroll = EditorGUILayout.BeginScrollView(reportScroll, GUILayout.Height(300));
            EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            EndCard();
        }

        private void ToolsTab()
        {
            PhysBoneTool();
            BeginCard("Проверка после сборки", Orange);
            GUILayout.Label("VRCFury и другие build-плагины запускаются на временной копии, как при загрузке, и лимиты считаются на результате.", small);
            using (new EditorGUI.DisabledScope(lastResult == null && avatar == null))
                // VRCFury saves all modified project assets during a build, exactly as on upload.
                if (GUILayout.Button("Проверить после сборки", GUILayout.Height(26)) &&
                    EditorUtility.DisplayDialog("BSLK Optimizer", "Build-плагины будут запущены на временной копии, как при загрузке. VRCFury при этом сохраняет все несохранённые изменения ассетов проекта. Продолжить?", "Проверить", "Отмена"))
                    EditorApplication.delayCall += BuildCheck;
            EndCard();
            HiddenBodyTool();
            AutoSimplifyTool();
            SimplifyTool();
        }

        private void AboutTab()
        {
            BeginCard("BSLK Optimizer " + BSLKOptimizer.Version, Orange);
            GUILayout.Label("Готовит отдельную клубную версию аватара VRChat под правила BELKA&STRELKA. Исходный аватар и его файлы не меняются.", wrap);
            GUILayout.Space(6);
            GUILayout.Label("По любым вопросам пишите " + BSLKOptimizer.Contact, EditorStyles.boldLabel);
            if (ColoredButton("Наш Discord: BELKA&STRELKA", Blue)) Application.OpenURL(BSLKOptimizer.Discord);
            EndCard();

            BeginCard("Обновления", Blue);
            GUILayout.Label("Новая версия ставится автоматически при запуске Unity.", small);
            using (new EditorGUI.DisabledScope(updating))
                if (GUILayout.Button("Проверить обновления сейчас", GUILayout.Height(26)))
                {
                    updating = true;
                    BSLKUpdater.CheckForUpdates(BSLKUpdater.Repo, status => { updateStatus = status; Repaint(); }, () => { updating = false; Repaint(); });
                }
            if (!string.IsNullOrEmpty(updateStatus)) GUILayout.Label(updateStatus, small);
            EndCard();
        }

        private void PhysBoneTool()
        {
            BeginCard("PhysBone-коллизии" + (stripColliders.Count > 0 ? " — отключить у " + stripColliders.Count : ""), Blue);
            using (new EditorGUI.DisabledScope(avatar == null))
                if (GUILayout.Button("Рассчитать по исходнику", GUILayout.Height(24))) physBones = BSLKClubInsights.PhysBones(avatar);
            if (physBones == null) { EndCard(); return; }
            List<BSLKClubInsights.PhysBoneEntry> colliding = physBones.Where(e => e.Checks > 0).OrderByDescending(e => e.Checks).ToList();
            long total = colliding.Sum(e => e.Checks), kept = colliding.Where(e => !stripColliders.Contains(e.key)).Sum(e => e.Checks);
            Bar(kept, BSLKClubOptimizer.CollisionLimit, "Оценка: " + total + " → " + kept + " / " + BSLKClubOptimizer.CollisionLimit);
            GUILayout.Label("Отмеченным цепочкам коллизии отключаются только в клубной копии. Точное значение даёт статистика SDK в отчёте.", small);
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
            EndCard();
        }

        private bool InResult(Renderer renderer)
        {
            return lastResult != null && renderer != null && renderer.transform.IsChildOf(lastResult.transform);
        }

        // Works on any renderer in the scene. The simplified mesh is always a new asset whose .meta
        // remembers the original mesh, so every simplified object can be restored later, even after
        // restarting Unity. The original mesh file is never edited.
        private void SimplifyTool()
        {
            BeginCard("Упрощение геометрии (вручную)", Orange);
            Renderer target = (Renderer)EditorGUILayout.ObjectField("Меш в сцене", simplifyTarget, typeof(Renderer), true);
            if (target != simplifyTarget) { simplifyTarget = target; simplifyOriginal = null; simplifyResult = null; }
            if (simplifyTarget != null && EditorUtility.IsPersistent(simplifyTarget))
            {
                EditorGUILayout.HelpBox("Выберите объект в сцене, а не prefab в папке проекта.", MessageType.Warning);
                simplifyTarget = null;
            }
            if (simplifyTarget != null && !InResult(simplifyTarget))
                GUILayout.Label("Объект в сцене получит новый упрощённый меш; файл исходного меша не меняется. Все упрощённые объекты перечислены ниже, их можно вернуть по одному или все сразу.", small);
            simplifyRatio = EditorGUILayout.Slider("Оставить треугольников, %", simplifyRatio * 100, 10, 95) / 100;
            simplifyLockShapes = EditorGUILayout.ToggleLeft("Сохранять форму blend shapes (лицо, мимика, shrink)", simplifyLockShapes);
            Mesh current = simplifyTarget != null ? BSLKClubGeometry.MeshOf(simplifyTarget) : null;
            if (current != null)
            {
                Mesh original = OriginalOf(current);
                GUILayout.Label("Сейчас треугольников: " + BSLKClubGeometry.TriangleCount(current) +
                    (original != null ? " (оригинал " + BSLKClubGeometry.TriangleCount(original) + ")" : ""), small);
            }
            using (new EditorGUI.DisabledScope(current == null))
                if (GUILayout.Button("Упростить", GUILayout.Height(26))) Simplify();
            if (simplifyTarget != null && simplifyOriginal != null && simplifyResult != null)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Показать оригинал")) { SetMesh(simplifyTarget, simplifyOriginal); simplified = null; }
                if (GUILayout.Button("Показать упрощённый")) { SetMesh(simplifyTarget, simplifyResult); simplified = null; }
                EditorGUILayout.EndHorizontal();
                if (InResult(simplifyTarget) && GUILayout.Button("Сохранить текущий вариант в prefab " + BSLKClubOptimizer.Suffix)) SaveSimplified();
            }

            if (simplified == null) simplified = FindSimplified();
            GUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Изменённые меши в сцене (упрощение, вырезание): " + simplified.Count, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Обновить", GUILayout.Width(80))) simplified = FindSimplified();
            EditorGUILayout.EndHorizontal();
            Renderer restore = null;
            foreach (Renderer renderer in simplified)
            {
                if (renderer == null) continue;
                Mesh mesh = BSLKClubGeometry.MeshOf(renderer), original = OriginalOf(mesh);
                if (original == null) continue;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(ObjectPath(renderer), EditorStyles.miniLabel, GUILayout.MinWidth(120));
                GUILayout.Label(BSLKClubGeometry.TriangleCount(original) + " → " + BSLKClubGeometry.TriangleCount(mesh), GUILayout.Width(110));
                if (GUILayout.Button("Вернуть", GUILayout.Width(70))) restore = renderer;
                if (GUILayout.Button("Выбрать", GUILayout.Width(70))) Select(renderer.gameObject);
                EditorGUILayout.EndHorizontal();
            }
            if (restore != null) { SetMesh(restore, OriginalOf(BSLKClubGeometry.MeshOf(restore))); simplified = null; }
            using (new EditorGUI.DisabledScope(simplified.Count == 0))
                if (GUILayout.Button("Вернуть все оригиналы", GUILayout.Height(24))) RestoreAll();
            EndCard();
        }

        private static string ObjectPath(Component component)
        {
            Transform root = component.transform.root;
            return component.transform == root ? root.name : root.name + "/" + AnimationUtility.CalculateTransformPath(component.transform, root);
        }

        // ---- Actions

        private const string OriginalMarker = "bslk-original:";

        private static Mesh OriginalOf(Mesh mesh)
        {
            if (mesh == null) return null;
            AssetImporter importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mesh));
            if (importer == null || importer.userData == null || !importer.userData.StartsWith(OriginalMarker, StringComparison.Ordinal)) return null;
            return GlobalObjectId.TryParse(importer.userData.Substring(OriginalMarker.Length), out GlobalObjectId id)
                ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Mesh : null;
        }

        private static List<Renderer> FindSimplified()
        {
            var result = new List<Renderer>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                        if ((renderer is SkinnedMeshRenderer || renderer is MeshRenderer) && OriginalOf(BSLKClubGeometry.MeshOf(renderer)) != null) result.Add(renderer);
            }
            return result;
        }

        private void RestoreAll()
        {
            Undo.SetCurrentGroupName("Вернуть оригинальные меши");
            int group = Undo.GetCurrentGroup();
            foreach (Renderer renderer in FindSimplified()) SetMesh(renderer, OriginalOf(BSLKClubGeometry.MeshOf(renderer)));
            Undo.CollapseUndoOperations(group);
            simplified = null;
            simplifyOriginal = null; simplifyResult = null;
        }

        private void Simplify()
        {
            Mesh current = BSLKClubGeometry.MeshOf(simplifyTarget);
            simplifyOriginal = OriginalOf(current) ?? current;
            try { simplifyResult = SimplifyRenderer(simplifyTarget, simplifyRatio, null); }
            catch (Exception exception) { EditorUtility.DisplayDialog("BSLK Optimizer", "Не удалось упростить: " + exception.Message, "OK"); }
        }

        // Always simplifies from the true original, never from an already simplified mesh, and records
        // that original in the new asset's .meta so it can be restored later.
        private Mesh SimplifyRenderer(Renderer renderer, float ratio, ICollection<int> protectedBones)
        {
            Mesh current = BSLKClubGeometry.MeshOf(renderer);
            Mesh original = OriginalOf(current) ?? current;
            string folder = InResult(renderer) ? ResultFolder() : SimplifiedFolder();
            Mesh mesh = BSLKClubGeometry.Simplify(original, ratio, simplifyLockShapes, protectedBones);
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + mesh.name + ".asset");
            AssetDatabase.CreateAsset(mesh, path);
            AssetImporter importer = AssetImporter.GetAtPath(path);
            importer.userData = OriginalMarker + GlobalObjectId.GetGlobalObjectIdSlow(original);
            importer.SaveAndReimport();
            Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            SetMesh(renderer, saved);
            simplified = null;
            return saved;
        }

        // ---- Body under clothing, on demand

        private const string ToggledCoverLabel = "Считать укрытием и одежду с тогглом (если снять её в игре, под ней будет пусто)";

        private void HiddenBodyTool()
        {
            BeginCard("Вырезать тело под одеждой", Orange);
            GameObject target = AutoTarget;
            if (target == null)
            {
                GUILayout.Label("Выберите аватар на вкладке «Создание».", small);
                EndCard();
                return;
            }
            GUILayout.Label("Аватар: " + target.name + (target == lastResult ? " (клубная копия)" : " (исходник: в сцене получит новые меши, файлы моделей не меняются)") +
                ". Убираются треугольники тела, полностью закрытые надетой одеждой в текущей позе. Прозрачная одежда укрытием не считается. Вернуть можно в списке изменённых мешей ниже.", small);
            hiddenUnderToggledClothing = EditorGUILayout.ToggleLeft(ToggledCoverLabel, hiddenUnderToggledClothing);
            if (ColoredButton("Вырезать скрытое тело", Orange)) EditorApplication.delayCall += CutHiddenBody;
            if (!string.IsNullOrEmpty(hiddenNote)) GUILayout.Label(hiddenNote, wrap);
            EndCard();
        }

        private void CutHiddenBody()
        {
            GameObject target = AutoTarget;
            if (target == null) return;
            List<AnimationClip> clips = BSLKClubOptimizer.AvatarClips(target).ToList();
            HashSet<Renderer> locked = BSLKClubOptimizer.RuntimeRenderers(target, clips, out HashSet<Renderer> toggleable);
            SkinnedMeshRenderer[] renderers = target.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var before = renderers.ToDictionary(r => r, r => r.sharedMesh);
            long trianglesBefore = BSLKClubOptimizer.TriangleTotal(target);
            var notes = new List<string>();
            Undo.SetCurrentGroupName("Вырезать тело под одеждой");
            int group = Undo.GetCurrentGroup();
            Undo.RecordObjects(renderers, "Вырезать тело под одеждой");
            try
            {
                string folder = target == lastResult ? ResultFolder() : SimplifiedFolder();
                BSLKClubOcclusion.RemoveHiddenTriangles(target, locked, renderer => hiddenUnderToggledClothing || !toggleable.Contains(renderer), mesh =>
                {
                    AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + mesh.name + "_hidden.asset"));
                    return mesh;
                }, notes, BSLKClubOptimizer.LiveShapes(target, clips, hiddenUnderToggledClothing));
                // Remember each original so the cut can be undone from the list of changed meshes later.
                foreach (SkinnedMeshRenderer renderer in renderers)
                {
                    if (renderer.sharedMesh == before[renderer] || before[renderer] == null) continue;
                    string path = AssetDatabase.GetAssetPath(renderer.sharedMesh);
                    AssetImporter importer = AssetImporter.GetAtPath(path);
                    importer.userData = OriginalMarker + GlobalObjectId.GetGlobalObjectIdSlow(OriginalOf(before[renderer]) ?? before[renderer]);
                    importer.SaveAndReimport();
                    renderer.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (PrefabUtility.IsPartOfPrefabInstance(renderer)) PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
                hiddenNote = string.Join("\n", notes) + "\nТреугольников: " + trianglesBefore + " → " + BSLKClubOptimizer.TriangleTotal(target) + ".";
            }
            catch (OperationCanceledException) { hiddenNote = "Отменено."; }
            catch (Exception exception) { hiddenNote = "Не удалось вырезать: " + exception.Message; Debug.LogException(exception); }
            finally { EditorUtility.ClearProgressBar(); }
            Undo.CollapseUndoOperations(group);
            simplified = null;
            insights = BSLKClubInsights.Collect(target);
            Repaint();
        }

        // ---- Automatic simplification toward the triangle limit

        internal sealed class PlanEntry
        {
            public Renderer renderer;
            public int triangles, planned;
        }

        private GameObject AutoTarget { get { return lastResult != null ? lastResult : avatar; } }

        private void AutoSimplifyTool()
        {
            BeginCard("Автоупрощение до лимита треугольников", Blue);
            GameObject target = AutoTarget;
            if (target == null)
            {
                GUILayout.Label("Выберите аватар на вкладке «Создание».", small);
                EndCard();
                return;
            }
            GUILayout.Label("Аватар: " + target.name + (target == lastResult ? " (клубная копия)" : " (исходник: в сцене получит новые меши, файлы моделей не меняются)"), small);
            autoBudget = EditorGUILayout.IntField("Цель, треугольников", autoBudget);
            autoProtectFaceHands = EditorGUILayout.ToggleLeft("Не упрощать лицо, голову и кисти рук", autoProtectFaceHands);
            if (GUILayout.Button("Рассчитать план", GUILayout.Height(24))) autoPlan = Plan(target, autoBudget, out autoPlanNote);
            if (autoPlan != null)
            {
                GUILayout.Label(autoPlanNote, wrap);
                foreach (PlanEntry entry in autoPlan)
                {
                    if (entry.renderer == null) continue;
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(ObjectPath(entry.renderer), EditorStyles.miniLabel, GUILayout.MinWidth(120));
                    GUILayout.Label(entry.triangles + " → ~" + entry.planned, GUILayout.Width(130));
                    if (GUILayout.Button("Выбрать", GUILayout.Width(70))) Select(entry.renderer.gameObject);
                    EditorGUILayout.EndHorizontal();
                }
                using (new EditorGUI.DisabledScope(autoPlan.Count == 0))
                    if (ColoredButton("Применить план", Blue)) EditorApplication.delayCall += ApplyPlan;
                GUILayout.Label("Результат можно вернуть кнопкой «Вернуть все оригиналы» в карточке ниже.", small);
            }
            EndCard();
        }

        // Every eligible mesh gives up the same share of its triangles; tiny meshes and the face mesh
        // used for visemes are left alone. Each mesh keeps at least a quarter of its triangles.
        private List<PlanEntry> Plan(GameObject target, int budget, out string note)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.gameObject.activeInHierarchy && (r is SkinnedMeshRenderer || r is MeshRenderer) && BSLKClubGeometry.MeshOf(r) != null).ToList();
            long total = renderers.Sum(r => (long)BSLKClubGeometry.TriangleCount(OriginalOf(BSLKClubGeometry.MeshOf(r)) ?? BSLKClubGeometry.MeshOf(r)));
            var face = new HashSet<Renderer>();
            Component descriptor = target.GetComponents<Component>().FirstOrDefault(x => x != null && x.GetType().Name == "VRCAvatarDescriptor");
            if (descriptor != null)
                using (var serialized = new SerializedObject(descriptor))
                    foreach (string property in new[] { "VisemeSkinnedMesh", "customEyeLookSettings.eyelidsSkinnedMesh" })
                        if (serialized.FindProperty(property)?.objectReferenceValue is Renderer renderer) face.Add(renderer);
            // Meshes under 0.5% of the avatar (at least 100 triangles) give too little to be worth the risk.
            long minimum = Math.Max(100, total / 200);
            var eligible = renderers.Where(r => !face.Contains(r) && BSLKClubGeometry.TriangleCount(OriginalOf(BSLKClubGeometry.MeshOf(r)) ?? BSLKClubGeometry.MeshOf(r)) >= minimum).ToList();
            long eligibleTotal = eligible.Sum(r => (long)BSLKClubGeometry.TriangleCount(OriginalOf(BSLKClubGeometry.MeshOf(r)) ?? BSLKClubGeometry.MeshOf(r)));
            long excess = total - budget;
            var plan = new List<PlanEntry>();
            if (excess <= 0) { note = "Сейчас " + total + " треугольников — уже в пределах цели " + budget + "."; return plan; }
            if (eligibleTotal == 0) { note = "Нет мешей, которые можно упростить (лицо и мелкие меши не трогаются)."; return plan; }
            float keep = Mathf.Max(0.25f, 1f - (float)excess / eligibleTotal);
            foreach (Renderer renderer in eligible.OrderByDescending(r => BSLKClubGeometry.TriangleCount(BSLKClubGeometry.MeshOf(r))))
            {
                int triangles = BSLKClubGeometry.TriangleCount(OriginalOf(BSLKClubGeometry.MeshOf(renderer)) ?? BSLKClubGeometry.MeshOf(renderer));
                plan.Add(new PlanEntry { renderer = renderer, triangles = triangles, planned = Mathf.RoundToInt(triangles * keep) });
            }
            long after = total - eligibleTotal + plan.Sum(e => (long)e.planned);
            note = "Сейчас " + total + ", цель " + budget + ". Оставить ~" + Mathf.RoundToInt(keep * 100) + "% треугольников у " + plan.Count + " мешей → ~" + after + "." +
                (after > budget ? " Даже так цель не достигается: остальное — лицо, мелкие меши и защищённые участки." : "") +
                " Края, швы UV" + (simplifyLockShapes ? ", участки, которые blend shapes двигают по-разному" : "") + (autoProtectFaceHands ? ", лицо и кисти" : "") + " не упрощаются, поэтому итог может быть выше плана.";
            return plan;
        }

        private void ApplyPlan()
        {
            if (autoPlan == null) return;
            Undo.SetCurrentGroupName("Автоупрощение");
            int group = Undo.GetCurrentGroup();
            try
            {
                for (int i = 0; i < autoPlan.Count; i++)
                {
                    PlanEntry entry = autoPlan[i];
                    if (entry.renderer == null || entry.triangles == 0) continue;
                    EditorUtility.DisplayProgressBar("BSLK Optimizer", "Упрощение " + entry.renderer.name, (float)i / autoPlan.Count);
                    SimplifyRenderer(entry.renderer, (float)entry.planned / entry.triangles, autoProtectFaceHands ? ProtectedBones(entry.renderer) : null);
                }
            }
            catch (Exception exception) { EditorUtility.DisplayDialog("BSLK Optimizer", "Автоупрощение остановлено: " + exception.Message, "OK"); }
            finally { EditorUtility.ClearProgressBar(); }
            Undo.CollapseUndoOperations(group);
            GameObject target = AutoTarget;
            long total = target.GetComponentsInChildren<Renderer>(true).Where(r => r.gameObject.activeInHierarchy && BSLKClubGeometry.MeshOf(r) != null)
                .Sum(r => (long)BSLKClubGeometry.TriangleCount(BSLKClubGeometry.MeshOf(r)));
            autoPlanNote = "Применено. Теперь треугольников: " + total + " / " + autoBudget + ".";
            autoPlan = null;
            simplified = null;
            insights = BSLKClubInsights.Collect(target);
            Repaint();
        }

        // Head, neck, jaw, eyes and all hand/finger bones of a humanoid rig, plus everything under the head.
        private static HashSet<int> ProtectedBones(Renderer renderer)
        {
            var result = new HashSet<int>();
            var skinned = renderer as SkinnedMeshRenderer;
            Animator animator = renderer.GetComponentInParent<Animator>();
            if (skinned == null || animator == null || !animator.isHuman) return result;
            var protectedTransforms = new HashSet<Transform>();
            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;
                string name = bone.ToString();
                if (bone != HumanBodyBones.Head && bone != HumanBodyBones.Neck && bone != HumanBodyBones.Jaw && !name.Contains("Eye") && !name.Contains("Hand") &&
                    !name.Contains("Thumb") && !name.Contains("Index") && !name.Contains("Middle") && !name.Contains("Ring") && !name.Contains("Little")) continue;
                Transform transform = animator.GetBoneTransform(bone);
                if (transform == null) continue;
                protectedTransforms.Add(transform);
                if (bone == HumanBodyBones.Head || bone == HumanBodyBones.LeftHand || bone == HumanBodyBones.RightHand)
                    foreach (Transform child in transform.GetComponentsInChildren<Transform>(true)) protectedTransforms.Add(child);
            }
            Transform[] bones = skinned.bones;
            for (int i = 0; i < bones.Length; i++) if (bones[i] != null && protectedTransforms.Contains(bones[i])) result.Add(i);
            return result;
        }

        // Simplified meshes of non-generated objects live next to the club results, never beside the source.
        private static string SimplifiedFolder()
        {
            if (!AssetDatabase.IsValidFolder(BSLKClubOptimizer.OutputRoot)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(BSLKClubOptimizer.OutputRoot));
            string folder = BSLKClubOptimizer.OutputRoot + "/Simplified";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(BSLKClubOptimizer.OutputRoot, "Simplified");
            return folder;
        }

        private string ResultFolder()
        {
            string prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(lastResult);
            if (string.IsNullOrEmpty(prefab) || !prefab.StartsWith(BSLKClubOptimizer.OutputRoot + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("Копия не связана с prefab в папке результатов.");
            return Path.GetDirectoryName(prefab).Replace('\\', '/');
        }

        private static void SetMesh(Renderer renderer, Mesh mesh)
        {
            if (renderer == null || mesh == null) return;
            Object holder = renderer is SkinnedMeshRenderer ? (Object)renderer : renderer.GetComponent<MeshFilter>();
            Undo.RecordObject(holder, "Упрощение геометрии");
            BSLKClubGeometry.Assign(renderer, mesh);
            if (PrefabUtility.IsPartOfPrefabInstance(holder)) PrefabUtility.RecordPrefabInstancePropertyModifications(holder);
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
            tab = 1;
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
                removeUnusedShapesAndBones = removeUnusedShapesAndBones, keepMmdShapes = keepMmdShapes, removeHiddenBody = removeHiddenBody, hiddenUnderToggledClothing = hiddenUnderToggledClothing
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
                tab = 1;
                Debug.Log("[BSLK-Optimizer]\n" + report, lastResult);
            }
            catch (OperationCanceledException exception) { report = exception.Message; tab = 1; }
            catch (Exception exception)
            {
                report = "Клубная копия не создана: " + exception.Message;
                tab = 1;
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

    // Source and club copy side by side with one shared orbit camera. Both are rendered from
    // temporary clones inside preview scenes, so the scene objects themselves are not touched.
    public class BSLKCompareWindow : EditorWindow
    {
        [SerializeField] private GameObject left, right;
        private PreviewRenderUtility leftPreview, rightPreview;
        private GameObject leftClone, rightClone;
        private Vector2 orbit = new Vector2(180, 5);
        private Vector3 pivot;
        private float distance = 3, size = 1;
        private Transform head;

        [MenuItem("BSLK-Optimizer/Сравнение исходника и копии")]
        private static void OpenEmpty() { Open(null, null); }

        internal static void Open(GameObject source, GameObject club)
        {
            var window = GetWindow<BSLKCompareWindow>("BSLK • Сравнение");
            window.minSize = new Vector2(640, 420);
            window.left = source;
            window.right = club;
            window.Setup();
        }

        private void OnEnable() { if (left != null || right != null) Setup(); }
        private void OnDisable() { Cleanup(); }

        private void Cleanup()
        {
            if (leftPreview != null) leftPreview.Cleanup();
            if (rightPreview != null) rightPreview.Cleanup();
            leftPreview = rightPreview = null;
            if (leftClone != null) DestroyImmediate(leftClone);
            if (rightClone != null) DestroyImmediate(rightClone);
        }

        private void Setup()
        {
            Cleanup();
            leftPreview = MakePreview(left, out leftClone);
            rightPreview = MakePreview(right, out rightClone);
            GameObject reference = leftClone != null ? leftClone : rightClone;
            Bounds bounds = new Bounds(Vector3.zero, Vector3.one);
            if (reference != null)
            {
                Renderer[] renderers = reference.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    bounds = renderers[0].bounds;
                    foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
                }
                Animator animator = reference.GetComponent<Animator>();
                head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            }
            size = Mathf.Max(0.1f, bounds.size.magnitude);
            // Fit the whole bounds into the 30° view with some margin.
            View(bounds.center, size * 2.2f, 180);
        }

        private static PreviewRenderUtility MakePreview(GameObject source, out GameObject clone)
        {
            clone = null;
            var preview = new PreviewRenderUtility();
            preview.camera.fieldOfView = 30;
            preview.camera.nearClipPlane = 0.01f;
            preview.camera.farClipPlane = 1000;
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color32(40, 42, 48, 255);
            preview.lights[0].intensity = 1.1f;
            preview.lights[0].transform.rotation = Quaternion.Euler(30, 150, 0);
            preview.lights[1].intensity = 0.6f;
            preview.ambientColor = new Color(0.35f, 0.35f, 0.38f);
            if (source == null) return preview;
            clone = Instantiate(source);
            clone.hideFlags = HideFlags.HideAndDontSave;
            clone.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            clone.SetActive(true);
            preview.AddSingleGO(clone);
            return preview;
        }

        private void View(Vector3 center, float newDistance, float yaw)
        {
            pivot = center;
            distance = newDistance;
            orbit = new Vector2(yaw, 5);
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GameObject newLeft = (GameObject)EditorGUILayout.ObjectField(left, typeof(GameObject), true, GUILayout.Width(200));
            GameObject newRight = (GameObject)EditorGUILayout.ObjectField(right, typeof(GameObject), true, GUILayout.Width(200));
            if (newLeft != left || newRight != right) { left = newLeft; right = newRight; Setup(); }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Спереди", EditorStyles.toolbarButton)) View(pivot, distance, 180);
            if (GUILayout.Button("Сзади", EditorStyles.toolbarButton)) View(pivot, distance, 0);
            if (GUILayout.Button("Сбоку", EditorStyles.toolbarButton)) View(pivot, distance, 90);
            using (new EditorGUI.DisabledScope(head == null))
                if (GUILayout.Button("Лицо", EditorStyles.toolbarButton)) View(head.position + Vector3.up * 0.05f, size * 0.25f, 180);
            if (GUILayout.Button("Обновить", EditorStyles.toolbarButton)) Setup();
            EditorGUILayout.EndHorizontal();

            Rect area = GUILayoutUtility.GetRect(position.width, position.height - 22);
            Rect leftRect = new Rect(area.x, area.y, area.width / 2 - 1, area.height);
            Rect rightRect = new Rect(area.x + area.width / 2 + 1, area.y, area.width / 2 - 1, area.height);
            Draw(leftPreview, leftRect, left != null ? "Исходник: " + left.name : "Перетащите исходник в поле слева");
            Draw(rightPreview, rightRect, right != null ? BSLKClubOptimizer.Suffix + ": " + right.name : "Перетащите копию в поле справа");
            EditorGUI.DrawRect(new Rect(area.x + area.width / 2 - 1, area.y, 2, area.height), new Color(0, 0, 0, 0.6f));
            GUI.Label(new Rect(area.x + 8, area.yMax - 20, area.width - 16, 18), "Перетаскивание — вращение, правая кнопка — сдвиг, колесо — масштаб", EditorStyles.miniLabel);

            // One camera for both views: the same drag, pan and zoom apply to source and copy.
            Event e = Event.current;
            if (!area.Contains(e.mousePosition)) return;
            if (e.type == EventType.MouseDrag && e.button == 0) { orbit += new Vector2(e.delta.x, e.delta.y) * 0.4f; orbit.y = Mathf.Clamp(orbit.y, -85, 85); e.Use(); Repaint(); }
            else if (e.type == EventType.MouseDrag && (e.button == 1 || e.button == 2))
            {
                Quaternion rotation = Quaternion.Euler(orbit.y, orbit.x, 0);
                pivot += (rotation * Vector3.left * e.delta.x + rotation * Vector3.up * e.delta.y) * distance * 0.002f;
                e.Use(); Repaint();
            }
            else if (e.type == EventType.ScrollWheel) { distance = Mathf.Clamp(distance * (1 + e.delta.y * 0.05f), 0.05f, 100); e.Use(); Repaint(); }
        }

        private void Draw(PreviewRenderUtility preview, Rect rect, string label)
        {
            if (preview == null || rect.width < 2 || rect.height < 2) return;
            if (Event.current.type == EventType.Repaint)
            {
                preview.BeginPreview(rect, GUIStyle.none);
                Quaternion rotation = Quaternion.Euler(orbit.y, orbit.x, 0);
                preview.camera.transform.position = pivot + rotation * (Vector3.back * distance);
                preview.camera.transform.rotation = rotation;
                preview.Render(true);
                preview.EndAndDrawPreview(rect);
            }
            GUI.Label(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, 18), label, EditorStyles.whiteBoldLabel);
        }
    }
}
