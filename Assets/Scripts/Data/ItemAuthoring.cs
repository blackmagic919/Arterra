using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

 
public class ItemAuthoringTemplate<TItem> : ItemAuthoring where TItem : IItem, new()
{
    public override IItem Item => new TItem();
}

public class ItemAuthoring : ScriptableObject
{
    public enum State{
        Solid = 0, 
        Liquid = 1,
    }
    [UISetting(Ignore = true)][JsonIgnore]
    public Option<Sprite> texture;
    //Not necessary if only an item
    public string MaterialName;
    public State MaterialState;
    public virtual IItem Item { get; }
    public bool IsSolid => MaterialState == State.Solid;
    public bool IsLiquid => MaterialState == State.Liquid;
}

public interface IItem : ICloneable{
    public bool IsStackable { get; }
    public int TexIndex { get; }
    //The index within its register
    public int Index { get; set; }
    //The id that is unique to the slot
    public string Display{ get; }
    public int AmountRaw{ get; set; }
    public bool IsDirty{ get; set; }
    public void Serialize(Func<string, int> dict);
    public void Deserialize(Func<int, string> dict);

    public void OnEnterSecondary(); //Called upon entering the secondary inventory
    public void OnLeaveSecondary();//Called upon leaving the secondary inventory
    public void OnEnterPrimary(); //Called upon entering the primary inventory
    public void OnLeavePrimary(); //Called upon leaaving the primary inventory
    public void OnSelect(); //Called upon becoming the selected item
    public void OnDeselect(); //Called upon no longer being the selectd item
    public void UpdateEItem(); //Called every frame it is an Entity Item
}