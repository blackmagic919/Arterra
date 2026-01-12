using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Configuration.Generation.Entity;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Experimental {
public class BehaviorEntity : Authoring {
    public Option<BehaviorSettings> _Setting;
    
    [JsonIgnore]
    public override Entity Entity { get => new Instance(); }
    [JsonIgnore]
    public override EntitySetting Setting { get => _Setting.value; set => _Setting.value = (BehaviorSettings)value; }
    [Serializable]
    public class BehaviorSettings : EntitySetting {
        public List<BehaviorConfig> Behaviors;
        public override void Preset(uint entityType) {
            GameObject controller = Config.CURRENT.Generation.Entities.Retrieve((int)entityType).Controller;
            foreach(BehaviorConfig behavior in Behaviors) {
                behavior.Preset(entityType);

                //Make sure controller has component
                if (!behavior.Behavior.IsAssignableFrom(typeof(EntityBehavior)))
                    throw new Exception("Behavior Instnace must Inherit EntityBehavior");
                if (controller.GetComponent(behavior.Behavior) != null) continue;
                controller.AddComponent(behavior.Behavior);
            }
        }
    }

    public class Instance : Entity{
        private EntityBehavior[] behaviors;
        private IEntityTransform tsf;
        [JsonProperty]
        private GameObject controller;
        public override ref TerrainCollider.Transform transform => ref tsf.transform;

        /// <summary> The entity's virtual update function. This will be called every game tick within a Unity Job to 
        /// perform computational-heavy tasks related to the entity. Only the Entity in question is provided mutual
        /// exclusivity. Accessing any external resources(e.g. creating an entity) needs to resynchronize using <see cref="EntityManager.AddHandlerEvent(Action)"/>
        /// </summary>
        public override void Update() {
            foreach(var behavior in behaviors) {
                behavior.Update(this);
            }
        }
        /// <summary> A callback when the entity is disabled. This will be called whenever the system attempts to destroy an 
        /// entity and should be used to release any resources tied with it. An entity should assume it is destroyed after 
        /// processing this callback. </summary> 
        public override void Disable() {
            foreach(var behavior in behaviors) {
                behavior.Disable(this);
            }
        }
        /// <summary>
        /// Initializes the entity's instance. Called when creating an instance of the entity.
        /// The callee may preset any default values during this process but it <b>must</b> guarantee
        /// that the entity returned is fully populated (i.e. virtual functions all set).
        /// </summary>
        /// <param name="setting">The setting of the entity. Specific to the authoring entry it's instantiated from. </param>
        /// <param name="controller">The controller responsible for displaying the entity. Passed from <see cref="Authoring.Controller"/> </param>
        /// <param name="GCoord">The position in grid space the entity was placed at. </param>
        public override void Initialize(EntitySetting setting, GameObject controller, float3 GCoord) {
            this.controller = controller;
            behaviors = controller.GetComponents<EntityBehavior>();
            Interfaces = new Dictionary<Type, object>();
            behaviors ??= new EntityBehavior[0];

            BehaviorSettings settings = (BehaviorSettings)setting;
            var behaviorMap = new Dictionary<Type, EntityBehavior>(behaviors.Length);
            foreach (var b in behaviors) behaviorMap[b.GetType()] = b;

            foreach(var behavior in settings.Behaviors) {
                EntityBehavior component = behaviorMap[behavior.Behavior];
                component.Initialize(behavior, this, GCoord);
            } 

            if (Is(out tsf)) return;
            throw new Exception("Entity Must Have Component Which Provides Transform");
        }
        /// <summary>
        /// Deserializes the entity's instance. Some of the entity's information may be retrieved from serialization
        /// while others may need to be thrown away. This function is called when the entity is deserialized 
        /// in case the entity needs to reframe its information.
        /// </summary>
        /// <param name="setting">The setting of the entity. Specific to the authoring entry it's instantiated from. </param>
        ///  <param name="controller">The controller responsible for displaying the entity. Passed from <see cref="Authoring.Controller"/> </param>
        /// <param name="GCoord">The position in grid space the entity was placed at. </param>
        public override void Deserialize(EntitySetting setting, GameObject controller, out int3 GCoord) {
            this.controller = controller;
            behaviors = controller.GetComponents<EntityBehavior>();
            Interfaces = new Dictionary<Type, object>();
            behaviors ??= new EntityBehavior[0];
            BehaviorSettings settings = (BehaviorSettings)setting;
            var behaviorMap = new Dictionary<Type, EntityBehavior>(behaviors.Length);
            foreach (var b in behaviors) behaviorMap[b.GetType()] = b;

            foreach(var behavior in settings.Behaviors) {
                EntityBehavior component = behaviorMap[behavior.Behavior];
                component.Deserialize(behavior, this);
            } 
            
            GCoord = (int3)origin;
            if (Is(out tsf)) return;
            throw new Exception("Entity Must Have Component Which Provides Transform");
        }

        public Dictionary<Type, object> Interfaces;
        public void RegisterInterface(Type type, object instance) {
            Interfaces[type] = instance;
        }
        
        public bool Is<T>(out T instance) {
            instance = default;
            if(!Interfaces.TryGetValue(typeof(T), out var value))
                return false;
            instance = (T)value;
            return true;
        }
    }

    public interface IEntityTransform {
        /// <summary> The transform of the entity used for positioning and collision detection. </summary>
        [JsonIgnore]
        public abstract ref TerrainCollider.Transform transform { get; }
    }

    public abstract class BehaviorConfig {
        public abstract Type Behavior { get; }
        public virtual void Preset(uint entityInd) {}
    }

    public abstract class EntityBehavior : MonoBehaviour {
        public abstract void Initialize(BehaviorConfig config, Instance entity, float3 GCoord);
        public abstract void Deserialize(BehaviorConfig config, Instance entity);
        public virtual void Update(Instance entity){}
        public virtual void Disable(Instance entity){}
    }
}
}