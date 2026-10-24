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
            HashSet<Component> worn = ResolveOutfitToggles(context);
            PruneActions(context, (type, action) =>
            {
                if (type.EndsWith(" VF.Model.StateAction.ObjectToggleAction", StringComparison.Ordinal))
                {
                    GameObject target = action.FindPropertyRelative("obj")?.objectReferenceValue as GameObject;
                    return target != null && context.fixedVisibility.Contains(AnimationUtility.CalculateTransformPath(target.transform, context.avatar.transform));
                }
                if (type.EndsWith(" VF.Model.StateAction.MaterialAction", StringComparison.Ordinal))
                {
                    Renderer target = action.FindPropertyRelative("renderer")?.objectReferenceValue as Renderer;
                    return target != null && context.rendererPaths.Contains(AnimationUtility.CalculateTransformPath(target.transform, context.avatar.transform));
                }
                return false;
            }, "VRCFury: удалено действий переключения одежды {0}, пустых переключателей {1}.", worn);
        }

        // A clothing toggle (object actions, optionally with shrink keys and other work) follows the
        // scene state the user selected. Worn: the toggle stays in the menu, intact, and starts ON, so
        // the club avatar loads in the chosen outfit and the item can still be taken off. Not worn:
        // the whole toggle, including its shrink keys, is removed with the item. Unclear: untouched here.
        private static HashSet<Component> ResolveOutfitToggles(BSLKClubOptimizer.Context context)
        {
            var worn = new HashSet<Component>();
            int removed = 0;
            foreach (Component component in context.avatar.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.GetType().FullName != "VF.Model.VRCFury") continue;
                bool? selected = null;
                using (var serialized = new SerializedObject(component))
                {
                    SerializedProperty content = serialized.FindProperty("content");
                    SerializedProperty legacy = serialized.FindProperty("config.features");
                    if (content == null || content.propertyType != SerializedPropertyType.ManagedReference ||
                        !content.managedReferenceFullTypename.EndsWith(" VF.Model.Feature.Toggle", StringComparison.Ordinal) ||
                        (legacy != null && legacy.arraySize != 0)) continue;
                    if (Flag(content, "enableDriveGlobalParam") || Flag(content, "useGlobalParam") || Flag(content, "slider") || Flag(content, "separateLocal")) continue;
                    SerializedProperty actions = content.FindPropertyRelative("state.actions");
                    if (actions == null || !actions.isArray) continue;
                    bool unclear = false;
                    for (int i = 0; i < actions.arraySize; i++)
                    {
                        SerializedProperty action = actions.GetArrayElementAtIndex(i);
                        string type = action.propertyType == SerializedPropertyType.ManagedReference ? action.managedReferenceFullTypename : "";
                        if (!type.EndsWith(" VF.Model.StateAction.ObjectToggleAction", StringComparison.Ordinal)) continue;
                        GameObject target = action.FindPropertyRelative("obj")?.objectReferenceValue as GameObject;
                        if (target == null || !target.transform.IsChildOf(context.avatar.transform)) { unclear = true; continue; }
                        // VRCFury modes: 0 TurnOn, 1 TurnOff, 2 Toggle (ON inverts the current state).
                        int mode = action.FindPropertyRelative("mode")?.enumValueIndex ?? 0;
                        bool on = mode == 0 ? target.activeInHierarchy : mode == 1 ? !target.activeInHierarchy : false;
                        if (selected == null) selected = on;
                        else if (selected != on) unclear = true;
                    }
                    if (unclear || selected == null) continue;
                    if (selected == true)
                    {
                        SerializedProperty defaultOn = content.FindPropertyRelative("defaultOn");
                        if (defaultOn != null) defaultOn.boolValue = true;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        worn.Add(component);
                        continue;
                    }
                }
                Object.DestroyImmediate(component);
                removed++;
            }
            context.notes.Add("VRCFury: переключателей надетой одежды оставлено в меню " + worn.Count + " (включены по умолчанию), переключателей удалённых вещей убрано " + removed + ".");
            return worn;
        }

        private static bool Flag(SerializedProperty parent, string name)
        {
            SerializedProperty property = parent.FindPropertyRelative(name);
            return property != null && property.propertyType == SerializedPropertyType.Boolean && property.boolValue;
        }

        // Any action (toggle, material, blend shape, flipbook, ...) that references an object about
        // to be deleted can do no useful work and would reach the build with a missing target.
        internal static void RemoveActionsForDeleted(BSLKClubOptimizer.Context context, HashSet<GameObject> deleted)
        {
            if (deleted.Count == 0) return;
            PruneActions(context, (type, action) =>
            {
                SerializedProperty property = action.Copy();
                SerializedProperty end = action.GetEndProperty();
                while (property.Next(true) && !SerializedProperty.EqualContents(property, end))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    Object reference = property.objectReferenceValue;
                    GameObject target = reference as GameObject ?? (reference as Component)?.gameObject;
                    if (target != null && deleted.Contains(target)) return true;
                }
                return false;
            }, "VRCFury: удалено действий, направленных на удалённые объекты, {0}; опустевших переключателей {1}.");
        }

        private static void PruneActions(BSLKClubOptimizer.Context context, Func<string, SerializedProperty, bool> shouldRemove, string note, HashSet<Component> skip = null)
        {
            int actionsRemoved = 0, togglesRemoved = 0;
            foreach (Component component in context.avatar.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.GetType().FullName != "VF.Model.VRCFury" || (skip != null && skip.Contains(component))) continue;
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
                            if (shouldRemove(action.managedReferenceFullTypename, action))
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
            context.notes.Add(string.Format(note, actionsRemoved, togglesRemoved));
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
                    // A new state defaults to Write Defaults ON; mixing it into a WD OFF avatar breaks animation.
                    bool writeDefaults = WriteDefaults(layer.stateMachine, controller);
                    // Keep the layer index: SDK layer-control behaviours can address it by number.
                    foreach (ChildAnimatorState state in layer.stateMachine.states) layer.stateMachine.RemoveState(state.state);
                    foreach (ChildAnimatorStateMachine child in layer.stateMachine.stateMachines) layer.stateMachine.RemoveStateMachine(child.stateMachine);
                    layer.stateMachine.anyStateTransitions = new AnimatorStateTransition[0];
                    layer.stateMachine.entryTransitions = new AnimatorTransition[0];
                    AnimatorState fixedState = layer.stateMachine.AddState("Club fixed outfit");
                    fixedState.writeDefaultValues = writeDefaults;
                    layer.stateMachine.defaultState = fixedState;
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

        // The layer's own default state decides; otherwise the controller's prevailing setting.
        private static bool WriteDefaults(AnimatorStateMachine layer, AnimatorController controller)
        {
            if (layer.defaultState != null) return layer.defaultState.writeDefaultValues;
            List<AnimatorState> states = States(layer).ToList();
            if (states.Count == 0) states = controller.layers.SelectMany(l => States(l.stateMachine)).ToList();
            return states.Count == 0 || states.Count(s => s.writeDefaultValues) * 2 > states.Count;
        }

        private static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
        {
            return machine.states.Select(s => s.state).Concat(machine.stateMachines.SelectMany(child => States(child.stateMachine)));
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
