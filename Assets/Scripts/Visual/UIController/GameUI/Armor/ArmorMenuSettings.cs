using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.XR;

namespace WorldConfig.Intrinsic {
    /// <summary>
    /// Settings controlling the apperance of the armor display menu. The armor 
    /// system allows the player to equip armor items onto their character.
    /// </summary>
    [CreateAssetMenu(menuName = "Settings/Armor/Options")]
    public class Armor : ScriptableObject {
        /// <summary> The name of the texture within the texture registry of 
        /// the icon displayed on the <see cref="PanelNavbarManager">Navbar</see>
        /// referring to Armor Display.  </summary>
        [RegistryReference("Textures")]
        public string DisplayIcon;
        public float ShrinkDistance = 0.05f;
        public float SeperationDistance = 0.15f;
        public Color BaseLineColor = Color.white;
        public Color HighlightLineColor = Color.yellow;
        public EquipableArmor.BoneBinding HeadBone;
        public Catalogue<EquipableArmor> Variants;

        public void BindBones(Transform rig) {
            HeadBone.Bind(rig);
            for (int i = 0; i < Variants.Count(); i++) {
                foreach (EquipableArmor.BoneRegion reg in Variants.Retrieve(i).Bones.value)
                    reg.Bone.Bind(rig);
            }
        }
    }
}