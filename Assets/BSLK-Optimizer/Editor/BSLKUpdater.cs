using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace BSLK.Optimizer
{
    // Updates install silently: once per editor session in the background, or from the window button.
    [InitializeOnLoad]
    public static class BSLKUpdater
    {
        public const string Repo = "GedHamus/BSLK-Optimizer";
        private const string SessionKey = "BSLK-Optimizer.AutoUpdateChecked";

        static BSLKUpdater()
        {
            // Never in batch mode: regression projects and CI must not rewrite their sources.
            if (Application.isBatchMode || SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
                CheckForUpdates(Repo, status => { });
            };
        }

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

                    downloading = true;
                    DownloadAndUpdate(repo, release.tag_name, onStatus, onComplete);
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

            // The release's own updater lists its file set, so a release may add editor files.
            string updater = UpdateFiles[UpdateFiles.Length - 1];
            Download(owner, repository, tag, updater, text =>
            {
                string[] files = UpdateFiles.Union(System.Text.RegularExpressions.Regex.Matches(text, "\"(Assets/BSLK-Optimizer/Editor/[A-Za-z0-9_]+\\.cs)\"")
                    .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value)).ToArray();
                DownloadFiles(owner, repository, tag, files, 0, new string[files.Length], onStatus, onComplete);
            }, () => { onStatus("Ошибка при скачивании: " + updater); onComplete?.Invoke(); });
        }

        private static void Download(string owner, string repository, string tag, string relative, Action<string> onText, Action onError)
        {
            string url = "https://raw.githubusercontent.com/" + owner + "/" + repository + "/" + tag + "/" + relative;
            UnityWebRequest request = UnityWebRequest.Get(url);
            request.timeout = 20;
            request.SetRequestHeader("User-Agent", "BSLK-Optimizer");
            request.SendWebRequest().completed += operation =>
            {
                string text = request.result == UnityWebRequest.Result.Success ? request.downloadHandler.text : null;
                request.Dispose();
                if (text == null || !text.Contains(ContentMarker)) onError();
                else onText(text);
            };
        }

        private static void DownloadFiles(string owner, string repository, string tag, string[] files, int index, string[] contents, Action<string> onStatus, Action onComplete)
        {
            if (index >= files.Length)
            {
                try { WriteFiles(files, contents, tag, onStatus); }
                catch (Exception e) { onStatus("Обновление не установлено: " + e.Message); }
                finally { onComplete?.Invoke(); }
                return;
            }
            Download(owner, repository, tag, files[index], text =>
            {
                contents[index] = text;
                DownloadFiles(owner, repository, tag, files, index + 1, contents, onStatus, onComplete);
            }, () => { onStatus("Ошибка при скачивании: " + files[index]); onComplete?.Invoke(); });
        }

        private static void WriteFiles(string[] files, string[] contents, string tag, Action<string> onStatus)
        {
            string version = tag.TrimStart('v', 'V');

            string[] previous = new string[files.Length];
            for (int i = 0; i < files.Length; i++)
            {
                if (string.IsNullOrEmpty(contents[i]) || !contents[i].Contains(ContentMarker))
                    throw new InvalidOperationException("Неполный комплект файлов обновления.");
                previous[i] = System.IO.File.Exists(files[i]) ? System.IO.File.ReadAllText(files[i]) : null;
            }
            AssetDatabase.DisallowAutoRefresh();
            try
            {
                for (int i = 0; i < files.Length; i++)
                {
                    if (previous[i] != null) System.IO.File.WriteAllText(files[i] + ".bak", previous[i]);
                    System.IO.File.WriteAllText(files[i], PatchVersionInContent(contents[i], version));
                }
            }
            catch
            {
                for (int i = 0; i < files.Length; i++)
                {
                    if (previous[i] != null) System.IO.File.WriteAllText(files[i], previous[i]);
                    else if (System.IO.File.Exists(files[i])) System.IO.File.Delete(files[i]);
                }
                throw;
            }
            finally { AssetDatabase.AllowAutoRefresh(); }

            Debug.Log("[BSLK-Optimizer] Установлена версия " + version + ".");
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
