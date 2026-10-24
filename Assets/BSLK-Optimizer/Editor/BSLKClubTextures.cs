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
                    // Retain existing block compression; BC7 only for uncompressed/automatic inputs. A fully
                    // opaque texture also tries DXT1, half the memory of BC7/DXT5; the error check decides.
                    TextureImporterFormat baseFormat = IsCompact(texture.format) ? ExistingFormat(texture.format) : TextureImporterFormat.BC7;
                    var formats = new List<TextureImporterFormat>();
                    if (baseFormat != TextureImporterFormat.DXT1 && reference.All(p => p.a == 255)) formats.Add(TextureImporterFormat.DXT1);
                    formats.Add(baseFormat);
                    TextureImporterFormat acceptedFormat = baseFormat;
                    // First try PC compression at current resolution; then halve until error budget is exhausted.
                    int candidateSize = Mathf.NextPowerOfTwo(acceptedMax);
                    while (candidateSize >= options.minimumTextureSize)
                    {
                        bool passed = false, applied = false;
                        foreach (TextureImporterFormat format in formats)
                        {
                            BSLKClubOptimizer.Progress("Проверка " + texture.name + " → " + candidateSize + " " + format, (float)i / Math.Max(1, textures.Length));
                            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings("Standalone");
                            settings.name = "Standalone";
                            settings.overridden = true;
                            settings.maxTextureSize = candidateSize;
                            settings.format = format;
                            settings.crunchedCompression = false;
                            settings.compressionQuality = 100;
                            importer.SetPlatformTextureSettings(settings);
                            importer.SaveAndReimport();
                            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                            long candidateBytes = Bytes(texture);
                            Color32[] candidate = Readback(texture, width, height);
                            float error = Error(reference, candidate, out float damagedFraction);
                            if (error > options.textureError || damagedFraction > 0.005f) continue;
                            passed = true;
                            if (candidateBytes < acceptedBytes)
                            {
                                acceptedSettings = importer.GetPlatformTextureSettings("Standalone");
                                acceptedBytes = candidateBytes;
                                acceptedMax = Math.Max(texture.width, texture.height);
                                acceptedFormat = format;
                                lastError = error;
                                anyAccepted = true;
                                applied = true;
                                break;
                            }
                        }
                        // Leave the importer in the last accepted state before stopping or halving again.
                        if (!applied)
                        {
                            importer.SetPlatformTextureSettings(acceptedSettings);
                            importer.SaveAndReimport();
                            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                        }
                        if (!passed) break;
                        candidateSize /= 2;
                    }
                    if (anyAccepted)
                    {
                        changed++;
                        context.notes.Add("Текстура " + texture.name + ": " + width + "×" + height + " → max " + acceptedMax +
                            ", " + (beforeBytes / 1048576.0).ToString("F2") + " → " + (acceptedBytes / 1048576.0).ToString("F2") +
                            " MiB, " + acceptedFormat + ", ошибка " + lastError.ToString("F4") + ".");
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

        internal static long Bytes(Texture2D texture)
        {
            long bytes = 0;
            for (int mip = 0; mip < texture.mipmapCount; mip++)
                bytes += (long)UnityEngine.Experimental.Rendering.GraphicsFormatUtility.ComputeMipmapSize(
                    Math.Max(1, texture.width >> mip), Math.Max(1, texture.height >> mip), texture.graphicsFormat);
            return bytes;
        }

        // Compare sRGB-encoded values. In a Linear project an 8-bit linear target would
        // crush dark tones and hide compression damage in hair, shadows and dark clothing.
        internal static Color32[] Readback(Texture texture, int width, int height)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D readable = null;
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                readable = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
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

namespace BSLK.Optimizer
{
    using System.Text;
    using UnityEngine.Rendering;

    // Duplicate result-owned materials and textures collapse onto one copy each. Equality is strict:
    // same shader, keywords, queue, tags, passes and every shader property; textures must decode to
    // the same pixels with the same import settings.
    internal static class BSLKClubMaterials
    {
        internal static int MergeIdenticalMaterials(BSLKClubOptimizer.Context context)
        {
            var canonical = new Dictionary<string, Material>();
            var map = new Dictionary<Object, Object>();
            foreach (Material material in context.avatar.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .Concat(context.copies.Values.OfType<Material>()).Where(m => m != null).Distinct())
            {
                if (!context.copiedPaths.Contains(AssetDatabase.GetAssetPath(material)) || material.shader == null) continue;
                string signature = Signature(material);
                if (canonical.TryGetValue(signature, out Material existing)) map[material] = existing;
                else canonical[signature] = material;
            }
            if (map.Count > 0) BSLKClubOptimizer.RemapAll(context, map);
            context.notes.Add("Объединено одинаковых материалов: " + map.Count + ".");
            return map.Count;
        }

        private static string Signature(Material material)
        {
            var text = new StringBuilder();
            Shader shader = material.shader;
            text.Append(shader.GetInstanceID()).Append('|').Append(material.renderQueue).Append('|')
                .Append(string.Join(",", material.shaderKeywords.OrderBy(k => k))).Append('|')
                .Append(material.enableInstancing).Append(material.doubleSidedGI).Append((int)material.globalIlluminationFlags);
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                int id = shader.GetPropertyNameId(i);
                text.Append('|').Append(shader.GetPropertyName(i)).Append('=');
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color: text.Append(material.GetColor(id).ToString("R")); break;
                    case ShaderPropertyType.Vector: text.Append(material.GetVector(id).ToString("R")); break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range: text.Append(material.GetFloat(id).ToString("R")); break;
                    case ShaderPropertyType.Int: text.Append(material.GetInteger(id)); break;
                    case ShaderPropertyType.Texture:
                        Texture texture = material.GetTexture(id);
                        text.Append(texture != null ? texture.GetInstanceID() : 0).Append(material.GetTextureScale(id).ToString("R")).Append(material.GetTextureOffset(id).ToString("R"));
                        break;
                }
            }
            using (var serialized = new SerializedObject(material))
                foreach (string name in new[] { "stringTagMap", "disabledShaderPasses" })
                {
                    SerializedProperty property = serialized.FindProperty(name);
                    if (property == null) continue;
                    SerializedProperty end = property.GetEndProperty();
                    for (property.Next(true); !SerializedProperty.EqualContents(property, end); property.Next(true))
                        if (property.propertyType == SerializedPropertyType.String) text.Append('|').Append(property.stringValue);
                }
            return text.ToString();
        }

        // Textures the user protected keep their own file so their exemption cannot spread or be lost.
        internal static int MergeDuplicateTextures(BSLKClubOptimizer.Context context, IEnumerable<Texture> protectedSources)
        {
            var exempt = new HashSet<Object>(protectedSources.Where(t => t != null && context.copies.ContainsKey(t)).Select(t => context.copies[t]));
            var textures = BSLKClubOptimizer.Textures(context.avatar).OfType<Texture2D>()
                .Where(t => !exempt.Contains(t) && context.copiedPaths.Contains(AssetDatabase.GetAssetPath(t)) && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) is TextureImporter)
                .ToList();
            var map = new Dictionary<Object, Object>();
            foreach (var group in textures.GroupBy(t => t.width + "x" + t.height + "|" + t.graphicsFormat + "|" + t.mipmapCount + "|" + ImportSignature(t)))
            {
                List<Texture2D> candidates = group.ToList();
                if (candidates.Count < 2) continue;
                var pixels = new Dictionary<Texture2D, Color32[]>();
                foreach (Texture2D texture in candidates)
                {
                    BSLKClubOptimizer.Progress("Поиск одинаковых текстур: " + texture.name, 0.5f);
                    pixels[texture] = BSLKClubTextures.Readback(texture, texture.width, texture.height);
                }
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (map.ContainsKey(candidates[i])) continue;
                    for (int j = i + 1; j < candidates.Count; j++)
                        if (!map.ContainsKey(candidates[j]) && Same(pixels[candidates[i]], pixels[candidates[j]])) map[candidates[j]] = candidates[i];
                }
            }
            if (map.Count > 0) BSLKClubOptimizer.RemapAll(context, map);
            context.notes.Add("Объединено одинаковых по содержимому текстур: " + map.Count + ".");
            return map.Count;
        }

        private static bool Same(Color32[] a, Color32[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) return false;
            return true;
        }

        private static string ImportSignature(Texture2D texture)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            return string.Join("|", importer.textureType, importer.textureShape, importer.sRGBTexture, importer.alphaSource, importer.alphaIsTransparency,
                importer.mipmapEnabled, importer.mipmapFilter, importer.streamingMipmaps, importer.wrapModeU, importer.wrapModeV, importer.filterMode,
                importer.anisoLevel, importer.npotScale, importer.maxTextureSize, importer.textureCompression, importer.crunchedCompression,
                importer.compressionQuality, standalone.overridden, standalone.maxTextureSize, standalone.format, standalone.crunchedCompression,
                android.overridden, android.maxTextureSize, android.format);
        }
    }
}
