using System.Collections.Generic;
using Arterra.Configuration;
using UnityEngine;

namespace Arterra.Data.Models {
    [CreateAssetMenu(menuName = "Generation/Custom Model Catalogue")]
    public class CustomModelCategory : Category<CustomModel> {
        public Option<List<Option<Category<CustomModel>>>> Children;
        protected override Option<List<Option<Category<CustomModel>>>>? GetChildren() => Children;
        protected override void SetChildren(Option<List<Option<Category<CustomModel>>>> value) => Children = value;
    }
}
