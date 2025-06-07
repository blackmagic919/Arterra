
using System;
using System.Collections.Generic;
using UnityEngine;
using WorldConfig;
using WorldConfig.Generation.Material;

namespace WorldConfig.Generation{

    [Serializable]
    [CreateAssetMenu(menuName = "Generation/NoiseCategory")]
    public class NoiseCategory : Category<Noise>
    {
        public Option<List<Option<Category<Noise>>>> Children;
        protected override Option<List<Option<Category<Noise>>>>? GetChildren() => Children;
        protected override void SetChildren(Option<List<Option<Category<Noise>>>> value) => Children = value;
}

}
