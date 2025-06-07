using System.Collections.Generic;
using UnityEngine;

namespace WorldConfig.Gameplay{
    [CreateAssetMenu(menuName = "GamePlay/Input/Category")]
    public class KeybindCategory : Category<KeyBind>
    {
        public Option<List<Option<Category<KeyBind>>>> Children;
        protected override Option<List<Option<Category<KeyBind>>>>? GetChildren() => Children;
        protected override void SetChildren(Option<List<Option<Category<KeyBind>>>> value) => Children = value;
}
}
