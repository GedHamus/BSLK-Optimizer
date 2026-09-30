using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BSLK.Optimizer
{
    internal static class BSLKClubAnimation
    {
        // Only remove a VRCFury action whose exact known schema targets fixed clothing.
        // Mixed toggles retain their other actions (gestures, blendshapes, sounds, etc.).
        internal static void FreezeBuildToggles(BSLKClubOptimizer.Context context)
        {
            int actionsRemoved = 0, togglesRemoved = 0;
            foreach (Component component in context.avatar.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.GetType().FullName != "VF.Model.VRCFury") continue;
                bool destroy = false;
                using (var serialized = new SerializedObject(component))
                {
                    var arrays = new List<string>();
                    SerializedProperty iterator = serialized.GetIterator();
                    while (iterator.Next(true))
                        if (iterator.isArray && iterator.propertyType != SerializedPropertyType.String && iterator.name == "actions") arrays.Add(iterator.propertyPath);
                    bool changed = false;
                    int remaining = 0;
                    foreach (string path in arrays)
                    {
                        SerializedProperty actions = serialized.FindProperty(path);
                        for (int i = actions.arraySize - 1; i >= 0; i--)
                        {
                            SerializedProperty action = actions.GetArrayElementAtIndex(i);
                            if (action.propertyType != SerializedPropertyType.ManagedReference) { remaining++; continue; }
                            string type = action.managedReferenceFullTypename;
                            bool remove = false;
                            if (type.EndsWith(" VF.Model.StateAction.ObjectToggleAction", StringComparison.Ordinal))
                            {
                                GameObject target = action.FindPropertyRelative("obj")?.objectReferenceValue as GameObject;
                                remove = target != null && context.fixedVisibility.Contains(AnimationUtility.CalculateTransformPath(target.transform, context.avatar.transform));
                            }
                            if (type.EndsWith(" VF.Model.StateAction.MaterialAction", StringComparison.Ordinal))
                            {
                                Renderer target = action.FindPropertyRelative("renderer")?.objectReferenceValue as Renderer;
                                remove = target != null && context.rendererPaths.Contains(AnimationUtility.CalculateTransformPath(target.transform, context.avatar.transform));
                            }
                            if (remove)
                            {
                                actions.DeleteArrayElementAtIndex(i);
                                changed = true;
                                actionsRemoved++;
                            }
                            else remaining++;
                        }
                    }
                    SerializedProperty content = serialized.FindProperty("content");
                    SerializedProperty legacy = serialized.FindProperty("config.features");
                    bool plainToggle = content != null && content.propertyType == SerializedPropertyType.ManagedReference &&
                        content.managedReferenceFullTypename.EndsWith(" VF.Model.Feature.Toggle", StringComparison.Ordinal) &&
                        (legacy == null || legacy.arraySize == 0);
                    // Parameter-driving toggles can affect unrelated functionality; retain them.
                    bool drivesParameter = content != null && ((content.FindPropertyRelative("enableDriveGlobalParam")?.boolValue ?? false) ||
                        (content.FindPropertyRelative("useGlobalParam")?.boolValue ?? false));
                    destroy = changed && remaining == 0 && plainToggle && !drivesParameter;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                if (destroy) { Object.DestroyImmediate(component); togglesRemoved++; }
            }
            context.notes.Add("VRCFury: удалено действий переключения одежды " + actionsRemoved + ", пустых переключателей " + togglesRemoved + ".");
        }

        internal static void PruneEmptyLayersAndMenus(BSLKClubOptimizer.Context context)
        {
            var candidates = new HashSet<string>();
            var stillUsed = new HashSet<string>();
            AnimatorController[] controllers = context.copies.Values.OfType<AnimatorController>().Distinct().ToArray();
            int removedLayers = 0, removedControls = 0;
            foreach (AnimatorController controller in controllers)
            {
                // Synchronized layers refer to layer indices; do not renumber these controllers.
                if (controller.layers.Any(l => l.syncedLayerIndex >= 0)) continue;
                for (int i = controller.layers.Length - 1; i > 0; i--)
                {
                    AnimatorControllerLayer layer = controller.layers[i];
                    if (!Empty(layer.stateMachine) || !Changed(layer.stateMachine, context.modifiedClips)) continue;
                    CollectParameters(layer.stateMachine, candidates);
                    // Keep the layer index: SDK layer-control behaviours can address it by number.
                    foreach (ChildAnimatorState state in layer.stateMachine.states) layer.stateMachine.RemoveState(state.state);
                    foreach (ChildAnimatorStateMachine child in layer.stateMachine.stateMachines) layer.stateMachine.RemoveStateMachine(child.stateMachine);
                    layer.stateMachine.anyStateTransitions = new AnimatorStateTransition[0];
                    layer.stateMachine.entryTransitions = new AnimatorTransition[0];
                    layer.stateMachine.defaultState = layer.stateMachine.AddState("Club fixed outfit");
                    removedLayers++;
                }
            }
            foreach (AnimatorController controller in controllers)
                foreach (AnimatorControllerLayer layer in controller.layers) CollectParameters(layer.stateMachine, stillUsed);
            candidates.ExceptWith(stillUsed);

            // Be conservative about custom behaviours/component strings that may drive a parameter.
            foreach (Object target in context.copies.Values.Concat(context.avatar.GetComponentsInChildren<Component>(true).Cast<Object>()).Distinct())
            {
                if (target == null || target is AnimatorController || target is AnimationClip || target is Material || target is Texture || target is Mesh) continue;
                if (target.GetType().Name == "VRCExpressionsMenu" || target.GetType().Name == "VRCExpressionParameters") continue;
                if (target.GetType().Namespace == "UnityEditor.Animations") continue;
                using (var serialized = new SerializedObject(target))
                {
                    SerializedProperty property = serialized.GetIterator();
                    while (property.Next(true))
                        if (property.propertyType == SerializedPropertyType.String) candidates.Remove(property.stringValue);
                }
            }
            foreach (Object menu in context.copies.Values.Distinct().Where(x => x != null && x.GetType().Name == "VRCExpressionsMenu"))
            {
                using (var serialized = new SerializedObject(menu))
                {
                    SerializedProperty controls = serialized.FindProperty("controls");
                    if (controls == null || !controls.isArray) continue;
                    for (int i = controls.arraySize - 1; i >= 0; i--)
                    {
                        SerializedProperty control = controls.GetArrayElementAtIndex(i);
                        SerializedProperty type = control.FindPropertyRelative("type");
                        string name = control.FindPropertyRelative("parameter.name")?.stringValue;
                        if (type == null || type.propertyType != SerializedPropertyType.Enum || string.IsNullOrEmpty(name) || !candidates.Contains(name)) continue;
                        string kind = type.enumNames[type.enumValueIndex];
                        if (kind != "Button" && kind != "Toggle") continue;
                        controls.DeleteArrayElementAtIndex(i);
                        removedControls++;
                    }
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            context.notes.Add("Очищено слоёв одежды с сохранением индексов: " + removedLayers + "; убрано связанных кнопок/переключателей меню: " + removedControls + ". Общие параметры сохранены.");
        }

        private static bool Changed(AnimatorStateMachine machine, HashSet<AnimationClip> changed)
        {
            return machine.states.Any(s => ChangedMotion(s.state.motion, changed)) || machine.stateMachines.Any(s => Changed(s.stateMachine, changed));
        }

        private static bool ChangedMotion(Motion motion, HashSet<AnimationClip> changed)
        {
            if (motion is AnimationClip clip) return changed.Contains(clip);
            return motion is BlendTree tree && tree.children.Any(child => ChangedMotion(child.motion, changed));
        }

        private static bool Empty(AnimatorStateMachine machine)
        {
            if (machine.behaviours.Length != 0) return false;
            foreach (ChildAnimatorState state in machine.states)
                if (state.state.behaviours.Length != 0 || !EmptyMotion(state.state.motion)) return false;
            return machine.stateMachines.All(child => Empty(child.stateMachine));
        }

        private static bool EmptyMotion(Motion motion)
        {
            if (motion == null) return true;
            if (motion is AnimationClip clip)
                return AnimationUtility.GetCurveBindings(clip).Length == 0 && AnimationUtility.GetObjectReferenceCurveBindings(clip).Length == 0 && AnimationUtility.GetAnimationEvents(clip).Length == 0;
            if (motion is BlendTree tree) return tree.children.All(child => EmptyMotion(child.motion));
            return false;
        }

        private static void CollectParameters(AnimatorStateMachine machine, HashSet<string> names)
        {
            foreach (AnimatorTransition transition in machine.entryTransitions)
                foreach (AnimatorCondition condition in transition.conditions) names.Add(condition.parameter);
            foreach (AnimatorStateTransition transition in machine.anyStateTransitions)
                foreach (AnimatorCondition condition in transition.conditions) names.Add(condition.parameter);
            foreach (ChildAnimatorState child in machine.states)
            {
                AnimatorState state = child.state;
                foreach (AnimatorStateTransition transition in state.transitions)
                    foreach (AnimatorCondition condition in transition.conditions) names.Add(condition.parameter);
                if (state.speedParameterActive) names.Add(state.speedParameter);
                if (state.timeParameterActive) names.Add(state.timeParameter);
                if (state.mirrorParameterActive) names.Add(state.mirrorParameter);
                if (state.cycleOffsetParameterActive) names.Add(state.cycleOffsetParameter);
                CollectBlendTree(state.motion as BlendTree, names);
            }
            foreach (ChildAnimatorStateMachine child in machine.stateMachines)
            {
                foreach (AnimatorTransition transition in machine.GetStateMachineTransitions(child.stateMachine))
                    foreach (AnimatorCondition condition in transition.conditions) names.Add(condition.parameter);
                CollectParameters(child.stateMachine, names);
            }
        }

        private static void CollectBlendTree(BlendTree tree, HashSet<string> names)
        {
            if (tree == null) return;
            names.Add(tree.blendParameter);
            names.Add(tree.blendParameterY);
            foreach (ChildMotion child in tree.children)
            {
                names.Add(child.directBlendParameter);
                CollectBlendTree(child.motion as BlendTree, names);
            }
        }
    }
}
