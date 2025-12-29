using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using Arterra.Configuration;
using Arterra.Configuration.Generation.Entity;
using Arterra.Configuration.Generation.Item;
using Arterra.Core.Events;

public class ArmorInventory : IInventory {
    private Catalogue<EquipableArmor> system => Config.CURRENT.System.Armor.value.Variants;
    [JsonIgnore]
    public ArmorSlot[] Display;
    public IArmorItem[] Info;
    private ItemContext.FinishSetup OnAddElement;
    private ItemContext.FinishSetup OnRemoveElement;
    public ArmorInventory() {
        Info = new IArmorItem[system.Count()];
    }

    public IItem PeekItem(int index) => Info[index];
    public int Capacity => (int)Info.Length;

    public bool OnDamaged(Entity entity, RefTuple<(float dmg, float3 kb)> cxt) {
        foreach (IArmorItem itm in Info) {
            itm?.OnDamaged(ref cxt.Value.dmg, ref cxt.Value.kb, ref entity);
        }
        return false;
    }

    public void AddCallbacks(
        ItemContext.FinishSetup OnAddItem = null,
        ItemContext.FinishSetup OnRemoveItem = null
    ) {
        OnAddElement = OnAddItem;
        OnRemoveElement = OnRemoveItem;
    }

    public void ReapplyHandles() {
        for (int i = 0; i < Info.Length; i++) {
            if (Info[i] == null) continue;
            Info[i].OnEnter(OnAddElement(new ItemContext(this, i)));
        }
    }
    public void UnapplyHandles() {
        for (int i = 0; i < Info.Length; i++) {
            if (Info[i] == null) continue;
            Info[i].OnLeave(OnRemoveElement(new ItemContext(this, i)));
        }
    }

    public void InitializeDisplay(GameObject Parent) {
        Display = new ArmorSlot[system.Count()];
        ArmorSlot.template = Resources.Load<GameObject>("Prefabs/GameUI/Armor/ArmorSlot");
        for (int i = 0; i < system.Count(); i++) {
            Display[i] = new ArmorSlot(Parent.transform);
            AttachDisplay(i);
        }
    }
    public void ReleaseDisplay() {
        if (Display == null) return;
        for (int i = 0; i < Display.Count(); i++) {
            ClearDisplay(i);
            Display[i]?.Release();
        }
        Display = null;
    }


    public int RemoveStackableKey(int KeyIndex, int delta, Action<IItem> OnRemove = null) {
        if (delta == 0) return 0;
        int start = delta; int remainder = start;

        for (int SlotIndex = 0; SlotIndex < Info.Length; SlotIndex++) {
            IItem mat = Info[SlotIndex];
            if (mat.Index != KeyIndex) continue;

            delta = mat.AmountRaw - math.max(mat.AmountRaw - remainder, 0);
            mat.AmountRaw -= delta; remainder -= delta;
            if (OnRemove != null) {
                IItem removed = mat.Clone() as IItem;
                removed.AmountRaw = delta;
                OnRemove(removed);
            }
            if (mat.AmountRaw == 0) RemoveEntry(SlotIndex);
        }
        return start - remainder;
    }

    public bool AddEntry(IItem item, int slot) {
        if (item is not IArmorItem aItem) return false;
        item = item.Clone() as IArmorItem;
        List<EquipInfo.Slot> slots = aItem.GetEquipInfo().Regions;
        string slotName = system.RetrieveName(slot);
        if (!slots.Exists((s) => s.PlaceableSlot == slotName))
            return false;
        EquipInfo.Slot matchSlot = slots.Find(s => s.PlaceableSlot == slotName);
        int[] placeSlots = slots.FindAll(s => s.GroupReference == matchSlot.GroupReference)
            .Select(s => system.RetrieveIndex(s.PlaceableSlot)).ToArray();
        foreach (int pSlot in placeSlots) {
            if (Info[pSlot] != null)
                return false;
        }
        foreach (int pSlot in placeSlots) {
            Info[pSlot] = aItem;
            ItemContext cxt = new ItemContext(this, pSlot);
            Info[pSlot].OnEnter(OnAddElement(cxt));
            AttachDisplay(pSlot);
        }
        return true;
    }

    public bool AddEntry(IItem item, out int head) {
        head = -1;
        if (item is not IArmorItem aItem) return false;
        item = item.Clone() as IArmorItem;
        List<EquipInfo.Slot> slots = aItem.GetEquipInfo().Regions;

        Dictionary<int, bool> groups = slots.Select(s => s.GroupReference).ToDictionary(i => i, i => true);
        foreach (EquipInfo.Slot slot in slots) {
            int slotIndex = system.RetrieveIndex(slot.PlaceableSlot);
            if (Info[slotIndex] == null) continue;
            groups[slot.GroupReference] = false;
        }

        List<KeyValuePair<int, bool>> kvGs = groups.ToList();
        if (!kvGs.Exists(s => s.Value)) return false;
        int groupIndex = kvGs.Find(s => s.Value).Key;

        int[] placeSlots = slots.FindAll(s => s.GroupReference == groupIndex)
            .Select(s => system.RetrieveIndex(s.PlaceableSlot)).ToArray();
        head = placeSlots.First();

        foreach (int pSlot in placeSlots) {
            Info[pSlot] = aItem;
            ItemContext cxt = new ItemContext(this, pSlot);
            Info[pSlot].OnEnter(OnAddElement(cxt));
            AttachDisplay(pSlot);
        }
        return true;
    }

    public void RemoveEntry(int slot) {
        if (Info[slot] == null) return;

        IArmorItem item = Info[slot];
        List<EquipInfo.Slot> slots = item.GetEquipInfo().Regions;
        string slotName = system.RetrieveName(slot);
        EquipInfo.Slot matchSlot = slots.Find(s => s.PlaceableSlot == slotName);
        int[] placeSlots = slots.FindAll(s => s.GroupReference == matchSlot.GroupReference)
            .Select(s => system.RetrieveIndex(s.PlaceableSlot)).ToArray();
        foreach (int pSlot in placeSlots) {
            ClearDisplay(slot);
            ItemContext cxt = new ItemContext(this, pSlot);
            Info[pSlot].OnLeave(OnRemoveElement(cxt));
            Info[pSlot] = null;
        }
    }

    public IItem LootItem() {
        return null;
    }

    private void AttachDisplay(int index) {
        if (Info == null || index >= Info.Length) return;
        if (Display == null || index >= Display.Length) return;
        Info[index]?.AttachDisplay(Display[index].ItemFrame);
    }

    private void ClearDisplay(int index) {
        if (Info == null || index >= Info.Length) return;
        if (Display == null || index >= Display.Length) return;
        Info[index]?.ClearDisplay(Display[index].ItemFrame);
    }

    public class ArmorSlot {
        //Externally defined template for slot
        public static GameObject template;
        public float3 position => slotObj.position;
        private RectTransform slotObj;
        private GameObject SlotBloom;
        private RectTransform SlotLine;
        public Transform ItemFrame;
        public float2 velocity;
        public bool active;
        public ArmorSlot(Transform parent) {
            if (template == null) template = Resources.Load<GameObject>("Prefabs/GameUI/Armor/ArmorSlot");
            slotObj = GameObject.Instantiate(template).GetComponent<RectTransform>();
            SlotBloom = slotObj.Find("Bloom").gameObject;
            ItemFrame = slotObj.Find("Slot");
            SlotLine = slotObj.Find("Line").GetComponent<RectTransform>();
            slotObj.SetParent(parent, false);
            active = false;
        }

        public void Release() {
            if (slotObj != null) GameObject.Destroy(slotObj.gameObject);
        }

        public void SetPosition(float2 positionSS) {
            slotObj.position = new Vector3(positionSS.x, positionSS.y, 0);
            velocity = float2.zero;
        }

        public void SetScale(float scale) {
            slotObj.localScale = new Vector3(scale, scale, scale);
            float invScale = 1 / math.max(scale, 0.001f);
            SlotLine.localScale = new Vector2(1, invScale);
        }

        public void SetSelect(bool selected) {
            var settings = Config.CURRENT.System.Armor.value;
            if (selected) {
                SlotLine.GetComponent<RawImage>().color = settings.HighlightLineColor;
                SlotBloom.SetActive(true);
            } else {
                SlotLine.GetComponent<RawImage>().color = settings.BaseLineColor;
                SlotBloom.SetActive(false);
            }
        }

        public void Update(float2 origin, float2 accel, (float2, float2) bounds) {
            if (!active) slotObj.gameObject.SetActive(false);
            else slotObj.gameObject.SetActive(true);

            velocity += accel;
            velocity *= 0.9f; //friction

            slotObj.position += new Vector3(velocity.x, velocity.y, 0) * Time.deltaTime;
            float2 clampedPos = math.clamp(((float3)slotObj.position).xy, bounds.Item1, bounds.Item2);
            slotObj.position = new Vector3(clampedPos.x, clampedPos.y, slotObj.position.z);

            float length = math.length(((float3)slotObj.position).xy - origin);
            SlotLine.sizeDelta = new Vector2(SlotLine.sizeDelta.x, length);
            SlotLine.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(slotObj.position.y - origin.y, slotObj.position.x - origin.x) * Mathf.Rad2Deg - 90);
        }
    }

    public interface IArmorItem : IItem {
        public EquipInfo GetEquipInfo();
        public void OnDamaged(ref float dmg, ref float3 knockback, ref Entity attacker);
        public void OnEquipped(string armor);
        public void OnUnequipped(string armor);
    }
    [Serializable]
    public struct EquipInfo {
        public Option<List<Slot>> Regions;
        [Serializable]
        public struct Slot {
            [RegistryReference("ArmorVariants")]
            public string PlaceableSlot;
            public int GroupReference;
        }
    }
}
