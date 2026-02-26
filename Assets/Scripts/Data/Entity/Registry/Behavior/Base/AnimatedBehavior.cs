using Arterra.Core.Storage;
using Arterra.Data.Entity.Behavior;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
//ToDo: Support multiple paths for animator
public class AnimatedBehavior : IBehavior {
    [JsonIgnore] public Animator animator;
    
    public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
        animator = self.controller.gameObject.GetComponent<Animator>();
        self.Register(this);
    }
    public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
        animator = self.controller.gameObject.GetComponent<Animator>();
        self.Register(this);
    }

    public void SetBool(string name, bool value) => animator.SetBool(name, value);
    public void SetTrigger(string name) => animator.SetTrigger(name);
}
}