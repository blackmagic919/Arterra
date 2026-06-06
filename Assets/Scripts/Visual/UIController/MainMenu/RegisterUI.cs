using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Arterra.Configuration;
using Unity.Mathematics;
using System.Linq;

namespace Arterra.Editor {

    /// <summary>
    /// A unified field that can hold either a TagRegistry.Tags value or a registry reference.
    /// If tagValue is None, registryValue is used.
    /// </summary>
    [Serializable]
    public struct TagOrRegistryReference {
        /// <summary>The tag value. If None, registryValue is used.</summary>
        public TagRegistry.Tags tagValue;
        /// <summary>The registry name when tagValue == None.</summary>
        public string registryValue;

        /// <summary> Implicitly converts the option to the value it holds. This is useful for obtaining the value</summary>
        /// <param name="option"> The option itself </param>
        public static implicit operator string(TagOrRegistryReference val) => val.registryValue;
        /// <summary> Implicitly converts the value to an option. This is useful for setting the value  </summary>
        /// <param name="val">The value which we want to set to <see cref="value"/></param>
        public static implicit operator TagOrRegistryReference(string val) => new TagOrRegistryReference(val);

        public TagOrRegistryReference(TagRegistry.Tags tag) {
            tagValue = tag;
            registryValue = "";
        }

        public TagOrRegistryReference(string registryName) {
            tagValue = TagRegistry.Tags.None;
            registryValue = registryName;
        }

        public bool IsTag => tagValue != TagRegistry.Tags.None;
        public bool IsRegistryReference => !IsTag;

        public bool Is(IRegistered registered, ICatalgoue catalogue) {
            if (IsTag) return catalogue.GetMostSpecificTag(tagValue, registered.Index, out _);
            return registered.Index == catalogue.RetrieveIndex(registryValue);
        }
    }

    /// <summary>
    /// Attribute for TagOrRegistryReference fields. Specifies which registry to use when this stores a registry reference.
    /// </summary>
    public class TagOrRegistryReferenceAttribute : PropertyAttribute {
        public string RegistryName;
        public string NamePropertyPath;
        
        public TagOrRegistryReferenceAttribute(string registryName, string namePropertyPath = "/Names") {
            this.RegistryName = registryName;
            this.NamePropertyPath = namePropertyPath;
        }
    }

    public class RegistryReference : PropertyAttribute {
        public string RegistryName;
        public string NamePropertyPath;
        public RegistryReference(string RegistryName, string NamePropertyPath = "/Names") {
            this.RegistryName = RegistryName;
            this.NamePropertyPath = NamePropertyPath;
        }
    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(RegistryReference))]
    public class RegistryReferenceDrawer : PropertyDrawer {
        public int BitShift = 0;
        public uint BitMask = 0xFFFFFFFF;
        public string NameLookupPath = "/Names";
        public static Dictionary<string, IRegister> RegistryAssociation;
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            SetupRegistries();

            string registryName = ((RegistryReference)attribute).RegistryName;
            NameLookupPath = ((RegistryReference)attribute).NamePropertyPath;

            if (registryName == null || !RegistryAssociation.TryGetValue(registryName, out IRegister registry)) {
                //make disabled field
                return;
            }

            if (IsSupportedList(property)) {
                DrawRegistryDropdownList(position, property, label, registry);
                return;
            }

            DrawRegistryDropdown(position, property, label, registry);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            if (IsSupportedList(property)) {
                float line = EditorGUIUtility.singleLineHeight;
                float spacing = EditorGUIUtility.standardVerticalSpacing;
                return (line * (property.arraySize + 1)) + (spacing * property.arraySize);
            }
            return base.GetPropertyHeight(property, label);
        }

        private static bool IsSupportedList(SerializedProperty property) {
            if (property == null || !property.isArray || property.propertyType != SerializedPropertyType.Generic)
                return false;
            if (property.arrayElementType == "int" || property.arrayElementType == "string")
                return true;
            if (property.arraySize == 0)
                return false;
            SerializedProperty first = property.GetArrayElementAtIndex(0);
            return first.propertyType == SerializedPropertyType.Integer || first.propertyType == SerializedPropertyType.String;
        }

        private void DrawRegistryDropdownList(Rect position, SerializedProperty property, GUIContent label, IRegister registry) {
            EditorGUI.BeginProperty(position, label, property);

            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            Rect row = new Rect(position.x, position.y, position.width, line);

            Rect prefixRect = EditorGUI.PrefixLabel(row, label);
            int newSize = EditorGUI.IntField(prefixRect, property.arraySize);
            if (newSize != property.arraySize)
                property.arraySize = Math.Max(0, newSize);

            List<string> options = BuildRegistryOptions(registry);

            for (int i = 0; i < property.arraySize; i++) {
                row.y += line + spacing;
                SerializedProperty element = property.GetArrayElementAtIndex(i);
                string currentValue = GetReferenceName(element);
                int selectedIndex = Math.Max(0, options.IndexOf(currentValue));

                DrawDropdownPopup(row, currentValue, options, selectedIndex, val => {
                    SetReferenceName(element, val);
                    property.serializedObject.ApplyModifiedProperties();
                });
            }

            EditorGUI.EndProperty();
        }

        public void DrawRegistryDropdown(Rect position, SerializedProperty property, GUIContent label, IRegister registry) {
            EditorGUI.BeginProperty(position, label, property);

            Rect labelRect = EditorGUI.PrefixLabel(position, label);

            int currentIndex;
            string currentValue = GetReferenceName(property);
            if (currentValue != null && registry.Contains(currentValue))
                currentIndex = registry.RetrieveIndex(currentValue);
            else
                currentIndex = 0;

            List<string> options = BuildRegistryOptions(registry);
            DrawDropdownPopup(labelRect, currentValue, options, currentIndex + 1, val => {
                SetReferenceName(property, val);
                property.serializedObject.ApplyModifiedProperties();
            });

            EditorGUI.EndProperty();
        }

        private static List<string> BuildRegistryOptions(IRegister registry) {
            List<string> options = new List<string>() { "" };
            for (int i = 0; i < registry.Count(); i++)
                options.Add(registry.RetrieveName(i));
            return options;
        }

        private static void DrawDropdownPopup(Rect rect, string currentValue, List<string> options, int selectedIndex, Action<string> onSelect) {
            string buttonText = string.IsNullOrEmpty(currentValue) ? "[Select]" : currentValue;
            if (!GUI.Button(rect, buttonText, EditorStyles.popup))
                return;

            int clampedIndex = math.clamp(selectedIndex, 0, math.max(options.Count - 1, 0));
            SearchablePopup popup = new SearchablePopup(
                options,
                clampedIndex,
                (i, val) => onSelect(val));

            PopupWindow.Show(rect, popup);
        }

        public static void SetupRegistries() {
            if (RegistryAssociation != null) return;
            Config.TEMPLATE = Config.TEMPLATE != null ?
                Config.TEMPLATE : Resources.Load<Config>("Config");

            IRegister.AssociateRegistries(Config.TEMPLATE, ref RegistryAssociation);
        }

        /// <summary>
        /// Helper used by other drawers to look up a registry by name.
        /// </summary>
        public static bool TryGetRegistry(string registryName, out IRegister registry) {
            SetupRegistries();
            if (RegistryAssociation != null)
                return RegistryAssociation.TryGetValue(registryName, out registry);
            registry = null;
            return false;
        }

        public string GetReferenceName(SerializedProperty property) {
            if (property.propertyType == SerializedPropertyType.String)
                return property.stringValue;
            else if (property.propertyType != SerializedPropertyType.Integer)
                return null;

            uint nameIndex = (property.uintValue >> BitShift) & BitMask;
            if (!GetNamesPropertyPath(property, NameLookupPath, out SerializedProperty namesProp))
                return null;

            if (nameIndex < 0 || namesProp.arraySize <= nameIndex) {
                nameIndex = (uint)math.max(namesProp.arraySize - 1, 0);
                property.uintValue = nameIndex;
                if (nameIndex >= namesProp.arraySize) return "";
            }

            SerializedProperty nameProp = namesProp.GetArrayElementAtIndex((int)nameIndex);
            return nameProp?.stringValue;
        }

        public void SetReferenceName(SerializedProperty property, string value) {
            if (property.propertyType == SerializedPropertyType.String)
                property.stringValue = value;
            if (property.propertyType != SerializedPropertyType.Integer)
                return;

            if (!GetNamesPropertyPath(property, NameLookupPath, out SerializedProperty namesProp))
                return;

            SerializedProperty nameProp;
            //Check if it already contains
            string valueLower = value.ToLower();
            for (uint i = 0; i < namesProp.arraySize; i++) {
                nameProp = namesProp.GetArrayElementAtIndex((int)i);
                if (nameProp == null || nameProp.propertyType != SerializedPropertyType.String)
                    continue;
                if (!nameProp.stringValue.ToLower().Equals(valueLower)) continue;
                property.uintValue = ((i & BitMask) << BitShift) | (property.uintValue & (~(BitMask << BitShift)));
                return;
            }

            int newIndex = namesProp.arraySize;
            namesProp.InsertArrayElementAtIndex(namesProp.arraySize);
            nameProp = namesProp.GetArrayElementAtIndex(newIndex);
            nameProp.stringValue = value;

            property.uintValue = (((uint)newIndex & BitMask) << BitShift)
                | (property.uintValue & (~(BitMask << BitShift)));

        }

        private bool GetNamesPropertyPath(SerializedProperty cur, string path, out SerializedProperty prop) {
            prop = cur.Copy();
            if (string.IsNullOrEmpty(path) || cur == null)
                return false;

            List<string> elements = path.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();

            //Handle absolute path
            if (path[0] == '/' && elements.Count >= 1) {
                prop = prop.serializedObject.FindProperty(elements[0]);
                if (prop == null) return false;
                elements.RemoveAt(0);
            }

            foreach (var element in elements) {
                if (element == "..") { //Process parent
                    if (!prop.propertyPath.Contains(".")) return false; //We cannot discover references above this
                    string parentPath = prop.propertyPath[..prop.propertyPath.LastIndexOf('.')];
                    prop = cur.serializedObject.FindProperty(parentPath);
                    if (prop == null) return false;
                } else if (prop.isArray) {
                    if (int.TryParse(element, out int index)) {
                        prop = prop.GetArrayElementAtIndex(index);
                        if (prop == null) return false;
                    } else {
                        return false; // invalid index
                    }
                } else {
                    prop = prop.FindPropertyRelative(element);
                    if (prop == null) return false;
                }
            }

            if (!prop.isArray) {
                //Assume the generic to be Option<List<string>>
                if (prop.propertyType == SerializedPropertyType.Generic)
                    prop = prop.FindPropertyRelative("value");
            }
            if (prop == null || !prop.isArray) return false;
            return true;
        }
    }

    [CustomPropertyDrawer(typeof(TagOrRegistryReferenceAttribute))]
    public class TagOrRegistryReferenceDrawer : PropertyDrawer {
        private static Dictionary<string, IRegister> RegistryAssociation;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            RegistryReferenceDrawer.SetupRegistries();
            RegistryAssociation = RegistryReferenceDrawer.RegistryAssociation;

            string registryName = ((TagOrRegistryReferenceAttribute)attribute).RegistryName;

            if (IsSupportedList(property)) {
                DrawTagOrRegistryList(position, property, label, registryName);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);
            Rect labelRect = EditorGUI.PrefixLabel(position, label);

            // Get the tagValue and registryValue properties
            SerializedProperty tagValueProp = property.FindPropertyRelative("tagValue");
            SerializedProperty registryValueProp = property.FindPropertyRelative("registryValue");

            if (tagValueProp == null || registryValueProp == null) {
                EditorGUI.LabelField(labelRect, "Error: TagOrRegistryReference structure mismatch");
                EditorGUI.EndProperty();
                return;
            }

            string displayText = GetDisplayText(tagValueProp, registryValueProp);

            if (GUI.Button(labelRect, string.IsNullOrEmpty(displayText) ? "[Select]" : displayText, EditorStyles.popup)) {
                BuildAndShowPopup(registryName, tagValueProp, registryValueProp);
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            if (IsSupportedList(property)) {
                float line = EditorGUIUtility.singleLineHeight;
                float spacing = EditorGUIUtility.standardVerticalSpacing;
                return (line * (property.arraySize + 1)) + (spacing * property.arraySize);
            }
            return base.GetPropertyHeight(property, label);
        }

        private static bool IsSupportedList(SerializedProperty property) {
            if (property == null || !property.isArray || property.propertyType != SerializedPropertyType.Generic)
                return false;
            if (property.arrayElementType == "TagOrRegistryReference")
                return true;
            if (property.arraySize == 0)
                return false;

            SerializedProperty first = property.GetArrayElementAtIndex(0);
            return first != null
                && first.FindPropertyRelative("tagValue") != null
                && first.FindPropertyRelative("registryValue") != null;
        }

        private void DrawTagOrRegistryList(Rect position, SerializedProperty property, GUIContent label, string registryName) {
            EditorGUI.BeginProperty(position, label, property);

            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            Rect row = new Rect(position.x, position.y, position.width, line);

            Rect prefixRect = EditorGUI.PrefixLabel(row, label);
            int newSize = EditorGUI.IntField(prefixRect, property.arraySize);
            if (newSize != property.arraySize)
                property.arraySize = Math.Max(0, newSize);

            for (int i = 0; i < property.arraySize; i++) {
                row.y += line + spacing;

                SerializedProperty element = property.GetArrayElementAtIndex(i);
                SerializedProperty tagValueProp = element.FindPropertyRelative("tagValue");
                SerializedProperty registryValueProp = element.FindPropertyRelative("registryValue");

                if (tagValueProp == null || registryValueProp == null) {
                    EditorGUI.LabelField(row, "Error: TagOrRegistryReference list element mismatch");
                    continue;
                }

                string displayText = GetDisplayText(tagValueProp, registryValueProp);
                if (GUI.Button(row, string.IsNullOrEmpty(displayText) ? "[Select]" : displayText, EditorStyles.popup))
                    BuildAndShowPopup(registryName, tagValueProp, registryValueProp);
            }

            EditorGUI.EndProperty();
        }

        private string GetDisplayText(SerializedProperty tagValueProp, SerializedProperty registryValueProp) {
            TagRegistry.Tags tagValue = (TagRegistry.Tags)tagValueProp.intValue;
            if (tagValue != TagRegistry.Tags.None)
                return tagValue.ToString();

            string registryValue = registryValueProp.stringValue;
            if (string.IsNullOrEmpty(registryValue))
                return "";
            return registryValue;
        }

        private void BuildAndShowPopup(string registryName, SerializedProperty tagValueProp, SerializedProperty registryValueProp) {
            List<string> options = new List<string>();
            Dictionary<string, (bool isTag, object value)> optionData = new Dictionary<string, (bool isTag, object value)>();

            // Add Tags section
            TagRegistry.Tags[] tags = (TagRegistry.Tags[])System.Enum.GetValues(typeof(TagRegistry.Tags));
            foreach (TagRegistry.Tags tag in tags) {
                string tagName = tag.ToString();
                string optionText = $"Tags > {tagName}";
                options.Add(optionText);
                optionData[optionText] = (true, tag);
            }

            // Add Registry section if registry is available
            if (registryName != null && RegistryAssociation.TryGetValue(registryName, out IRegister registry)) {
                for (int i = 0; i < registry.Count(); i++) {
                    string itemName = registry.RetrieveName(i);
                    string optionText = $"Registry > {itemName}";
                    options.Add(optionText);
                    optionData[optionText] = (false, itemName);
                }
            }

            // Determine current selection
            int currentIndex = 0;
            TagRegistry.Tags currentTag = (TagRegistry.Tags)tagValueProp.intValue;
            if (currentTag != TagRegistry.Tags.None) {
                string searchTag = $"Tags > {currentTag}";
                currentIndex = options.FindIndex(o => o == searchTag);
                if (currentIndex < 0) currentIndex = 0;
            } else {
                string currentRegistry = registryValueProp.stringValue;
                string searchRegistry = $"Registry > {currentRegistry}";
                currentIndex = options.FindIndex(o => o == searchRegistry);
                if (currentIndex < 0) currentIndex = 0;
            }

            SearchablePopup popup = new SearchablePopup(
                options,
                currentIndex,
                (i, val) => {
                    if (optionData.TryGetValue(val, out var data)) {
                        if (data.isTag) {
                            tagValueProp.intValue = (int)(TagRegistry.Tags)data.value;
                            registryValueProp.stringValue = "";
                        } else {
                            tagValueProp.intValue = (int)TagRegistry.Tags.None;
                            registryValueProp.stringValue = (string)data.value;
                        }

                        tagValueProp.serializedObject.ApplyModifiedProperties();
                    }
                });

            PopupWindow.Show(new Rect(Event.current.mousePosition.x, Event.current.mousePosition.y, 300, 0), popup);
        }
    }

    public class SearchablePopup : PopupWindowContent {
        private List<string> _options;
        private Action<int, string> _onSelect;
        private string _search = "";
        private Vector2 _scroll;
        private int _selectedIndex;

        public SearchablePopup(List<string> options, int selectedIndex, Action<int, string> onSelect) {
            _options = options;
            _selectedIndex = selectedIndex;
            _onSelect = onSelect;
        }

        public override Vector2 GetWindowSize() {
            return new Vector2(300, 300);
        }

        public override void OnGUI(Rect rect) {
            _search = EditorGUILayout.TextField(_search);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _options.Count; i++) {
                string option = _options[i];
                if (!string.IsNullOrEmpty(_search) && !option.ToLower().Contains(_search.ToLower()))
                    continue;

                GUIStyle style = (i == _selectedIndex) ? EditorStyles.boldLabel : EditorStyles.label;
                if (GUILayout.Button(option, style)) {
                    _onSelect(i, option);
                    editorWindow.Close();
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
#endif
}