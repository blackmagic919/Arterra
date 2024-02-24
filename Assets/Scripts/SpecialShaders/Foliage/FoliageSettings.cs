using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static SpecialShader;

[CreateAssetMenu(menuName = "ShaderData/FoliageShader/Settings")]
public class FoliageSettings : ShaderSettings
{
    [Tooltip("Size of Quad Images")]
    public float QuadSize = 1.0f;
    [Tooltip("Distance Extruded Along Normal")]
    public float Inflation = 0f;

    [Tooltip("For determining dispatch args for shader")]
    public ComputeShader indirectArgsShader = default;
    [Tooltip("The grass geometry creating compute shader")]
    public ComputeShader foliageComputeShader = default;
    public Material material;
}