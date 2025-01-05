using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class SpecialShader : ScriptableObject
{
    [System.Serializable]
    public abstract class MaterialReference : ScriptableObject
    {
        public Shader shader;

        public virtual Material GetMaterial()
        {
            return null;
        }
    }
    
    private const int DRAW_STRIDE = (sizeof(float) * 4) + (sizeof(float) * 3) * 2 + sizeof(float) * 2;
    public virtual Material GetMaterial()
    {
        return null;
    }

    public virtual void ReleaseTempBuffers()
    {
        
    }

    public virtual void ProcessGeoShader(GenerationPreset.MemoryHandle memoryHandle, int vertAddress, int triAddress, 
                                         int baseGeoStart, int baseGeoCount, int geoCounter, int geoStart, int geoInd)
    {

    }

    public virtual void Release()
    {

    }
}