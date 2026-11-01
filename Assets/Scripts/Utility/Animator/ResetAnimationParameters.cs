using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
#endif

namespace Arterra.GamePlay.UI {
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
            public bool randomize;
            public int intMin;
            public int intMaxExclusive;
            public float floatMin;
            public float floatMax;

        }

        [Tooltip("Parameters to set when entering this state.")]
        public ParameterToSet[] onEnterParameters;

        [Tooltip("Parameters to set when exiting this state.")]
        public ParameterToSet[] onExitParameters;

        private void ApplyParameters(Animator animator, ParameterToSet[] parameters) {
            if (parameters == null) return;
            foreach (var param in parameters) {
                switch (param.type) {
                    case ParameterType.Bool:
                        animator.SetBool(param.parameterName, param.randomize ? Random.value < 0.5f : param.boolValue);
                        break;
                    case ParameterType.Int:
                        animator.SetInteger(param.parameterName, param.randomize ? Random.Range(param.intMin, param.intMaxExclusive) : param.intValue);
                        break;
                    case ParameterType.Float:
                        animator.SetFloat(param.parameterName, param.randomize ? Random.Range(param.floatMin, param.floatMax) : param.floatValue);
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

            public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
                bool randomBool = property.FindPropertyRelative("randomize").boolValue
                    && (ParameterType)property.FindPropertyRelative("type").enumValueIndex == ParameterType.Bool;
                return EditorGUIUtility.singleLineHeight + (randomBool
                    ? EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing : 0);
            }

            public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
                EditorGUI.BeginProperty(position, label, property);

                SerializedProperty paramNameProp = property.FindPropertyRelative("parameterName");
                SerializedProperty typeProp = property.FindPropertyRelative("type");
                SerializedProperty boolProp = property.FindPropertyRelative("boolValue");
                SerializedProperty intProp = property.FindPropertyRelative("intValue");
                SerializedProperty floatProp = property.FindPropertyRelative("floatValue");
                SerializedProperty randomProp = property.FindPropertyRelative("randomize");

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
                float availableWidth = position.width - spacing * 2;
                float parameterWidth = availableWidth * 0.35f;
                float modeWidth = availableWidth * 0.23f;

                // Parameter dropdown
                int selected = Mathf.Max(0, System.Array.IndexOf(paramNames, paramNameProp.stringValue));
                Rect paramRect = new Rect(position.x, position.y, parameterWidth, EditorGUIUtility.singleLineHeight);

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

                Rect modeRect = new Rect(paramRect.xMax + spacing, position.y, modeWidth, EditorGUIUtility.singleLineHeight);
                randomProp.boolValue = EditorGUI.Popup(modeRect, randomProp.boolValue ? 1 : 0,
                    new[] { "Fixed", "Random" }) == 1;

                Rect valueRect = new Rect(modeRect.xMax + spacing, position.y,
                    position.xMax - modeRect.xMax - spacing, EditorGUIUtility.singleLineHeight);
                var paramType = (ParameterType)typeProp.enumValueIndex;
                if (!randomProp.boolValue) {
                    SerializedProperty value = paramType == ParameterType.Bool ? boolProp
                        : paramType == ParameterType.Int ? intProp : floatProp;
                    EditorGUI.PropertyField(valueRect, value, GUIContent.none);
                } else if (paramType == ParameterType.Bool) {
                    Rect messageRect = new Rect(position.x,
                        position.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing,
                        position.width, EditorGUIUtility.singleLineHeight);
                    EditorGUI.LabelField(messageRect, "Equal chance of true or false");
                } else {
                    bool integer = paramType == ParameterType.Int;
                    Rect minRect = new Rect(valueRect.x, valueRect.y, (valueRect.width - spacing) / 2, valueRect.height);
                    Rect maxRect = new Rect(minRect.xMax + spacing, valueRect.y, minRect.width, valueRect.height);
                    float oldLabelWidth = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = 25;
                    EditorGUI.PropertyField(minRect, property.FindPropertyRelative(integer ? "intMin" : "floatMin"), new GUIContent("Min", "Minimum (inclusive)"));
                    EditorGUI.PropertyField(maxRect, property.FindPropertyRelative(integer ? "intMaxExclusive" : "floatMax"),
                        new GUIContent("Max", integer ? "Maximum (exclusive)" : "Maximum (inclusive)"));
                    EditorGUIUtility.labelWidth = oldLabelWidth;
                }

                EditorGUI.EndProperty();
            }
        }
#endif
    }
}
