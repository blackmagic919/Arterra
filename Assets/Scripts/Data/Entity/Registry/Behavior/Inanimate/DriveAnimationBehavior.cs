using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class DriveAnimationSettings : IBehaviorSetting {
        public string AnimatorPath = "";
        public string MovingParameter = "Moving";
        public string SteeringParameter = "Steering";
        public float MinSpeed = 0.5f;
        public float MinSteeringAngle = -90f;
        public float MaxSteeringAngle = 90f;

        public object Clone() => MemberwiseClone();
    }

    public class DriveAnimationBehavior : SpeciesBehavior {
        private DriveAnimationSettings settings;
        private ContactDriveBehavior drive;
        private Animator animator;

        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> hierarchy) =>
            hierarchy.TryAdd(Behaviors.ContactDrive, hierarchy.Count);

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> hierarchy) =>
            hierarchy.TryAdd(typeof(DriveAnimationSettings), new DriveAnimationSettings());

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 coord) =>
            Bind(self, setting);

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 coord) =>
            Bind(self, setting);

        private void Bind(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting) {
            if (!setting.Is(out settings) || !self.Is(out drive))
                throw new InvalidOperationException("DriveAnimation requires DriveAnimationSettings and ContactDrive.");
            Transform root = string.IsNullOrEmpty(settings.AnimatorPath)
                ? self.controller.transform : self.controller.transform.Find(settings.AnimatorPath);
            animator = root == null ? null : root.GetComponent<Animator>();
            if (animator == null)
                throw new InvalidOperationException("DriveAnimation requires an Animator at AnimatorPath.");
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (!self.ShouldUpdateAnimations()) return;
            bool moving = math.lengthsq(self.velocity.xz) >= settings.MinSpeed * settings.MinSpeed;
            animator.SetBool(settings.MovingParameter, moving);
            if (moving)
                animator.SetFloat(settings.SteeringParameter,
                    Mathf.InverseLerp(settings.MinSteeringAngle, settings.MaxSteeringAngle, drive.SteeringAngle));
        }
    }
}
