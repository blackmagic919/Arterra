using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Configuration.Gameplay.Player;
using Arterra.Data.Entity;
using Arterra.Data.Entity.Behavior;
using Unity.Mathematics;

public class MMove : IBehaviorSetting {
    public Option<List<StateMovement>> movements;
    public Option<List<SubProfiles>> profiles;
    public Option<List<Genetics.GeneFeature>> customSpeeds;

    private Dictionary<EntitySMTasks, StateMovement> _movements;
    private List<EntitySetting.ProfileInfo> _profiles;
    [Serializable]
    public struct StateMovement {
        public EntitySMTasks state;
        public int ProfileIndex;
        public bool allow3DRot;
        public bool useGravity;
        public ToggleField<int> CustomSpeed;
    }

    public void OnValidate(BehaviorEntity.AnimalSetting settings) {
        profiles.value[0] = new SubProfiles{
            bounds = settings.profile.bounds,
            offset = 0
        };
    }

    [Serializable]
    public struct SubProfiles {
        public uint3 bounds;
        public uint offset;
    }

    public object Clone() {
        return new MMove {
            movements = movements,
            profiles = profiles
        };
    }

    public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
        _profiles = new();
        profiles.value ??= new ();
        foreach(SubProfiles profile in profiles.value) {
            _profiles.Add(new EntitySetting.ProfileInfo {
                bounds = profile.bounds,
                profileStart = setting.profile.profileStart + profile.offset
            });
        }

        _movements = new ();
        movements.value ??= new ();
        for(int i = 0; i < movements.value.Count; i++) {
            StateMovement mv = movements.value[i];
            _movements.Add(mv.state, mv);
        }

        customSpeeds.value ??= new();
        for (int i = 0; i < customSpeeds.value.Count; i++) {
            var geneFeature = customSpeeds.value[i];
            Genetics.AddGene(entityType, ref geneFeature);
            customSpeeds.value[i] = geneFeature;
        }
    }

    public static bool UseGravity(MMove instance, EntitySMTasks state) {
        if (instance == null) return true;
        if (!instance._movements.TryGetValue(state, out StateMovement sm))
            return true;
        return sm.useGravity;
    }

    public static bool Allow3DRot(MMove instance, EntitySMTasks state) {
        if (instance == null) return false;
        if (!instance._movements.TryGetValue(state, out StateMovement sm))
            return false;
        return sm.allow3DRot;
    }

    public static float Speed(MMove instance, EntitySMTasks state, Genetics genes, Genetics.GeneFeature baseValue) {
        if (instance == null) return genes.Get(baseValue);
        if (!instance._movements.TryGetValue(state, out StateMovement sm))
            return genes.Get(baseValue);
        if (!sm.CustomSpeed.enabled) return genes.Get(baseValue);
        return genes.Get(instance.customSpeeds.value[sm.CustomSpeed.value]);
    }

    public static EntitySetting.ProfileInfo Profile(MMove instance, EntitySMTasks state, EntitySetting settings) {
        if (instance == null) return settings.profile;
        if (!instance._movements.TryGetValue(state, out StateMovement sm))
            return settings.profile;
        return instance._profiles[sm.ProfileIndex];
    }
}