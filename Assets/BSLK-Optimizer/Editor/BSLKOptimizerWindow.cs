using UnityEditor;
using UnityEngine;

namespace BSLK.Optimizer
{
    public class BSLKOptimizerWindow : EditorWindow
    {
        private const string PrefDeleteObjects = "BSLK.Optimizer.DeleteDisabledObjects";
        private const string PrefClampSize = "BSLK.Optimizer.ClampTextureSize";
        private const string PrefDisableCrunch = "BSLK.Optimizer.DisableCrunch";
        private const string PrefRemoveBones = "BSLK.Optimizer.RemoveUnusedBones";
        private const string PrefRemoveLights = "BSLK.Optimizer.RemoveLights";
        private const string PrefRemoveCamerasAudio = "BSLK.Optimizer.RemoveCamerasAudio";
        private const string PrefRemoveProbes = "BSLK.Optimizer.RemoveProbes";
        private const string PrefRemoveEmptySlots = "BSLK.Optimizer.RemoveEmptySlots";
        private const string PrefOptimizeMeshes = "BSLK.Optimizer.OptimizeMeshes";
        private const string PrefRemoveBlendShapes = "BSLK.Optimizer.RemoveBlendShapes";
        private const string PrefRemoveUnusedTextures = "BSLK.Optimizer.RemoveUnusedTextures";
        private const string PrefReportOnly = "BSLK.Optimizer.ReportOnly";
        private const string PrefRemoveMissingScripts = "BSLK.Optimizer.RemoveMissingScripts";
        private const string PrefConvertRgbNoAlpha = "BSLK.Optimizer.ConvertRgbNoAlpha";
        private const string PrefDisableTextureReadWrite = "BSLK.Optimizer.DisableTextureReadWrite";
        private const string Repo = "GedHamus/BSLK-Optimizer";

        private GameObject avatar;
        private bool deleteDisabledObjects = true;
        private bool clampTextureSize = true;
        private bool disableCrunch = true;
        private bool removeUnusedBones = true;
        private bool removeLights = true;
        private bool removeCamerasAudio = true;
        private bool removeProbes = true;
        private bool removeEmptySlots = true;
        private bool optimizeMeshes = true;
        private bool removeBlendShapes = true;
        private bool removeUnusedTextures = true;
        private bool reportOnly;
        private bool removeMissingScripts = true;
        private bool convertRgbNoAlpha = true;
        private bool disableTextureReadWrite = true;

        private string updateStatus = "";
        private bool updating;

        private Vector2 scroll;
        private Vector2 togglesScroll;
        private string report = "";

        [MenuItem("Tools/BSLK-Optimizer")]
        private static void Open()
        {
            BSLKOptimizerWindow window = GetWindow<BSLKOptimizerWindow>("BSLK-Optimizer");
            window.minSize = new Vector2(460, 480);
        }

        private void OnEnable()
        {
            deleteDisabledObjects = EditorPrefs.GetBool(PrefDeleteObjects, true);
            clampTextureSize = EditorPrefs.GetBool(PrefClampSize, true);
            disableCrunch = EditorPrefs.GetBool(PrefDisableCrunch, true);
            removeUnusedBones = EditorPrefs.GetBool(PrefRemoveBones, true);
            removeLights = EditorPrefs.GetBool(PrefRemoveLights, true);
            removeCamerasAudio = EditorPrefs.GetBool(PrefRemoveCamerasAudio, true);
            removeProbes = EditorPrefs.GetBool(PrefRemoveProbes, true);
            removeEmptySlots = EditorPrefs.GetBool(PrefRemoveEmptySlots, true);
            optimizeMeshes = EditorPrefs.GetBool(PrefOptimizeMeshes, true);
            removeBlendShapes = EditorPrefs.GetBool(PrefRemoveBlendShapes, true);
            removeUnusedTextures = EditorPrefs.GetBool(PrefRemoveUnusedTextures, true);
            reportOnly = EditorPrefs.GetBool(PrefReportOnly, false);
            removeMissingScripts = EditorPrefs.GetBool(PrefRemoveMissingScripts, true);
            convertRgbNoAlpha = EditorPrefs.GetBool(PrefConvertRgbNoAlpha, true);
            disableTextureReadWrite = EditorPrefs.GetBool(PrefDisableTextureReadWrite, true);
        }

        private void OnDisable()
        {
            EditorPrefs.SetBool(PrefDeleteObjects, deleteDisabledObjects);
            EditorPrefs.SetBool(PrefClampSize, clampTextureSize);
            EditorPrefs.SetBool(PrefDisableCrunch, disableCrunch);
            EditorPrefs.SetBool(PrefRemoveBones, removeUnusedBones);
            EditorPrefs.SetBool(PrefRemoveLights, removeLights);
            EditorPrefs.SetBool(PrefRemoveCamerasAudio, removeCamerasAudio);
            EditorPrefs.SetBool(PrefRemoveProbes, removeProbes);
            EditorPrefs.SetBool(PrefRemoveEmptySlots, removeEmptySlots);
            EditorPrefs.SetBool(PrefOptimizeMeshes, optimizeMeshes);
            EditorPrefs.SetBool(PrefRemoveBlendShapes, removeBlendShapes);
            EditorPrefs.SetBool(PrefRemoveUnusedTextures, removeUnusedTextures);
            EditorPrefs.SetBool(PrefReportOnly, reportOnly);
            EditorPrefs.SetBool(PrefRemoveMissingScripts, removeMissingScripts);
            EditorPrefs.SetBool(PrefConvertRgbNoAlpha, convertRgbNoAlpha);
            EditorPrefs.SetBool(PrefDisableTextureReadWrite, disableTextureReadWrite);
        }

        private void OnGUI()
        {
            GUILayout.Space(8);

            GUIStyle header = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            GUILayout.Label("BSLK-Optimizer", header);
            GUILayout.Label("Оптимизация проекта для VR", EditorStyles.miniLabel);

            GUILayout.Space(12);

            avatar = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Аватар", "Перетащите аватар, чтобы обработать его копию. Пусто = вся открытая сцена."),
                avatar,
                typeof(GameObject),
                true);
            if (avatar != null)
                EditorGUILayout.HelpBox("Будет создана копия " + avatar.name + " BSLK-Optimized, оригинал не изменится.", MessageType.Info);
            else
                EditorGUILayout.HelpBox("Аватар не указан — обработаются вся открытая сцена и все текстуры проекта.", MessageType.Warning);

            GUILayout.Space(8);

            reportOnly = EditorGUILayout.ToggleLeft("Только отчёт, ничего не изменяет", reportOnly);

            GUILayout.Space(6);

            togglesScroll = EditorGUILayout.BeginScrollView(togglesScroll, GUILayout.Height(210));

            GUILayout.Label("Объекты", EditorStyles.boldLabel);
            deleteDisabledObjects = EditorGUILayout.ToggleLeft(
                "Удалить выключенные объекты",
                deleteDisabledObjects);
            removeMissingScripts = EditorGUILayout.ToggleLeft(
                "Удалить Missing Scripts",
                removeMissingScripts);
            removeLights = EditorGUILayout.ToggleLeft(
                "Удалить все источники света",
                removeLights);
            removeCamerasAudio = EditorGUILayout.ToggleLeft(
                "Удалить Camera и AudioListener",
                removeCamerasAudio);
            removeProbes = EditorGUILayout.ToggleLeft(
                "Удалить Reflection Probe",
                removeProbes);

            GUILayout.Space(6);
            GUILayout.Label("Материалы", EditorStyles.boldLabel);
            removeEmptySlots = EditorGUILayout.ToggleLeft(
                "Удалить пустые слоты материалов",
                removeEmptySlots);
            removeUnusedTextures = EditorGUILayout.ToggleLeft(
                "Удалить неиспользуемые текстуры из материалов",
                removeUnusedTextures);

            GUILayout.Space(6);
            GUILayout.Label("Меши", EditorStyles.boldLabel);
            optimizeMeshes = EditorGUILayout.ToggleLeft(
                "Оптимизировать меши",
                optimizeMeshes);
            removeBlendShapes = EditorGUILayout.ToggleLeft(
                "Удалить неиспользуемые Blend Shapes",
                removeBlendShapes);
            removeUnusedBones = EditorGUILayout.ToggleLeft(
                "Удалить неиспользуемые кости",
                removeUnusedBones);

            GUILayout.Space(6);
            GUILayout.Label("Текстуры", EditorStyles.boldLabel);
            clampTextureSize = EditorGUILayout.ToggleLeft(
                "Ограничить разрешение текстур",
                clampTextureSize);
            disableCrunch = EditorGUILayout.ToggleLeft(
                "Выключить Crunch Compression",
                disableCrunch);
            convertRgbNoAlpha = EditorGUILayout.ToggleLeft(
                "Убрать альфу у текстур без прозрачности",
                convertRgbNoAlpha);
            disableTextureReadWrite = EditorGUILayout.ToggleLeft(
                "Отключить Read/Write у текстур",
                disableTextureReadWrite);

            EditorGUILayout.EndScrollView();

            GUILayout.Space(10);

            bool anySelected = deleteDisabledObjects || clampTextureSize || disableCrunch || removeUnusedBones ||
                               removeLights || removeCamerasAudio || removeProbes || removeEmptySlots ||
                               optimizeMeshes || removeBlendShapes || removeUnusedTextures ||
                               removeMissingScripts || convertRgbNoAlpha || disableTextureReadWrite ||
                               reportOnly;

            GUI.enabled = anySelected;
            string buttonLabel = reportOnly ? "Показать отчёт" : "Оптимизировать";
            if (GUILayout.Button(buttonLabel, GUILayout.Height(36)))
                RunOptimization();
            GUI.enabled = true;

            GUILayout.Space(10);

            EditorGUILayout.LabelField("Обновление", EditorStyles.boldLabel);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Версия: " + BSLKOptimizer.Version, GUILayout.ExpandWidth(true));
            GUI.enabled = !updating;
            if (GUILayout.Button("Проверить обновления", GUILayout.Width(150)))
                CheckUpdates();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(updateStatus))
                EditorGUILayout.HelpBox(updateStatus, MessageType.Info);

            GUILayout.Space(10);

            if (!string.IsNullOrEmpty(report))
            {
                EditorGUILayout.LabelField("Отчёт", EditorStyles.boldLabel);
                scroll = EditorGUILayout.BeginScrollView(scroll);
                GUILayout.Label(report, EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndScrollView();
            }
        }

        private void CheckUpdates()
        {
            updating = true;
            updateStatus = "Проверка обновлений...";
            BSLKUpdater.CheckForUpdates(Repo, status =>
            {
                updating = false;
                updateStatus = status;
                Repaint();
            });
        }

        private void RunOptimization()
        {
            bool anySelected = deleteDisabledObjects || clampTextureSize || disableCrunch || removeUnusedBones ||
                               removeLights || removeCamerasAudio || removeProbes || removeEmptySlots ||
                               optimizeMeshes || removeBlendShapes || removeUnusedTextures ||
                               removeMissingScripts || convertRgbNoAlpha || disableTextureReadWrite ||
                               reportOnly;
            if (!anySelected)
            {
                EditorUtility.DisplayDialog("BSLK-Optimizer", "Не выбран ни один пункт.", "OK");
                return;
            }

            report = "";

            if (reportOnly)
            {
                string reportText = BSLKOptimizer.GatherReport(avatar);
                report = reportText;
                EditorUtility.DisplayDialog("BSLK-Optimizer", reportText, "OK");
                Debug.Log("[BSLK-Optimizer]\n" + reportText);
                return;
            }

            if (avatar != null)
            {
                string avatarNameBefore = avatar.name;
                avatar = BSLKOptimizer.PrepareAvatarCopy(avatar);
                if (avatar.name != avatarNameBefore)
                    report += "Создана копия аватара: " + avatar.name + " — все изменения применены к ней, оригинал не тронут\n";
            }

            if (deleteDisabledObjects)
            {
                int removed = BSLKOptimizer.RemoveDisabledObjects(avatar, out int preserved);
                report += string.Format("Удалено выключенных объектов: {0}", removed);
                if (preserved > 0)
                    report += string.Format(", сохранено с PhysBone/DynamicBone: {0}", preserved);
                report += "\n";
            }

            if (removeMissingScripts)
            {
                int missingRemoved = BSLKOptimizer.RemoveMissingScripts(avatar);
                report += string.Format("Удалено Missing Scripts: {0}\n", missingRemoved);
            }

            if (removeLights)
            {
                int lightsRemoved = BSLKOptimizer.RemoveLights(avatar);
                report += string.Format("Удалено источников света: {0}\n", lightsRemoved);
            }

            if (removeCamerasAudio)
            {
                CameraAudioReport cameraReport = BSLKOptimizer.RemoveCamerasAndAudioListeners(avatar);
                report += string.Format(
                    "Удалено Camera: {0}, AudioListener: {1}\n",
                    cameraReport.cameras,
                    cameraReport.audioListeners);
            }

            if (removeProbes)
            {
                int probesRemoved = BSLKOptimizer.RemoveReflectionProbes(avatar);
                report += string.Format("Удалено Reflection Probe: {0}\n", probesRemoved);
            }

            if (removeEmptySlots)
            {
                int slotsCleaned = BSLKOptimizer.RemoveEmptyMaterialSlots(avatar);
                report += string.Format("Удалено пустых слотов материалов: {0}\n", slotsCleaned);
            }

            if (removeUnusedTextures)
            {
                TextureCleanupReport cleanupReport = BSLKOptimizer.RemoveUnusedTexturesFromMaterials(avatar);
                report += string.Format(
                    "Удалено неиспользуемых текстур: {0} (материалов: {1})\n",
                    cleanupReport.texturesRemoved,
                    cleanupReport.materials);
            }

            if (removeBlendShapes)
            {
                BlendShapeReport blendShapeReport = BSLKOptimizer.RemoveUnusedBlendShapes(avatar);
                report += string.Format(
                    "Удалено Blend Shapes: {0} (мешей: {1}",
                    blendShapeReport.shapesRemoved,
                    blendShapeReport.meshes);
                if (blendShapeReport.skipped > 0)
                    report += string.Format(", пропущено нечитаемых: {0}", blendShapeReport.skipped);
                report += ")\n";
            }

            if (optimizeMeshes)
            {
                MeshReport meshReport = BSLKOptimizer.OptimizeMeshes(avatar);
                report += string.Format(
                    "Оптимизировано мешей: {0} из {1}",
                    meshReport.optimized,
                    meshReport.meshes);
                if (meshReport.skipped > 0)
                    report += string.Format(", пропущено: {0}", meshReport.skipped);
                report += "\n";
            }

            if (clampTextureSize || disableCrunch || convertRgbNoAlpha || disableTextureReadWrite)
            {
                TextureReport textureReport = BSLKOptimizer.ProcessTextures(clampTextureSize, disableCrunch, avatar);
                report += string.Format(
                    "Текстур обработано: {0}, изменено: {1}",
                    textureReport.total,
                    textureReport.changed);
                if (avatar != null)
                    report += " (только текстуры аватара)";
                report += "\n";
            }

            if (convertRgbNoAlpha)
            {
                TextureReport rgbReport = BSLKOptimizer.ConvertTexturesToRgbWithoutAlpha(avatar);
                report += string.Format(
                    "Текстур без альфы конвертировано в RGB: {0} из {1}\n",
                    rgbReport.changed,
                    rgbReport.total);
            }

            if (disableTextureReadWrite)
            {
                TextureReport rwReport = BSLKOptimizer.DisableTextureReadWrite(avatar);
                report += string.Format(
                    "Отключен Read/Write у текстур: {0} из {1}\n",
                    rwReport.changed,
                    rwReport.total);
            }

            if (removeUnusedBones)
            {
                BoneReport boneReport = BSLKOptimizer.RemoveUnusedBones(avatar);
                report += string.Format(
                    "\nРендереров обработано: {0}, костей удалено: {1}",
                    boneReport.renderers,
                    boneReport.bonesRemoved);
                if (boneReport.skippedNotReadable > 0 || boneReport.skippedMismatch > 0)
                {
                    report += string.Format(
                        " (пропущено: нечитаемых мешей {0}, с неверными bindposes {1})",
                        boneReport.skippedNotReadable,
                        boneReport.skippedMismatch);
                }
            }

            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("BSLK-Optimizer", "Оптимизация завершена.\n\n" + report, "OK");
            Debug.Log("[BSLK-Optimizer]\n" + report);
        }
    }
}