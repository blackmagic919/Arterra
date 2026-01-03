using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.Configuration.Generation.Entity;
using Arterra.UI.ToolTips;
using Arterra.Core.Events;


[Serializable]
public class ToolTag : ICloneable {
    /// <summary> The speed at which the user can terraform the terrain. As terraforming is a 
    /// continuous process, the speed is measured in terms of change in density per frame. </summary>
    public float TerraformSpeed;
    /// <summary> Scales how much a tool is damaged when removing this material. Damaging
    /// a tool decreases its durability. If this is a material, this usually scales
    /// the amount of material used up instead </summary>
    public float ToolDamage = 0;
    /// <summary> Whether or not the material removed by this tool will give 
    /// the player the corresponding item. </summary>
    public bool GivesItem;
    public virtual object Clone() {
        return new ToolTag {
            TerraformSpeed = TerraformSpeed,
            ToolDamage = ToolDamage,
            GivesItem = GivesItem
        };
    }
}


[Serializable]
public class ConvertibleTag : IMaterialConverting {
    public Arterra.Configuration.Generation.Structure.StructureData.CheckInfo _convertBounds;
    /// <summary> The <see cref="MapData"/> requirements of at least one neighbor of the material that the grass can spread onto.  </summary>
    public Arterra.Configuration.Generation.Structure.StructureData.CheckInfo _neighborBounds;
    [JsonIgnore]
    public Arterra.Configuration.Generation.Structure.StructureData.CheckInfo ConvertBounds => _convertBounds;
    [JsonIgnore]
    public Arterra.Configuration.Generation.Structure.StructureData.CheckInfo NeighborBounds => _neighborBounds;

    public bool GivesItem;

    public virtual object Clone() {
        return new ConvertibleTag {
            _convertBounds = _convertBounds,
            _neighborBounds = _neighborBounds,
            GivesItem = GivesItem
        };
    }
}

[Serializable]
public class ConvertibleToolTag : ToolTag, IMaterialConverting {
    public Arterra.Configuration.Generation.Structure.StructureData.CheckInfo _convertBounds;
    /// <summary> The <see cref="MapData"/> requirements of at least one neighbor of the material that the grass can spread onto.  </summary>
    public Arterra.Configuration.Generation.Structure.StructureData.CheckInfo _neighborBounds;
    [JsonIgnore]
    public Arterra.Configuration.Generation.Structure.StructureData.CheckInfo ConvertBounds => _convertBounds;
    [JsonIgnore]
    public Arterra.Configuration.Generation.Structure.StructureData.CheckInfo NeighborBounds => _neighborBounds;

    public override object Clone() {
        return new ConvertibleToolTag {
            TerraformSpeed = TerraformSpeed,
            ToolDamage = ToolDamage,
            GivesItem = GivesItem,
            _convertBounds = _convertBounds,
            _neighborBounds = _neighborBounds,
        };
    }
}

[Serializable]
public class ConverterToolTag : ConvertibleToolTag {
    [RegistryReference("Materials")]
    public string ConvertTarget;
    public override object Clone() {
        return new ConverterToolTag {
            TerraformSpeed = TerraformSpeed,
            ToolDamage = ToolDamage,
            GivesItem = GivesItem,
            _convertBounds = _convertBounds,
            _neighborBounds = _neighborBounds,
            ConvertTarget = ConvertTarget
        };
    }
}
[Serializable]
public class ProjectileTag : ICloneable {
    [RegistryReference("Entities")]
    public string ProjectileEntity;
    public float LaunchSpeedMultiplier = 1.0f;
    public AudioEvents FireSound;
    public virtual object Clone() {
        return new ProjectileTag {
            ProjectileEntity = ProjectileEntity,
            LaunchSpeedMultiplier = LaunchSpeedMultiplier
        };
    }

    public void LaunchProjectile(float3 position, float3 velocity) {
        LaunchProjectile(position, velocity, null);
    }

    public void LaunchProjectile(Entity parent, float3 velocity) {
        float3 dir = math.normalize(velocity);
        float3 rayOrigin = parent.position;
        float3 min = parent.origin;
        float3 max = parent.origin + parent.transform.size;
        float3 t1 = (min - rayOrigin) / dir;
        float3 t2 = (max - rayOrigin) / dir;

        float3 tmax = math.max(t1, t2);
        float tExit = math.cmin(tmax); // the nearest "exit" distance
        rayOrigin += + dir * (tExit + 0.05f);
        LaunchProjectile(rayOrigin, velocity, parent);
    }

    private void LaunchProjectile(float3 position, float3 velocity, Entity parent) {
        var entityInfo = Config.CURRENT.Generation.Entities;
        int entityInd = entityInfo.RetrieveIndex(ProjectileEntity);
        var entity = Config.CURRENT.Generation.Entities.Retrieve(entityInd).Entity;
        EntityManager.AddHandlerEvent(() =>  AudioManager.CreateEvent(FireSound, position));
        EntityManager.CreateEntity(position, (uint)entityInd, entity, () => {
            entity.transform.position = position;
            entity.transform.velocity = velocity * LaunchSpeedMultiplier;
            entity.transform.rotation = Quaternion.LookRotation(math.normalize(velocity));
            entity.position = position;
            if (parent != null && entity is Projectile.ProjectileEntity pEntity)
                pEntity.ParentId = parent.info.entityId;
        });
    }
}
[Serializable]
public class CombustibleTag : ICloneable {

    public float Temperature; // The temperature the item can provide

    public float BurningRate; // How fate the item burns in terms of amount per second

    public object Clone() {
        return new CombustibleTag {
            Temperature = Temperature,
            BurningRate = BurningRate
        };
    }
}


/// <summary>
/// Tooltip Tag defining the configurations for tooltips associated with an entity/item or other game elements.
/// </summary>
[Serializable]
public class TooltipTag : ICloneable {

    // List of tooltip configurations associated with this tag.
    public Option<List<TooltipConfig>> Tooltips;

    public object Clone() {;
        Option<List<TooltipConfig>> cloneTooltips = Tooltips;
        cloneTooltips.Clone();
        return new TooltipTag {
            Tooltips = cloneTooltips
        };
    }

}

[Serializable]
public struct TooltipConfig : ICloneable {

    // The time since this popup is enqueued (potentially displayed)  before we auto-acknowledge this popup. 
    // If the popup is only auto-acknowledged by another event, set this to TimeSpan.Infinity.

    public float AcknowledgeTime; // in seconds

    // Once the event is enqueued(potentially displayed), the event that when triggered will auto-acknowledge
    // this popup (i.e. dequeuing it). If the popup is only auto-acknowledged after a certain time, 
    // set this to EventType.None
    public Arterra.Core.Events.GameEvent AcknowledgeEvent;

    public TooltipPriority Priority;

    // Used to distinguish, for example, between OnInteractEntity and OnAttackEntity since they both read this tag.
    // If the tag is EventType.None then trigger on any of these.
    public Arterra.Core.Events.GameEvent TriggerEvent;

    //The time since the trigger event occurred before the popup can be enqueued(potentially displayed). 
    // Set this to TimeSpan.Zero to consider it immediately.
    public float TriggerTime; // in seconds

    public string PrefabPath;

    // Whether, once this tooltip is auto-acknowledged, its name is put into a blacklist hash preventing it 
    // from ever triggering again. This should almost always be true (maybe except for some ondeath/startup msg 
    // or something)

    public bool BlockingTooltips;

    public object Clone() {
        return new TooltipConfig {
            AcknowledgeTime = AcknowledgeTime,
            AcknowledgeEvent = AcknowledgeEvent,
            Priority = Priority,
            TriggerEvent = TriggerEvent,
            TriggerTime = TriggerTime,
            BlockingTooltips = BlockingTooltips,
            PrefabPath = PrefabPath
        };
    }
}

// Tooltip dismissor tag data
[Serializable]
public class TooltipDismissorTag : ICloneable {
    // List of tooltip dismissor configurations associated with this tag.
    public Option<List<TooltipDismissorConfig>> Dismissors;

    public object Clone() {
        Option<List<TooltipDismissorConfig>> cloneDismissors = Dismissors;
        cloneDismissors.Clone();
        return new TooltipDismissorTag {
            Dismissors = cloneDismissors
        };
    }
}

[Serializable]
public struct TooltipDismissorConfig : ICloneable {
    // The event that will dismiss the tooltip.
    public GameEvent DismissEvent;

    public string PrefabPath; // The prefab path of the tooltip to be dismissed.

    public object Clone() {
        return new TooltipDismissorConfig {
            DismissEvent = DismissEvent,
            PrefabPath = PrefabPath
        };
    }
}


public interface IMaterialConverting : ICloneable {
    public Arterra.Configuration.Generation.Structure.StructureData.CheckInfo ConvertBounds { get; }
    public Arterra.Configuration.Generation.Structure.StructureData.CheckInfo NeighborBounds { get; }

    static readonly Unity.Mathematics.int3[] dP = new Unity.Mathematics.int3[6]{
        new (0,1,0),
        new (0,-1,0),
        new (1,0,0),
        new (0,0,-1),
        new (-1,0,0),
        new (0,0,1),
    };
    public static bool CanConvert<T>(MapData neighbor, Unity.Mathematics.int3 GCoord, TagRegistry.Tags tag, out T TagInfo)
        where T : class, IMaterialConverting {
        var matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        TagInfo = null;

        if (matInfo.GetMostSpecificTag(tag, neighbor.material, out object prop))
            TagInfo = prop as T; //matInfo.RetrieveIndex((prop as ConverterToolTag).ConvertTarget);
        else return false;
        if (!TagInfo.ConvertBounds.Contains(neighbor)) return false;
        //No neighbor bounds, so always valid
        if (TagInfo.NeighborBounds.IsNull) return true;
        for (int i = 0; i < 6; i++) {
            MapData nNeighbor = CPUMapManager.SampleMap(GCoord + dP[i]);
            if (TagInfo.NeighborBounds.Contains(nNeighbor)) return true;
        }
        return false;
    }
}

[Serializable]
public struct TagRegistry
{
    public static readonly Dictionary<Tags, ICloneable> TagTemplates = new(){
        //Tool Tags
        { Tags.None, null },
        { Tags.BareHand, new ToolTag() },
        { Tags.WoodAxe, new ToolTag() },
        { Tags.WoodShovel, new ToolTag() },
        { Tags.WoodPickaxe, new ToolTag() },
        { Tags.WoodHoe, new ToolTag() },
        { Tags.StoneAxe, new ToolTag() },
        { Tags.StoneShovel, new ToolTag() },
        { Tags.StonePickaxe, new ToolTag() },
        { Tags.StoneHoe, new ToolTag() },
        //Converter Tags
        { Tags.Flammable, new ConverterToolTag() },
        { Tags.Tillable, new ConverterToolTag() },
        { Tags.Seedable, new ConvertibleToolTag() },
        //Convertable Tags
        { Tags.Grassy, new ConvertibleTag() },
        { Tags.Vegetative, new ConvertibleTag() },
        { Tags.AquaMicrobial, new ConvertibleTag() },
        { Tags.Combustible, new CombustibleTag() },
        //Interaction Type
        { Tags.FocusedPlace, null },
        { Tags.Tooltip, new TooltipTag() },
        { Tags.TooltipDismissor, new TooltipDismissorTag() },
        // Projectiles
        { Tags.ArrowTag, new ProjectileTag() }
    };

    public enum Tags {
        //Tools
        None = 0, BareHand = 1, WoodAxe = 2, WoodShovel = 3, WoodPickaxe = 4, WoodHoe = 5,
        StoneAxe = 12, StoneShovel = 13, StonePickaxe = 14, StoneHoe = 15,
        //Converters
        Flammable = 1000, Tillable = 1001, Seedable = 1002, Combustible = 1003,
        //Convertables
        Grassy = 2000, Vegetative = 2001, AquaMicrobial = 2002,
        //Interactions
        FocusedPlace = 9000, Tooltip = 9001, TooltipDismissor = 9002,
        // Projectiles 
        ArrowTag = 10000
    }

    public Option<List<Pair>> Reg;
    [HideInInspector]
    [UISetting(Ignore = true)]
    [JsonIgnore]
    private Dictionary<Tags, int> Index;

    public void Construct()
    {
        Index = new Dictionary<Tags, int>();
        Reg.value ??= new List<Pair>();
        for (int i = 0; i < Reg.value.Count; i++)
        {
            Index.TryAdd(Reg.value[i].Tag, i);//
        }
    }

    public void OnValidate()
    {
        if (Reg.value == null) return;
        for (int i = 0; i < Reg.value.Count; i++)
        {
            Tags tag = Reg.value[i].Tag;
            var pair = Reg.value[i];
            if (tag == Tags.None) pair.value.value = null;
            else
            {
                if (pair.value.value != null && pair.value.value.GetType() == TagTemplates[tag]?.GetType())
                    continue;
                pair.value.value = TagTemplates[tag]?.Clone() as ICloneable;

            } Reg.value[i] = pair;//
        }
    }

    public int RetrieveIndex(Tags tag)
    {
        return Index[tag];
    }
    public Tags RetrieveName(int index)
    {
        return Reg.value[index].Tag;
    }

    public ICloneable Retrieve(Tags tag)
    {
        return Reg.value[Index[tag]].value.value;
    }
    public ICloneable Retrieve(int index)
    {
        return Reg.value[index].value.value;
    }
    public bool Contains(Tags tag)
    {
        if (Index == null) return false;
        return Index.ContainsKey(tag);
    }
    public bool Contains(int index)
    {
        return index >= 0 && index < Reg.value.Count;
    }
    public void Add(Tags tag, ICloneable value)
    {
        Reg.value ??= new List<Pair>();
        Index ??= new Dictionary<Tags, int>();

        Reg.value.Add(new Pair { Tag = tag, value = new ReferenceOption<ICloneable> { value = value } });
        Index.Add(tag, Reg.value.Count - 1);
    }

    public bool TryRemove(Tags tag)
    {
        if (Reg.value == null || Index == null) return false;
        if (!Index.ContainsKey(tag)) return false;

        Reg.value.RemoveAt(Index[tag]);
        Index.Remove(tag);
        Construct(); //Rebuild the index
        return true;
    }

    public bool TrySet(Tags tag, ICloneable value)
    {
        if (Reg.value == null || Index == null) return false;
        if (!Index.ContainsKey(tag)) return false;

        int index = Index[tag];
        var tPair = Reg.value[index];
        tPair.value.value = value;
        Reg.value[index] = tPair;
        return true;
    }

    public object Clone()
    {
        return new TagRegistry { Reg = Reg };
    }

    [Serializable]
    public struct Pair : ICloneable
    {
        public Tags Tag;
        [UISetting(Alias = "Value")]
        public ReferenceOption<ICloneable> value;

        public object Clone()
        {
            return new Pair
            {
                Tag = Tag,
                value = value
            };
        }
    }

}
