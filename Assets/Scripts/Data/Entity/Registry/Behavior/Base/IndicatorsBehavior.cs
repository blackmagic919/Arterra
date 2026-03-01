using Arterra.Core.Events;
using Arterra.Core.Storage;
using Arterra.Data.Entity;
using Arterra.Data.Entity.Behavior;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
//ToDo: Support multiple paths for animator
public class InidcatorsBehavior : IBehavior {
    [JsonIgnore] public Indicators indicators;
    
    private void OnDamaged(object self, object attacker, object cxt) {
        RefTuple<(float damage, float3 kb)> data = cxt as RefTuple<(float damage, float3 kb)>;
        Indicators.DisplayDamageParticle((self as Entity).position, data.Value.kb);
    }
    public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
        indicators = new Indicators(self.controller.gameObject, self);
        self.eventCtrl.AddEventHandler(GameEvent.Entity_Damaged, OnDamaged);
    }
    public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
        indicators = new Indicators(self.controller.gameObject, self);
        self.eventCtrl.AddEventHandler(GameEvent.Entity_Damaged, OnDamaged);
    }

    public void UpdateController(BehaviorEntity.Animal self, BehaviorEntity.AnimalController controller) => indicators.Update();

    public void OnDrawGizmos(BehaviorEntity.Animal self) {
        // Use golden ratio to spread hues evenly across the spectrum
        float h = (self.info.entityType * 0.618033988749f) % 1f;
        float s = 0.85f;
        float v = 0.95f;
        Gizmos.color = Color.HSVToRGB(h, s, v);
        Gizmos.DrawWireCube(CPUMapManager.GSToWS(self.position), self.settings.collider.size * 2);
    }

    public void Disable(BehaviorEntity.Animal self) {
        indicators.Release();
    }
}
}