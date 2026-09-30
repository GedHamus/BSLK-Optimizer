using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BSLK.Optimizer
{
    internal static class BSLKClubTextures
    {
        internal static void Optimize(BSLKClubOptimizer.Context context, BSLKClubOptimizer.Options options)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64 &&
                EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows &&
                EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneLinux64 &&
                EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneOSX)
            {
                context.notes.Add("Текстуры не изменены: адаптивное сжатие рассчитано на PC. Переключите Build Target на Standalone.");
                return;
            }
            var protectedCopies = new HashSet<Texture>();
            foreach (Texture texture in options.protectedTextures)
                if (texture != null && context.copies.TryGetValue(texture, out Object copy) && copy is Texture t) protectedCopies.Add(t);

            // Data maps may have shader semantics that image error cannot capture.
            foreach (Material material in context.copies.Values.OfType<Material>())
                foreach (string property in material.GetTexturePropertyNames())
                {
                    string name = property.ToLowerInvariant();
                    if (name.Contains("mask") || name.Contains("normal") || name.Contains("bump") || name.Contains("height") ||
                        name.Contains("metal") || name.Contains("rough") || name.Contains("detail") || name.Contains("occlusion"))
                    {
                        Texture texture = material.GetTexture(property);
                        if (texture != null) protectedCopies.Add(texture);
                    }
                }

            Texture2D[] textures = BSLKClubOptimizer.Textures(context.avatar).OfType<Texture2D>().OrderByDescending(t => Bytes(t)).ToArray();
            int changed = 0, protectedCount = 0;
            for (int i = 0; i < textures.Length; i++)
            {
                Texture2D texture = textures[i];
                string path = AssetDatabase.GetAssetPath(texture);
                BSLKClubOptimizer.Progress("Текстуры: " + texture.name, (float)i / Math.Max(1, textures.Length));
                if (!context.copiedPaths.Contains(path)) continue;
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null || protectedCopies.Contains(texture) || importer.textureType != TextureImporterType.Default ||
                    !importer.sRGBTexture || !IsColorFormat(texture.format) || texture.width > 4096 || texture.height > 4096)
                { protectedCount++; continue; }

                TextureImporterPlatformSettings originalSettings = importer.GetPlatformTextureSettings("Standalone");
                bool originalReadable = importer.isReadable;
                try
                {
                    int width = texture.width, height = texture.height;
                    long beforeBytes = Bytes(texture);
                    Color32[] reference = Readback(texture, width, height);
                    TextureImporterPlatformSettings acceptedSettings = originalSettings;
                    int acceptedMax = Math.Max(width, height);
                    long acceptedBytes = beforeBytes;
                    float lastError = 0;
                    bool anyAccepted = false;
                    // First try PC compression at current resolution; then halve until error budget is exhausted.
                    int candidateSize = Mathf.NextPowerOfTwo(acceptedMax);
                    while (candidateSize >= options.minimumTextureSize)
                    {
                        BSLKClubOptimizer.Progress("Проверка " + texture.name + " → " + candidateSize, (float)i / Math.Max(1, textures.Length));
                        TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings("Standalone");
                        settings.name = "Standalone";
                        settings.overridden = true;
                        settings.maxTextureSize = candidateSize;
                        // Retain existing block compression. BC7 is used only for uncompressed/automatic inputs.
                        settings.format = IsCompact(texture.format) ? ExistingFormat(texture.format) : TextureImporterFormat.BC7;
                        settings.crunchedCompression = false;
                        settings.compressionQuality = 100;
                        importer.SetPlatformTextureSettings(settings);
                        importer.SaveAndReimport();
                        texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                        long candidateBytes = Bytes(texture);
                        Color32[] candidate = Readback(texture, width, height);
                        float error = Error(reference, candidate, out float damagedFraction);
                        if (error > options.textureError || damagedFraction > 0.005f)
                        {
                            importer.SetPlatformTextureSettings(acceptedSettings);
                            importer.SaveAndReimport();
                            break;
                        }
                        if (candidateBytes < acceptedBytes)
                        {
                            acceptedSettings = importer.GetPlatformTextureSettings("Standalone");
                            acceptedBytes = candidateBytes;
                            acceptedMax = Math.Max(texture.width, texture.height);
                            lastError = error;
                            anyAccepted = true;
                        }
                        else
                        {
                            importer.SetPlatformTextureSettings(acceptedSettings);
                            importer.SaveAndReimport();
                            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                        }
                        candidateSize /= 2;
                    }
                    if (anyAccepted)
                    {
                        changed++;
                        context.notes.Add("Текстура " + texture.name + ": " + width + "×" + height + " → max " + acceptedMax +
                            ", " + (beforeBytes / 1048576.0).ToString("F2") + " → " + (acceptedBytes / 1048576.0).ToString("F2") +
                            " MiB, ошибка " + lastError.ToString("F4") + ".");
                    }
                    // Read/Write affects CPU memory, not the GPU budget; do not advertise it as VRAM savings.
                    if (importer.isReadable) { importer.isReadable = false; importer.SaveAndReimport(); }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                {
                    importer.SetPlatformTextureSettings(originalSettings);
                    importer.isReadable = originalReadable;
                    importer.SaveAndReimport();
                    context.notes.Add("Текстура сохранена без изменений: " + path + " — " + error.Message);
                }
            }
            context.notes.Add("Уменьшено текстур: " + changed + "; защищено/неподдерживаемых: " + protectedCount + ".");
            context.notes.Add("Проверка текстур — эвристика по пикселям, не оценка внешнего вида шейдера. Маски, normal maps, linear/HDR и текстуры >4096 автоматически не уменьшаются.");
        }

        private static bool IsCompact(TextureFormat format)
        {
            return format == TextureFormat.DXT1 || format == TextureFormat.DXT1Crunched ||
                format == TextureFormat.DXT5 || format == TextureFormat.DXT5Crunched || format == TextureFormat.BC7;
        }

        private static bool IsColorFormat(TextureFormat format)
        {
            // HDR/data/packed formats must not be judged through an 8-bit color readback.
            return IsCompact(format) || format == TextureFormat.RGB24 || format == TextureFormat.RGBA32 ||
                format == TextureFormat.ARGB32 || format == TextureFormat.BGRA32;
        }

        private static TextureImporterFormat ExistingFormat(TextureFormat format)
        {
            if (format == TextureFormat.DXT1 || format == TextureFormat.DXT1Crunched) return TextureImporterFormat.DXT1;
            if (format == TextureFormat.DXT5 || format == TextureFormat.DXT5Crunched) return TextureImporterFormat.DXT5;
            return TextureImporterFormat.BC7;
        }

        private static long Bytes(Texture2D texture)
        {
            long bytes = 0;
            for (int mip = 0; mip < texture.mipmapCount; mip++)
                bytes += (long)UnityEngine.Experimental.Rendering.GraphicsFormatUtility.ComputeMipmapSize(
                    Math.Max(1, texture.width >> mip), Math.Max(1, texture.height >> mip), texture.graphicsFormat);
            return bytes;
        }

        private static Color32[] Readback(Texture texture, int width, int height)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Texture2D readable = null;
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                readable = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readable.Apply(false, false);
                return readable.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                if (readable != null) Object.DestroyImmediate(readable);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        internal static float Error(Color32[] reference, Color32[] candidate, out float damagedFraction)
        {
            if (reference.Length != candidate.Length || reference.Length == 0) throw new ArgumentException("Размеры изображений различаются.");
            double sum = 0;
            long damaged = 0;
            for (int i = 0; i < reference.Length; i++)
            {
                int r = reference[i].r - candidate[i].r, g = reference[i].g - candidate[i].g;
                int b = reference[i].b - candidate[i].b, a = reference[i].a - candidate[i].a;
                sum += r * r + g * g + b * b + a * a;
                if (Math.Max(Math.Max(Math.Abs(r), Math.Abs(g)), Math.Max(Math.Abs(b), Math.Abs(a))) > 32) damaged++;
            }
            damagedFraction = (float)damaged / reference.Length;
            return (float)Math.Sqrt(sum / (reference.Length * 4.0 * 255.0 * 255.0));
        }
    }
}
