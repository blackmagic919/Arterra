using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static SpecialShader;

[CreateAssetMenu(menuName = "ShaderData/GrassShader/Settings")]
public class GrassSettings : ShaderSettings
{
    [Tooltip("Total height of grass layer stack")]
    public float grassHeight = 0.5f;
    [Tooltip("Maximum # of grass layers")]
    public int maxLayers = 16;
    [Tooltip("Multiplier on World Position if using world position as UV")]
    public float worldPositionUVScale;

    [Tooltip("For determining dispatch args for shader")]
    public ComputeShader indirectArgsShader = default;
    [Tooltip("The grass geometry creating compute shader")]
    public ComputeShader grassComputeShader = default;
    public Material material;
}