using Unity.Mathematics;
using Newtonsoft.Json;
using System.Collections.Generic;
using System;
using UnityEngine;
using System.Linq;

namespace Arterra.Data.Entity.Behavior {

    public class Modifier : SpeciesBehavior {
        public const int Base = 0;
        public const int Physicality = 1000;
        public const int Decision = 2000;
        public const int Mobility = 3000;
        public const int Interaction = 4000;
        public const int Effects = 5000;
        public const int Player = 6000;
        public const int GroupStep = -65536;

        public Dictionary<MSettings, List<SettingModifier>>  ModifiedSettings;
        [JsonIgnore]
        private Dictionary<Guid, SettingModifier> ModifierIndex;
        [JsonIgnore]
        public Genetics genes;

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!self.Is(out genes)) genes = null;
            ModifiedSettings = new Dictionary<MSettings, List<SettingModifier>>();
            ModifierIndex = new Dictionary<Guid, SettingModifier>();
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!self.Is(out genes)) genes = null;
            ModifiedSettings ??= new Dictionary<MSettings, List<SettingModifier>>();
            ModifierIndex = new Dictionary<Guid, SettingModifier>();
            foreach (var pair in ModifiedSettings) {
                if (pair.Value == null) continue;
                foreach (SettingModifier modifier in pair.Value) {
                    if (modifier == null) continue;
                    if (modifier.Id == Guid.Empty)
                        modifier._Create();
                    ModifierIndex[modifier.Id] = modifier;
                }
            }
        }

        public static float Get(Modifier self, MSettings name, float defaultValue) {
            float value = defaultValue;
            if (self == null) return value;
            if (self.genes != null) value = self.genes.Get(name, value);
            return self.ApplyModifiedSettings(name, value);
        }

        public static int GetInt(Modifier self, MSettings name, float defaultValue) {
            float value = defaultValue;
            if (self == null) return Mathf.RoundToInt(defaultValue);
            if (self.genes != null) value = self.genes.Get(name, value);
            return Mathf.RoundToInt(self.ApplyModifiedSettings(name, value));
        }

        private float ApplyModifiedSettings(MSettings name, float value) {
            if (!ModifiedSettings.TryGetValue(name, out var mList)) return value;
            foreach (var modifier in mList) value = modifier.ApplyModiifer(name, value);
            return value;
        }

        public void ApplyModifier(MSettings name, SettingModifier modifier) {
            modifier._Create();
            ModifierIndex[modifier.Id] = modifier;
            if(!ModifiedSettings.TryGetValue(name, out List<SettingModifier> mList)) 
                ModifiedSettings[name] = new List<SettingModifier>{ modifier };
            else {
                mList.Add(modifier); mList.Sort((a, b) => a.type.CompareTo(b.type));
            } 
        }

        public void RemoveModifier(MSettings name, Guid Id) {
            if(!ModifiedSettings.TryGetValue(name, out List<SettingModifier> mList)) 
                return;
            mList = mList.Where(m => m.Id != Id).ToList();
            if (mList.Count == 0) ModifiedSettings.Remove(name);
        }

        public bool TryGetModifier(Guid Id, out SettingModifier m) {
            m = null; return ModifierIndex?.TryGetValue(Id, out m) ?? false;
        }
    }

    public enum MSettings {
        AttackDistance = Modifier.Physicality + 0,
        AttackDamage = Modifier.Physicality + 1,
        AttackCooldown = Modifier.Physicality + 2,
        KBStrength = Modifier.Physicality + 3,

        MaxHealth = Modifier.Physicality + 4,
        HoldBreathTime = Modifier.Physicality + 5,
        NaturalRegen = Modifier.Physicality + 6,
        InvincTime = Modifier.Physicality + 7,

        WalkSpeed = Modifier.Physicality + 8,
        RunSpeed = Modifier.Physicality + 9,

        BlindDist = Modifier.Physicality + 10,
        ChargeTime = Modifier.Physicality + 11,
        DecompositionTime = Modifier.Physicality + 12,

        DryOutTime = Modifier.Physicality + 13,
        FlopStrength = Modifier.Physicality + 14,
        StarveRate = Modifier.Physicality + 15,


        CallFriendRadius = Modifier.Decision + 0,
        HelpFriendAffection = Modifier.Decision + 1,
        HuntThreshold = Modifier.Decision + 2,
        StopHuntThreshold = Modifier.Decision + 3,

        OnFedAffection = Modifier.Decision + 4,
        OnProtectAffection = Modifier.Decision + 5,
        OnSaveAffection = Modifier.Decision + 6,
        OnMateAffection = Modifier.Decision + 7,
        OnBetrayalAffection = Modifier.Decision + 8,
        OnRivalMateAffection = Modifier.Decision + 9,
        OnAttackAffection = Modifier.Decision + 10,
        ForgetFalloff = Modifier.Decision + 11,
        BaseForgetRate = Modifier.Decision + 12,
        GossipCooldown = Modifier.Decision + 13,
        GossipRadius = Modifier.Decision + 14,
        GossipCloseness = Modifier.Decision + 15,
        GossipStrength = Modifier.Decision + 16,
        GossipAmount = Modifier.Decision + 17,

        AverageFlightTime = Modifier.Decision + 18,
        AverageFlightVariance = Modifier.Decision + 19,
        SeperationWeight = Modifier.Decision + 20,
        AlignmentWeight = Modifier.Decision + 21,
        CohesionWeight = Modifier.Decision + 22,
        BoidSightDistance = Modifier.Decision + 23,

        BurrowMinDist = Modifier.Decision + 24,
        BurrowMaxDist = Modifier.Decision + 25,
        UnburrowThresh = Modifier.Decision + 26,
        DigDist = Modifier.Decision + 27,


        SearchDistance = Modifier.Decision + 28,
        SearchChance = Modifier.Decision + 29,
        SearchEnemyDist = Modifier.Decision + 30,
        ChaseDistance = Modifier.Decision + 31,

        SearchFriendDist = Modifier.Decision + 32,
        ChaseFriendProbability = Modifier.Decision + 33,
        FightEnemyAffection = Modifier.Decision + 34,

        MateThreshold = Modifier.Decision + 35,
        MateSearchDist = Modifier.Decision + 36,
        SearchPlantDist = Modifier.Decision + 37,
        SearchPreyDist = Modifier.Decision + 38,

        AverageIdleTime = Modifier.Decision + 39,
        AverageIdleVariance = Modifier.Decision + 40,
        LandAngleMax = Modifier.Decision + 41,
        LandAngleFalloff = Modifier.Decision + 42,
        VerticalFlightFreedom = Modifier.Decision + 43,

        AverageWalkTime = Modifier.Decision + 44,
        AverageWalkVariance = Modifier.Decision + 45,
        AllowRideAffinity = Modifier.Decision + 46,
        
        SurfaceThresh = Modifier.Decision + 47,
        SurfaceAngleMax = Modifier.Decision + 48,
        SurfaceAngleFalloff = Modifier.Decision + 49,


        Nutrition = Modifier.Interaction + 0,
        MateCost = Modifier.Interaction + 1,
        AmountPerParent = Modifier.Interaction + 2,
        DropAmount = Modifier.Interaction + 3,

        Inflict_PoisonStrength = Modifier.Effects + 0,
        Recieve_PoisonStrength = Modifier.Effects + 1, 
        Inflict_PoisonDuration = Modifier.Effects + 2,
        Recieve_PoisonDuration = Modifier.Effects + 3, 
        Inflict_BleedingStrength = Modifier.Effects + 4,
        Recieve_BleedingStrength = Modifier.Effects + 5, 
        Inflict_BleedingDuration = Modifier.Effects + 6,
        Recieve_BleedingDuration = Modifier.Effects + 7, 
        Inflict_NauseaStrength = Modifier.Effects + 8,
        Recieve_NauseaStrength = Modifier.Effects + 9,
        Inflict_NauseaDuration = Modifier.Effects + 10,
        Recieve_NauseaDuration = Modifier.Effects + 11,
        Inflict_DizzinessStrength = Modifier.Effects + 12,
        Recieve_DizzinessStrength = Modifier.Effects + 13,
        Inflict_DizzinessDuration = Modifier.Effects + 14,
        Recieve_DizzinessDuration = Modifier.Effects + 15,
        Inflict_BlindnessStrength = Modifier.Effects + 16,
        Recieve_BlindnessStrength = Modifier.Effects + 17,
        Inflict_BlindnessDuration = Modifier.Effects + 18,
        Recieve_BlindnessDuration = Modifier.Effects + 19,
        Inflict_BurningStrength = Modifier.Effects + 20,
        Recieve_BurningStrength = Modifier.Effects + 21,
        Inflict_BurningDuration = Modifier.Effects + 22,
        Recieve_BurningDuration = Modifier.Effects + 23,

        CameraSensitivity = Modifier.Player + 0,
        MinimumX = Modifier.Player + 1,
        MaximumX = Modifier.Player + 2,
        TerraformRadius = Modifier.Player + 3,
        ReachDistance = Modifier.Player + 4,
        CylinderRadius = Modifier.Player + 5,
        PickupRate = Modifier.Player + 6,
        RecoverRate = Modifier.Player + 7,
        FlightSpeedMult = Modifier.Player + 8,
        JumpForce = Modifier.Player + 9,
        AccelTime = Modifier.Player + 10,
    }

    public class SettingModifier {
        private Guid id;
        public MType type;
        public float value;
        public enum MType {
            Set = 0, //Defining order of application
            Multiply = 1,
            Add = 2, 
            Override = 3,
        }

        public void _Create() => id = Guid.NewGuid();
        public Guid Id => id;

        public float ApplyModiifer(MSettings name, float input) {
            if (type == MType.Add) return input + this.value;
            if (type == MType.Multiply) return input * this.value;
            else input = this.value;
            return input;
        }
    }

}