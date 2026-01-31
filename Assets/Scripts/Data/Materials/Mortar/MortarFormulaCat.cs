
using System;
using System.Collections.Generic;
using UnityEngine;
using Arterra.Configuration;

namespace Arterra.Data.Intrinsic.Mortar {
    [Serializable]
    [CreateAssetMenu(menuName = "Settings/Mortar/MortarCategory")]
    public class FornaceFormulaCat : Category<MortarFormula> {
        public Option<List<Option<Category<MortarFormula>>>> Children;
        protected override Option<List<Option<Category<MortarFormula>>>>? GetChildren() => Children;
        protected override void SetChildren(Option<List<Option<Category<MortarFormula>>>> value) => Children = value;

    }
}
