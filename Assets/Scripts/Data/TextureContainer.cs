using UnityEngine;
using Arterra.Configuration;

[CreateAssetMenu(menuName = "Texture/Container")]
public class TextureContainer : Category<TextureContainer>
{
    [UIHidden]
    [UISetting(Defaulting = true)]
    [Newtonsoft.Json.JsonIgnore]
    public Sprite self;
}
