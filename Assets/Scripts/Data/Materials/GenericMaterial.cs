using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace WorldConfig.Generation.Material
{
/// <summary> A concrete material type with no explicit interaction behavior. That is,
/// it does not need to do anything when updated. By default most materials
/// should not need to do anything. </summary>
[CreateAssetMenu(menuName = "Generation/MaterialData/GenericMat")]
public class GenericMaterial : MaterialData
{
    /// <summary> Even though it does nothing, it needs to fufill the contract so
    /// that it can be used in the same way as other materials. </summary>
    /// <param name="GCoord">The coordinate in grid space of a map entry that is this material</param>
    public override void UpdateMat(int3 GCoord)
    {

    }
}
}