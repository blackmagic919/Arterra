using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static PaginatedUIEditor;
using static SegmentedUIEditor;
[System.Serializable]
public struct Optional<T>
{
    [UISetting(Ignore = true)]
    public bool Enabled;
    public T Value;

    public Optional(T value)
    {
        Enabled = true;
        Value = value;
    }
}


[CustomPropertyDrawer(typeof(Optional<>))]
public class OptionalPropertyDrawer : PropertyDrawer {
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
        EditorGUI.BeginProperty(position, label, property);

        // Get the 'Enabled' and 'Value' properties
        SerializedProperty enabledProperty = property.FindPropertyRelative("Enabled");
        SerializedProperty valueProperty = property.FindPropertyRelative("Value");

        // Calculate rects for drawing
        Rect toggleRect = new Rect(position.x, position.y, 15f, position.height);
        Rect labelRect = new Rect(position.x + 15f, position.y, EditorGUIUtility.labelWidth - 15f, position.height);
        Rect valueRect = new Rect(position.x + EditorGUIUtility.labelWidth, position.y, position.width - EditorGUIUtility.labelWidth, position.height);

        // Draw the toggle and label
        enabledProperty.boolValue = EditorGUI.Toggle(toggleRect, enabledProperty.boolValue);
        EditorGUI.LabelField(labelRect, label);

        // Draw the value field, potentially disabled
        using (new EditorGUI.DisabledScope(!enabledProperty.boolValue)) {
            EditorGUI.PropertyField(valueRect, valueProperty, GUIContent.none);
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
        // Ensure proper height for nested types if necessary
        return EditorGUI.GetPropertyHeight(property.FindPropertyRelative("Value"));
    }
}


public class PageOptionalSerializer : PaginatedUIEditor.IConverter {
    public bool CanConvert(Type type) {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Optional<>);
    }

    public void Serialize(GameObject page, GameObject parent, FieldInfo field, object value, ParentUpdate OnUpdate) {
        //Don't capture because multiple listeners can reaquire latest state 
        FieldInfo enabledField = value.GetType().GetField("Enabled");
        bool enabled = (bool)enabledField.GetValue(value);
        Toggle toggleField = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/PaginatedUI/Bool_Input"), parent.transform).GetComponent<Toggle>();
        toggleField.isOn = enabled;
        toggleField.onValueChanged.AddListener((bool nState) => OnUpdate((ref object parent) => {
            value = field.GetValue(parent);
            if (nState == enabled) return;
            enabled = nState;

            if (enabled) SerializeOptionalPage();
            else ReleaseOptionalPage();
        })); if (enabled) SerializeOptionalPage();


        void SerializeOptionalPage() {
            Button PaginateButton = parent.AddComponent<Button>();
            PaginateButton.onClick.AddListener(() => {
                GameObject newPage = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/PaginatedUI/Page"), page.transform.parent);
                Button ReturnButton = GetPageReturn(newPage).GetComponent<Button>();
                page.SetActive(false);

                ReturnButton.onClick.AddListener(() => {
                    GameObject.Destroy(newPage);
                    page.SetActive(true);
                });

                CreatePageDisplay(value, newPage, OnUpdate);
            });
        }

        void ReleaseOptionalPage() {
            if (parent.TryGetComponent(typeof(Button), out Component PaginateButton)) {
                GameObject.Destroy(PaginateButton);
            }
        }
    }
}


public class SegmentOptionalSerializer : SegmentedUIEditor.IConverter
{
    public bool CanConvert(Type iType, Type fType) { return iType.IsGenericType && iType.GetGenericTypeDefinition() == typeof(Optional<>); }

    public void Serialize(GameObject parent, FieldInfo field, object value, ParentUpdate OnUpdate) {
        //Don't capture because multiple listeners can reaquire latest state 
        FieldInfo enabledField = value.GetType().GetField("Enabled");
        bool enabled = (bool)enabledField.GetValue(value);
        Toggle toggleField = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/SegmentedUI/Bool_Input"), parent.transform.GetChild(0)).GetComponent<Toggle>();
        toggleField.isOn = enabled;
        toggleField.onValueChanged.AddListener((bool nState) => OnUpdate((ref object pObject) => {
            value = field.GetValue(pObject);
            if (nState == enabled) return;
            enabled = nState;

            if (enabled) CreateOptionDisplay(value, parent, OnUpdate);
            else ReleaseDisplay(parent);
            SegmentedUIEditor.ForceLayoutRefresh(parent.transform);
        }));

        if (enabled) CreateOptionDisplay(value, parent, OnUpdate);
    }
    
    public void Deserialize(ref object destination, ref object source)
    { //Default Behavior
        SupplementTree(ref destination, ref source); 
    }
}
