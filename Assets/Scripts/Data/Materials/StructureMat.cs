using System.Collections.Generic;
using Arterra.Core.Storage;
using Unity.Mathematics;
using Arterra.Utils;
using Arterra.Configuration;

namespace Arterra.Data.Material {
    public abstract class PlaceableStructureMat : MaterialData {
        public Option<List<ConditionedGrowthMat.MapSamplePoint>> Structure;
        public int SelfIndex;
        /// <summary> Whether or not placement of the structure may include random rotations around the vertical-axis. </summary>
        public bool randYRot;
        /// <summary> Whether or not placement of the structure may include random rotations around the major horizontal-axis. The horizontal
        /// axis that it is rotated upon may shift depending on the <see cref="randYRot"/>.  </summary>
        public bool randXRot;
        /// <summary> Whether or not placement of the structure may include random rotations around the minor horizontal-axis. The horizontal
        /// axis that it is rotated upon may shift depending on the <see cref="randYRot"/> and <see cref="randXRot"/>.  </summary>
        public bool randZRot;
        private static Catalogue<MaterialData> matInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        private static int2[] MainAxis = { new(0, 0), new(1, 0), new(2, 0), new(3, 0), new(1, 0), new(3, 0) };
        public bool VerifyStructurePlaceement(int3 GCoord) {
            int3 offset = Structure.value[SelfIndex].Offset;
            if (randYRot && randXRot && randZRot) { //24 Unique Rots with 3 axis
                for (int i = 0; i < 6; i++) {
                    int3 rot = 0; rot.xy = MainAxis[i];
                    for (rot.z = 0; rot.z < 4; rot.z++) {
                        int3 delta = math.mul(CustomUtility.RotationLookupTable[rot.y, rot.x, rot.z], offset);
                        if (VerifySampleChecks(GCoord - delta, rot))
                            return true;
                    }
                }
            } else { // 16 Unique rotations if 2 axis of freedom
                int3 rot = int3.zero;
                for(rot.x = 0; rot.x <= (randXRot ? 3 : 0); rot.x++) {
                for(rot.y = 0; rot.y <= (randYRot ? 3 : 0); rot.y++) {
                for(rot.z = 0; rot.z <= (randZRot ? 3 : 0); rot.z++) {
                    int3 delta = math.mul(CustomUtility.RotationLookupTable[rot.y, rot.x, rot.z], offset);
                    if (VerifySampleChecks(GCoord - delta, rot))
                        return true;
                }}}   
            } return false;
        }
        

        private bool VerifySampleChecks(int3 GCoord, int3 rot, bool UseExFlag = true) {
            bool anyC = false; bool any0 = false;
            List<ConditionedGrowthMat.MapSamplePoint> checks = Structure.value;
            for(int i = 0; i < checks.Count; i++){
                ConditionedGrowthMat.MapSamplePoint samplePoint = checks[i];
                if (samplePoint.check.ExFlag && UseExFlag) continue;
                bool valid = SatisfiesCheck(samplePoint, GCoord, rot);
                if (samplePoint.check.AndFlag && !valid) return false;
                anyC = anyC || (valid && samplePoint.check.OrFlag);
                any0 = any0 || samplePoint.check.OrFlag;
            } 
            return !any0 || anyC;
        }
        private bool SatisfiesCheck(ConditionedGrowthMat.MapSamplePoint pt, int3 BaseCoord, int3 rot) {
            int3 offset = math.mul(CustomUtility.RotationLookupTable[rot.y, rot.x, rot.z], pt.Offset);
            int3 GCoord = BaseCoord + offset;
            MapData mapData = CPUMapManager.SampleMap(GCoord);
            if (!pt.check.bounds.Contains(mapData)) return false;
            if (!pt.HasMaterialCheck) return true;
            int checkMat = matInfo.RetrieveIndex(
                RetrieveKey((int)pt.material));
            return mapData.material == checkMat;
        }
    }
}