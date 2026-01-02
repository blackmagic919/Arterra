
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arterra.Configuration.Generation{

    [Serializable]
    [CreateAssetMenu(menuName = "Generation/NoiseCategory")]
    public class NoiseCategory : Category<Noise>
    {
        public Option<List<Option<Category<Noise>>>> Children;
        protected override Option<List<Option<Category<Noise>>>>? GetChildren() => Children;
        protected override void SetChildren(Option<List<Option<Category<Noise>>>> value) => Children = value;
}

}
