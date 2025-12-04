using System;
using System.Collections.Generic;
using UnityEngine;
namespace WorldConfig.Generation.Furnace {
    /// <summary>
    /// A structure defining how a material is converted in a furnace.
    /// 
    /// This conversion can be used for input materials being smelted or output materials being produced.
    /// </summary>
    [Serializable]
    public struct FurnaceMaConversion {
        /// <summary>
        /// The name of the material.
        /// </summary>
        [RegistryReference("Items")]
        public int Index;

        /// <summary>
        /// The rate of amount per second at which the material is converted or being converted to.
        /// </summary>
        public float Rate;
    }

    [Serializable]
    [CreateAssetMenu(menuName = "Settings/Furnace/FurnaceFormula")]
    public class FurnaceFormula : Category<FurnaceFormula> {
        public Option<List<string>> Names;

        public float Temperature;
        public List<FurnaceMaConversion> Inputs;
        public List<FurnaceMaConversion> Outputs;
        public bool IsMatch(Item.IItem fuelItem, TagRegistry.Tags fuelTag, List<Item.IItem> inputItems, List<Item.IItem> outputItems) {
            // Check input items matching
            if (Inputs.Count != inputItems.Count) return false;
            var itemInfo = Config.CURRENT.Generation.Items;
            foreach (var input in Inputs) {
                bool found = false;
                foreach (var item in inputItems) {
                    string matName = itemInfo.Retrieve(item.Index).Name;
                    if (matName == Names.value[input.Index]) {
                        found = true;
                        break;
                    }
                }
                if (!found) return false;
            }

            // Check fuelTime temperature matching
            if (fuelItem == null) return false;
            if (!itemInfo.GetMostSpecificTag(fuelTag, fuelItem.Index, out object prop))
                return false;
            var combustible = prop as CombustibleTag;
            float fuelTemperature = combustible.Temperature;
            if (fuelTemperature < Temperature) return false;


            // Check output items matching. The output either match with the formula or is empty
            if (outputItems.Count > 0) {
                if (Outputs.Count != outputItems.Count) return false;
                foreach (var output in Outputs) {
                    bool found = false;
                    foreach (var item in outputItems) {
                        string matName = itemInfo.Retrieve(item.Index).Name;
                        if (matName == Names.value[output.Index]) {
                            found = true;
                            break;
                        }
                    }
                    if (!found) return false;
                }
            }


            return true;
        }
        public List<Item.IItem> CreateOutputItems() {
            List<Item.IItem> outputItems = new List<Item.IItem>();
            var itemInfo = Config.CURRENT.Generation.Items;
            foreach (var output in Outputs) {
                int itemIndex = itemInfo.RetrieveIndex(Names.value[output.Index]);
                Item.IItem item = itemInfo.Retrieve(itemIndex).Item;
                item.Create(itemIndex, 0); // Assuming Rate is amount per second
                outputItems.Add(item);
            }
            return outputItems;
        }
    }

    static class FurnaceFormulas {
        public static FurnaceFormula GetMatchingFormula(Item.IItem fuelItem, TagRegistry.Tags fuelTag, List<Item.IItem> inputItems, List<Item.IItem> outputItems) {
            var formulaInfo = Config.CURRENT.System.FurnaceFormulas;
            foreach (var formula in formulaInfo.Reg) {
                if (formula.IsMatch(fuelItem, fuelTag, inputItems, outputItems)) {
                    return formula;
                }
            }
            return null;
        }
    }


}