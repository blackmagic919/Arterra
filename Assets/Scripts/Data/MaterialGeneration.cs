using UnityEngine;
using WorldConfig;

namespace WorldConfig.Generation.Material{
/// <summary> The container for all materials that can be generated. Materials determine how the terrain appears
/// when it is solid, liquid, or atmospheric as well as <see cref="TerrainGeneration.TerrainUpdate"> possible 
/// interactions </see>. Materials only exist as part of the terrain and once picked up are handled by the 
/// <see cref="ItemAuthoring"/> system. </summary>
[CreateAssetMenu(menuName = "Settings/TextureDict")]
public class Generation : ScriptableObject
{
    /// <summary> The registry containing all materials that can be generated. The number of materials that can be generated is limited by this registry.
    /// See <see cref="MaterialData"/> for more information. </summary>
    [SerializeField]
    public Registry<MaterialData> MaterialDictionary;
    /// <summary> The liquid fine wave texture that is blended with <see cref="liquidCoarseWave"/> to create waves. This can
    /// be tuned by <see cref="MaterialData.liquidData"/> depending on the liquid that is being displayed. </summary>
    [UISetting(Ignore = true)]
    public Option<Texture2D>liquidFineWave;
    /// <summary> The liquid coarse wave texture that is blended with <see cref="liquidFineWave"/> to create waves. This can
    /// be tuned by <see cref="MaterialData.liquidData"/> depending on the liquid that is being displayed. </summary>
    [UISetting(Ignore = true)]
    public Option<Texture2D> liquidCoarseWave;
}
}
