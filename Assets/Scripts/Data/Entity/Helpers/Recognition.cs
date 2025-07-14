using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using WorldConfig;
using WorldConfig.Generation.Entity;
using WorldConfig.Generation.Structure;
using WorldConfig.Generation.Material;
using MapStorage;


public interface IMateable{
    public void MateWith(Entity entity);
    public bool CanMateWith(Entity entity);
}

[Serializable]
public class Recognition {
    //There is no explicit order with predators, an entity will run
    //from the closest predator to it.
    public Option<List<string>> Predators;
    //Mates are entities that can breed with the entity, and the offspring they create
    public Option<List<Mate>> Mates;
    //Edible items produced by entities
    public Option<List<Consumable>> Edibles;
    public int SightDistance;
    public int FleeDistance;
    public bool FightAggressor;

    [UISetting(Ignore = true)]
    [JsonIgnore]
    [HideInInspector]
    internal Dictionary<int, Recognizable> AwarenessTable;

    public virtual void Construct() {
        //constructs awarness table
    }
    [Serializable]
    public struct Mate {
        public string MateType;
        public string ChildType;
        public float AmountPerParent;
    }
    [Serializable]
    public struct Consumable {
        public string EdibleType;
        public float Nutrition;
    }

    [Serializable]

    public struct Recognizable {
        public uint data;
        public int Preference {
            readonly get => (int)(data & 0x3FFFFFF);
            set => data = (data & 0xC0000000) | ((uint)value & 0x3FFFFFF);
        }
        public bool IsUnknown {
            readonly get => ((data >> 30) & 0x3) == 0;
            set => data = (data & 0x3FFFFFFF) | (uint)(value ? 0 : 0);
        }
        public bool IsPredator {
            readonly get => ((data >> 30) & 0x3) == 1;
            set => data = (data & 0x3FFFFFFF) | (uint)(value ? 0x10000000 : 0);
        }
        public bool IsPrey {
            readonly get => ((data >> 30) & 0x3) == 2;
            set => data = (data & 0x3FFFFFFF) | (uint)(value ? 0x20000000 : 0);
        }
        public bool IsMate {
            readonly get => ((data >> 30) & 0x3) == 3;
            set => data = (data & 0x3FFFFFFF) | (uint)(value ? 0x30000000 : 0);
        }

        public Recognizable(int Preference, uint type) {
            data = (uint)(Preference & 0x3FFFFFFF) | ((type & 0x3) << 30);
        }
    }

    public bool FindClosestPredator(Entity self, out Entity entity) {
        entity = null; if (AwarenessTable == null) return false;
        if (Predators.value == null || Predators.value.Count == 0) return false;

        Entity cEntity = null; float closestDist = SightDistance + 1;
        Dictionary<int, Recognition.Recognizable> Awareness = AwarenessTable;
        Bounds bounds = new(self.position, 2 * new float3(SightDistance));
        EntityManager.ESTree.Query(bounds, (Entity nEntity) => {
            if (nEntity == null) return;
            if (nEntity.info.entityId == self.info.entityId) return;
            if (!Awareness.ContainsKey((int)nEntity.info.entityType)) return;

            Recognizable entityInfo = Awareness[(int)nEntity.info.entityType];
            if (!entityInfo.IsPredator) return;
            float dist = GetColliderDist(self, nEntity);
            if (dist >= closestDist) return;
            cEntity = nEntity;
            closestDist = dist;
        });
        entity = cEntity;
        return entity != null;
    }

    //Finds the most preferred mate it can see, then the closest one it prefers
    public bool FindPreferredMate(Entity self, out Entity entity) {
        entity = null; if (AwarenessTable == null) return false;
        if (Mates.value == null || Mates.value.Count == 0) return false;

        Entity cEntity = null; int pPref = -1;
        float closestDist = SightDistance + 1;

        Dictionary<int, Recognizable> Awareness = AwarenessTable;
        Bounds bounds = new(self.position, 2 * new float3(SightDistance));
        EntityManager.ESTree.Query(bounds, (Entity nEntity) => {
            if (nEntity == null) return;
            if (!Awareness.ContainsKey((int)nEntity.info.entityType)) return;
            if (nEntity.info.entityId == self.info.entityId) return;

            Recognizable entityInfo = Awareness[(int)nEntity.info.entityType];
            if (!entityInfo.IsMate) return;
            if (nEntity is not IMateable) return;
            if (!(nEntity as IMateable).CanMateWith(self)) return;
            if (cEntity != null) {
                if (entityInfo.Preference > pPref) return;
                if (pPref == entityInfo.Preference &&
                GetColliderDist(nEntity, self) >= closestDist)
                    return;
            }

            cEntity = nEntity;
            pPref = entityInfo.Preference;
            closestDist = GetColliderDist(nEntity, self);
        });
        entity = cEntity;
        return entity != null;
    }


    public bool MateWithEntity(Entity entity, ref Unity.Mathematics.Random random) {
        if (Mates.value == null) return false;
        if (AwarenessTable == null) return false;
        int index = (int)entity.info.entityType;
        if (!AwarenessTable.ContainsKey(index)) return false;
        Mate mate = Mates.value[AwarenessTable[index].Preference];
        float delta = mate.AmountPerParent;
        int amount = Mathf.FloorToInt(delta) + (random.NextFloat() < math.frac(delta) ? 1 : 0);
        uint childIndex = (uint)Config.CURRENT.Generation.Entities.RetrieveIndex(mate.ChildType);

        for (int i = 0; i < amount; i++) {
            EntityManager.InitializeEntity((int3)entity.position, childIndex);
        }

        return true;
    }

    public bool CanConsume(WorldConfig.Generation.Item.IItem item, out float nutrition) {
        nutrition = 0;
        if (Edibles.value == null) return false;
        if (AwarenessTable == null) return false;
        if (!AwarenessTable.ContainsKey(-item.Index)) return false;
        nutrition = Edibles.value[AwarenessTable[-item.Index].Preference].Nutrition;
        if (item.IsStackable) nutrition *= item.AmountRaw / 255.0f;
        return true;
    }

    public bool CanMateWith(Entity entity) {
        if (Mates.value == null) return false;
        if (AwarenessTable == null) return false;
        int index = (int)entity.info.entityType;
        return AwarenessTable.ContainsKey(index);
    }

    public Recognizable Recognize(Entity entity) {
        if (AwarenessTable == null) return new Recognizable { IsUnknown = true };
        if (!AwarenessTable.TryGetValue((int)entity.info.entityType, out Recognizable ret))
            return new Recognizable { IsUnknown = true };
        return ret;
    }

    public static float GetColliderDist(Entity a, Entity b) {
        if (a == null || b == null) return float.PositiveInfinity;
        var reg = Config.CURRENT.Generation.Entities;
        EntitySetting aSet = reg.Retrieve((int)a.info.entityType).Setting;
        EntitySetting bSet = reg.Retrieve((int)b.info.entityType).Setting;
        Bounds aBounds = new Bounds(a.position, aSet.collider.size);
        Bounds bBounds = new Bounds(b.position, bSet.collider.size);
        if (aBounds.Intersects(bBounds)) return 0;
        Vector3 aMin = aBounds.min, aMax = aBounds.max;
        Vector3 bMin = bBounds.min, bMax = bBounds.max;
        float dx = Mathf.Max(0, Mathf.Max(aMin.x - bMax.x, bMin.x - aMax.x));
        float dy = Mathf.Max(0, Mathf.Max(aMin.y - bMax.y, bMin.y - aMax.y));
        float dz = Mathf.Max(0, Mathf.Max(aMin.z - bMax.z, bMin.z - aMax.z));
        return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public static float GetColliderDist(Entity entity, float3 point) {
        var reg = Config.CURRENT.Generation.Entities;
        EntitySetting aSet = reg.Retrieve((int)entity.info.entityType).Setting;
        Bounds aBounds = new Bounds(entity.position, aSet.collider.size);
        float3 nPoint = aBounds.ClosestPoint(point);
        return math.distance(nPoint, point);
    }
}
[Serializable]
public class RCarnivore : Recognition{
    //The order of the list describes the order of preference for the entity
    //An entity won't chase a prey if a more preferred prey is in range
    public Option<List<string>> Prey;

    public override void Construct(){
        AwarenessTable = new Dictionary<int, Recognizable>();
        Catalogue<Authoring> eReg = Config.CURRENT.Generation.Entities;
        Catalogue<WorldConfig.Generation.Item.Authoring> iReg = Config.CURRENT.Generation.Items;

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
            int itemIndex = iReg.RetrieveIndex(Edibles.value[i].EdibleType);
            //negative so it doesn't conflict with entity indexes
            AwarenessTable.TryAdd(-itemIndex, new Recognizable(i, 0)); 
        }}
    }

    //Finds most preferred it can see, then the closest one it prefers
    public bool FindPreferredPrey(Entity self, out Entity entity, Func<Entity, bool> CanHunt = null){
        entity = null; if(AwarenessTable == null) return false;
        if(Prey.value == null || Prey.value.Count == 0) return false;

        Entity cEntity = null; int pPref = -1;
        float closestDist = SightDistance + 1;

        Dictionary<int, Recognizable> Awareness = AwarenessTable;
        Bounds bounds = new (self.position, 2 * new float3(SightDistance));
        EntityManager.ESTree.Query(bounds, (Entity nEntity) => {
            if(nEntity == null) return;
            if(nEntity.info.entityId == self.info.entityId) return;
            if(!Awareness.ContainsKey((int)nEntity.info.entityType)) return;

            Recognizable eInfo = Awareness[(int)nEntity.info.entityType];
            if(!eInfo.IsPrey) return;
            if(CanHunt != null && !CanHunt(nEntity)) 
                return;
                
            if(cEntity != null){
            if(eInfo.Preference > pPref) return;
            if(eInfo.Preference == pPref && GetColliderDist(nEntity, self) >= closestDist) return;
            }
            
            cEntity = nEntity;
            pPref = eInfo.Preference;
            closestDist = GetColliderDist(nEntity, self);
        });
        entity = cEntity;
        return entity != null;
    }
    
}


[Serializable]
public class RHerbivore : Recognition{
    public int PlantFindDist;
    //The order of the list describes the order of preference for the entity
    //An entity won't chase a prey if a more preferred prey is in range
    public Option<List<Plant>> Prey;
    private int materialStart => Config.CURRENT.Generation.Entities.Reg.Count;

    public override void Construct(){
        AwarenessTable = new Dictionary<int, Recognizable>();
        Catalogue<Authoring> eReg = Config.CURRENT.Generation.Entities;
        IRegister mReg = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        Catalogue<WorldConfig.Generation.Item.Authoring> iReg = Config.CURRENT.Generation.Items;


        if(Predators.value != null){
        for(int i = 0; i < Predators.value.Count; i++){
            int entityIndex = eReg.RetrieveIndex(Predators.value[i]);
            AwarenessTable.TryAdd(entityIndex, new Recognizable(i, 1));
        }}  if(Mates.value != null) { 
        for(int i = 0; i < Mates.value.Count; i++){
            int entityIndex = eReg.RetrieveIndex(Mates.value[i].MateType);
            AwarenessTable.TryAdd(entityIndex, new Recognizable(i, 3));
        }} if(Prey.value != null) {
        for(int i = 0; i < Prey.value.Count; i++){
            int materialIndex = mReg.RetrieveIndex(Prey.value[i].Material);
            AwarenessTable.TryAdd(materialIndex + materialStart,  new Recognizable(i, 2));
        }} if(Edibles.value != null) {
        for(int i = 0; i < Edibles.value.Count; i++){
            int edibleIndex = iReg.RetrieveIndex(Edibles.value[i].EdibleType);
            //negative so it doesn't conflict with entity indexes
            AwarenessTable.TryAdd(-edibleIndex, new Recognizable(i, 0)); 
        }}
    }

    //Finds the closest prey near it
    public bool FindPreferredPrey(int3 center, out int3 entry){
        int3 dx = new int3(0); entry = new int3(0);
        Dictionary<int, Recognizable> Awareness = AwarenessTable;
        float minDist = -1;
        for(dx.x = -PlantFindDist; dx.x < PlantFindDist; dx.x++){
        for(dx.y = -PlantFindDist; dx.y < PlantFindDist; dx.y++){
        for(dx.z = -PlantFindDist; dx.z < PlantFindDist; dx.z++){
            int3 GCoord = center + dx;
            MapData mapInfo = CPUMapManager.SampleMap(GCoord);
            int mIndex = mapInfo.material + materialStart;
            if(!Awareness.TryGetValue(mIndex, out Recognizable rInfo)) continue;
            if(!Prey.value[rInfo.Preference].Bounds.Contains(mapInfo)) continue;
            if(!rInfo.IsPrey) continue;
            float dist = math.csum(math.abs(dx)); //manhattan distance
            if(minDist == -1 || dist < minDist) {
                minDist = dist;
                entry = GCoord;
            } 
        }}}
        return minDist != -1;
    }

    //Eat Food
    public WorldConfig.Generation.Item.IItem ConsumeFood(Entity self, int3 preyCoord){
        MapData mapData = CPUMapManager.SampleMap(preyCoord);
        if (mapData.IsNull) return null;
        int mIndex = mapData.material + materialStart;
        
        Dictionary<int, Recognizable> Awareness = AwarenessTable;
        if(!Awareness.TryGetValue(mIndex, out Recognizable rInfo)) return null;
        Plant plant = Prey.value[rInfo.Preference];
        if(!plant.Bounds.Contains(mapData)) return null;
        if(!rInfo.IsPrey) return null;
        Catalogue<MaterialData> matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;

        string key = plant.Replacement;
        WorldConfig.Generation.Item.IItem nMat = matInfo.Retrieve(mapData.material).AcquireItem(mapData);
        if (!String.IsNullOrEmpty(key) && matInfo.Contains(key)) {
            int newMaterial = matInfo.RetrieveIndex(key);
            if (!MaterialData.SwapMaterial(preyCoord, newMaterial, self))
                return null;
            return nMat;
        } else {
            if (matInfo.Retrieve(mapData.material).OnRemoving(preyCoord, self))
                return null;
            matInfo.Retrieve(mapData.material).OnRemoved(preyCoord, mapData);
            mapData.viscosity = 0;
            mapData.density = 0;
            CPUMapManager.SetMap(mapData, preyCoord);
            return nMat;
        }
    }


    [Serializable]
    public struct Plant{
        public string Material;
        public StructureData.CheckInfo Bounds;
        //If null, gradually removes it
        public string Replacement;

        readonly static int3[] dp = new int3[6]{
            new (0, 0, 1),
            new (0, 0, -1),
            new (1, 0, 0),
            new (-1, 0, 0),
            new (0, 1, 0),
            new (0, -1, 0),
        };
    }
}
