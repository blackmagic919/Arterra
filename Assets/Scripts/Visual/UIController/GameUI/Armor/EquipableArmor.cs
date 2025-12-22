using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Config;

[CreateAssetMenu(menuName = "Settings/Armor/Equipable")]
public class EquipableArmor : Category<EquipableArmor> {
    public float TargetSlotDistance = 0.2f;
    public Option<List<Option<BoneRegion>>> Bones;
    public BoneRegion Root => Bones.value[0];

    [Serializable]
    public class BoneRegion {
        public BoneBinding Bone;
        public float SelectRadius;
    }
    [Serializable]
    public class BoneBinding {
        public string BonePath;
        [UISetting(Ignore = true)]
        [JsonIgnore]
        [NonSerialized]
        public Transform Transform;
        public float3 position => Transform.position;
        public quaternion rotation => Transform.rotation;

        public void Bind(Transform rig) {
            this.Transform = rig.Find(BonePath);
        }
    }

}