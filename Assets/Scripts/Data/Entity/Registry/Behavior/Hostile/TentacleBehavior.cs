using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

using Arterra.Configuration;
using Arterra.Editor;
using Unity.Mathematics;
using System.Linq;
using TerrainCollider = Arterra.GamePlay.Interaction.TerrainCollider;
using Arterra.Core.Storage;
using UnityEngine.Animations.Rigging;
using Arterra.Data.Item;

namespace Arterra.Data.Entity.Behavior
{
	public class TentacleSettings : IBehaviorSetting
	{
		public int MaxGrabTentacles = 2;
		public float GrabBreakDist = 2f;
		public float3 GrabHoldPos = new (0, -10, 0);
		public EntitySMTasks AttackState = EntitySMTasks.AttackTarget;
		public Option<List<TentacleConfig>> Tentacles;

		[Serializable]
		public class TentacleConfig : LeadHeadBehavior.AppendageSettings {
            public float AttackChance = 0.25f;
			public float Length = 5f;
			public float Speed = 10f;
			public float Damage = 10f;
			public float Knockback = 5f;
			public float Proximity = 2f;
            public float RubberBandStrength = 5f;
			public float Acceleration = 200f;
		}

		public object Clone() {
			return new TentacleSettings {
				Tentacles = Tentacles,
			};
		}
	}

	public class TentacleBehavior : IBehavior {
		private TentacleSettings settings;
		private AnimatedBehavior animated;
		private StateMachineManagerBehavior manager;
		[JsonProperty] private Tentacle[] tentacles;
		private IAttackable selfAtk;

		public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
			heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
			heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
		}

		public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
			heirarchy.TryAdd(typeof(TentacleSettings), new TentacleSettings());
		}

		public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
			if (!setting.Is(out settings))
				throw new Exception("Entity: TentacleBehavior requires TentacleSettings");
			if (!self.Is(out selfAtk))
				throw new Exception("Entity: TentacleBehavior requires IAttackable");
			if (!self.Is(out manager))
				throw new Exception("Entity: TentacleBehavior requires StateMachineManager");
			if (!self.Is(out animated)) animated = null;
			SetUp(self);
		}

		public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
			if (!setting.Is(out settings))
				throw new Exception("Entity: TentacleBehavior requires TentacleSettings");
			if (!self.Is(out selfAtk))
				throw new Exception("Entity: TentacleBehavior requires IAttackable");
			if (!self.Is(out manager))
				throw new Exception("Entity: TentacleBehavior requires StateMachineManager");
			if (!self.Is(out animated)) animated = null;
			SetUp(self);
		}

		private void SetUp(BehaviorEntity.Animal self) {
			List<TentacleSettings.TentacleConfig> tSettings = settings.Tentacles.value;
			tentacles ??= new Tentacle[tSettings.Count];
            Transform root = self.controller.gameObject.transform;
			for(int i = 0; i < tSettings.Count; i++) {
				if(tentacles[i] == null) {
					tentacles[i] = new Tentacle();
					tentacles[i].Initialize(self, tSettings[i], root);
				} else tentacles[i].Deserialize(self, tSettings[i], root);
			}
		}

        private void AttackUsingTentacles(BehaviorEntity.Animal self) {
			if (!EntityManager.TryGetEntity(manager.TaskTarget, out Entity target))
				return;
            foreach(Tentacle tentacle in tentacles) {
                tentacle.TryAttack(self, target);
            }
        }

		private bool CanGrabTarget(Entity target) {
			int HoldingTentacles = 0;
			foreach(Tentacle tentacle in tentacles){
				if(tentacle.State == Tentacle.TentacleState.Grabbing)
					HoldingTentacles++;
			}

			if (HoldingTentacles > settings.MaxGrabTentacles) return false;
			return true;
		}

		public void Update(BehaviorEntity.Animal self) {
			if (manager.TaskIndex == settings.AttackState) 
				AttackUsingTentacles(self);
             foreach(Tentacle l in tentacles) {
                if (selfAtk.IsDead) l.Update(self);
                else l.UpdateMovement(self);
            };
		}

		public void UpdateController(BehaviorEntity.Animal self, BehaviorEntity.AnimalController controller) {
			if (controller.gameObject == null) return;
			if (animated == null) return;
			foreach(var c in tentacles) c.UpdateController();
		}

		public void Disable(BehaviorEntity.Animal self) {
			if (tentacles == null) return;
			foreach(Tentacle t in tentacles) t.Disable();
		}

		public void OnDrawGizmos(BehaviorEntity.Animal self) {
            Gizmos.color = Color.green;
            foreach(Tentacle l in tentacles) {
                if (l.State == Tentacle.TentacleState.Attack) Gizmos.color = Color.red;
                else Gizmos.color = Color.green;
                Gizmos.DrawWireCube(CPUMapManager.GSToWS(
                    l.collider.transform.position + l.collider.transform.size/2),
                    l.collider.transform.size * 2);
            }
        }

		private class Tentacle : LeadHeadBehavior.Appendage, IAttackable {
			[JsonIgnore]
			public TentacleSettings.TentacleConfig settings;
            private Transform AnimatedTentacle;
            public TentacleState State;
            private Guid AttackTarget;
			private IAttackable selfAtk;
			private TentacleBehavior tb;
            public enum TentacleState {
                Animated,
                BlendAttack,
                Attack,
                BlendAnimate,
				Grabbing,
            }

			[JsonConstructor]
			public Tentacle() {}

			public bool IsDead => selfAtk.IsDead;
            public void Interact(Entity caller, IItem item = null) => selfAtk.Interact(caller, item);
            public IItem Collect(Entity caller, float collectRate) => selfAtk.Collect(caller, collectRate);
            public bool TakeDamage(float damage, float3 knockback, Entity attacker = null) {
                if (selfAtk.TakeDamage(damage, float3.zero, attacker)) {
                    velocity += knockback;
					if (State != TentacleState.Animated) 
						State = TentacleState.BlendAnimate;
                    return true;
                } return false;
            }
            

			public override void Initialize(BehaviorEntity.Animal self, LeadHeadBehavior.AppendageSettings settings, Transform root) {
				this.settings = settings as TentacleSettings.TentacleConfig;
				this.collider = new (this.settings.collider, self.position + settings.RestOffset) {
                    useGravity = false
                };

                base.Initialize(self, settings, root);
                SetConstraint<ChainIKConstraint>();
                AnimatedTentacle = (constraint as ChainIKConstraint).data.tip;
				visible = true;

                this.State = TentacleState.Animated;
                this.AttackTarget = Guid.Empty;
                constraint.weight = 0;
				active = true;

				if(!self.Is(out selfAtk)) throw new Exception("Entity: Tentacle expected entity to be IAttackable"); 
				if(!self.Is(out tb)) throw new Exception("Entity: Tentacle expected entity to be TentacleBehavior"); 
			}

			public override void Deserialize(BehaviorEntity.Animal self, LeadHeadBehavior.AppendageSettings settings, Transform root) {
                base.Deserialize(self, settings, root);
                SetConstraint<ChainIKConstraint>();
				AnimatedTentacle = (constraint as ChainIKConstraint).data.tip;
				visible = true;

				this.settings = settings as TentacleSettings.TentacleConfig;
                this.State = TentacleState.Animated;
                constraint.weight = 0;
				active = true;

				if(!self.Is(out selfAtk)) throw new Exception("Entity: Tentacle expected entity to be IAttackable"); 
				if(!self.Is(out tb)) throw new Exception("Entity: Tentacle expected entity to be TentacleBehavior"); 
			}


            public override void UpdateController() {
				base.UpdateController();
				if (State == TentacleState.BlendAttack) {
					constraint.weight = math.lerp(constraint.weight, 1, Time.deltaTime * settings.Speed);
					if (math.abs(constraint.weight - 1f) < 0.01f) {
						constraint.weight = 1f;
					} return;
				} else if (State != TentacleState.BlendAnimate && State != TentacleState.Animated)
					return;

                if (State == TentacleState.BlendAnimate) {
					constraint.weight = math.lerp(constraint.weight, 0, Time.deltaTime * settings.Speed);
					if (math.abs(constraint.weight) < 0.01f) {
						constraint.weight = 0f;
						State = TentacleState.Animated;
					} 
                }
				float3 animatedPos = CPUMapManager.WSToGS(AnimatedTentacle.transform.position);
                float3 direction = math.normalizesafe(animatedPos - this.position);
                if(math.dot(collider.transform.velocity, direction) < settings.Speed)
                    collider.transform.velocity += settings.Acceleration * EntityJob.cxt.deltaTime * direction;
			}

            public void Update(BehaviorEntity.Animal self) {
                float3 root = self.position + math.mul(self.Rotation, settings.RestOffset);
                float dist = math.distance(root, collider.transform.position);
                if (dist > settings.Length) { //Rubber banding
                    dist -= settings.Length;
                    float3 dir = math.normalizesafe(root - collider.transform.position);
                    float strength = math.pow(dist, settings.RubberBandStrength);
                    collider.transform.velocity += strength * EntityJob.cxt.deltaTime * dir; 
                }
				base.Update();
            }

            public void UpdateMovement(BehaviorEntity.Animal self) {
                switch(State) {
					case TentacleState.BlendAttack:
						ApproachState(self);
						break;
					case TentacleState.Attack:
						AttackState(self);
						break;
					case TentacleState.Grabbing:
						GrabState(self);
						break;
					default:
						break;
				}

                Update(self);
            }

            public bool TryAttack(BehaviorEntity.Animal self, Entity target) {
                if (State != TentacleState.Animated) return false;
				float chance = settings.AttackChance * EntityJob.cxt.deltaTime;
                if (self.random.NextFloat() > chance) return false;
                if (!IsCloseEnough(target)) return false;
                if (!HasLineOfSight(target)) return false;
                bool HasLineOfSight(Entity target) {
                    float3 origin = collider.transform.position;
                    float3 targetPos = target.position;
                    // Simple raycast for line of sight
                    return !CPUMapManager.RayCastTerrain(origin, math.normalizesafe(targetPos - origin), math.distance(origin, targetPos), CPUMapManager.RayTestSolid, out _);
                }

                bool IsCloseEnough(Entity target) {
                    float3 root = self.position  + math.mul(self.Rotation, settings.RestOffset);
                    float3 targetPos = target.position;
                    return math.distance(root, targetPos) <= settings.Length;
                }

                State = TentacleState.BlendAttack;
                AttackTarget = target.info.rtEntityId;
                return true;
            }


			public void ApproachState(BehaviorEntity.Animal self) {
				if (tb.manager.TaskIndex != tb.settings.AttackState) {
					State = TentacleState.BlendAnimate;
					return;
				}
				
                if (!EntityManager.TryGetEntity(AttackTarget, out Entity target)) {
                    State = TentacleState.BlendAnimate;
                    return;
                };

				float3 origin = collider.transform.position;
				float3 targetPos = target.position;
				float3 dir = math.normalizesafe(targetPos - origin);
				if (math.dot(collider.transform.velocity, dir) < settings.Speed)
					collider.transform.velocity += settings.Acceleration * EntityJob.cxt.deltaTime * dir;
				// Check proximity
				if (ColliderUpdateBehavior.GetColliderDist(collider.transform, target.transform) >
					settings.Proximity) return;

				if (tb.CanGrabTarget(target))  
					State = TentacleState.Grabbing;
				else State = TentacleState.Attack;
			}

			public void AttackState(BehaviorEntity.Animal self) {
				if (!EntityManager.TryGetEntity(AttackTarget, out Entity target)) {
                    State = TentacleState.BlendAnimate;
                    return;
                };
				
				if (ColliderUpdateBehavior.GetColliderDist(collider.transform, target.transform) >
					settings.Proximity) {
						State = TentacleState.BlendAttack;
						return;
				}

				State = TentacleState.BlendAnimate;
                if (!target.Is(out IAttackable atkTarget)) return;
                AttackBehavior.RealAttack(self, atkTarget, settings.Damage, settings.Knockback);
			}

			public void GrabState(BehaviorEntity.Animal self) {
				if (!EntityManager.TryGetEntity(AttackTarget, out Entity target)) {
                    State = TentacleState.BlendAnimate;
                    return;
                };

				if (ColliderUpdateBehavior.GetColliderDist(this, target) > tb.settings.GrabBreakDist){
					State = TentacleState.BlendAnimate;
					return;
				}

				float3 moveTarget = self.position + math.mul(self.Rotation, tb.settings.GrabHoldPos);
				float3 direction = math.normalizesafe(moveTarget - this.position);
                if(math.dot(collider.transform.velocity, direction) < settings.Speed)
                    collider.transform.velocity += settings.Acceleration * EntityJob.cxt.deltaTime * direction;

				float3 dir = math.normalizesafe(this.position - target.position);
				target.transform.velocity += EntityJob.cxt.deltaTime * settings.Acceleration * dir;
			}
		}
	}
}
