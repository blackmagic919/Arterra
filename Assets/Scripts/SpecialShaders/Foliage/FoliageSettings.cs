using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "ShaderData/FoliageSetting")]
public class FoliageSettings : ShaderSettings
{
    [Tooltip("Size of Quad Images")]
    public float QuadSize = 1.0f;
    [Tooltip("Distance Extruded Along Normal")]
    public float Inflation = 0f;

    [Tooltip("The grass geometry creating compute shader")]
    public ComputeShader foliageComputeShader = default;
    [Tooltip("The triangle count adjustment compute shader")]
    public ComputeShader triToVertComputeShader = default;
    [Tooltip("The material to render the grass mesh")]
    public Material material = default;

}