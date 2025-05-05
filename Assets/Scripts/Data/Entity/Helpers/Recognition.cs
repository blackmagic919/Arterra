using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using WorldConfig;
using WorldConfig.Generation.Entity;
using WorldConfig.Generation.Structure;
using WorldConfig.Generation.Material;
using static EntityManager.STree;
using WorldConfig.Generation;


public interface IMateable{
    public void MateWith(Entity entity);
    public bool CanMateWith(Entity entity);
}

[Serializable]
public class Recognition{
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

    [UISetting(Ignore = true)][JsonIgnore][HideInInspector]
    internal Dictionary<int, Recognizable> AwarenessTable;

    public virtual void Construct(){
        //constructs awarness table
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

    public struct Recognizable{
        public uint data;
        public int Preference{
            readonly get => (int)(data & 0x3FFFFFF);
            set => data = (data & 0xC0000000) | ((uint)value & 0x3FFFFFF);
        }
        public bool IsUnknown{
            readonly get => ((data >> 30) & 0x3) == 0;
            set => data = (data & 0x3FFFFFFF) | (uint)(value ? 0 : 0);
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

        Entity cEntity = null; float closestDist = SightDistance + 1;
        Dictionary<int, Recognition.Recognizable> Awareness = AwarenessTable;
        Bounds bounds = new (self.position, 2 * new float3(SightDistance));
        EntityManager.ESTree.Query(bounds, (Entity nEntity) => {
            if(nEntity == null) return;
            if(nEntity.info.entityId == self.info.entityId) return;
            if(!Awareness.ContainsKey((int)nEntity.info.entityType)) return;

            Recognizable entityInfo = Awareness[(int)nEntity.info.entityType];
            if(!entityInfo.IsPredator) return;
            float dist = math.distance(self.position, nEntity.position);
            if(dist >= closestDist) return;
            cEntity = nEntity;
            closestDist = dist;
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

        Dictionary<int, Recognizable> Awareness = AwarenessTable;
        Bounds bounds = new (self.position, 2 * new float3(SightDistance));
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

    public Recognizable Recognize(Entity entity){
        if(AwarenessTable == null) return new Recognizable{IsUnknown = true};
        if(!AwarenessTable.TryGetValue((int)entity.info.entityType, out Recognizable ret)) 
            return new Recognizable{IsUnknown = true};
        return ret;
    }

    public static void DetectMapInteraction(float3 originGS, Action<float> OnInSolid = null, Action<float> OnInLiquid = null, Action<float> OnInGas = null){
        static (float, float) TrilinearBlend(float3 posGS){
            //Calculate Density
            int x0 = (int)Math.Floor(posGS.x); int x1 = x0 + 1;
            int y0 = (int)Math.Floor(posGS.y); int y1 = y0 + 1;
            int z0 = (int)Math.Floor(posGS.z); int z1 = z0 + 1;

            uint c000 = CPUMapManager.SampleMap(new int3(x0, y0, z0)).data;
            uint c100 = CPUMapManager.SampleMap(new int3(x1, y0, z0)).data;
            uint c010 = CPUMapManager.SampleMap(new int3(x0, y1, z0)).data;
            uint c110 = CPUMapManager.SampleMap(new int3(x1, y1, z0)).data;
            uint c001 = CPUMapManager.SampleMap(new int3(x0, y0, z1)).data;
            uint c101 = CPUMapManager.SampleMap(new int3(x1, y0, z1)).data;
            uint c011 = CPUMapManager.SampleMap(new int3(x0, y1, z1)).data;
            uint c111 = CPUMapManager.SampleMap(new int3(x1, y1, z1)).data;

            float xd = posGS.x - x0;
            float yd = posGS.y - y0;
            float zd = posGS.z - z0;

            float c00 = (c000 & 0xFF) * (1 - xd) + (c100 & 0xFF) * xd;
            float c01 = (c001 & 0xFF) * (1 - xd) + (c101 & 0xFF) * xd;
            float c10 = (c010 & 0xFF) * (1 - xd) + (c110 & 0xFF) * xd;
            float c11 = (c011 & 0xFF) * (1 - xd) + (c111 & 0xFF) * xd;

            float c0 = c00 * (1 - yd) + c10 * yd;
            float c1 = c01 * (1 - yd) + c11 * yd;
            float density = c0 * (1 - zd) + c1 * zd;

            c000 = c000 >> 8 & 0xFF; c100 = c100 >> 8 & 0xFF;
            c010 = c010 >> 8 & 0xFF; c110 = c110 >> 8 & 0xFF;
            c001 = c001 >> 8 & 0xFF; c101 = c101 >> 8 & 0xFF;
            c011 = c011 >> 8 & 0xFF; c111 = c111 >> 8 & 0xFF;

            c00 = c000 * (1 - xd) + c100 * xd;
            c01 = c001 * (1 - xd) + c101 * xd;
            c10 = c010 * (1 - xd) + c110 * xd;
            c11 = c011 * (1 - xd) + c111 * xd;

            c0 = c00 * (1 - yd) + c10 * yd;
            c1 = c01 * (1 - yd) + c11 * yd;
            float viscosity = c0 * (1 - zd) + c1 * zd;
            return (density, viscosity);
        }

        (float density, float viscoity) = TrilinearBlend(originGS);
        if(viscoity > CPUMapManager.IsoValue) OnInSolid?.Invoke(viscoity);
        else if(density - viscoity > CPUMapManager.IsoValue) OnInLiquid?.Invoke(density - viscoity);
        else OnInGas?.Invoke(density);
        
        //int3 coordGS = (int3)math.round(centerGS);
        //int material = SampleMap(coordGS).material;
    }
}
[Serializable]
public class RCarnivore : Recognition{
    //The order of the list describes the order of preference for the entity
    //An entity won't chase a prey if a more preferred prey is in range
    public Option<List<string>> Prey;

    public override void Construct(){
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
            if(eInfo.Preference == pPref && math.distance(nEntity.position, self.position) > closestDist) return;
            }
            
            cEntity = nEntity;
            pPref = eInfo.Preference;
            closestDist = math.distance(nEntity.position, self.position);
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
    private int materialStart => Config.CURRENT.Generation.Entities.Reg.value.Count;

    public override void Construct(){
        AwarenessTable = new Dictionary<int, Recognizable>();
        Registry<Authoring> eReg = Config.CURRENT.Generation.Entities;
        IRegister mReg = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        Registry<WorldConfig.Generation.Item.Authoring> iReg = Config.CURRENT.Generation.Items;


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
            CPUMapManager.MapData mapInfo = CPUMapManager.SampleMap(GCoord);
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
    public WorldConfig.Generation.Item.IItem ConsumeFood(int3 preyCoord){
        CPUMapManager.MapData mapInfo = CPUMapManager.SampleMap(preyCoord);
        int mIndex = mapInfo.material + materialStart;
        
        Dictionary<int, Recognizable> Awareness = AwarenessTable;
        if(!Awareness.TryGetValue(mIndex, out Recognizable rInfo)) return null;
        Plant plant = Prey.value[rInfo.Preference];
        if(!plant.Bounds.Contains(mapInfo)) return null;
        if(!rInfo.IsPrey) return null;
        Registry<MaterialData> matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        Registry<WorldConfig.Generation.Item.Authoring> itemInfo = Config.CURRENT.Generation.Items;
        MaterialData material = matInfo.Retrieve(mapInfo.material);
        string key; int delta;

        if(mapInfo.IsSolid){
            key = material.RetrieveKey(material.SolidItem);
            delta = mapInfo.SolidDensity;
        } else {
            key = material.RetrieveKey(material.LiquidItem);
            delta = mapInfo.LiquidDensity;
        } 

        if(!itemInfo.Contains(key)) return null;
        int itemIndex = itemInfo.RetrieveIndex(key);
        WorldConfig.Generation.Item.IItem nMat = itemInfo.Retrieve(itemIndex).Item;
        nMat.Index = itemIndex;
        nMat.AmountRaw = delta;

        key = plant.Replacement;
        if(String.IsNullOrEmpty(key) || !matInfo.Contains(key)){
            if(mapInfo.IsSolid) mapInfo.viscosity -= delta;
            else if(mapInfo.IsLiquid) mapInfo.density -= delta; 
        } else {
            mIndex = matInfo.RetrieveIndex(key);
            mapInfo.material = mIndex;
            if(mapInfo.IsSolid && (plant.ReplaceState == 
            WorldConfig.Generation.Item.Authoring.State.Liquid)) mapInfo.viscosity -= delta;
            else if(mapInfo.IsLiquid && (plant.ReplaceState == 
            WorldConfig.Generation.Item.Authoring.State.Solid)) mapInfo.viscosity += delta;
        }
        CPUMapManager.SetMap(mapInfo, preyCoord);

        return nMat;
    }


    [Serializable]
    public struct Plant{
        public string Material;
        public StructureData.CheckInfo Bounds;
        //If null, gradually removes it
        public string Replacement;
        public WorldConfig.Generation.Item.Authoring.State ReplaceState;

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
