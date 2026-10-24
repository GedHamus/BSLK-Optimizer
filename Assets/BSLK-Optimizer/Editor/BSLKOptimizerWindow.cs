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
        [SerializeField] private int tab;
        private Vector2 scroll;
        private Vector2 reportScroll;
        private bool showTextureOptions;
        private bool showGeometryOptions;
        private List<BSLKClubInsights.Section> insights;
        private List<BSLKClubInsights.PhysBoneEntry> physBones;
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

        private void SimplifyTool()
        {
            BeginCard("Упрощение геометрии (вручную)", Orange);
            if (lastResult == null)
            {
                GUILayout.Label("Доступно после создания клубной версии: упрощается только меш в копии " + BSLKClubOptimizer.Suffix + ".", small);
                EndCard();
                return;
            }
            Renderer target = (Renderer)EditorGUILayout.ObjectField("Меш в копии", simplifyTarget, typeof(Renderer), true);
            if (target != simplifyTarget) { simplifyTarget = target; simplifyOriginal = null; simplifyResult = null; }
            if (simplifyTarget != null && !simplifyTarget.transform.IsChildOf(lastResult.transform))
            {
                EditorGUILayout.HelpBox("Выберите рендерер внутри созданной копии " + lastResult.name + ". Исходник не упрощается.", MessageType.Warning);
                EndCard();
                return;
            }
            simplifyRatio = EditorGUILayout.Slider("Оставить треугольников, %", simplifyRatio * 100, 10, 95) / 100;
            simplifyLockShapes = EditorGUILayout.ToggleLeft("Не трогать вершины blend shapes (лицо, мимика, shrink)", simplifyLockShapes);
            Mesh current = simplifyTarget != null ? BSLKClubGeometry.MeshOf(simplifyTarget) : null;
            if (current != null) GUILayout.Label("Сейчас треугольников: " + BSLKClubGeometry.TriangleCount(current), small);
            using (new EditorGUI.DisabledScope(current == null))
                if (GUILayout.Button("Упростить", GUILayout.Height(26))) Simplify();
            if (simplifyOriginal != null && simplifyResult != null)
            {
                GUILayout.Label("Оригинал " + BSLKClubGeometry.TriangleCount(simplifyOriginal) + " → упрощённый " + BSLKClubGeometry.TriangleCount(simplifyResult), EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Показать оригинал")) SetMesh(simplifyOriginal);
                if (GUILayout.Button("Показать упрощённый")) SetMesh(simplifyResult);
                EditorGUILayout.EndHorizontal();
                if (GUILayout.Button("Сохранить текущий вариант в prefab")) SaveSimplified();
            }
            EndCard();
        }

        // ---- Actions

        private void Simplify()
        {
            Mesh current = BSLKClubGeometry.MeshOf(simplifyTarget);
            if (simplifyOriginal == null || (current != simplifyOriginal && current != simplifyResult)) simplifyOriginal = current;
            try
            {
                string folder = ResultFolder();
                Mesh mesh = BSLKClubGeometry.Simplify(simplifyOriginal, simplifyRatio, simplifyLockShapes);
                AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + mesh.name + ".asset"));
                simplifyResult = mesh;
                SetMesh(mesh);
            }
            catch (Exception exception) { EditorUtility.DisplayDialog("BSLK Optimizer", "Не удалось упростить: " + exception.Message, "OK"); }
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
}
