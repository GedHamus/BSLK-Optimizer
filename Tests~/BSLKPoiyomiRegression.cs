using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using BSLK.Optimizer;
using Object = UnityEngine.Object;

// Needs the Poiyomi Toon package (com.poiyomi.toon) in an isolated Unity 2022.3 project.
// Run: Unity -batchmode -projectPath <project> -executeMethod BSLKPoiyomiRegression.Run -logFile <log>
public static class BSLKPoiyomiRegression
{
    private static int checks;

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
        checks++;
        Debug.Log("PASS: " + message);
    }

    public static void Run()
    {
        try
        {
            string folder = "Assets/PoiTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            Shader shader = Shader.Find(".poiyomi/Poiyomi Toon");
            Check(shader != null, "Poiyomi Toon shader found");
            var plain = new Material(shader) { name = "Plain" };
            plain.SetFloat("_LightingMode", 5);
            plain.SetFloat("_LightingAdditiveType", 3);
            plain.SetFloat("_MochieBRDF", 0);
            plain.SetFloat("_LightingForceColorEnabled", 1);
            AssetDatabase.CreateAsset(plain, folder + "/Plain.mat");
            var shiny = new Material(shader) { name = "Shiny" };
            shiny.SetFloat("_MochieBRDF", 1);
            shiny.EnableKeyword("MOCHIE_PBR");
            shiny.SetFloat("_MochieMetallicMultiplier", 0.37f);
            shiny.SetFloat("_MochieRoughnessMultiplier", 0.21f);
            AssetDatabase.CreateAsset(shiny, folder + "/Shiny.mat");
            var other = new Material(Shader.Find("Standard")) { name = "Other" };
            AssetDatabase.CreateAsset(other, folder + "/Other.mat");
            AssetDatabase.SaveAssets();

            // Lock (optimize) the plain material the way Poiyomi does before upload.
            Type optimizer = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Thry.ThryEditor.ShaderOptimizer", false)).First(t => t != null);
            MethodInfo lockMaterials = optimizer.GetMethods(BindingFlags.Public | BindingFlags.Static).First(m => m.Name == "LockMaterials");
            lockMaterials.Invoke(null, lockMaterials.GetParameters().Select((p, i) => i == 0 ? (object)new[] { plain } : p.DefaultValue).ToArray());
            plain = AssetDatabase.LoadAssetAtPath<Material>(folder + "/Plain.mat");
            Func<Material, bool> isLocked = m => (bool)optimizer.GetMethod("IsMaterialLocked", new[] { typeof(Material) }).Invoke(null, new object[] { m });
            Check(isLocked(plain) && plain.shader.name.StartsWith("Hidden/Locked/"), "Fixture: plain material is locked (" + plain.shader.name + ")");

            var root = new GameObject("PoiAvatar");
            foreach (Material material in new[] { plain, shiny, other })
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.SetParent(root.transform);
                go.GetComponent<Renderer>().sharedMaterial = material;
            }
            string note = BSLKPoiyomi.MakeRealistic(root);
            Debug.Log("Note: " + note);
            plain = AssetDatabase.LoadAssetAtPath<Material>(folder + "/Plain.mat");
            shiny = AssetDatabase.LoadAssetAtPath<Material>(folder + "/Shiny.mat");
            Check(!isLocked(plain) && plain.shader.name == ".poiyomi/Poiyomi Toon", "Locked material unlocked to the editable shader");
            Check(plain.GetFloat("_LightingMode") == 6 && plain.IsKeywordEnabled("_LIGHTINGMODE_REALISTIC") && !plain.IsKeywordEnabled("_LIGHTINGMODE_FLAT"), "Base pass lighting set to Realistic");
            Check(plain.GetFloat("_LightingAdditiveType") == 0, "Add pass lighting set to Realistic");
            Check(plain.GetFloat("_LightingForceColorEnabled") == 0, "Force Light Color switched off");
            Check(BSLKPoiyomi.MajorVersion(plain) >= 9, "Poiyomi version read from the shader (" + BSLKPoiyomi.MajorVersion(plain) + ")");
            Check(plain.GetFloat("_MochieBRDF") == 1 && plain.IsKeywordEnabled("MOCHIE_PBR"), "Reflections & Specular enabled where it was off");
            Check(shiny.GetFloat("_MochieBRDF") == 1 && Mathf.Approximately(shiny.GetFloat("_MochieMetallicMultiplier"), 0.37f) &&
                Mathf.Approximately(shiny.GetFloat("_MochieRoughnessMultiplier"), 0.21f), "Already enabled Reflections & Specular left untouched");
            Check(shiny.GetFloat("_LightingMode") == 6 && shiny.GetFloat("_LightingAdditiveType") == 0, "Every Poiyomi material gets Realistic");
            Check(other.shader.name == "Standard", "Non-Poiyomi materials untouched");
            Check(note.Contains("разблокировано 1"), "Note reports the unlock");
            Object.DestroyImmediate(root);
            Debug.Log("BSLK POIYOMI REGRESSION PASSED: " + checks + " assertions");
            File.WriteAllText("poi-test-results.txt", "PASSED: " + checks + " assertions");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            File.WriteAllText("poi-test-results.txt", "FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }
}
