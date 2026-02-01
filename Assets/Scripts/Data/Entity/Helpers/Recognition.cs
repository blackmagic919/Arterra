using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Configuration.Generation.Entity;
using Arterra.Configuration.Generation.Structure;
using Arterra.Configuration.Generation.Material;
using Arterra.Configuration.Generation.Item;
using Arterra.Core.Storage;
using System.Linq;


public interface IMateable {
    public void MateWith(Entity entity);
    public bool CanMateWith(Entity entity);
    public Genetics Genetics{ get; set; }
}

public interface IEntitySearchItem {
    public IItem[] GetItems();
}

[Serializable]
//Recognition for the basic minimal capability to run away. Some
//Creatures cannot eat and mate and thus do not need other fields in Recognition
public class MinimalRecognition {
    //There is no explicit order with predators, an entity will run
    public Option<List<EntityWrapper>> Predators;
    public Genetics.GeneFeature SightDistance;
    public Genetics.GeneFeature PlantFindDist;
    //The order of the list describes the order of preference for the entity
    //An entity won't chase a prey if a more preferred prey is in range
    public Option<List<EntityWrapper>> PreyEntity;
    //The order of the list describes the order of preference for the entity
    //An entity won't chase a prey if a more preferred prey is in range
    public Option<List<Plant>> PreyPlant;
    public int FleeDistance;
    public bool FightAggressor;
    [UISetting(Ignore = true)]
    [JsonIgnore]
    [HideInInspector]
    internal Dictionary<int, Recognizable> AwarenessTable;
    protected int materialStart => Config.CURRENT.Generation.Entities.Reg.Count;
    public bool HasEntityPrey => PreyEntity.value != null && PreyEntity.value.Count > 0;
    public bool HasPlantPrey => PreyPlant.value != null && PreyPlant.value.Count > 0;
    
    public virtual void Construct(){
        AwarenessTable = new Dictionary<int, Recognizable>();
        Catalogue<Arterra.Configuration.Generation.Entity.Authoring> eReg = Config.CURRENT.Generation.Entities;
        IRegister mReg = Config.CURRENT.Generation.Materials.value.MaterialDictionary;


        if(Predators.value != null){
        for(int i = 0; i < Predators.value.Count; i++){
            int entityIndex = eReg.RetrieveIndex(Predators.value[i].EntityType);
            AwarenessTable.TryAdd(entityIndex, new Recognizable(i, 1));
        }} if(PreyEntity.value != null) {
        for(int i = 0; i < PreyEntity.value.Count; i++){
            int entityIndex = eReg.RetrieveIndex(PreyEntity.value[i].EntityType);
            AwarenessTable.TryAdd(entityIndex, new Recognizable(i, 2)); 
        }} if(PreyPlant.value != null) {
        for(int i = 0; i < PreyPlant.value.Count; i++){
            int materialIndex = mReg.RetrieveIndex(PreyPlant.value[i].Material);
            AwarenessTable.TryAdd(materialIndex + materialStart,  new Recognizable(i, 2));
        }} 
    }
    public virtual void InitGenome(uint entityType) {
        Genetics.AddGene(entityType, ref SightDistance);
        Genetics.AddGene(entityType, ref PlantFindDist);
    }

    public bool FindClosestPredator(Entity self, float sightDist, out Entity entity) {
        entity = null; if (AwarenessTable == null) return false;
        if (Predators.value == null || Predators.value.Count == 0) return false;

        Entity cEntity = null; float closestDist = sightDist + 1;
        Dictionary<int, Recognizable> Awareness = AwarenessTable;
        Bounds bounds = new(self.position, 2 * new float3(sightDist));
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

    public Recognizable Recognize(Entity entity) {
        if (AwarenessTable == null) return new Recognizable { IsUnknown = true };
        if (!AwarenessTable.TryGetValue((int)entity.info.entityType, out Recognizable ret))
            return new Recognizable { IsUnknown = true };
        return ret;
    }

    //Finds most preferred it can see, then the closest one it prefers
    public bool FindPreferredPreyEntity(Entity self, float sightDist, out Entity entity, Func<Entity, bool> CanHunt = null){
        entity = null; if(AwarenessTable == null) return false;
        if(PreyEntity.value == null || PreyEntity.value.Count == 0) return false;

        Entity cEntity = null; int pPref = -1;
        float closestDist = sightDist + 1;

        Dictionary<int, Recognizable> Awareness = AwarenessTable;
        Bounds bounds = new (self.position, 2 * new float3(sightDist));
        EntityManager.ESTree.Query(bounds, (nEntity) => {
            if(nEntity == null) return;
            if(nEntity.info.entityId == self.info.entityId) return;

            if (!Awareness.TryGetValue((int)nEntity.info.entityType, out Recognizable eInfo)
                && !TrySearchEntityItems(nEntity, Awareness, out eInfo))
                return;

            if (!eInfo.IsPrey) return;
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

        static bool TrySearchEntityItems(Entity entity, Dictionary<int, Recognizable> awareness, out Recognizable recognizable) {
            recognizable = default;
            if (entity is not IEntitySearchItem itemHolder) return false;

            IItem[] items = itemHolder.GetItems();
            if (items == null) return false;
            foreach(IItem item in items) {
                if (item == null) continue;
                if(awareness.TryGetValue(-item.Index, out recognizable))
                    return true;
            } return false;
        }
    }

    //Finds the closest prey near it
    public bool FindPreferredPreyPlant(int3 center, int pFindDist, out int3 entry){
        int3 dx = new int3(0); entry = new int3(0);
        if(PreyPlant.value == null || PreyPlant.value.Count == 0) return false;
        Dictionary<int, Recognizable> Awareness = AwarenessTable;
        float minDist = -1;
        for(dx.x = -pFindDist; dx.x < pFindDist; dx.x++){
        for(dx.y = -pFindDist; dx.y < pFindDist; dx.y++){
        for(dx.z = -pFindDist; dx.z < pFindDist; dx.z++){
            int3 GCoord = center + dx;
            MapData mapInfo = CPUMapManager.SampleMap(GCoord);
            int mIndex = mapInfo.material + materialStart;
            if(!Awareness.TryGetValue(mIndex, out Recognizable rInfo)) continue;
            if(!PreyPlant.value[rInfo.Preference].Bounds.Contains(mapInfo)) continue;
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
    public IItem ConsumePlant(Entity self, int3 preyCoord){
        MapData mapData = CPUMapManager.SampleMap(preyCoord);
        if (mapData.IsNull) return null;
        int mIndex = mapData.material + materialStart;
        
        Dictionary<int, Recognizable> Awareness = AwarenessTable;
        if(!Awareness.TryGetValue(mIndex, out Recognizable rInfo)) return null;
        Plant plant = PreyPlant.value[rInfo.Preference];
        if(!plant.Bounds.Contains(mapData)) return null;
        if(!rInfo.IsPrey) return null;
        Catalogue<MaterialData> matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;

        string key = plant.Replacement;
        if (!String.IsNullOrEmpty(key) && matInfo.Contains(key)) {
            int newMaterial = matInfo.RetrieveIndex(key);
            if (!MaterialData.SwapMaterial(preyCoord, newMaterial,
                out IItem nMat, self))
                return null;
            return nMat;
        } else {
            MaterialInstance authoring = new (preyCoord, mapData.material);
            if (authoring.Authoring.OnRemoving(preyCoord, self))
                return null;
            IItem nMat =
                authoring.Authoring.OnRemoved(preyCoord, mapData);
            self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_RemoveMaterial, self, authoring, mapData);
            mapData.viscosity = 0;
            mapData.density = 0;
            CPUMapManager.SetMap(mapData, preyCoord);
            return nMat;
        }
    }

    public static float GetColliderDist(Entity a, Entity b) {
        if (a == null || b == null) return float.PositiveInfinity;
        var reg = Config.CURRENT.Generation.Entities;
        Bounds aBounds = new Bounds(a.position, a.transform.size);
        Bounds bBounds = new Bounds(b.position, b.transform.size);
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
        Bounds aBounds = new Bounds(entity.position, entity.transform.size);
        float3 nPoint = aBounds.ClosestPoint(point);
        return math.distance(nPoint, point);
    }

    public static bool RayTestSolid<T>(T entity, float reach, out float3 hitPt) where T : Entity, IAttackable {
        static uint RayTestSolid(int3 coord) {
            MapData pointInfo = CPUMapManager.SampleMap(coord);
            return (uint)pointInfo.viscosity;
        }
        return CPUMapManager.RayCastTerrain(entity.head, entity.Forward, reach, RayTestSolid, out hitPt);
    }

    public static bool RayTestLiquid<T>(T entity, float reach, out float3 hitPt) where T : Entity, IAttackable {
        static uint RayTestLiquid(int3 coord) {
            MapData pointInfo = CPUMapManager.SampleMap(coord);
            return (uint)Mathf.Max(pointInfo.viscosity, pointInfo.density - pointInfo.viscosity);
        }
        return CPUMapManager.RayCastTerrain(entity.head, entity.Forward, reach, RayTestLiquid, out hitPt);
    }

    public static bool CylinderTestSolid<T>(T entity, float reach, float radius, out float3 hitPt) where T : Entity, IAttackable {
        static uint CylinderTestSolid(int3 coord) {
            MapData pointInfo = CPUMapManager.SampleMap(coord);
            return (uint)pointInfo.viscosity;
        }
        if (RayTestSolid(entity, reach, out hitPt) && math.lengthsq(hitPt - entity.head) < radius * radius * 4)
            return true;
        return CPUMapManager.CylinderCastTerrain(entity.head + 2 * radius * entity.Forward,
            entity.Forward, radius, reach, CylinderTestSolid, out hitPt);
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

    [Serializable]
    public struct Plant{
        [RegistryReference("Materials")]
        public string Material;
        [RegistryReference("Materials")]
        //If null, gradually removes it
        public string Replacement;
        public StructureData.CheckInfo Bounds;

        readonly static int3[] dp = new int3[6]{
            new (0, 0, 1),
            new (0, 0, -1),
            new (1, 0, 0),
            new (-1, 0, 0),
            new (0, 1, 0),
            new (0, -1, 0),
        };
    }

    [Serializable]
    public struct EntityWrapper {
        [RegistryReference("Entities")]
        public string EntityType;
    }
}

[Serializable]
public class Recognition : MinimalRecognition{
    //Mates are entities that can breed with the entity, and the offspring they create
    public Option<List<Mate>> Mates;
    //Edible items produced by entities
    public Option<List<Consumable>> Edibles;

    public override void Construct() {
        base.Construct();
        AwarenessTable ??= new Dictionary<int, Recognizable>();
        Catalogue<Arterra.Configuration.Generation.Entity.Authoring> eReg = Config.CURRENT.Generation.Entities;
        Catalogue<Arterra.Configuration.Generation.Item.Authoring> iReg = Config.CURRENT.Generation.Items;

        if(Mates.value != null) { 
        for(int i = 0; i < Mates.value.Count; i++){
            int entityIndex = eReg.RetrieveIndex(Mates.value[i].MateType);
            AwarenessTable.TryAdd(entityIndex, new Recognizable(i, 3));
        }} if(Edibles.value != null) {
        for(int i = 0; i < Edibles.value.Count; i++){
            int edibleIndex = iReg.RetrieveIndex(Edibles.value[i].EdibleType);
            //negative so it doesn't conflict with entity indexes
            AwarenessTable.TryAdd(-edibleIndex, new Recognizable(i, 2)); 
        }}
    }
    [Serializable]
    public struct Mate {
        [RegistryReference("Entities")]
        public string MateType;
        [RegistryReference("Entities")]
        public string ChildType;
        public Genetics.GeneFeature AmountPerParent;
        public float GeneMutationRate;
    }

    [Serializable]
    public struct Consumable {
        [RegistryReference("Items")]
        public string EdibleType;
        public Genetics.GeneFeature Nutrition;
    }

    public override void InitGenome(uint entityType) {
        base.InitGenome(entityType);
        for (int i = 0; i < Mates.value.Count; i++) {
            Mate mate = Mates.value[i];
            Genetics.AddGene(entityType, ref mate.AmountPerParent);
            Mates.value[i] = mate;
        } 
        for (int i = 0; i < Edibles.value.Count; i++) {
            Consumable consumable = Edibles.value[i];
            Genetics.AddGene(entityType, ref consumable.Nutrition);
            Edibles.value[i] = consumable;
        } 
    }

    //Finds the most preferred mate it can see, then the closest one it prefers
    public bool FindPreferredMate(Entity self, float sightDist, out Entity entity) {
        entity = null; if (AwarenessTable == null) return false;
        if (Mates.value == null || Mates.value.Count == 0) return false;

        Entity cEntity = null; int pPref = -1;
        float closestDist = sightDist + 1;

        Dictionary<int, Recognizable> Awareness = AwarenessTable;
        Bounds bounds = new(self.position, 2 * new float3(sightDist));
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


    public bool MateWithEntity(Genetics genetics, Entity entity, ref Unity.Mathematics.Random random) {
        if (Mates.value == null) return false;
        if (AwarenessTable == null) return false;
        int index = (int)entity.info.entityType;
        if (!AwarenessTable.ContainsKey(index)) return false;
        if (entity is not IMateable mate) return false;

        Mate ofsp = Mates.value[AwarenessTable[index].Preference];
        float delta = genetics.Get(ofsp.AmountPerParent);
        int amount = Mathf.FloorToInt(delta) + (random.NextFloat() < math.frac(delta) ? 1 : 0);
        uint childIndex = (uint)Config.CURRENT.Generation.Entities.RetrieveIndex(ofsp.ChildType);

        for (int i = 0; i < amount; i++) {
            Entity child = Config.CURRENT.Generation.Entities.Retrieve((int)childIndex).Entity;
            EntityManager.CreateEntity((int3)entity.position, childIndex, child);
            if (child is not IMateable childM) continue;

            childM.Genetics = genetics.CrossGenes(
                mate.Genetics,
                ofsp.GeneMutationRate,
                childIndex,
                ref random
            );
        }

        return true;
    }

    public bool CanConsume(Genetics genetics, IItem item, out float nutrition) {
        nutrition = 0;
        if (Edibles.value == null) return false;
        if (AwarenessTable == null) return false;
        if (!AwarenessTable.ContainsKey(-item.Index)) return false;
        nutrition = genetics.Get(Edibles.value[AwarenessTable[-item.Index].Preference].Nutrition);
        nutrition *= (float)item.AmountRaw / item.UnitSize;
        return true;
    }

    public bool CanMateWith(Entity entity) {
        if (Mates.value == null) return false;
        if (AwarenessTable == null) return false;
        int index = (int)entity.info.entityType;
        return AwarenessTable.ContainsKey(index);
    }
}