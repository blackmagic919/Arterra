using System.Collections.Generic;
using UnityEngine;
using Arterra.Configuration;

namespace Arterra.Configuration
{
    [CreateAssetMenu(menuName = "ShaderData/Tesselation/Category")]
    public class TesselCategory : Category<TesselSettings>
    {
        public Option<List<Option<Category<TesselSettings>>>> Children;
        protected override Option<List<Option<Category<TesselSettings>>>>? GetChildren() => Children;
        protected override void SetChildren(Option<List<Option<Category<TesselSettings>>>> value) => Children = value;
    }
}