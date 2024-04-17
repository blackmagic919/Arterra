using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class SpecialShader : UpdatableData
{
    [System.Serializable]
    public abstract class MaterialReference : UpdatableData
    {
        public Shader shader;

        public virtual Material GetMaterial()
        {
            return null;
        }
    }

    [System.Serializable]
    public abstract class ShaderSettings : UpdatableData
    {
        //public MaterialReference material;
    }

    private const int DRAW_STRIDE = (sizeof(float) * 4) + (sizeof(float) * 3) * 2 + sizeof(float) * 2;

    public virtual void Instantiate(Transform transform)
    {

    }

    public virtual Material GetMaterial()
    {
        return null;
    }

    public virtual void ReleaseTempBuffers()
    {
        
    }

    public virtual void ProcessGeoShader(Transform transform, ComputeBuffer sourceTriangles, ComputeBuffer startIndex, ComputeBuffer drawTriangles, int shaderIndex)
    {

    }

    public virtual void Release()
    {

    }
}