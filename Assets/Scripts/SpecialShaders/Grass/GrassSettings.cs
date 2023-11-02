using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "ShaderData/GrassSetting")]
public class GrassSettings : ShaderSettings
{
    [Tooltip("Total height of grass layer stack")]
    public float grassHeight = 0.5f;
    [Tooltip("Maximum # of grass layers")]
    public int maxLayers = 16;
    [Tooltip("LOD Setting, Minimum Distance for LOD")]
    public float lodMinCameraDistance = 1;
    [Tooltip("LOD Setting, Maximum Distance for LOD")]
    public float lodMaxCameraDistance = 1;
    [Tooltip("LOD Setting, Power Applied to LOD Distance Falloff"), Range(0, 10)]
    public float lodFactor = 2;
    [Tooltip("Use world position XZ as the UV")]
    public bool useWorldPositionAsUV;
    [Tooltip("Multiplier on World Position if using world position as UV")]
    public float worldPositionUVScale;

    [Tooltip("The grass geometry creating compute shader")]
    public ComputeShader grassComputeShader = default;
    [Tooltip("The triangle count adjustment compute shader")]
    public ComputeShader triToVertComputeShader = default;
    [Tooltip("The material to render the grass mesh")]
    public Material material = default;
}