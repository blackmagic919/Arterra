using System;
using System.Collections.Generic;
using Arterra.Configuration;
using UnityEngine;
using Unity.Mathematics;
using Arterra.Utils;

namespace Arterra.Data.Models {
    [CreateAssetMenu(menuName = "Generation/Custom Model")]
    public class CustomModel : Category<CustomModel> {
        [Tooltip("Appearance variants, selected by byte 1 of ModelRenderInfo. Slots 0 and 1 of the catalogue remain reserved for regular and flipped terrain.")]
        public List<ModelData> Models = new();
        public ModelRenderInfo DefaultRenderInfo;

        [Serializable]
        public struct ModelData {
            [Tooltip("Readable Mesh in Resources, without the extension.")]
            public string MeshPath;
        }
    }

    [Serializable]
    public struct ModelRenderInfo {
        public uint Data;

        // Source meshes face +Z. The low byte stores direction + 6 * clockwise local roll.
        public const int OrientationCount = 24;
        [Newtonsoft.Json.JsonIgnore]
        public Facing Direction {
            get => (Facing)(Data & 255);
            set => Data = (Data & 0xFFFFFF00u) | ((uint)value & 255);
        }

        // Byte 1 selects a variant within the material's model catalogue entry.
        [Newtonsoft.Json.JsonIgnore]
        public byte Variant {
            get => (byte)((Data >> 8) & 255);
            set => Data = (Data & 0xFFFF00FFu) | ((uint)value << 8);
        }

        public void Validate(int modelsCount) {
            if ((Data & 255) >= OrientationCount)
                throw new InvalidOperationException($"Invalid custom model facing direction: {Data & 255}.");
            if (modelsCount < 1 || modelsCount > 256)
                throw new InvalidOperationException($"Custom model collections require 1–256 variants; found {modelsCount}.");
            if (Variant >= modelsCount)
                throw new InvalidOperationException($"Custom model variant {Variant} is outside the collection of {modelsCount} models.");
        }

        public ModelRenderInfo Rotate(int3 rotation) {
            // Invalid metadata is rejected by the compute shader.
            if ((Data & 255) >= OrientationCount) return this;

            int3x3 placement = CustomUtility.RotationLookupTable[rotation.y, rotation.x, rotation.z];
            int3x3 target = math.mul(placement, GetRotation(Direction));
            ModelRenderInfo result = this;
            for (int i = 0; i < OrientationCount; i++) {
                if (!GetRotation((Facing)i).Equals(target)) continue;
                result.Direction = (Facing)i;
                return result;
            }
            throw new InvalidOperationException("Placement rotation is not a rotational symmetry of a cube.");
        }

        public static int3x3 GetRotation(Facing facing) {
            int orientation = (int)facing;
            int3x3 direction = (orientation % 6) switch {
                1 => CustomUtility.RotationLookupTable[3, 0, 0],
                2 => CustomUtility.RotationLookupTable[2, 0, 0],
                3 => CustomUtility.RotationLookupTable[1, 0, 0],
                4 => CustomUtility.RotationLookupTable[0, 1, 0],
                5 => CustomUtility.RotationLookupTable[0, 3, 0],
                _ => CustomUtility.RotationLookupTable[0, 0, 0]
            };
            return math.mul(direction, CustomUtility.RotationLookupTable[0, 0, orientation / 6]);
        }

        public enum Facing {
            Forward, Right, Back, Left, Up, Down,
            ForwardRoll90, RightRoll90, BackRoll90, LeftRoll90, UpRoll90, DownRoll90,
            ForwardRoll180, RightRoll180, BackRoll180, LeftRoll180, UpRoll180, DownRoll180,
            ForwardRoll270, RightRoll270, BackRoll270, LeftRoll270, UpRoll270, DownRoll270
        }
    }
}

#if UNITY_EDITOR
namespace Arterra.Data.Models {
    [UnityEditor.CustomPropertyDrawer(typeof(ModelRenderInfo))]
    public class ModelRenderInfoDrawer : UnityEditor.PropertyDrawer {
        public override float GetPropertyHeight(UnityEditor.SerializedProperty property, GUIContent label) {
            return UnityEditor.EditorGUIUtility.singleLineHeight * 2 + UnityEditor.EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, UnityEditor.SerializedProperty property, GUIContent label) {
            UnityEditor.EditorGUI.BeginProperty(position, label, property);
            var data = property.FindPropertyRelative(nameof(ModelRenderInfo.Data));
            float lineHeight = UnityEditor.EditorGUIUtility.singleLineHeight;
            Rect row = new(position.x, position.y, position.width, lineHeight);
            row = UnityEditor.EditorGUI.PrefixLabel(row, label);

            int indent = UnityEditor.EditorGUI.indentLevel;
            float labelWidth = UnityEditor.EditorGUIUtility.labelWidth;
            bool mixed = UnityEditor.EditorGUI.showMixedValue;
            UnityEditor.EditorGUI.indentLevel = 0;
            UnityEditor.EditorGUIUtility.labelWidth = 60;
            UnityEditor.EditorGUI.showMixedValue = data.hasMultipleDifferentValues;

            UnityEditor.EditorGUI.BeginChangeCheck();
            var direction = (ModelRenderInfo.Facing)UnityEditor.EditorGUI.EnumPopup(row, "Direction",
                (ModelRenderInfo.Facing)(data.uintValue & 255));
            if (UnityEditor.EditorGUI.EndChangeCheck()) SetBits(data, (uint)direction, 255);

            row.y += lineHeight + UnityEditor.EditorGUIUtility.standardVerticalSpacing;
            UnityEditor.EditorGUI.BeginChangeCheck();
            int variant = UnityEditor.EditorGUI.IntSlider(row, "Variant", (int)((data.uintValue >> 8) & 255), 0, 255);
            if (UnityEditor.EditorGUI.EndChangeCheck()) SetBits(data, (uint)variant << 8, 0xFF00);

            UnityEditor.EditorGUI.indentLevel = indent;
            UnityEditor.EditorGUIUtility.labelWidth = labelWidth;
            UnityEditor.EditorGUI.showMixedValue = mixed;
            UnityEditor.EditorGUI.EndProperty();
        }

        private static void SetBits(UnityEditor.SerializedProperty data, uint value, uint mask) {
            if (!data.serializedObject.isEditingMultipleObjects) {
                data.uintValue = (data.uintValue & ~mask) | (value & mask);
                return;
            }

            // Preserve each selected object's other byte and reserved bits.
            foreach (var target in data.serializedObject.targetObjects) {
                using var serialized = new UnityEditor.SerializedObject(target);
                var targetData = serialized.FindProperty(data.propertyPath);
                targetData.uintValue = (targetData.uintValue & ~mask) | (value & mask);
                serialized.ApplyModifiedProperties();
            }
            data.serializedObject.Update();
        }
    }
}
#endif
