using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using BSLK.Optimizer;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.PhysBone.Components;
using Object = UnityEngine.Object;

public static class BSLKClubSdkRegression
{
    public static void Run()
    {
        try
        {
            string fixture = "Assets/SdkTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(fixture));
            var root = new GameObject("SdkAvatar");
            var descriptor = root.AddComponent<VRCAvatarDescriptor>();
            var wardrobe = new GameObject("Shirt"); wardrobe.transform.SetParent(root.transform);
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            wardrobe.AddComponent<MeshFilter>().sharedMesh = mesh;
            wardrobe.AddComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Standard"));
            var clip = new AnimationClip();
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Shirt", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 1, 0));
            AssetDatabase.CreateAsset(clip, fixture + "/clothes.anim");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(fixture + "/Clothes.controller");
            controller.AddParameter("Clothes", AnimatorControllerParameterType.Bool);
            controller.AddLayer("Outfit");
            var state = controller.layers[1].stateMachine.AddState("Clothing"); state.motion = clip;
            controller.layers[1].stateMachine.AddAnyStateTransition(state).AddCondition(AnimatorConditionMode.If, 0, "Clothes");
            root.AddComponent<Animator>().runtimeAnimatorController = controller;
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.controls.Add(new VRCExpressionsMenu.Control { name = "Clothes", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "Clothes" } });
            menu.controls.Add(new VRCExpressionsMenu.Control { name = "Gesture", type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "Gesture" } });
            AssetDatabase.CreateAsset(menu, fixture + "/menu.asset");
            descriptor.customExpressions = true; descriptor.expressionsMenu = menu;

            var boneRoot = new GameObject("Hair"); boneRoot.transform.SetParent(root.transform);
            var boneChild = new GameObject("HairChild"); boneChild.transform.SetParent(boneRoot.transform); boneChild.transform.localPosition = Vector3.up;
            var boneLeaf = new GameObject("HairLeaf"); boneLeaf.transform.SetParent(boneChild.transform); boneLeaf.transform.localPosition = Vector3.up;
            var physBone = boneRoot.AddComponent<VRCPhysBone>(); physBone.rootTransform = boneRoot.transform;
            var collider = root.AddComponent<VRCPhysBoneCollider>();
            physBone.colliders.Clear();
            physBone.colliders.Add(collider); physBone.colliders.Add(null); physBone.colliders.Add(collider);
            Type furyType = FindType("VF.Model.VRCFury");
            if (furyType == null) throw new Exception("This integration fixture also requires VRCFury.");
            Component toggle = root.AddComponent(furyType);
            object toggleContent = Activator.CreateInstance(FindType("VF.Model.Feature.Toggle"), true);
            object action = Activator.CreateInstance(FindType("VF.Model.StateAction.ObjectToggleAction"), true);
            action.GetType().GetField("obj").SetValue(action, wardrobe);
            object toggleState = toggleContent.GetType().GetField("state").GetValue(toggleContent);
            ((System.Collections.IList)toggleState.GetType().GetField("actions").GetValue(toggleState)).Add(action);
            furyType.GetField("content").SetValue(toggle, toggleContent);
            Component sps = root.AddComponent(furyType);
            furyType.GetField("content").SetValue(sps, Activator.CreateInstance(FindType("VF.Model.Feature.SpsOptions"), true));
            if (!BSLKClubSdk.TryRead(root, out var before, out var reason)) throw new Exception("SDK stats unavailable: " + reason);
            Debug.Log("SDK BEFORE: collisions=" + before.collisions + " triangles=" + before.triangles);
            var result = BSLKClubOptimizer.Create(root, new BSLKClubOptimizer.Options { optimizeTextures = false });
            if (!BSLKClubSdk.TryRead(result.avatar, out var after, out reason)) throw new Exception("SDK stats after copy unavailable: " + reason);
            var copiedBone = result.avatar.GetComponentInChildren<VRCPhysBone>();
            if (copiedBone.colliders.Count != 1 || physBone.colliders.Count != 3) throw new Exception("PhysBone cleanup/isolation failed");
            if (result.avatar.GetComponents(furyType).Length != 0 || root.GetComponents(furyType).Length != 2)
                throw new Exception("VRCFury clothing/SPS cleanup or source isolation failed");
            var copiedMenu = result.avatar.GetComponent<VRCAvatarDescriptor>().expressionsMenu;
            if (copiedMenu == menu || menu.controls.Count != 2 || copiedMenu.controls.Count != 1 || copiedMenu.controls[0].name != "Gesture")
                throw new Exception("Expression menu cleanup/isolation failed: " + copiedMenu.controls.Count);
            Debug.Log("SDK AFTER: collisions=" + after.collisions + " triangles=" + after.triangles);
            Debug.Log("BSLK SDK REGRESSION PASSED: real SDK stats, PhysBone links, menu cleanup, VRCFury toggles/SPS, source isolation");
            File.WriteAllText("sdk-test-results.txt", "PASSED: SDK integration, PhysBone, expression menus, VRCFury toggles/SPS");
            Object.DestroyImmediate(root); Object.DestroyImmediate(result.avatar);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            File.WriteAllText("sdk-test-results.txt", "FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }

    private static Type FindType(string name)
    {
        return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);
    }
}
