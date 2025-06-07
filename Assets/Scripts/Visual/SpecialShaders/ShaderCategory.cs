using System.Collections.Generic;
using UnityEngine;

namespace WorldConfig.Quality
{
    [CreateAssetMenu(menuName = "ShaderData/Category")]
    public class ShaderCategory : Category<GeoShader>
    {
        public Option<List<Option<Category<GeoShader>>>> Children;
        protected override Option<List<Option<Category<GeoShader>>>>? GetChildren() => Children;
        protected override void SetChildren(Option<List<Option<Category<GeoShader>>>> value) => Children = value;
    }
}
