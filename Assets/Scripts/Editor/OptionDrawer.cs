using Arterra.Configuration;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arterra.Editor {
    [CustomPropertyDrawer(typeof(Option<>))]
    [CustomPropertyDrawer(typeof(ReferenceOption<>))]
    [CustomPropertyDrawer(typeof(DirtyOption<>))]
    [CustomPropertyDrawer(typeof(DirtyReferenceOption<>))]
    public class OptionDrawer : PropertyDrawer {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            return EditorGUI.GetPropertyHeight(property.FindPropertyRelative("value"), label, true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            using (new EditorGUI.PropertyScope(position, label, property)) {
                EditorGUI.PropertyField(position, property.FindPropertyRelative("value"), label, true);
            }
        }

        public override VisualElement CreatePropertyGUI(SerializedProperty property) {
            return new PropertyField(property.FindPropertyRelative("value"), preferredLabel ?? property.displayName) {
                tooltip = property.tooltip
            };
        }
    }
}
