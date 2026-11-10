using System;
using System.Collections.Generic;
using System.Linq;
using Arterra.Core.Storage;
using Unity.Mathematics;
using Arterra.Utils;
using Arterra.Configuration;

namespace Arterra.Data.Material {
    public abstract class PlaceableStructureMat : MaterialBehavior {
        private static Catalogue<MaterialData> matInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        protected bool TryGetKeystone(ref int3 GCoord) {
            if (!CPUMapManager.TryGetExistingMapMeta(GCoord, out StructurePartInfo part)) return true;
            int3 delta = part.Delta;
            if (math.all(delta == 0)) return true;

            int3 keystone = GCoord + delta;
            MapData source = CPUMapManager.SampleMap(GCoord);
            MapData target = CPUMapManager.SampleMap(keystone);
            if (source.IsNull || !source.IsSolid) return false;
            if (target.IsNull || !target.IsSolid) return false;
            // Resolve one hop only; a part cannot own another part.
            if (CPUMapManager.TryGetExistingMapMeta(keystone, out StructurePartInfo owner)
                && math.any(owner.Delta != 0)) return false;

            GCoord = keystone;
            return true;
        }

        public abstract int LayoutCount { get; }
        public virtual bool LinksAllMembers => false;

        public abstract StructureLayout GetLayout(int layout = 0);

        public override void Preset(MaterialData self) {
            self.Register(typeof(PlaceableStructureMat), this);
        }

        public virtual int GetPlacementItemMaterial(int material, int layout) => material;

        public virtual bool CanPlaceLayout(int3 origin, int3 rotation, int layout) =>
            layout >= 0 && layout < LayoutCount && GetLayout(layout).AllowsRotation(rotation);

        public bool TryResolveLayout(int3 keystone, float3 direction, out int3 rotation, out int layout) {
            rotation = int3.zero;
            for (layout = 0; layout < LayoutCount; layout++) {
                StructureLayout placement = GetLayout(layout);
                int3 preferred = placement.GetRotation(direction);
                if (CanPlaceLayout(placement.GetOrigin(keystone, preferred), preferred, layout)) {
                    rotation = preferred;
                    return true;
                }

                int3 count = placement.RotationCount;
                int3 candidate = int3.zero;
                for (candidate.x = 0; candidate.x < count.x; candidate.x++) {
                    for (candidate.y = 0; candidate.y < count.y; candidate.y++) {
                        for (candidate.z = 0; candidate.z < count.z; candidate.z++) {
                            if (math.all(candidate == preferred)) continue;
                            if (!CanPlaceLayout(placement.GetOrigin(keystone, candidate), candidate, layout)) continue;
                            rotation = candidate;
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        public int GetPlacedLayout(int3 coord) => CPUMapManager.TryGetExistingMapMeta(coord,
            out StructureLayoutInfo info) ? info.Layout : 0;

        public virtual void InstallPlacementMetadata(int3 coord, int3 keystone, int3 rotation, int layout) {
            CPUMapManager.SetExistingMapMeta(coord, new StructurePartInfo {
                Delta = keystone - coord,
                Rotation = rotation
            });
        }

        public bool VerifyStructurePlacement(int3 GCoord) {
            if (CPUMapManager.TryGetExistingMapMeta(GCoord, out StructurePartInfo part)) {
                int layout = GetPlacedLayout(GCoord);
                if (layout < 0 || layout >= LayoutCount) return false;
                return VerifyLayout(GCoord + part.Delta, part.Rotation, layout);
            }

            for (int layout = 0; layout < LayoutCount; layout++) {
                int3 count = GetLayout(layout).RotationCount;
                int3 rot = int3.zero;
                for (rot.x = 0; rot.x < count.x; rot.x++) {
                    for (rot.y = 0; rot.y < count.y; rot.y++) {
                        for (rot.z = 0; rot.z < count.z; rot.z++) {
                            if (VerifyLayout(GCoord, rot, layout)) return true;
                        }
                    }
                }
            }
            return false;
        }

        private bool VerifyLayout(int3 keystone, int3 rotation, int layout) {
            var placement = GetLayout(layout);
            int3 origin = placement.GetOrigin(keystone, rotation);
            return CanPlaceLayout(origin, rotation, layout)
                && VerifySampleChecks(placement.Structure.Select(p => p.map), origin, rotation);
        }

        protected bool VerifySampleChecks(IEnumerable<MapSamplePoint> checks, int3 origin, int3 rotation,
            bool useExFlag = true) {
            bool anyC = false;
            bool any0 = false;
            foreach (MapSamplePoint sample in checks) {
                if (sample.check.ExFlag && useExFlag) continue;
                bool valid = SatisfiesCheck(sample, origin, rotation);
                if (sample.check.AndFlag && !valid) return false;
                anyC |= valid && sample.check.OrFlag;
                any0 |= sample.check.OrFlag;
            }
            return !any0 || anyC;
        }

        private bool SatisfiesCheck(MapSamplePoint pt, int3 BaseCoord, int3 rot) {
            int3 offset = math.mul(CustomUtility.RotationLookupTable[rot.y, rot.x, rot.z], pt.Offset);
            int3 GCoord = BaseCoord + offset;
            MapData mapData = CPUMapManager.SampleMap(GCoord);
            if (mapData.IsNull || !pt.check.bounds.Contains(mapData)) return false;
            if (!pt.HasMaterialCheck) return true;
            
            // Retrieve material name from cached names list using index
            if (Names.value == null || (int)pt.material < 0 || (int)pt.material >= Names.value.Count)
                return false;
            string materialName = Names.value[(int)pt.material];
            if (string.IsNullOrEmpty(materialName))
                return false;
            
            int checkMat = matInfo.RetrieveIndex(materialName);
            return mapData.material == checkMat;
        }
    }

    public abstract class SingleLayoutStructureMat : PlaceableStructureMat {
        public StructureLayout Layout;

        public override int LayoutCount => 1;
        public override StructureLayout GetLayout(int layout = 0) => Layout;
    }

    [Serializable]
    public class StructureLayout {
        public List<MapPlacement> Structure;
        public int SelfIndex;
        public bool randYRot;
        public bool randXRot;
        public bool randZRot;

        public int3 RotationCount => new(randXRot ? 4 : 1, randYRot ? 4 : 1, randZRot ? 4 : 1);

        public bool AllowsRotation(int3 rotation) => math.all(rotation >= 0) && math.all(rotation < RotationCount);

        public int3 GetOrigin(int3 keystone, int3 rotation) => keystone - math.mul(
            CustomUtility.RotationLookupTable[rotation.y, rotation.x, rotation.z], Structure[SelfIndex].map.Offset);

        public int3 GetRotation(float3 direction) {
            float3 forward = math.normalizesafe(direction);
            if (math.lengthsq(forward) == 0) return int3.zero;

            // Keep world up where possible; use a fixed roll when looking vertically.
            float3 right = math.cross(new float3(0, 1, 0), forward);
            if (math.lengthsq(right) < 1E-10f)
                right = math.cross(new float3(0, 0, 1), forward);
            right = math.normalize(right);
            float3 up = math.cross(forward, right);

            int3 bestRotation = int3.zero;
            float bestScore = float.NegativeInfinity;
            int3 rotationCount = RotationCount;

            for (int y = 0; y < rotationCount.y; y++) {
                for (int x = 0; x < rotationCount.x; x++) {
                    for (int z = 0; z < rotationCount.z; z++) {
                        int3x3 candidate = CustomUtility.RotationLookupTable[y, x, z];
                        // Maximizing the basis dot products minimizes the rotation angle.
                        float score = math.dot((float3)candidate.c0, right)
                            + math.dot((float3)candidate.c1, up)
                            + math.dot((float3)candidate.c2, forward);
                        if (score <= bestScore) continue;

                        bestScore = score;
                        bestRotation = new int3(x, y, z);
                    }
                }
            }

            return bestRotation;
        }
    }

    [Serializable]
    public struct StructurePartInfo {
        public uint Data;

        // Signed offsets in placed grid space. Rotation occupies the upper byte.
        [Newtonsoft.Json.JsonIgnore]
        public int3 Delta {
            get => new(unchecked((sbyte)(Data & 255)), unchecked((sbyte)((Data >> 8) & 255)),
                unchecked((sbyte)((Data >> 16) & 255)));
            set {
                if (math.any(value < -128) || math.any(value > 127))
                    throw new ArgumentOutOfRangeException(nameof(value), "Keystone offsets must fit in signed bytes.");
                Data = (Data & 0xFF000000u) | ((uint)value.x & 255)
                    | (((uint)value.y & 255) << 8) | (((uint)value.z & 255) << 16);
            }
        }

        // Two bits per axis store the placement's quarter turns; the top two bits remain reserved.
        [Newtonsoft.Json.JsonIgnore]
        public int3 Rotation {
            get => new((int)((Data >> 24) & 3), (int)((Data >> 26) & 3), (int)((Data >> 28) & 3));
            set => Data = (Data & 0xC0FFFFFFu) | (((uint)value.x & 3) << 24)
                | (((uint)value.y & 3) << 26) | (((uint)value.z & 3) << 28);
        }
    }

    [Serializable]
    public struct StructureLayoutInfo {
        public int Layout;
    }

    [Serializable]
    public struct MapPlacement {
        public MapSamplePoint map;
        public Optional<Models.ModelRenderInfo> modelInfo;
    }
}
