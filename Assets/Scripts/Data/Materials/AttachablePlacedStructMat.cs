using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Material {
    [CreateAssetMenu(menuName = "Generation/MaterialData/Behaviors/AttachablePlacedStructMat")]
    public class AttachablePlacedStructMat : PlaceableStructureMat {
        public Option<List<Attachment>> Attachments;

        [Serializable]
        public struct Attachment {
            public string Name;
            public Option<List<MapSamplePoint>> Checks;
            public StructureLayout Layout;
        }

        public override int LayoutCount => Attachments.value.Count;
        public override bool LinksAllMembers => true;

        public override StructureLayout GetLayout(int layout = 0) => Attachments.value[layout].Layout;

        public override int GetPlacementItemMaterial(int material, int layout) {
            var placement = GetLayout(layout);
            return Config.CURRENT.Generation.Materials.value.MaterialDictionary.RetrieveIndex(
                RetrieveKey((int)placement.Structure.value[placement.SelfIndex].map.material));
        }

        public override bool CanPlaceLayout(int3 origin, int3 rotation, int layout) {
            if (!base.CanPlaceLayout(origin, rotation, layout)) return false;
            if (!VerifySampleChecks(Attachments.value[layout].Checks.value, origin, rotation, false)) return false;

            var materials = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
            int3x3 transform = CustomUtility.RotationLookupTable[rotation.y, rotation.x, rotation.z];
            foreach (MapPlacement placement in GetLayout(layout).Structure.value) {
                MapData sample = CPUMapManager.SampleMap(origin + math.mul(transform, placement.map.Offset));
                if (sample.IsNull) return false;
                if (!sample.IsSolid) continue;
                if (!placement.map.HasMaterialCheck || sample.material != materials.RetrieveIndex(
                    RetrieveKey((int)placement.map.material))) return false;
            }
            return true;
        }

        public override void InstallPlacementMetadata(int3 coord, int3 keystone, int3 rotation, int layout) {
            base.InstallPlacementMetadata(coord, keystone, rotation, layout);
            CPUMapManager.SetExistingMapMeta(coord, new StructureLayoutInfo { Layout = layout });
        }
    }
}
