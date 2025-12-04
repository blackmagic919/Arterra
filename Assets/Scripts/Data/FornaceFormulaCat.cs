
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldConfig.Generation.Furnace {
    // Create a silimar category class for FurnaceFormula
    [Serializable]
    [CreateAssetMenu(menuName = "Settings/Furnace/FurnaceFormulaCategory")]
    public class FornaceFormulaCat : Category<FurnaceFormula> {
        public Option<List<Option<Category<FurnaceFormula>>>> Children;
        protected override Option<List<Option<Category<FurnaceFormula>>>>? GetChildren() => Children;
        protected override void SetChildren(Option<List<Option<Category<FurnaceFormula>>>> value) => Children = value;

    }
}
