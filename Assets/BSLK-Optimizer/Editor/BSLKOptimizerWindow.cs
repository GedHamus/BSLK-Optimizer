using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BSLK.Optimizer
{
    public class BSLKOptimizerWindow : EditorWindow
    {
        private const string Repo = "GedHamus/BSLK-Optimizer";
        [SerializeField] private GameObject avatar;
        [SerializeField] private bool singleOutfit = true;
        [SerializeField] private bool optimizeTextures = true;
        [SerializeField] private bool limitParticles = true;
        [SerializeField] private int minimumTextureSize = 512;
        [SerializeField] private float textureError = 0.02f;
        [SerializeField] private List<Texture> protectedTextures = new List<Texture>();
        [SerializeField] private string report = "";
        [SerializeField] private GameObject lastSource;
        [SerializeField] private GameObject lastResult;
        private Vector2 scroll;
        private bool showTextureOptions;
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
            avatar = (GameObject)EditorGUILayout.ObjectField("Исходный аватар", avatar, typeof(GameObject), true);
            EditorGUILayout.HelpBox("В сцене включите нужную одежду и выключите остальные вещи. Изменения будут сохранены в отдельную копию и отдельные ассеты. Исходник останется без изменений.", MessageType.None);
            singleOutfit = EditorGUILayout.ToggleLeft("Один образ: удалить выключенное и закрепить одежду", singleOutfit);
            optimizeTextures = EditorGUILayout.ToggleLeft("Адаптивно уменьшать текстуры, даже ниже 150 MiB", optimizeTextures);
            limitParticles = EditorGUILayout.ToggleLeft("Распределить общий бюджет 2000 частиц", limitParticles);
            showTextureOptions = EditorGUILayout.Foldout(showTextureOptions, "Качество и защищённые текстуры", true);
            if (showTextureOptions)
            {
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
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(avatar == null || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Проверить лимиты без изменений")) report = BSLKClubOptimizer.Analyze(avatar);
                if (GUILayout.Button("Создать клубную версию", GUILayout.Height(38))) Run();
            }
            if (lastResult != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Сравнение в сцене", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Показать исходник")) ShowComparison(false);
                if (GUILayout.Button("Показать Club")) ShowComparison(true);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.HelpBox("Обе версии сохранены. Эти кнопки только переключают их видимость в сцене; действие можно отменить через Undo.", MessageType.None);
            }
            if (!string.IsNullOrEmpty(report))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Отчёт", EditorStyles.boldLabel);
                EditorGUILayout.TextArea(report, GUILayout.MinHeight(250));
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Версия " + BSLKOptimizer.Version, EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(updating))
                if (GUILayout.Button("Проверить обновления"))
                {
                    updating = true;
                    BSLKUpdater.CheckForUpdates(Repo, status => { updateStatus = status; Repaint(); }, () => { updating = false; Repaint(); });
                }
            if (!string.IsNullOrEmpty(updateStatus)) EditorGUILayout.HelpBox(updateStatus, MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private void Run()
        {
            var options = new BSLKClubOptimizer.Options
            {
                singleOutfit = singleOutfit, optimizeTextures = optimizeTextures,
                limitParticles = limitParticles, minimumTextureSize = minimumTextureSize, textureError = textureError
            };
            foreach (Texture texture in protectedTextures) if (texture != null) options.protectedTextures.Add(texture);
            try
            {
                BSLKClubOptimizer.Result result = BSLKClubOptimizer.Create(avatar, options);
                lastSource = avatar;
                lastResult = result.avatar;
                report = result.report;
                lastResult.SetActive(false);
                Debug.Log("[BSLK-Optimizer]\n" + report, lastResult);
            }
            catch (OperationCanceledException exception) { report = exception.Message; }
            catch (Exception exception)
            {
                report = "Клубная копия не создана: " + exception.Message;
                Debug.LogException(exception);
            }
            finally { EditorUtility.ClearProgressBar(); }
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
