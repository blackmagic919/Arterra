using System.Collections.Generic;
using UnityEngine;
using Arterra.Configuration;

[CreateAssetMenu(menuName = "Texture/Category")]
public class TextureCategory : Category<TextureContainer>
{
    public Option<List<Option<Category<TextureContainer>>>> Children;

    protected override Option<List<Option<Category<TextureContainer>>>>? GetChildren() => Children;
    protected override void SetChildren(Option<List<Option<Category<TextureContainer>>>> value) => Children = value;
}
