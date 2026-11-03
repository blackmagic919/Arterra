using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arterra.GamePlay.UI;
using UnityEngine;

namespace Arterra.GamePlay {
    // One instance per visible entity. Register on the holder before reapplying its armor inventory.
    public sealed class ArmorVisuals : IDisposable {
        private readonly Transform rig;
        private readonly Transform rootBone;
        private readonly Transform[] bones;
        private readonly int layer;
        private readonly Dictionary<GameObject, ClothingState> clothing = new();
        private readonly Dictionary<ArmorInventory.IArmorItem, Visual> items = new(new ItemIdentity());

        private sealed class Visual {
            public GameObject Model;
            public readonly Dictionary<int, HashSet<GameObject>> Slots = new();
        }

        private sealed class ClothingState {
            public int Users;
            public bool WasActive;
        }

        private sealed class ItemIdentity : IEqualityComparer<ArmorInventory.IArmorItem> {
            public bool Equals(ArmorInventory.IArmorItem a, ArmorInventory.IArmorItem b) => ReferenceEquals(a, b);
            public int GetHashCode(ArmorInventory.IArmorItem item) => RuntimeHelpers.GetHashCode(item);
        }

        public ArmorVisuals(Transform rig, Transform rootBone, Transform[] bones, int layer) {
            this.rig = rig;
            this.rootBone = rootBone;
            this.bones = bones;
            this.layer = layer;
        }

        public void Register(ArmorInventory.IArmorItem item, ArmorInventory inventory, int slot) {
            if (rig == null || rootBone == null || bones == null || bones.Length == 0) return;
            var equip = item.GetEquipInfo();
            if (!items.TryGetValue(item, out Visual visual)) {
                if (string.IsNullOrEmpty(equip.ModelPath)) return;
                GameObject prefab = Resources.Load<GameObject>(equip.ModelPath);
                if (prefab == null) return;
                if (prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0) return;
                visual = new Visual { Model = UnityEngine.Object.Instantiate(prefab, rig, false) };
                foreach (Transform child in visual.Model.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = layer;
                foreach (var renderer in visual.Model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                    renderer.bones = bones;
                    renderer.rootBone = rootBone;
                }
                items.Add(item, visual);
            }
            if (visual.Slots.ContainsKey(slot)) return;
            var hidden = new HashSet<GameObject>();
            visual.Slots.Add(slot, hidden);

            string slotName = inventory.system.RetrieveName(slot);
            foreach (var region in equip.Regions.value)
                if (region.PlaceableSlot == slotName && region.showBaseClothing)
                    return;

            var defaults = inventory.system.Retrieve(slot).DefaultClothing.value;
            if (defaults == null) return;
            foreach (var binding in defaults) {
                string path = binding.value?.ObjectPath;
                if (string.IsNullOrEmpty(path)) continue;
                Transform target = rig.Find(path);
                if (target == null || !hidden.Add(target.gameObject)) continue;
                GameObject obj = target.gameObject;
                if (!clothing.TryGetValue(obj, out ClothingState state)) {
                    state = new ClothingState { WasActive = obj.activeSelf };
                    clothing.Add(obj, state);
                }
                state.Users++;
                obj.SetActive(false);
            }
        }

        public void Unregister(ArmorInventory.IArmorItem item, int slot) {
            if (!items.TryGetValue(item, out Visual visual) || !visual.Slots.TryGetValue(slot, out var hidden)) return;
            foreach (GameObject obj in hidden) {
                ClothingState state = clothing[obj];
                if (--state.Users != 0) continue;
                if (obj != null) obj.SetActive(state.WasActive);
                clothing.Remove(obj);
            }
            visual.Slots.Remove(slot);
            if (visual.Slots.Count != 0) return;
            UnityEngine.Object.Destroy(visual.Model);
            items.Remove(item);
        }

        public void Dispose() {
            foreach (var visual in items.Values) UnityEngine.Object.Destroy(visual.Model);
            foreach (var entry in clothing)
                if (entry.Key != null) entry.Key.SetActive(entry.Value.WasActive);
            items.Clear();
            clothing.Clear();
        }
    }
}
