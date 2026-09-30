using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace BSLK.Optimizer
{
    public static class BSLKUpdater
    {
        [Serializable]
        private class GitHubRelease
        {
            public string tag_name;
        }

        private static readonly string[] UpdateFiles =
        {
            "Assets/BSLK-Optimizer/Editor/BSLKOptimizer.cs",
            "Assets/BSLK-Optimizer/Editor/BSLKOptimizerWindow.cs",
            "Assets/BSLK-Optimizer/Editor/BSLKClubOptimizer.cs",
            "Assets/BSLK-Optimizer/Editor/BSLKClubTextures.cs",
            "Assets/BSLK-Optimizer/Editor/BSLKClubSdk.cs",
            "Assets/BSLK-Optimizer/Editor/BSLKClubAnimation.cs",
            "Assets/BSLK-Optimizer/Editor/BSLKUpdater.cs"
        };

        private const string ContentMarker = "namespace BSLK.Optimizer";

        public static void CheckForUpdates(string repo, Action<string> onStatus, Action onComplete = null)
        {
            if (string.IsNullOrEmpty(repo) || repo.IndexOf('/') <= 0)
            {
                onStatus("Укажите репозиторий в формате owner/repo.");
                onComplete?.Invoke();
                return;
            }

            onStatus("Проверка обновлений...");

            string url = "https://api.github.com/repos/" + repo + "/releases/latest";
            UnityWebRequest request = UnityWebRequest.Get(url);
            request.timeout = 15;
            request.SetRequestHeader("User-Agent", "BSLK-Optimizer");
            request.SendWebRequest().completed += operation =>
            {
                bool downloading = false;
                try
                {
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        onStatus("Ошибка проверки: " + request.error);
                        return;
                    }

                    GitHubRelease release = JsonUtility.FromJson<GitHubRelease>(request.downloadHandler.text);
                    if (release == null || string.IsNullOrEmpty(release.tag_name))
                    {
                        onStatus("Ошибка: не удалось прочитать данные релиза.");
                        return;
                    }

                    int comparison = CompareVersions(release.tag_name, BSLKOptimizer.Version);
                    if (comparison <= 0)
                    {
                        onStatus("Установлена актуальная версия " + BSLKOptimizer.Version);
                        return;
                    }

                    if (EditorUtility.DisplayDialog(
                            "BSLK-Optimizer",
                            "Доступна новая версия: " + release.tag_name +
                            "\nТекущая: " + BSLKOptimizer.Version +
                            "\n\nОбновить сейчас?",
                            "Обновить",
                            "Позже"))
                    {
                        downloading = true;
                        DownloadAndUpdate(repo, release.tag_name, onStatus, onComplete);
                    }
                    else
                    {
                        onStatus("Доступна новая версия: " + release.tag_name);
                    }
                }
                finally
                {
                    request.Dispose();
                    if (!downloading) onComplete?.Invoke();
                }
            };
        }

        private static void DownloadAndUpdate(string repo, string tag, Action<string> onStatus, Action onComplete)
        {
            onStatus("Скачивание " + tag + "...");

            int slash = repo.IndexOf('/');
            string owner = repo.Substring(0, slash);
            string repository = repo.Substring(slash + 1);

            DownloadFiles(owner, repository, tag, 0, new string[UpdateFiles.Length], onStatus, onComplete);
        }

        private static void DownloadFiles(string owner, string repository, string tag, int index, string[] contents, Action<string> onStatus, Action onComplete)
        {
            if (index >= UpdateFiles.Length)
            {
                try { WriteFiles(contents, tag, onStatus); }
                catch (Exception e) { onStatus("Обновление не установлено: " + e.Message); }
                finally { onComplete?.Invoke(); }
                return;
            }

            string relative = UpdateFiles[index];
            string url = "https://raw.githubusercontent.com/" + owner + "/" + repository + "/" + tag + "/" + relative;
            UnityWebRequest request = UnityWebRequest.Get(url);
            request.timeout = 20;
            request.SetRequestHeader("User-Agent", "BSLK-Optimizer");
            request.SendWebRequest().completed += operation =>
            {
                try
                {
                    if (request.result != UnityWebRequest.Result.Success || !request.downloadHandler.text.Contains(ContentMarker))
                    {
                        onStatus("Ошибка при скачивании: " + relative);
                        onComplete?.Invoke();
                        return;
                    }

                    contents[index] = request.downloadHandler.text;
                    DownloadFiles(owner, repository, tag, index + 1, contents, onStatus, onComplete);
                }
                finally
                {
                    request.Dispose();
                }
            };
        }

        private static void WriteFiles(string[] contents, string tag, Action<string> onStatus)
        {
            string version = tag.TrimStart('v', 'V');

            string[] previous = new string[UpdateFiles.Length];
            for (int i = 0; i < UpdateFiles.Length; i++)
            {
                if (string.IsNullOrEmpty(contents[i]) || !contents[i].Contains(ContentMarker))
                    throw new InvalidOperationException("Неполный комплект файлов обновления.");
                previous[i] = System.IO.File.Exists(UpdateFiles[i]) ? System.IO.File.ReadAllText(UpdateFiles[i]) : null;
            }
            AssetDatabase.DisallowAutoRefresh();
            try
            {
                for (int i = 0; i < UpdateFiles.Length; i++)
                {
                    if (previous[i] != null) System.IO.File.WriteAllText(UpdateFiles[i] + ".bak", previous[i]);
                    System.IO.File.WriteAllText(UpdateFiles[i], PatchVersionInContent(contents[i], version));
                }
            }
            catch
            {
                for (int i = 0; i < UpdateFiles.Length; i++)
                {
                    if (previous[i] != null) System.IO.File.WriteAllText(UpdateFiles[i], previous[i]);
                    else if (System.IO.File.Exists(UpdateFiles[i])) System.IO.File.Delete(UpdateFiles[i]);
                }
                throw;
            }
            finally { AssetDatabase.AllowAutoRefresh(); }

            EditorUtility.DisplayDialog(
                "BSLK-Optimizer",
                "Обновление до версии " + version + " установлено. Редактор перекомпилируется...",
                "OK");
            onStatus("Установлена версия " + version + ".");
            AssetDatabase.Refresh();
        }

        private static string PatchVersionInContent(string content, string version)
        {
            const string marker = "public const string Version = \"";
            int start = content.IndexOf(marker);
            if (start < 0)
                return content;

            int valueStart = start + marker.Length;
            int valueEnd = content.IndexOf('"', valueStart);
            if (valueEnd < 0)
                return content;

            return content.Substring(0, valueStart) + version + content.Substring(valueEnd);
        }

        public static int CompareVersions(string a, string b)
        {
            int[] av = ParseVersion(a);
            int[] bv = ParseVersion(b);
            for (int i = 0; i < 3; i++)
            {
                if (av[i] != bv[i])
                    return av[i].CompareTo(bv[i]);
            }
            return 0;
        }

        private static int[] ParseVersion(string version)
        {
            string clean = version.TrimStart('v', 'V');
            string[] parts = clean.Split('.');
            int[] result = { 0, 0, 0 };
            for (int i = 0; i < parts.Length && i < 3; i++)
            {
                int value;
                if (int.TryParse(parts[i], out value))
                    result[i] = value;
            }
            return result;
        }
    }
}
