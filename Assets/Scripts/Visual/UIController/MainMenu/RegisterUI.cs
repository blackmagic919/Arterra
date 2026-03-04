using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Arterra.Configuration;
using Unity.Mathematics;
using System.Linq;

namespace Arterra.Editor {

    /// <summary>
    /// Determines whether a TagOrRegistryReference stores a Tag or a Registry reference.
    /// </summary>
    public enum ReferenceMode {
        Tag = 0,
        RegistryReference = 1
    }

    /// <summary>
    /// A unified field that can hold either a TagRegistry.Tags value or a registry reference.
    /// The mode determines which type of value is being stored.
    /// </summary>
    [Serializable]
    public struct TagOrRegistryReference {
        /// <summary>Determines whether this holds a Tag or a RegistryReference.</summary>
        public ReferenceMode mode;
        /// <summary>The tag value when mode == Tag.</summary>
        public TagRegistry.Tags tagValue;
        /// <summary>The registry name when mode == RegistryReference.</summary>
        public string registryValue;

        public TagOrRegistryReference(TagRegistry.Tags tag) {
            mode = ReferenceMode.Tag;
            tagValue = tag;
            registryValue = "";
        }

        public TagOrRegistryReference(string registryName) {
            mode = ReferenceMode.RegistryReference;
            tagValue = TagRegistry.Tags.None;
            registryValue = registryName;
        }

        public bool IsTag => mode == ReferenceMode.Tag;
        public bool IsRegistryReference => mode == ReferenceMode.RegistryReference;

        public bool Is(IRegistered registered, ICatalgoue catalogue) {
            if (IsTag) return catalogue.GetMostSpecificTag(tagValue, registered.Index, out _);
            return registered.Index == catalogue.RetrieveIndex(registryValue);
        }
    }

    /// <summary>
    /// Attribute for TagOrRegistryReference fields. Specifies which registry to use when the reference is in RegistryReference mode.
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

            DrawRegistryDropdown(position, property, label, registry);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            return base.GetPropertyHeight(property, label);
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

            List<string> options = new List<string>() { "" };
            for (int i = 0; i < registry.Count(); i++) {
                options.Add(registry.RetrieveName(i));
            }

            if (GUI.Button(labelRect, string.IsNullOrEmpty(currentValue) ? "[Select]" : currentValue, EditorStyles.popup)) {
                SearchablePopup popup = new SearchablePopup(
                    options,
                    currentIndex + 1,
                    (i, val) => {
                        SetReferenceName(property, val);
                        property.serializedObject.ApplyModifiedProperties();
                    });

                PopupWindow.Show(labelRect, popup);
            }

            EditorGUI.EndProperty();
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
            string nameLookupPath = ((TagOrRegistryReferenceAttribute)attribute).NamePropertyPath;

            EditorGUI.BeginProperty(position, label, property);
            Rect labelRect = EditorGUI.PrefixLabel(position, label);

            // Get the mode, tagValue, and registryValue properties
            SerializedProperty modeProp = property.FindPropertyRelative("mode");
            SerializedProperty tagValueProp = property.FindPropertyRelative("tagValue");
            SerializedProperty registryValueProp = property.FindPropertyRelative("registryValue");

            if (modeProp == null || tagValueProp == null || registryValueProp == null) {
                EditorGUI.LabelField(labelRect, "Error: TagOrRegistryReference structure mismatch");
                EditorGUI.EndProperty();
                return;
            }

            ReferenceMode currentMode = (ReferenceMode)modeProp.intValue;
            string displayText = GetDisplayText(currentMode, tagValueProp, registryValueProp, registryName, nameLookupPath);

            if (GUI.Button(labelRect, string.IsNullOrEmpty(displayText) ? "[Select]" : displayText, EditorStyles.popup)) {
                BuildAndShowPopup(registryName, nameLookupPath, modeProp, tagValueProp, registryValueProp);
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            return base.GetPropertyHeight(property, label);
        }

        private string GetDisplayText(ReferenceMode mode, SerializedProperty tagValueProp, SerializedProperty registryValueProp, string registryName, string nameLookupPath) {
            if (mode == ReferenceMode.Tag) {
                TagRegistry.Tags tagValue = (TagRegistry.Tags)tagValueProp.intValue;
                return tagValue == TagRegistry.Tags.None ? "" : tagValue.ToString();
            } else {
                string registryValue = registryValueProp.stringValue;
                if (string.IsNullOrEmpty(registryValue))
                    return "";
                return registryValue;
            }
        }

        private void BuildAndShowPopup(string registryName, string nameLookupPath, SerializedProperty modeProp, SerializedProperty tagValueProp, SerializedProperty registryValueProp) {
            List<string> options = new List<string>();
            Dictionary<string, (ReferenceMode, object)> optionData = new Dictionary<string, (ReferenceMode, object)>();

            // Add Tags section
            TagRegistry.Tags[] tags = (TagRegistry.Tags[])System.Enum.GetValues(typeof(TagRegistry.Tags));
            foreach (TagRegistry.Tags tag in tags) {
                string tagName = tag.ToString();
                string optionText = $"Tags > {tagName}";
                options.Add(optionText);
                optionData[optionText] = (ReferenceMode.Tag, tag);
            }

            // Add Registry section if registry is available
            if (registryName != null && RegistryAssociation.TryGetValue(registryName, out IRegister registry)) {
                for (int i = 0; i < registry.Count(); i++) {
                    string itemName = registry.RetrieveName(i);
                    string optionText = $"Registry > {itemName}";
                    options.Add(optionText);
                    optionData[optionText] = (ReferenceMode.RegistryReference, itemName);
                }
            }

            // Determine current selection
            ReferenceMode currentMode = (ReferenceMode)modeProp.intValue;
            int currentIndex = 0;
            if (currentMode == ReferenceMode.Tag) {
                TagRegistry.Tags currentTag = (TagRegistry.Tags)tagValueProp.intValue;
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
                        modeProp.intValue = (int)data.Item1;
                        
                        if (data.Item1 == ReferenceMode.Tag) {
                            tagValueProp.intValue = (int)(TagRegistry.Tags)data.Item2;
                            registryValueProp.stringValue = "";
                        } else {
                            tagValueProp.intValue = (int)TagRegistry.Tags.None;
                            registryValueProp.stringValue = (string)data.Item2;
                        }
                        
                        modeProp.serializedObject.ApplyModifiedProperties();
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