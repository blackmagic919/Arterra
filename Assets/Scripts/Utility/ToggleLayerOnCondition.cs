using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
#endif

public class ToggleLayerOnCondition : StateMachineBehaviour {
    public enum ConditionType { Bool, Int, Float }
    public enum Comparison { Equals, NotEquals, Greater, Less, GreaterOrEqual, LessOrEqual }

    [System.Serializable]
    public struct Condition {
        public string parameterName;
        public ConditionType type;
        public Comparison comparison;

        public bool boolValue;
        public int intValue;
        public float floatValue;

        public bool Evaluate(Animator animator) {
            switch (type) {
                case ConditionType.Bool:
                    bool b = animator.GetBool(parameterName);
                    return comparison switch {
                        Comparison.Equals => b == boolValue,
                        Comparison.NotEquals => b != boolValue,
                        _ => false,
                    };

                case ConditionType.Int:
                    int i = animator.GetInteger(parameterName);
                    return comparison switch {
                        Comparison.Equals => i == intValue,
                        Comparison.NotEquals => i != intValue,
                        Comparison.Greater => i > intValue,
                        Comparison.Less => i < intValue,
                        Comparison.GreaterOrEqual => i >= intValue,
                        Comparison.LessOrEqual => i <= intValue,
                        _ => false,
                    };

                case ConditionType.Float:
                    float f = animator.GetFloat(parameterName);
                    return comparison switch {
                        Comparison.Equals => Mathf.Approximately(f, floatValue),
                        Comparison.NotEquals => !Mathf.Approximately(f, floatValue),
                        Comparison.Greater => f > floatValue,
                        Comparison.Less => f < floatValue,
                        Comparison.GreaterOrEqual => f >= floatValue,
                        Comparison.LessOrEqual => f <= floatValue,
                        _ => false,
                    };
            }
            return false;
        }
    }

    [Tooltip("All conditions must be true for the layer to toggle on.")]
    public Condition[] conditions;

    [Tooltip("If true, smooth fade instead of instant toggle.")]
    public bool smoothFade = true;

    [Tooltip("Speed for fading layer weight if smoothFade is enabled.")]
    public float fadeSpeed = 5f;

    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) {
        // Check all conditions (AND logic)
        bool allTrue = true;
        foreach (var condition in conditions) {
            if (!condition.Evaluate(animator)) {
                allTrue = false;
                break;
            }
        }

        float targetWeight = allTrue ? 1f : 0f;
        float current = animator.GetLayerWeight(layerIndex);

        if (smoothFade) {
            float newWeight = Mathf.MoveTowards(current, targetWeight, Time.deltaTime * fadeSpeed);
            animator.SetLayerWeight(layerIndex, newWeight);
        } else {
            animator.SetLayerWeight(layerIndex, targetWeight);
        }
    }
}
#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(ToggleLayerOnCondition.Condition))]
public class ConditionDrawer : PropertyDrawer {
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
        SerializedProperty comparisonProp = property.FindPropertyRelative("comparison");
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
                // Infer type
                switch (paramTypes[newIndex]) {
                    case AnimatorControllerParameterType.Bool:
                        typeProp.enumValueIndex = (int)ToggleLayerOnCondition.ConditionType.Bool;
                        break;
                    case AnimatorControllerParameterType.Int:
                        typeProp.enumValueIndex = (int)ToggleLayerOnCondition.ConditionType.Int;
                        break;
                    case AnimatorControllerParameterType.Float:
                        typeProp.enumValueIndex = (int)ToggleLayerOnCondition.ConditionType.Float;
                        break;
                    case AnimatorControllerParameterType.Trigger:
                        typeProp.enumValueIndex = (int)ToggleLayerOnCondition.ConditionType.Bool;
                        break;
                }
            }
        } else {
            paramNameProp.stringValue = EditorGUI.TextField(paramRect, paramNameProp.stringValue);
        }

        // Comparison dropdown
        Rect compRect = new Rect(paramRect.xMax + spacing, position.y, third, position.height);
        EditorGUI.PropertyField(compRect, comparisonProp, GUIContent.none);

        // Value field
        Rect valueRect = new Rect(compRect.xMax + spacing, position.y, third, position.height);
        var condType = (ToggleLayerOnCondition.ConditionType)typeProp.enumValueIndex;

        switch (condType) {
            case ToggleLayerOnCondition.ConditionType.Bool:
                EditorGUI.PropertyField(valueRect, boolProp, GUIContent.none);
                break;
            case ToggleLayerOnCondition.ConditionType.Int:
                EditorGUI.PropertyField(valueRect, intProp, GUIContent.none);
                break;
            case ToggleLayerOnCondition.ConditionType.Float:
                EditorGUI.PropertyField(valueRect, floatProp, GUIContent.none);
                break;
        }

        EditorGUI.EndProperty();
    }
}
#endif