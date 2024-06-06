using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Generation/Resource Wrapper")]
public class GenerationResources : ScriptableObject
{
    public Material terrainMat;
    public Material waterMat;

    [Header("Global Settings")]
    public TextureData texData;
    public StructureGenerationData structData;
    public BiomeGenerationData biomeData;
    public NoiseGenerationData noiseData;

    [Header("Generation Utilities")]
    public ReadbackSettings readbackSettings;
    public GeneratorSettings geoSettings;
    public MeshCreatorSettings meshCreator;
    public SurfaceCreatorSettings surfaceSettings;

    [Header("Bake Data")]
    
    public AtmosphereBake atmosphereBake;

    [Header("Misc")]
    public List<ScriptableObject> MiscResources;
}
