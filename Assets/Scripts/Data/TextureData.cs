using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using static WorldOptions;
using Newtonsoft.Json;

[CreateAssetMenu(menuName = "Settings/TextureDict")]
public class TextureData : ScriptableObject
{
    [SerializeField]
    public Registry<MaterialData> MaterialDictionary;
    [UISetting(Ignore = true)]
    public Option<Texture2D>liquidFineWave;
    [UISetting(Ignore = true)]
    public Option<Texture2D> liquidCoarseWave;
}
