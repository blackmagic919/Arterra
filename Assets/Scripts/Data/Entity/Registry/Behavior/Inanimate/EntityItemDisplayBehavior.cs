using System;
using System.Collections.Generic;
using Arterra.Data.Item;
using Arterra.Engine.Terrain.Readback;
using static Arterra.Engine.Terrain.Readback.IVertFormat;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class EntityItemDisplaySettings : IBehaviorSetting {
        public string MeshFilterPath = "";
        public int2 SpriteSampleSize;
        public float AlphaClip;
        public float ExtrudeHeight;

        public object Clone() {
            return new EntityItemDisplaySettings {
                MeshFilterPath = MeshFilterPath,
                SpriteSampleSize = SpriteSampleSize,
                AlphaClip = AlphaClip,
                ExtrudeHeight = ExtrudeHeight,
            };
        }
    }

    public class EntityItemDisplayBehavior : SpeciesBehavior {
        [JsonIgnore]
        public EntityItemDisplaySettings settings;

        private EntityItemBehavior itemBehavior;
        private MeshFilter meshFilter;
        private bool active;
        private bool awaitingMesh;
        private int currentTexIndex = int.MinValue;

        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.EntityItem, heirarchy.Count);
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(EntityItemDisplaySettings), new EntityItemDisplaySettings());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: EntityItemDisplayBehavior requires AnimalSettings to have EntityItemDisplaySettings");
            if (!self.Is(out itemBehavior))
                throw new Exception("Entity: EntityItemDisplayBehavior requires AnimalInstance to have EntityItemBehavior");

            meshFilter = ResolveMeshFilter(self.controller.transform, settings.MeshFilterPath);
            if (meshFilter == null)
                throw new Exception("Entity: EntityItemDisplayBehavior failed to find MeshFilter at MeshFilterPath");

            active = true;
            currentTexIndex = int.MinValue;
            TryRefreshDisplay();
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: EntityItemDisplayBehavior requires AnimalSettings to have EntityItemDisplaySettings");
            if (!self.Is(out itemBehavior))
                throw new Exception("Entity: EntityItemDisplayBehavior requires AnimalInstance to have EntityItemBehavior");

            meshFilter = ResolveMeshFilter(self.controller.transform, settings.MeshFilterPath);
            if (meshFilter == null)
                throw new Exception("Entity: EntityItemDisplayBehavior failed to find MeshFilter at MeshFilterPath");

            active = true;
            currentTexIndex = int.MinValue;
            TryRefreshDisplay();
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.Job) return;
            if (self.context == BehaviorEntity.UpdateContext.Fixed) return;
            if (!active) return;
            TryRefreshDisplay();
        }

        public override void Disable(BehaviorEntity.Animal self) {
            active = false;
            awaitingMesh = false;
            meshFilter = null;
            itemBehavior = null;
        }

        private void TryRefreshDisplay() {
            if (meshFilter == null) return;

            IItem item = itemBehavior.Item;
            int targetTexIndex = item == null ? -1 : item.TexIndex;
            if (targetTexIndex == currentTexIndex) return;

            if (targetTexIndex < 0) {
                meshFilter.sharedMesh = null;
                currentTexIndex = -1;
                return;
            }

            if (awaitingMesh) return;
            awaitingMesh = true;
            int requestedTexIndex = targetTexIndex;

            SpriteExtruder.Extrude(new SpriteExtruder.ExtrudeSettings {
                ImageIndex = requestedTexIndex,
                SampleSize = settings.SpriteSampleSize,
                AlphaClip = settings.AlphaClip,
                ExtrudeHeight = settings.ExtrudeHeight,
            }, (meshInfo) => OnMeshReceived(meshInfo, requestedTexIndex));
        }

        private void OnMeshReceived(ReadbackTask<SVert>.SharedMeshInfo meshInfo, int texIndex) {
            try {
                if (!active || meshFilter == null) return;
                meshFilter.sharedMesh = meshInfo.GenerateMesh(UnityEngine.Rendering.IndexFormat.UInt32);
                currentTexIndex = texIndex;
            } finally {
                awaitingMesh = false;
                meshInfo.Release();
            }
        }

        private static MeshFilter ResolveMeshFilter(Transform root, string path) {
            if (root == null) return null;
            if (string.IsNullOrEmpty(path)) return root.GetComponent<MeshFilter>();

            Transform target = root.Find(path);
            return target == null ? null : target.GetComponent<MeshFilter>();
        }
    }
}
