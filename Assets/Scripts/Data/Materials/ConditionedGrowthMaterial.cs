using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using Unity.Burst;
using System;
using WorldConfig.Generation.Structure;
using MapStorage;
using System.Collections.Generic;
using WorldConfig.Generation.Material;
/*
y
^      0  5        z
|      | /        /\
|      |/         /
| 4 -- c -- 2    /
|     /|        /
|    / |       /
|   3  1      /
+----------->x
*/

namespace WorldConfig.Generation.Material{

    /// <summary> A concrete material that will attempt to spread itself to neighboring entries 
    /// when and only when  randomly updated. </summary>
    [BurstCompile]
    [CreateAssetMenu(menuName = "Generation/MaterialData/ConditionedGrowthMat")]
    public class ConditionedGrowthMat : MaterialData {
        public Option<List<MapSamplePoint>> ConversionData;
        public Option<List<MapSampleRegion>> ConvertRegions;
        [Header("Self Condition")]
        public StructureData.CheckInfo SelfCondition;

        private static Catalogue<MaterialData> matInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;

        public override void RandomMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng) {
            MapData cur = CPUMapManager.SampleMap(GCoord); //Current 
            if (!SelfCondition.Contains(cur)) return;
            foreach (MapSampleRegion region in ConvertRegions.value) {
                if (prng.NextFloat() > region.convertChance) continue;

                int i = (int)region.checkStart;
                for (; i <= region.checkEnd; i++) {
                    MapSamplePoint point = ConversionData.value[i];
                    if (!SatisfiesCheck(point, GCoord)) break;
                }
                if (i <= region.checkEnd) continue;
                for (i = (int)region.convertStart; i <= region.convertEnd; i++) {
                    MapSamplePoint point = ConversionData.value[i];
                    PlaceEntry(point, GCoord, ref prng);
                }
            }
        }

        private void PlaceEntry(MapSamplePoint pt, int3 BaseCoord, ref Unity.Mathematics.Random prng) {
            int3 GCoord = BaseCoord + pt.Offset;
            MapData cur = CPUMapManager.SampleMap(GCoord);
            if (cur.IsNull) return;

            MapData newData = new MapData { data = 0 };
            if (pt.HasMaterialCheck) {
                newData.material = matInfo.RetrieveIndex(RetrieveKey((int)pt.material));
            } else newData.material = cur.material;
            newData.viscosity = Mathf.RoundToInt(pt.CheckBounds.MinSolid +
                prng.NextFloat() * (pt.CheckBounds.MaxSolid - pt.CheckBounds.MinSolid));

            int liquidDensity = Mathf.RoundToInt(pt.CheckBounds.MinLiquid +
                prng.NextFloat() * (pt.CheckBounds.MaxLiquid - pt.CheckBounds.MinLiquid));
            newData.density = Mathf.Clamp(newData.viscosity + liquidDensity, 0, MapData.MaxDensity);


            if (matInfo.Retrieve(cur.material).OnRemoving(GCoord, null))
                return;

            if (matInfo.Retrieve(newData.material).OnPlacing(GCoord, null))
                return;

            matInfo.Retrieve(cur.material).OnRemoved(GCoord, cur);
            matInfo.Retrieve(newData.material).OnPlaced(GCoord, newData);
            CPUMapManager.SetMap(newData, GCoord);
        }
        
        public bool SatisfiesCheck(MapSamplePoint pt, int3 BaseCoord) {
            int3 GCoord = BaseCoord + pt.Offset;
            MapData mapData = CPUMapManager.SampleMap(GCoord);
            if (!pt.CheckBounds.Contains(mapData)) return false;
            if (!pt.HasMaterialCheck) return true;
            int material = matInfo.RetrieveIndex(RetrieveKey((int)pt.material));
            return mapData.material == material;
        }

        public override void PropogateMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng = default) {
            //nothing to do here
        }

        /// <summary> The handler controlling how materials are dropped when
        /// <see cref="OnRemoved"/> is called. See <see cref="MaterialData.MultiLooter"/> 
        /// for more info.  </summary>
        public MultiLooter MaterialDrops;

        /// <summary> See <see cref="MaterialData.OnRemoved"/> for more information. </summary>
        /// <param name="amount">The map data indicating the amount of material removed
        /// and the state it was removed as</param>
        /// <param name="GCoord">The location of the map information being</param>
        /// <returns>The item to give.</returns>
        public override Item.IItem OnRemoved(int3 GCoord, in MapData amount) {
            return MaterialDrops.LootItem(amount, Names);
        }

        [Serializable]
        public struct MapSamplePoint {
            public StructureData.CheckInfo CheckBounds;
            public uint _material;
            public uint _offset;

            public int3 Offset {
                readonly get => new int3((int)(_offset >> 20) & 0x3FF, (int)(_offset >> 10) & 0x3FF, (int)(_offset & 0x3FF)) - new int3(512, 512, 512);
                set {
                    value += new int3(512, 512, 512);
                    _offset = (uint)((value.x & 0x3FF) << 20 | (value.y & 0x3FF) << 10 | (value.z & 0x3FF));
                }
            }

            public uint material {
                readonly get => _material & 0x7FFFFFFF;
                set => _material = value & 0x7FFFFFFF;
            }

            public bool HasMaterialCheck {
                readonly get => (_material & 0x80000000) != 0;
                set {
                    if (value) _material |= 0x80000000;
                    else _material &= 0x7FFFFFFF;
                }
            }
        }

        [Serializable]
        public struct MapSampleRegion {
            public uint checkStart;
            public uint checkEnd;
            public uint convertStart;
            public uint convertEnd;
            public float convertChance;
        }
    }
}


#if UNITY_EDITOR
/// <summary> A utility class to override serialization of <see cref="StructureData.CheckInfo"/> into a Unity Inspector format.
/// It exposes the internal components of the bitmap so it can be more easily understood by the developer. </summary>
[CustomPropertyDrawer(typeof(ConditionedGrowthMat.MapSamplePoint))]
public class StructCheckDrawer : PropertyDrawer{
    /// <summary>  Callback for when the GUI needs to be rendered for the property. </summary>
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
        // Draw check bounds normally
        SerializedProperty checkProp = property.FindPropertyRelative("CheckBounds");
        Rect rect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        EditorGUI.PropertyField(rect, checkProp);
        rect.y += EditorGUIUtility.singleLineHeight * 2;

        SerializedProperty materialProp = property.FindPropertyRelative("_material");
        SerializedProperty offsetProp = property.FindPropertyRelative("_offset");

        uint matInfo = materialProp.uintValue;
        int material = (int)(matInfo & 0x7FFFFFFF);
        bool hasCheck = (matInfo & 0x80000000) != 0;
        int[] offset = new int[3]{
            (int)((offsetProp.intValue >> 20) & 0x3FF) - 512,
            (int)((offsetProp.intValue >> 10) & 0x3FF) - 512,
            (int)(offsetProp.intValue & 0x3FF) - 512
        };

        float labelWidth = EditorGUIUtility.labelWidth;
        float fieldWidth = (rect.width - labelWidth) / 2f;

        Rect hasMatRect = new(rect.x + labelWidth, rect.y, fieldWidth, rect.height);
        Rect materialRect = new(hasMatRect.x + fieldWidth, rect.y, fieldWidth, rect.height);
        EditorGUI.LabelField(rect, "HasMaterial (Material)");
        rect.y += EditorGUIUtility.singleLineHeight;
        
        Rect offsetLabelRect = new(rect.x, rect.y, fieldWidth, rect.height);
        Rect offsetRect = new(offsetLabelRect.x + labelWidth, rect.y, fieldWidth * 2f, rect.height);
        EditorGUI.LabelField(offsetLabelRect, "Offset ");
        EditorGUI.MultiIntField(offsetRect, new GUIContent[] { new("x"), new("y"), new("z") }, offset);
        rect.y += EditorGUIUtility.singleLineHeight;

        material = EditorGUI.IntField(materialRect, material);
        hasCheck = EditorGUI.Toggle(hasMatRect, hasCheck);
        materialProp.uintValue = (uint)(material & 0x7FFFFFFF) | (hasCheck ? 0x80000000u : 0u);
        offsetProp.intValue = ((offset[0] + 512) & 0x3FF) << 20 |
            ((offset[1] + 512) & 0x3FF) << 10 | ((offset[2] + 512) & 0x3FF);
    }

    /// <summary>  Callback for when the GUI needs to know the height of the Inspector element. </summary>
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight * 4;
    }
}
#endif