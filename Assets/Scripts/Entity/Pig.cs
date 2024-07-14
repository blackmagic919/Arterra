using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;

[CreateAssetMenu(menuName = "Entity/Pig")]
public class Pig : EntityAuthoring
{
    [UIgnore]
    public Option<GameObject> _Controller;
    public Option<PigEntity> _Entity;
    public Option<List<uint2> > _Profile;
    public override GameObject Controller { get { return _Controller.value; } set => _Controller.value = value; }
    public override IEntity Entity { get => _Entity.value; set => _Entity.value = (PigEntity)value; }
    public override uint2[] Profile { get => _Profile.value.ToArray(); set => _Profile.value = value.ToList(); }

    [System.Serializable]
    public struct PigEntity : IEntity
    {        
        public IEntity.Info _info;
        public IEntity.Info info { get; set; }

        public void Initialize()
        {

        }

        public void Update()
        {

        }

        public void Release()
        {

        }
    }
}
