using UnityEditor;
using UnityEngine;

[System.Serializable]
public struct ToggleField<TValue> {
    public bool enabled;
    public TValue value;
}

// A generic property drawer for ToggleField<TValue>.
// Shows a checkbox next to the field name. When the checkbox is checked
// an expandable arrow appears and the value property is drawn underneath.

[CustomPropertyDrawer(typeof(ToggleField<>))]
public class ToggleFieldDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        SerializedProperty enabledProp = property.FindPropertyRelative("enabled");
        SerializedProperty valueProp = property.FindPropertyRelative("value");

        EditorGUI.BeginProperty(position, label, property);
        
        position.height = EditorGUIUtility.singleLineHeight;

        // Draw the enabled checkbox using PropertyField (handles events/hover correctly)
        Rect checkboxRect = new Rect(position.x, position.y, position.width - 16, position.height);
        EditorGUI.PropertyField(checkboxRect, enabledProp, label);

        // if enabled, show an arrow that controls expansion of the inner value
        if (enabledProp.boolValue)
        {
            Rect arrowRect = new Rect(position.xMax - 16, position.y, 16, position.height);
            property.isExpanded = EditorGUI.Foldout(arrowRect, property.isExpanded, GUIContent.none);
        }

        // draw the inner value if expanded
        if (enabledProp.boolValue && property.isExpanded)
        {
            float yOffset = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            Rect valueRect = new Rect(position.x, position.y + yOffset, position.width, 
                                      EditorGUI.GetPropertyHeight(valueProp, true));
            EditorGUI.indentLevel++;
            EditorGUI.PropertyField(valueRect, valueProp, GUIContent.none, true);
            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        SerializedProperty enabledProp = property.FindPropertyRelative("enabled");
        SerializedProperty valueProp = property.FindPropertyRelative("value");

        float height = EditorGUIUtility.singleLineHeight;
        if (enabledProp.boolValue && property.isExpanded)
        {
            height += EditorGUIUtility.standardVerticalSpacing +
                      EditorGUI.GetPropertyHeight(valueProp, true);
        }

        return height;
    }
}