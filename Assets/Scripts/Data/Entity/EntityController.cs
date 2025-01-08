using System;
using UnityEngine;

namespace WorldConfig.Generation.Entity{
/// <summary>
/// The controller for an entity. An entity controller reflects the state of 
/// the entity by changing its animation, physical position, and other properties. 
/// Each entity should define its own entity controller that is aware of the type 
/// of the entity it holds.
/// </summary> <remarks>
/// An entity controller can be thought of as the body of the entity, responsible 
/// for following the instructions of the <see cref="Entity"/> structure, which can be
/// thought of as the brain. Any computation intensive tasks should be handled by the
/// <see cref="Entity"/> and the <see cref="EntityController"/> should only be responsible
/// for reflecting the output of the decisions made by the <see cref="Entity"/> object.
/// </remarks>
public abstract class EntityController : MonoBehaviour
{
    /// <summary> Initializes the entity controller with the <see cref="Entity"/>
    /// that is associated with it.  </summary>
    /// <param name="entity">A pointer to the <see cref="Entity"/> structure that dictates the controller. </param>
    public virtual void Initialize(IntPtr entity){
        TerrainGeneration.OctreeTerrain.OrderedDisable.AddListener(Disable);
    }

    /// <summary> Called every frame by Unity's update loop. This function should be used to read from 
    /// the <see cref="Entity"/> object and update the entity's visual state accordingly. </summary>
    public abstract void Update();

    /// <summary> Called when the entity has been disabled. Once an entity has set a <see cref="Entity.active"> flag </see>
    /// indicating it has been disabled, the <see cref="EntityManager"/> system will call this function on the controller
    /// to allow it to clean up any resources that has been allocated. The <see cref="Entity"/> structure will no longer be
    /// executing or be able to execute when this function is called. </summary>
    public virtual void Disable(){
        TerrainGeneration.OctreeTerrain.OrderedDisable.RemoveListener(Disable);
    }
}
}
