using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using WorldConfig;
using WorldConfig.Generation.Entity;
using static EntityManager.STree;


public interface IMateable{
    public void MateWith(Entity entity);
    public bool CanMateWith(Entity entity);
}

[Serializable]
public struct Recognition{
    //There is no explicit order with predators, an entity will run
    //from the closest predator to it.
    public Option<List<string>> Predators;
    //The order of the list describes the order of preference for the entity
    //An entity won't chase a prey if a more preferred prey is in range
    public Option<List<string>> Prey;
    //Mates are entities that can breed with the entity, and the offspring they create
    public Option<List<Mate>> Mates;
    //Edible items produced by entities
    public Option<List<Consumable>> Edibles;
    public int SightDistance;
    public int FleeDistance;

    [UISetting(Ignore = true)][JsonIgnore][HideInInspector]
    internal Dictionary<int, Recognizable> AwarenessTable;

    public void Construct(){
        AwarenessTable = new Dictionary<int, Recognizable>();
        Registry<Authoring> eReg = Config.CURRENT.Generation.Entities;
        Registry<WorldConfig.Generation.Item.Authoring> iReg = Config.CURRENT.Generation.Items;

        if(Predators.value != null){
        for(int i = 0; i < Predators.value.Count; i++){
            int entityIndex = eReg.RetrieveIndex(Predators.value[i]);
            AwarenessTable.TryAdd(entityIndex, new Recognizable(i, 1));
        }} if(Prey.value != null) {
        for(int i = 0; i < Prey.value.Count; i++){
            int entityIndex = eReg.RetrieveIndex(Prey.value[i]);
            AwarenessTable.TryAdd(entityIndex,  new Recognizable(i, 2));
        }} if(Mates.value != null) { 
        for(int i = 0; i < Mates.value.Count; i++){
            int entityIndex = eReg.RetrieveIndex(Mates.value[i].MateType);
            AwarenessTable.TryAdd(entityIndex, new Recognizable(i, 3));
        }} if(Edibles.value != null) {
        for(int i = 0; i < Edibles.value.Count; i++){
            int entityIndex = iReg.RetrieveIndex(Edibles.value[i].EdibleType);
            //negative so it doesn't conflict with entity indexes
            AwarenessTable.TryAdd(-entityIndex, new Recognizable(i, 0)); 
        }}
    }

    [Serializable]
    public struct Mate{
        public string MateType;
        public string ChildType;
        public float AmountPerParent;
    }
    [Serializable]
    public struct Consumable{
        public string EdibleType;
        public float Nutrition;
    }

    [Serializable]

    internal struct Recognizable{
        public uint data;
        public int Preference{
            readonly get => (int)(data & 0x3FFFFFF);
            set => data = (data & 0xC0000000) | ((uint)value & 0x3FFFFFF);
        }
        public bool IsPredator{
            readonly get => ((data >> 30) & 0x3) == 1;
            set => data = (data & 0x3FFFFFFF) | (uint)(value ? 0x10000000 : 0);
        }
        public bool IsPrey{
            readonly get => ((data >> 30) & 0x3) == 2;
            set => data = (data & 0x3FFFFFFF) | (uint)(value ? 0x20000000 : 0);
        }
        public bool IsMate{
            readonly get => ((data >> 30) & 0x3) == 3;
            set => data = (data & 0x3FFFFFFF) | (uint)(value ? 0x30000000 : 0);
        }
        
        public Recognizable(int Preference, uint type){
            data = (uint)(Preference & 0x3FFFFFFF) | ((type & 0x3) << 30) ;
        }
    }

    public bool FindClosestPredator(Entity self, out Entity entity){
        entity = null; if(AwarenessTable == null) return false;
        if(Predators.value == null || Predators.value.Count == 0) return false;

        int3 ePos = (int3)math.floor(self.position);
        Entity cEntity = null; float closestDist = SightDistance + 1;
        Dictionary<int, Recognition.Recognizable> Awareness = AwarenessTable;
        TreeNode.Bounds bounds = new (){
            Min = ePos - new int3(SightDistance),
            Max = ePos + new int3(SightDistance)
        };
        EntityManager.ESTree.Query(bounds, (Entity nEntity) => {
            if(nEntity == null) return;
            if(nEntity.info.entityId == self.info.entityId) return;
            if(!Awareness.ContainsKey((int)nEntity.info.entityType)) return;

            Recognizable entityInfo = Awareness[(int)nEntity.info.entityType];
            if(!entityInfo.IsPredator) return;
            float dist = math.distance(ePos, nEntity.position);
            if(dist >= closestDist) return;
            cEntity = nEntity;
            closestDist = dist;
        });
        entity = cEntity;
        return entity != null;
    }

    //Finds most preferred it can see, then the closest one it prefers
    public bool FindPreferredPrey(Entity self, out Entity entity){
        entity = null; if(AwarenessTable == null) return false;
        if(Prey.value == null || Prey.value.Count == 0) return false;

        Entity cEntity = null; int pPref = -1;
        float closestDist = SightDistance + 1;

        int3 ePos = (int3)math.floor(self.position);
        Dictionary<int, Recognizable> Awareness = AwarenessTable;
        TreeNode.Bounds bounds = new (){
            Min = ePos - new int3(SightDistance),
            Max = ePos + new int3(SightDistance)
        };
        EntityManager.ESTree.Query(bounds, (Entity nEntity) => {
            if(nEntity == null) return;
            if(nEntity.info.entityId == self.info.entityId) return;
            if(!Awareness.ContainsKey((int)nEntity.info.entityType)) return;

            Recognizable entityInfo = Awareness[(int)nEntity.info.entityType];
            if(!entityInfo.IsPrey) return;
            if(cEntity != null){
            if(entityInfo.Preference > pPref) return;
            if(pPref == entityInfo.Preference && math.distance(nEntity.position, ePos) > closestDist) 
                return;
            }
            
            cEntity = nEntity;
            pPref = entityInfo.Preference;
            closestDist = math.distance(nEntity.position, ePos);
        });
        entity = cEntity;
        return entity != null;
    }

    //Finds the most preferred mate it can see, then the closest one it prefers
    public bool FindPreferredMate(Entity self, out Entity entity){
        entity = null; if(AwarenessTable == null) return false; 
        if(Mates.value == null || Mates.value.Count == 0) return false;

        Entity cEntity = null; int pPref = -1;
        float closestDist = SightDistance + 1;

        int3 ePos = (int3)math.floor(self.position);
        Dictionary<int, Recognizable> Awareness = AwarenessTable;
        TreeNode.Bounds bounds = new (){
            Min = ePos - new int3(SightDistance),
            Max = ePos + new int3(SightDistance)
        };
        EntityManager.ESTree.Query(bounds, (Entity nEntity) => {
            if(nEntity == null) return;
            if(!Awareness.ContainsKey((int)nEntity.info.entityType)) return;
            if(nEntity.info.entityId == self.info.entityId) return;

            Recognizable entityInfo = Awareness[(int)nEntity.info.entityType];
            if(!entityInfo.IsMate) return;
            if(nEntity is not IMateable) return;
            if(!(nEntity as IMateable).CanMateWith(self)) return;
            if(cEntity != null){
            if(entityInfo.Preference > pPref) return;
            if(pPref == entityInfo.Preference && math.distance(
                nEntity.position, self.position) > closestDist) 
                return;
            } 
            
            cEntity = nEntity;
            pPref = entityInfo.Preference;
            closestDist = math.distance(nEntity.position, self.position);
        });
        entity = cEntity;
        return entity != null;
    }


    public bool MateWithEntity(Entity entity, ref Unity.Mathematics.Random random){
        if(Mates.value == null) return false; 
        if(AwarenessTable == null) return false;
        int index = (int)entity.info.entityType;
        if(!AwarenessTable.ContainsKey(index)) return false;
        Mate mate = Mates.value[AwarenessTable[index].Preference];
        float delta = mate.AmountPerParent;
        int amount = Mathf.FloorToInt(delta) + (random.NextFloat() < math.frac(delta) ? 1 : 0);
        uint childIndex = (uint)Config.CURRENT.Generation.Entities.RetrieveIndex(mate.ChildType);
        
        for(int i = 0; i < amount; i++){
            EntityManager.InitializeEntity((int3)entity.position, childIndex);
        }
        
        return true;
    }

    public bool CanConsume(WorldConfig.Generation.Item.IItem item, out float nutrition){
        nutrition = 0; 
        if(Edibles.value == null) return false; 
        if(AwarenessTable == null) return false;
        if(!AwarenessTable.ContainsKey(-item.Index)) return false;
        nutrition = Edibles.value[AwarenessTable[-item.Index].Preference].Nutrition;
        if(item.IsStackable) nutrition *= item.AmountRaw;
        return true;
    }

    public bool CanMateWith(Entity entity){
        if(Mates.value == null) return false;
        if(AwarenessTable == null) return false;
        int index = (int)entity.info.entityType;
        return AwarenessTable.ContainsKey(index);
    }
}
