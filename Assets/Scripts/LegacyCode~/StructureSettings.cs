using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class StructureSettings : ScriptableObject
{
    public int minimumLOD; 
    /* It is important to realize minimumLOD isn't a setting related to generation, but the size of the structure itself and thus belongs here
     * 
     * The higher the minimumLOD, the farther away it must be considered to generate,
     * a lower LOD means structure is smaller and code is more efficient -->

   LOD->   0     1   2   3 
       ._______.___.___.___.
       |       |   |   |   |
       .___.___.___.___.___.
       |   |   |   |   |   |
       |___|___|___|___|___|
       |   |   |   |   |   |
       |___|___|___|___|___|
       |   |   |   |   |   |
       |___|___|___|___|___|
       |   |   |   |   |   |
       |___|___|___|___|___|
       |   |   |   |   |   |
       |___|___|___|___|___|

    *Diagarm extends in 3 dimensions
     */

    public bool randThetaRot;
    public bool randPhiRot;
    public int sizeX;
    public int sizeY;
    public int sizeZ;

    [HideInInspector]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct StructSettingsCopy
    {
        public int minimumLOD;

        public uint randThetaRot;
        public uint randPhiRot;
        public int sizeX;
        public int sizeY;
        public int sizeZ;

        public StructSettingsCopy(StructureSettings settings)
        {
            this.minimumLOD = settings.minimumLOD;
            this.randThetaRot = settings.randThetaRot ? 1u : 0u;
            this.randPhiRot = settings.randPhiRot ? 1u : 0u;
            this.sizeX = settings.sizeX;
            this.sizeY = settings.sizeY;
            this.sizeZ = settings.sizeZ;
        }
    }
}
