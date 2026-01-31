using System.Collections.Generic;
using UnityEngine;

namespace Arterra.Configuration
{
    [CreateAssetMenu(menuName = "Settings/Armor/Category")]
    public class EquipableCategory : Category<EquipableArmor>
    {
        public Option<List<Option<Category<EquipableArmor>>>> Children;
        protected override Option<List<Option<Category<EquipableArmor>>>>? GetChildren() => Children;
        protected override void SetChildren(Option<List<Option<Category<EquipableArmor>>>> value) => Children = value;
    }
}
