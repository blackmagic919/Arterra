using System.Collections.Generic;
using UnityEngine;
using Arterra.Config;

[CreateAssetMenu(menuName = "ShaderData/ShellTexture/Category")]
public class ShellCategory : Category<ShellSetting>
{
    public Option<List<Option<Category<ShellSetting>>>> Children;
    protected override Option<List<Option<Category<ShellSetting>>>>? GetChildren() => Children;
    protected override void SetChildren(Option<List<Option<Category<ShellSetting>>>> value) => Children = value;
}
