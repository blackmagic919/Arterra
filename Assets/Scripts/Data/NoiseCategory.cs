
using System;
using System.Collections.Generic;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Data.Generation;

namespace Arterra.Data.Noise{

    [Serializable]
    [CreateAssetMenu(menuName = "Generation/NoiseCategory")]
    public class NoiseCategory : Category<Arterra.Data.Generation.Noise>
    {
        public Option<List<Option<Category<Arterra.Data.Generation.Noise>>>> Children;
        protected override Option<List<Option<Category<Arterra.Data.Generation.Noise>>>>? GetChildren() => Children;
        protected override void SetChildren(Option<List<Option<Category<Arterra.Data.Generation.Noise>>>> value) => Children = value;
}

}
