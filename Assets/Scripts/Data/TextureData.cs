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
    public Option<List<Option<MaterialData> > > MaterialDictionary;
    [UIgnore]
    public Option<Texture2D>liquidFineWave;
    [UIgnore]
    public Option<Texture2D> liquidCoarseWave;
}
