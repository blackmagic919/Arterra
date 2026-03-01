using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Arterra.Utils
{
    /// <summary>
    /// Put this on an Option&lt;List&lt;TElement&gt;&gt; field to display the list with per-element labels
    /// derived from a nested SerializeReference field on each element (default: "value").
    ///
    /// Example:
    /// [TypeNameElementList("value")]
    /// public Option&lt;List&lt;ReferenceOption&lt;IBehaviorSetting&gt;&gt;&gt; Settings;
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TypeNameElementListAttribute : PropertyAttribute
    {
        public readonly string elementValuePath;
        public readonly Type[] allowedTypes;

        /// <param name="elementValuePath">
        /// Relative path from the array element to the SerializeReference field used for naming.
        /// For ReferenceOption&lt;IBehaviorSetting&gt; this is "value".
        /// </param>
        /// <param name="allowedTypes">
        /// Array of types that can be added to the list. If provided, clicking + will show a popup to select from these types.
        /// </param>
        public TypeNameElementListAttribute(string elementValuePath = "value", params Type[] allowedTypes)
        {
            this.elementValuePath = elementValuePath;
            this.allowedTypes = allowedTypes;
        }
    }

    [CustomPropertyDrawer(typeof(TypeNameElementListAttribute))]
    public sealed class TypeNameElementListDrawer : PropertyDrawer
    {
        // Cache RL per-inspector binding. Must be conservative because SerializedObjects get disposed often.
        static readonly Dictionary<string, ReorderableList> s_lists = new();

        static TypeNameElementListDrawer()
        {
            // Clear caches on transitions that commonly invalidate SerializedObjects.
            AssemblyReloadEvents.beforeAssemblyReload += ClearCache;
            EditorApplication.playModeStateChanged += _ => ClearCache();
            Selection.selectionChanged += ClearCache;
        }

        public static void ClearCache()
        {
            s_lists.Clear();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var listProp = FindInnerListProperty(property);
            if (listProp == null || !listProp.isArray)
                return EditorGUI.GetPropertyHeight(property, label, includeChildren: true);

            // Multi-object editing where values differ: show help + default field.
            if (IsMultiEditAndDifferent(property, listProp))
                return EditorGUIUtility.singleLineHeight * 3.2f;

            var rl = GetOrCreateList(property, listProp, label);
            return rl != null ? rl.GetHeight() : EditorGUI.GetPropertyHeight(property, label, includeChildren: true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var listProp = FindInnerListProperty(property);
            if (listProp == null || !listProp.isArray)
            {
                EditorGUI.PropertyField(position, property, label, includeChildren: true);
                return;
            }

            if (IsMultiEditAndDifferent(property, listProp))
            {
                using (new EditorGUI.PropertyScope(position, label, property))
                {
                    var box = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight * 2.2f);
                    EditorGUI.HelpBox(
                        box,
                        "Type-named list UI is disabled for multi-object editing when values differ.\nSelect a single object to edit with type labels.",
                        MessageType.Info);

                    var fieldRect = new Rect(
                        position.x,
                        position.y + box.height + 2,
                        position.width,
                        EditorGUIUtility.singleLineHeight);

                    EditorGUI.PropertyField(fieldRect, property, label, includeChildren: true);
                }
                return;
            }

            var rl = GetOrCreateList(property, listProp, label);
            if (rl == null)
            {
                EditorGUI.PropertyField(position, property, label, includeChildren: true);
                return;
            }

            // IMPORTANT: always rebind the list to the current listProp before drawing.
            rl.serializedProperty = listProp;

            rl.DoList(position);
        }

        static bool IsMultiEditAndDifferent(SerializedProperty optionProp, SerializedProperty listProp)
        {
            var so = optionProp.serializedObject;
            if (so == null) return false;

            if (!so.isEditingMultipleObjects) return false;

            // If different sizes/types/etc, don't try to reconcile (ReorderableList isn't safe here).
            return optionProp.hasMultipleDifferentValues || listProp.hasMultipleDifferentValues;
        }

        /// <summary>
        /// Given Option&lt;List&lt;...&gt;&gt; property, find its inner list at relative path "value".
        /// (Matches your Option usage: _Setting.value, Settings.value, etc.)
        /// </summary>
        static SerializedProperty FindInnerListProperty(SerializedProperty optionProp)
        {
            // Your Option<T> uses a field named "value" (based on your code)
            return optionProp.FindPropertyRelative("value");
        }

        ReorderableList GetOrCreateList(SerializedProperty optionProp, SerializedProperty listProp, GUIContent label)
        {
            if (optionProp.serializedObject == null)
                return null;

            // Cache key: per serialized object instance + property path.
            // Using GetHashCode() avoids some collisions across inspectors.
            string key = $"{optionProp.serializedObject.GetHashCode()}:{optionProp.propertyPath}";

            if (s_lists.TryGetValue(key, out var existing) && existing != null)
            {
                // Rebind to the latest listProp.
                existing.serializedProperty = listProp;
                return existing;
            }

            var attr = (TypeNameElementListAttribute)attribute;

            var rl = new ReorderableList(optionProp.serializedObject, listProp, true, true, true, true);

            rl.drawHeaderCallback = r => EditorGUI.LabelField(r, label);

            // CRITICAL: do NOT capture listProp in callbacks.
            rl.elementHeightCallback = index =>
            {
                var sp = rl.serializedProperty;
                if (sp == null || sp.serializedObject == null)
                    return EditorGUIUtility.singleLineHeight;

                if (index < 0 || index >= sp.arraySize)
                    return EditorGUIUtility.singleLineHeight;

                var el = sp.GetArrayElementAtIndex(index);
                return EditorGUI.GetPropertyHeight(el, includeChildren: true) + 4;
            };

            rl.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                var sp = rl.serializedProperty;
                if (sp == null || sp.serializedObject == null)
                    return;

                if (index < 0 || index >= sp.arraySize)
                    return;

                rect.y += 2;
                rect.height -= 4;

                var element = sp.GetArrayElementAtIndex(index);
                var ifaceProp = element.FindPropertyRelative(attr.elementValuePath);

                string typeName = GetManagedRefTypeName(ifaceProp) ?? "None";
                EditorGUI.PropertyField(rect, element, new GUIContent(typeName), includeChildren: true);
            };

            #region Add Callback with Type Selection
            rl.onAddCallback = list =>
            {
                var sp = list.serializedProperty;
                if (sp == null || sp.serializedObject == null)
                    return;

                // If allowedTypes are specified, show a context menu
                if (attr.allowedTypes != null && attr.allowedTypes.Length > 0)
                {
                    ShowTypeSelectionMenu(attr, sp);
                }
                else
                {
                    // Original behavior: just add with null value
                    AddElementWithType(sp, attr, null);
                }
            };
            #endregion

            // Optional: nicer remove behavior (keeps selection sane)
            rl.onRemoveCallback = list =>
            {
                var sp = list.serializedProperty;
                if (sp == null || sp.serializedObject == null)
                    return;

                ReorderableList.defaultBehaviours.DoRemoveButton(list);
                sp.serializedObject.ApplyModifiedProperties();
            };

            s_lists[key] = rl;
            return rl;
        }

        #region Type Selection Menu

        static void ShowTypeSelectionMenu(TypeNameElementListAttribute attr, SerializedProperty listProp)
        {
            var menu = new GenericMenu();

            foreach (var type in attr.allowedTypes)
            {
                string typeName = type.Name;
                menu.AddItem(new GUIContent(typeName), false, () => AddElementWithType(listProp, attr, type));
            }

            menu.ShowAsContext();
        }

        static void AddElementWithType(SerializedProperty listProp, TypeNameElementListAttribute attr, Type typeToCreate)
        {
            int i = listProp.arraySize;
            listProp.arraySize++;

            var element = listProp.GetArrayElementAtIndex(i);
            var ifaceProp = element.FindPropertyRelative(attr.elementValuePath);

            if (ifaceProp != null && ifaceProp.propertyType == SerializedPropertyType.ManagedReference)
            {
                // Create instance of the selected type using Activator.CreateInstance
                if (typeToCreate != null)
                {
                    try
                    {
                        object instance = Activator.CreateInstance(typeToCreate);
                        ifaceProp.managedReferenceValue = instance;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Failed to create instance of type {typeToCreate.Name}: {ex.Message}");
                        ifaceProp.managedReferenceValue = null;
                    }
                }
                else
                {
                    ifaceProp.managedReferenceValue = null;
                }
            }

            listProp.serializedObject.ApplyModifiedProperties();
        }

        #endregion

        static string GetManagedRefTypeName(SerializedProperty prop)
        {
            if (prop == null)
                return null;

            // This is what you want for interface impls stored via [SerializeReference]
            if (prop.propertyType == SerializedPropertyType.ManagedReference)
            {
                object obj = prop.managedReferenceValue;
                if (obj != null)
                    return obj.GetType().Name;

                // Fallback string can be present even when null; format varies by Unity version.
                string full = prop.managedReferenceFullTypename;
                if (!string.IsNullOrEmpty(full))
                {
                    // Often "AssemblyName Full.Namespace.TypeName"
                    int space = full.LastIndexOf(' ');
                    string typePart = space >= 0 ? full[(space + 1)..] : full;

                    int lastDot = typePart.LastIndexOf('.');
                    return lastDot >= 0 ? typePart[(lastDot + 1)..] : typePart;
                }
            }

            // If you ever switch to UnityEngine.Object references, this will still label nicely.
            if (prop.propertyType == SerializedPropertyType.ObjectReference)
            {
                var o = prop.objectReferenceValue;
                return o != null ? o.GetType().Name : "None";
            }

            return null;
        }
    }
}