using System.Collections.Generic;
using UnityEngine;
using Arterra.Configuration;

namespace Arterra.Configuration
{
    [CreateAssetMenu(menuName = "ShaderData/MiniMesh/Category")]
    public class MiniMeshCategory : Category<MiniMeshSetting>
    {
        public Option<List<Option<Category<MiniMeshSetting>>>> Children;

        protected override Option<List<Option<Category<MiniMeshSetting>>>>? GetChildren() => Children;

        protected override void SetChildren(Option<List<Option<Category<MiniMeshSetting>>>> value) => Children = value;
    }
}
