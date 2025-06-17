using System.Collections.Generic;
using UnityEngine;
using WorldConfig;

[CreateAssetMenu(menuName = "ShaderData/QuadShader/Category")]
public class QuadCategory : Category<QuadSetting>
{
    public Option<List<Option<Category<QuadSetting>>>> Children;
    protected override Option<List<Option<Category<QuadSetting>>>>? GetChildren() => Children;
    protected override void SetChildren(Option<List<Option<Category<QuadSetting>>>> value) => Children = value;
}
