using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using WorldConfig;
using Unity.Mathematics;
using System.Linq;


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
    private static Dictionary<string, IRegister> RegistryAssociation;
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
                currentIndex,
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
        } if (prop == null || !prop.isArray) return false;
        return true;
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