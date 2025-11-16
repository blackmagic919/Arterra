using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using Unity.Burst;
using System;
using WorldConfig.Generation.Structure;
using MapStorage;
using System.Collections.Generic;
using WorldConfig.Generation.Material;
using WorldConfig;
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
        public StructureData.CheckInfo SelfCondition;

        private static Catalogue<MaterialData> matInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;

        public override void RandomMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng) {
            MapData cur = CPUMapManager.SampleMap(GCoord); //Current 
            if (!SelfCondition.Contains(cur)) return;
            foreach (MapSampleRegion region in ConvertRegions.value) {
                if (prng.NextFloat() > region.convertChance) continue;

                if (!VerifySampleChecks(
                    this, ConversionData.value,
                    new int2((int)region.checkStart, (int)region.checkEnd), GCoord
                )) continue;

                for (int i = (int)region.convertStart; i <= region.convertEnd; i++) {
                    MapSamplePoint point = ConversionData.value[i];
                    point.PlaceEntry(this, GCoord, ref prng);
                }
            }
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

        public static bool VerifySampleChecks(MaterialData mat,
            List<MapSamplePoint> checks, int2 range,
            int3 GCoord, bool UseExFlag = true
        ) {
            bool anyC = false; bool any0 = false;
            for(int i = range.x; i <= range.y; i++){
                MapSamplePoint samplePoint = checks[i];
                if (samplePoint.check.ExFlag && UseExFlag) continue;
                bool valid = samplePoint.SatisfiesCheck(mat, GCoord);
                if (samplePoint.check.AndFlag && !valid) return false;
                anyC = anyC || (valid && samplePoint.check.OrFlag);
                any0 = any0 || samplePoint.check.OrFlag;
            }
            return !any0 || anyC;
        }

        [Serializable]
        public struct MapSamplePoint {
            public Entity.ProfileE check;
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
                set => _material = value & 0x7FFFFFFF | (_material & 0x80000000);
            }

            public bool HasMaterialCheck {
                readonly get => (_material & 0x80000000) != 0;
                set {
                    if (value) _material |= 0x80000000;
                    else _material &= 0x7FFFFFFF;
                }
            }

            public bool SatisfiesCheck(MaterialData mat, int3 BaseCoord) {
                int3 GCoord = BaseCoord + Offset;
                MapData mapData = CPUMapManager.SampleMap(GCoord);
                if (!check.bounds.Contains(mapData)) return false;
                if (!HasMaterialCheck) return true;
                int checkMat = matInfo.RetrieveIndex(
                    mat.RetrieveKey((int)material));
                return mapData.material == checkMat;
            }

            public void PlaceEntry(MaterialData mat, int3 BaseCoord, ref Unity.Mathematics.Random prng, bool DropItem = false) {
                int3 GCoord = BaseCoord + Offset;
                MapData cur = CPUMapManager.SampleMap(GCoord);
                if (cur.IsNull) return;

                MapData newData = new MapData { data = 0 };

                if (HasMaterialCheck) {
                    newData.material = matInfo.RetrieveIndex(mat.RetrieveKey((int)material));
                } else newData.material = cur.material;


                if (check.flags != 0) {
                    newData.viscosity = cur.viscosity;
                    newData.density = cur.density;
                } else {
                    newData.viscosity = Mathf.RoundToInt(check.bounds.MinSolid +
                        prng.NextFloat() * (check.bounds.MaxSolid - check.bounds.MinSolid));
                    int liquidDensity = Mathf.RoundToInt(check.bounds.MinLiquid +
                        prng.NextFloat() * (check.bounds.MaxLiquid - check.bounds.MinLiquid));
                    newData.density = Mathf.Clamp(newData.viscosity + liquidDensity, 0, MapData.MaxDensity);
                }

                MapData deltaC = cur; MapData deltaN = newData;
                if (newData.material == cur.material) {
                    //If it's the same material, recalculate cur as the delta viscosity and density
                    deltaC.viscosity -= math.min(cur.SolidDensity, newData.SolidDensity);
                    deltaC.density = deltaC.viscosity + deltaC.LiquidDensity
                        - math.min(cur.LiquidDensity, newData.LiquidDensity);

                    deltaN.viscosity = math.max(cur.viscosity, newData.viscosity) - cur.viscosity;
                    deltaN.density = deltaN.viscosity - cur.LiquidDensity
                        + math.max(cur.LiquidDensity, newData.LiquidDensity);
                }

                if (matInfo.Retrieve(cur.material).OnRemoving(GCoord, null))
                    return;

                if (matInfo.Retrieve(newData.material).OnPlacing(GCoord, null))
                    return;

                Item.IItem drop = matInfo.Retrieve(cur.material).OnRemoved(GCoord, deltaC);
                if (DropItem) InventoryController.DropItem(drop, GCoord);
                matInfo.Retrieve(newData.material).OnPlaced(GCoord, deltaN);

                CPUMapManager.SetMap(newData, GCoord);
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
public class StructCheckDrawer : PropertyDrawer {
    private static readonly Dictionary<string, bool> _foldouts = new();
    /// <summary>  Callback for when the GUI needs to be rendered for the property. </summary>
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
        string path = property.propertyPath;
        if (!_foldouts.ContainsKey(path))
            _foldouts[path] = false;

        _foldouts[path] = EditorGUI.Foldout(
            new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight),
            _foldouts[path], label, true); // triangle on left, label is clickable

        if (!_foldouts[path]) return;

        // Draw check bounds normally
        SerializedProperty checkProp = property.FindPropertyRelative("check");
        Rect rect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        rect.y += EditorGUIUtility.singleLineHeight;

        EditorGUI.PropertyField(rect, checkProp);
        rect.y += EditorGUI.GetPropertyHeight(checkProp);

        SerializedProperty materialProp = property.FindPropertyRelative("_material");
        SerializedProperty offsetProp = property.FindPropertyRelative("_offset");

        uint matInfo = materialProp.uintValue;
        bool hasCheck = (matInfo & 0x80000000) != 0;
        int[] offset = new int[3]{
            (int)((offsetProp.intValue >> 20) & 0x3FF) - 512,
            (int)((offsetProp.intValue >> 10) & 0x3FF) - 512,
            (int)(offsetProp.intValue & 0x3FF) - 512
        };

        float labelWidth = EditorGUIUtility.labelWidth;
        float fieldWidth = rect.width / 2f;

        Rect hasMatRect = new(rect.x, rect.y, fieldWidth, rect.height);
        EditorGUI.LabelField(hasMatRect, "HasMaterial");
        hasMatRect.x += labelWidth;
        hasCheck = EditorGUI.Toggle(hasMatRect, hasCheck);

        if (hasCheck) {
            Rect materialRect = new(rect.x + fieldWidth, rect.y, fieldWidth, rect.height);
            RegistryReferenceDrawer.SetupRegistries();
            RegistryReferenceDrawer materialDrawer = new RegistryReferenceDrawer { BitMask = 0x7FFFFFFF, BitShift = 0 };
            materialDrawer.DrawRegistryDropdown(materialRect, materialProp, new GUIContent("Material"),
                Config.TEMPLATE.Generation.Materials.value.MaterialDictionary);
        }

        rect.y += EditorGUIUtility.singleLineHeight;
        Rect offsetLabelRect = new(rect.x, rect.y, labelWidth, rect.height);
        Rect offsetRect = new(offsetLabelRect.x + labelWidth, rect.y, rect.width - labelWidth, rect.height);
        EditorGUI.LabelField(offsetLabelRect, "Offset ");
        EditorGUI.MultiIntField(offsetRect, new GUIContent[] { new("x"), new("y"), new("z") }, offset);
        rect.y += EditorGUIUtility.singleLineHeight;

        materialProp.uintValue = (materialProp.uintValue & 0x7FFFFFFF) | (hasCheck ? 0x80000000u : 0u);
        offsetProp.intValue = ((offset[0] + 512) & 0x3FF) << 20 |
            ((offset[1] + 512) & 0x3FF) << 10 | ((offset[2] + 512) & 0x3FF);

    }

    /// <summary>  Callback for when the GUI needs to know the height of the Inspector element. </summary>
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
        bool isExpanded = _foldouts.TryGetValue(property.propertyPath, out bool val) && val;
        if (!isExpanded) return EditorGUIUtility.singleLineHeight;

        SerializedProperty checkProp = property.FindPropertyRelative("check");
        return EditorGUI.GetPropertyHeight(checkProp) + EditorGUIUtility.singleLineHeight * 3;
    }
}
#endif