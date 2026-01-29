using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Configuration.Generation.Entity;
using Arterra.Configuration.Quality;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public class ProceduralAnimated {
    public abstract class AppendageSettings {
        public TerrainCollider.Settings collider;
        //The offset relative to the center of the body collider
        // of the center of the appendge collider
        public float3 RestOffset;
        public float3 BoneOffset;
        public string BonePath;
    }
    public abstract class Appendage {
        [JsonIgnore]
        public abstract TerrainCollider collider{get;}
        [JsonIgnore]
        public virtual float3 desiredBody {get => default;}
        [JsonConstructor]
        public Appendage() {} //Newtonsoft path
        public abstract void Initialize(Entity animal, AppendageSettings settings);
        public abstract void Deserialize(Entity animal, AppendageSettings settings); 
        public abstract void Update();
        public abstract void UpdateMovement();
        public abstract void Disable();
    }

    [Serializable]
    public class HeadInfo {
        public float3 Offset;
        public TerrainCollider.Settings Collider;
        public float RubberbandStrength = 10.0f;
    }
    public abstract class PASettings {
        public virtual List<AppendageSettings> Appendages{get;}
        public Option<HeadInfo> Head;
        public float RubberbandStrength = 1.0f;
        [UISetting(Ignore = true)]
        public List<AnimOverride> AnimControl;
        [UISetting(Ignore = true)][JsonIgnore]
        public Dictionary<int, uint> AnimToggle;
        public bool OverrideAnimByDefault = false;
        public bool BodyUseGravity = true;

        public void Preset() {
            AnimToggle = new Dictionary<int, uint>();
            foreach(AnimOverride anim in AnimControl) {
                int hash = Animator.StringToHash(anim.AnimName);
                AnimToggle.TryAdd(hash, anim.LegControlBitmap);
            }
        }
    }

    [Serializable]
    public struct AnimOverride {
        public string AnimName;
        public uint LegControlBitmap;
    }


    //METHODOLOGY:
    //Appendages Follow (Invisible) Body
    //Head Follow Appendages
    //Head is Main Entity Transform


    //Ignored Members
    [JsonIgnore]
    protected Entity self;
    [JsonIgnore]
    protected IAttackable selfAtk;
    [JsonIgnore]
    protected PASettings settings;
    [JsonIgnore]
    public float3 BodyPosition {
        get => Body.transform.position + Body.transform.size / 2;
        set => Body.transform.position = value - Body.transform.size / 2;
    }
    [JsonIgnore]
    public float3 BodyOrigin {
        get => Body.transform.position;
        set => Body.transform.position = value;
    }
    [JsonIgnore]
    public int3 BodyGCoord => (int3)math.floor(BodyOrigin);

    [JsonIgnore]
    public float3 HeadPosition {
        get => Head.transform.position + Head.transform.size / 2;
        set => Head.transform.position = value - Head.transform.size / 2;
    }
    [JsonIgnore]
    public float3 HeadOrigin {
        get => Head.transform.position;
        set => Head.transform.position = value;
    }

    //Saved Members
    public Appendage[] appendages;
    public TerrainCollider Head;
    public TerrainCollider Body;

    public virtual void Initialize<T>(
        Entity entity, PASettings settings, 
        float3 GCoord, TerrainCollider.Settings RootCollider,
        Action<float> ProcessFallDamage
    ) where T : Appendage, new() {
        self = entity;
        selfAtk = entity as IAttackable;
        this.settings = settings;
        List<AppendageSettings> AppendageSettings = settings.Appendages;
        appendages ??= new T[AppendageSettings.Count];
        this.Body = new TerrainCollider(RootCollider, GCoord, ProcessFallDamage);
        this.Head = new TerrainCollider(settings.Head.value.Collider, GCoord + settings.Head.value.Offset);
        Body.useGravity = settings.BodyUseGravity;
        for(int i = 0; i < AppendageSettings.Count; i++){
            if(appendages[i] == null) {
                appendages[i] = new T();
                appendages[i].Initialize(entity, AppendageSettings[i]);
            } else appendages[i].Deserialize(entity, AppendageSettings[i]);
        }
    }

    public virtual void Deserialize<T>(
        Entity entity, PASettings settings, 
        Action<float> ProcessFallDamage
    ) where T : Appendage, new() {
        self = entity;
        selfAtk = entity as IAttackable;
        this.settings = settings;
        Body.OnHitGround = ProcessFallDamage;

        List<AppendageSettings> AppendageSettings = settings.Appendages;
        appendages ??= new T[AppendageSettings.Count];
        for(int i = 0; i < AppendageSettings.Count; i++){
            if(appendages[i] == null) {
                appendages[i] = new T();
                appendages[i].Initialize(entity, AppendageSettings[i]);
            } else appendages[i].Deserialize(entity, AppendageSettings[i]);
        }
    }

    public virtual void Update() {
        foreach(Appendage l in appendages) {
            if (selfAtk.IsDead) l.Update();
            else l.UpdateMovement();

            float dist = math.distance(l.desiredBody, BodyPosition);
            //Apply weaker rubber banding to invisible body
            if (dist > l.collider.transform.size.y) { 
                dist -= l.collider.transform.size.y;
                float3 dir = math.normalizesafe(l.desiredBody - BodyPosition) * settings.RubberbandStrength;
                Body.transform.velocity += dist * EntityJob.cxt.deltaTime * dir;
            }
            
            float3 desiredHead = l.desiredBody + settings.Head.value.Offset;
            Head.transform.velocity += EntityJob.cxt.deltaTime * (desiredHead - HeadPosition) * settings.Head.value.RubberbandStrength;
        };

        Head.transform.rotation = Body.transform.rotation;
        Head.useGravity = false; //Head doesn't use gravity
        Head.Update(self);
        Body.Update();
    }

    public virtual void Disable() {
        if (appendages == null) return;
        foreach(Appendage l in appendages) l.Disable();
    }

    public class AnimalController<TConstraint> where TConstraint : IRigConstraint {
        protected Animator animator;
        protected GameObject gameObject;
        protected GameObject root;
        protected Transform transform;
        protected AppendageController[] appendages;
        protected PASettings settings;
        private uint LastOverrideState;

        public AnimalController(GameObject controller,  Appendage[] appendages, PASettings settings) {
                this.appendages = new AppendageController[appendages.Length];
                this.settings = settings;

                this.root = GameObject.Instantiate(controller);
                this.transform = root.transform;
                this.gameObject = transform.GetChild(0).gameObject;
                this.animator = gameObject.transform.GetComponent<Animator>();
                this.LastOverrideState = 0;

                for(int i = 0; i < appendages.Length; i++) {
                    this.appendages[i] = new AppendageController(appendages[i],
                        settings.Appendages[i],
                        gameObject.transform
                    );
                }
        }

        public virtual void Update() {
            if (gameObject == null) return;
            foreach(AppendageController c in appendages) c.Update();
            int hash = animator.GetCurrentAnimatorStateInfo(0).shortNameHash; 
            if(!settings.AnimToggle.TryGetValue(hash, out uint bitmap))
                bitmap = settings.OverrideAnimByDefault ? 0xFFFFFFFF : 0;
            if (bitmap != LastOverrideState) {
                LastOverrideState = bitmap;
                for(int i = 0; i < appendages.Length; i++) {
                    appendages[i].SetActive(((bitmap >> i) & 0x1) != 0);
                }
            }
        }

        public class AppendageController {
            public Appendage Apdg;
            public AppendageSettings settings;
            private Transform Controller;
            private TConstraint contraint;
            private bool active;
            public AppendageController(Appendage apdg, AppendageSettings settings, Transform root) {
                this.Apdg = apdg;
                this.settings = settings;
                this.active = false;
                this.Controller = root.Find(settings.BonePath);
                if (Controller == null) Debug.Log(settings.BonePath);
                this.contraint = Controller.transform.parent.GetComponent<TConstraint>();
            }
            public void Update() {
                if (!active) return;
                float3 BonePos = Apdg.collider.transform.position + settings.BoneOffset;
                Controller.SetPositionAndRotation(Arterra.Core.Storage.CPUMapManager.GSToWS(BonePos), Apdg.collider.transform.rotation * Quaternion.Euler(-90f, 0f, 0f));
            }

            public void SetActive(bool enabled) {
                if (active != enabled) {
                    active = enabled;
                    contraint.weight = active ? 1 : 0;
                } if (!active) return;
            }
        }
    }
}