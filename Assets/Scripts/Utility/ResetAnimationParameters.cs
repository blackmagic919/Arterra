using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
#endif

public class ResetAnimatorParameters : StateMachineBehaviour {
    [System.Serializable]
    public enum ParameterType { Bool, Int, Float }

    [System.Serializable]
    public struct ParameterToSet {
        public string parameterName;
        public ParameterType type;
        public bool boolValue;
        public int intValue;
        public float floatValue;
    }

    [Tooltip("Parameters to set when entering this state.")]
    public ParameterToSet[] onEnterParameters;

    [Tooltip("Parameters to set when exiting this state.")]
    public ParameterToSet[] onExitParameters;

    private void ApplyParameters(Animator animator, ParameterToSet[] parameters) {
        foreach (var param in parameters) {
            switch (param.type) {
                case ParameterType.Bool:
                    animator.SetBool(param.parameterName, param.boolValue);
                    break;
                case ParameterType.Int:
                    animator.SetInteger(param.parameterName, param.intValue);
                    break;
                case ParameterType.Float:
                    animator.SetFloat(param.parameterName, param.floatValue);
                    break;
            }
        }
    }

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) {
        ApplyParameters(animator, onEnterParameters);
    }

    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) {
        ApplyParameters(animator, onExitParameters);
    }
#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(ResetAnimatorParameters.ParameterToSet))]
    public class ResetParameterDrawer : PropertyDrawer {
        private AnimatorController GetAnimatorController(SerializedProperty property) {
            Object target = property.serializedObject.targetObject;
            string path = AssetDatabase.GetAssetPath(target);
            if (!string.IsNullOrEmpty(path)) {
                return AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            }
            return null;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            EditorGUI.BeginProperty(position, label, property);

            SerializedProperty paramNameProp = property.FindPropertyRelative("parameterName");
            SerializedProperty typeProp = property.FindPropertyRelative("type");
            SerializedProperty boolProp = property.FindPropertyRelative("boolValue");
            SerializedProperty intProp = property.FindPropertyRelative("intValue");
            SerializedProperty floatProp = property.FindPropertyRelative("floatValue");

            // Fetch AnimatorController
            AnimatorController controller = GetAnimatorController(property);

            string[] paramNames = new string[0];
            AnimatorControllerParameterType[] paramTypes = new AnimatorControllerParameterType[0];

            if (controller != null) {
                var parameters = controller.parameters;
                paramNames = new string[parameters.Length];
                paramTypes = new AnimatorControllerParameterType[parameters.Length];
                for (int i = 0; i < parameters.Length; i++) {
                    paramNames[i] = parameters[i].name;
                    paramTypes[i] = parameters[i].type;
                }
            }

            float spacing = 4f;
            float third = (position.width - spacing * 2) / 3f;

            // Parameter dropdown
            int selected = Mathf.Max(0, System.Array.IndexOf(paramNames, paramNameProp.stringValue));
            Rect paramRect = new Rect(position.x, position.y, third, position.height);

            if (paramNames.Length > 0) {
                int newIndex = EditorGUI.Popup(paramRect, selected, paramNames);
                if (newIndex != selected || string.IsNullOrEmpty(paramNameProp.stringValue)) {
                    paramNameProp.stringValue = paramNames[newIndex];

                    // Infer type automatically
                    switch (paramTypes[newIndex]) {
                        case AnimatorControllerParameterType.Bool:
                            typeProp.enumValueIndex = (int)ResetAnimatorParameters.ParameterType.Bool;
                            break;
                        case AnimatorControllerParameterType.Int:
                            typeProp.enumValueIndex = (int)ResetAnimatorParameters.ParameterType.Int;
                            break;
                        case AnimatorControllerParameterType.Float:
                            typeProp.enumValueIndex = (int)ResetAnimatorParameters.ParameterType.Float;
                            break;
                        case AnimatorControllerParameterType.Trigger:
                            typeProp.enumValueIndex = (int)ResetAnimatorParameters.ParameterType.Bool; // Treat trigger like bool
                            break;
                    }
                }
            } else {
                paramNameProp.stringValue = EditorGUI.TextField(paramRect, paramNameProp.stringValue);
            }

            // Value field (depends on inferred type)
            Rect valueRect = new Rect(paramRect.xMax + spacing, position.y, position.width - paramRect.width - spacing, position.height);
            var paramType = (ResetAnimatorParameters.ParameterType)typeProp.enumValueIndex;

            switch (paramType) {
                case ResetAnimatorParameters.ParameterType.Bool:
                    EditorGUI.PropertyField(valueRect, boolProp, GUIContent.none);
                    break;
                case ResetAnimatorParameters.ParameterType.Int:
                    EditorGUI.PropertyField(valueRect, intProp, GUIContent.none);
                    break;
                case ResetAnimatorParameters.ParameterType.Float:
                    EditorGUI.PropertyField(valueRect, floatProp, GUIContent.none);
                    break;
            }

            EditorGUI.EndProperty();
        }
    }
#endif
}