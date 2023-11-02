using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class SpecialShader : MonoBehaviour
{

    public virtual void SetSettings(ShaderSettings settings)
    {

    }

    public virtual void SetMesh(Mesh mesh)
    {

    }

    public virtual void Enable()
    {

    }

    public virtual void Disable()
    {

    }
}

public abstract class ShaderSettings : UpdatableData
{

}