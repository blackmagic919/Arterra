using System;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Configuration.Generation.Material;
using Arterra.Core.Storage;
using static Arterra.Core.Player.PlayerInteraction;
using Arterra.Core.Player;
using Arterra.Core.Events;
using System.Collections.Generic;
using System.Linq;
using Arterra.Core.Terrain;

namespace Arterra.Configuration.Generation.Item
{
    [CreateAssetMenu(menuName = "Generation/Items/FishingRod")]
    public class FishingRodAuthoring : AuthoringTemplate<FishingRod> {
        /// <summary>  The maximum durability of the item, the durability it possesses when it is first
        /// created. Removing material with tools will decrease durability by the amount indicated
        /// in the tag <see cref="ToolTag"/> for more info. </summary>
        public float MaxDurability;
        /// <summary> The tag used to determine how each material's removal should be handled.
        /// A material's most specific tag that fits this enum will be used to determine
        /// how this tool effects it. </summary>
        public float MinDrawTime = 0.5f;
        public float LineLength = 20f;
        public float LineDetail = 1;
        public float LineDamping = 0.05f;
        public float FullDrawTime = 2f; //seconds to full draw
        public float MaxLaunchSpeed = 20.0f;
        public float MinLaunchSpeed = 5.0f;
        public float ReelSpeed = 0.5f;

        [RegistryReference("Entities")]
        public string HookEntity;
        public GameEvent HookEntityEvent;
        public MinimalProjectileTag HookProjectile;
        /// <summary> If none, it will always cast Hook Entity, which can be useful
        /// if we don't want the rod to take an item from the holder's inventory and cast it </summary>
        public TagRegistry.Tags BaitTag = TagRegistry.Tags.None;
        public Optional<GameObject> Model;
    }

    [Serializable]
    public class FishingRod : IItem
    {
        public uint data;
        public float durability;
        private FishingLine line;
        protected static Catalogue<Authoring> ItemInfo => Config.CURRENT.Generation.Items;
        protected static Catalogue<MaterialData> MatInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        protected static Catalogue<TextureContainer> TextureAtlas => Config.CURRENT.Generation.Textures;
        [JsonIgnore]
        public int TexIndex => TextureAtlas.RetrieveIndex(ItemInfo.Retrieve(Index).TextureName);
        private FishingRodAuthoring settings => ItemInfo.Retrieve(Index) as FishingRodAuthoring;

        [JsonIgnore]
        public int Index
        {
            get => (int)(data >> 16) & 0x7FFF;
            set => data = (data & 0x0000FFFF) | (((uint)value & 0x7FFF) << 16);
        }
        [JsonIgnore]
        public int AmountRaw
        {
            get => (int)(data & 0xFFFF);
            set => data = (data & 0x7FFF0000) | ((uint)value & 0xFFFF);
        }

        public IRegister GetRegistry() => Config.CURRENT.Generation.Items;
        public virtual object Clone() => new FishingRod { data = data, durability = durability };
        public void Create(int Index, int AmountRaw)
        {
            this.Index = Index;
            this.AmountRaw = AmountRaw;
            this.durability = settings.MaxDurability;
        }
        public void UpdateEItem() { }
        public virtual void OnEnter(ItemContext cxt) {
            durability = 100;
            if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
            if (cxt.TryGetHolder(out IEventControlled effect) && settings.Model.Enabled) 
                effect.RaiseEvent(GameEvent.Item_HoldTool, effect, this, ref settings.Model.Value);
            InputPoller.AddKeyBindChange(() => {
                InputPoller.AddBinding(new ActionBind(
                    "BowDraw", _ => StartDrawingRod(cxt),
                    ActionBind.Exclusion.ExcludeLayer), "ITEM:FishingRod:DRW", "5.0::GamePlay");
                InputPoller.AddBinding(new ActionBind(
                    "BowRelease", _ => CastLine(cxt),
                    ActionBind.Exclusion.ExcludeLayer), "ITEM:FishingRod:FIRE", "5.0::GamePlay");
                InputPoller.AddBinding(new ActionBind("ReelLine", _ => ReelLine(cxt)), "ITEM:FishingRod:REEL", "5.0::GamePlay");
                InputPoller.AddBinding(new ActionBind("ReleaseLine", _ => ReleaseLine(cxt)), "ITEM:FishingRod:RLS", "5.0::GamePlay");
            });
        }
        public virtual void OnLeave(ItemContext cxt) {
            if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;
            if (cxt.TryGetHolder(out IEventControlled effect) && settings.Model.Enabled)
                effect.RaiseEvent(GameEvent.Item_UnholdTool, effect, this, ref settings.Model.Value);
            
            if(line != null){
                if(line.Active) TryRecollectBait(cxt, line);
                line?.Release();
            }

            InputPoller.AddKeyBindChange(() => {
                InputPoller.RemoveBinding("ITEM:FishingRod:DRW", "5.0::GamePlay");
                InputPoller.RemoveBinding("ITEM:FishingRod:FIRE", "5.0::GamePlay");
                InputPoller.RemoveBinding("ITEM:FishingRod:REEL", "5.0::GamePlay");
                InputPoller.RemoveBinding("ITEM:FishingRod:RLS", "5.0::GamePlay");
                if (cxt.TryGetHolder(out IEventControlled effect))
                    effect.RaiseEvent(GameEvent.Item_ReleaseBow, effect, this);
            });
        }

        protected GameObject display;
        public virtual void AttachDisplay(Transform parent)
        {
            if (display != null)
            {
                display.transform.SetParent(parent, false);
                return;
            }

            display = Indicators.ToolItems.Get();
            display.transform.SetParent(parent, false);
            display.transform.GetComponent<UnityEngine.UI.Image>().sprite = TextureAtlas.Retrieve(ItemInfo.Retrieve(Index).TextureName).self;
            UpdateDisplay();
        }

        public virtual void ClearDisplay(Transform parent)
        {
            if (display == null) return;
            Indicators.ToolItems.Release(display);
            display = null;
        }

        private float drawTime;
        void StartDrawingRod(ItemContext cxt) {
            if (line != null && line.Active) return;

            if (cxt.TryGetHolder(out IEventControlled effect))
                effect.RaiseEvent(GameEvent.Item_DrawRod, effect, this);
            drawTime += Time.deltaTime;
        }

        private void CastLine(ItemContext cxt) {
            if (line != null && line.Active) return;

            float timeDraw = drawTime; drawTime = 0;
            if(timeDraw < settings.MinDrawTime) return;
            float drawPercent = Mathf.InverseLerp(settings.MinDrawTime, settings.FullDrawTime, timeDraw);
            float launchSpeed = Mathf.Lerp(settings.MinLaunchSpeed, settings.MaxLaunchSpeed, drawPercent);
            if (!CastHook(cxt, launchSpeed)) return;
            if (cxt.TryGetHolder(out Entity.Entity effect)) {
                effect.eventCtrl.RaiseEvent(GameEvent.Item_ReleaseRod, effect, this);
            }

            durability -= 1.0f;
            if (durability > 0) return;
            cxt.TryRemove();
        }

        private bool CastHook(ItemContext cxt, float launchSpeed) {
            if (!cxt.TryGetHolder(out Entity.Entity h)) return false;

            Entity.Entity Bait;
            var EntityReg = Config.CURRENT.Generation.Entities;
            if (TryGetBaitItem(cxt, out IItem item)) {
                Bait = new EItem.EItemEntity(item);
                Bait.Index = EntityReg.RetrieveIndex("EntityItem");
            } else {
                int entInd = EntityReg.RetrieveIndex(settings.HookEntity);
                Bait = EntityReg.Retrieve(entInd).Entity;
                Bait.Index = entInd;
            }

            settings.HookProjectile.LaunchProjectile(Bait, h, h.Forward * launchSpeed, (hook) => {
                line = new FishingLine(h, hook,
                    settings.HookEntityEvent, settings.LineLength,
                    settings.LineDetail, settings.LineDamping
                );
            });
            return true;
        }

        private void ReelLine(ItemContext cxt) {
            if (line == null || !line.Active) return;
            line.ReelLine(settings.ReelSpeed, Time.deltaTime);
            InputPoller.SuspendKeybindPropogation("ReelLine");
        }

        private void ReleaseLine(ItemContext cxt) {
            if (line == null || !line.Active) return;
            line.ReleaseLine(settings.ReelSpeed, Time.deltaTime);
            InputPoller.SuspendKeybindPropogation("ReleaseLine");
        }


        protected virtual void UpdateDisplay() {
            if (display == null) return;
            Transform durbBar = display.transform.Find("Bar");
            durbBar.GetComponent<UnityEngine.UI.Image>().fillAmount = durability / settings.MaxDurability;
        }

        private bool TryGetBaitItem(ItemContext cxt, out IItem item) {
            item = null;
            if (this.settings.BaitTag == TagRegistry.Tags.None)
                return false;
            if (!cxt.TryGetInventory(out IInventory inv))
                return false;
            if(!HoldingBaitItem(inv, cxt.InvId, out int slot))
                return false;
            item = (IItem)inv.PeekItem(slot).Clone();
            item.AmountRaw = inv.RemoveStackableSlot(slot, inv.PeekItem(slot).UnitSize);
            return true;

            bool HoldingBaitItem(IInventory inv, int start, out int slot) {
                for (slot = (start + 1) % inv.Capacity;
                    slot != start;
                    slot = (slot + 1) % inv.Capacity) {
                    slot %= inv.Capacity;
                    if (inv.PeekItem(slot) == null) continue;
                    if (!ItemInfo.GetMostSpecificTag(this.settings.BaitTag, inv.PeekItem(slot).Index, out _))
                        continue;
                    return true;
                }
                return false;
            }
        }

        private bool TryRecollectBait(ItemContext cxt, FishingLine line) {
            if (!cxt.TryGetInventory(out IInventory inv))
                return false;
            if (line.HookingEntity is not EItem.EItemEntity eItem)
                return false;
            foreach(IItem item in eItem.GetItems())
                inv.AddStackable(item);   
            EntityManager.ReleaseEntity(eItem.info.entityId);
            return true;
        }

        private class FishingLine : IUpdateSubscriber{
            public Entity.Entity Holder;
            public Entity.Entity HookingEntity;
            private bool active = false;
            public bool Active {get => active; set=> active = value;}

            private GameObject LineObject;
            private LineRenderer renderer;

            private readonly int MaxLineSegs;
            private readonly float MaxLineLength;

            public int RealSegments => Mathf.CeilToInt(LineSegments);
            private float LineSegments;
            private float LineSegLength;
            private float damping;
            private float3 hookedOffset;
            private const int SolveIters = 5;
            private const float tensionThreshold = 0.35f;
            private struct LinePoint {
                public float3 pos;
                public float3 prevPos;
            }
            private List<LinePoint> points = new();
            public FishingLine(Entity.Entity holder, Entity.Entity hook, GameEvent HookEvent,  float LineLength, float lineDetail, float damping) {
                Holder = holder;
                HookingEntity = hook;
                LineSegLength = lineDetail;
                MaxLineSegs = math.max(2, (int)(LineLength / LineSegLength));
                MaxLineLength = LineSegLength;

                this.hookedOffset = 0;
                this.LineSegments = MaxLineSegs;
                this.damping = 1 - damping; //higher damp => less transmission
                this.active = true;

                LineObject = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/FishingLine"));
                renderer = LineObject.GetComponent<LineRenderer>();
                OctreeTerrain.MainLoopUpdateTasks.Enqueue(this);

                HookingEntity.eventCtrl.AddContextlessEventHandler(HookEvent, OnHookedEntity);
                HookingEntity.eventCtrl.AddContextlessEventHandler(GameEvent.Entity_InLiquid, SinkEntity);
                InitializePoints();
            }

            private void SinkEntity(object source, object target) {
                Entity.Entity hook = source as Entity.Entity;
                //x1 to counteract upwards float force, x1 to add gravity back since it's cancelled
                hook.velocity += 2 * EntityJob.cxt.deltaTime * EntityJob.cxt.gravity;
            }
            private void OnHookedEntity(object source, object target) {
                if (target is not Entity.Entity entity) return;
                hookedOffset = HookingEntity.position - entity.position;
                HookingEntity = entity;
            }

            private void InitializePoints() {
                points.Clear();

                float3 start = Holder.position;
                float3 end = HookingEntity.position;
                float3 dir = math.normalizesafe(end - start);

                int count = math.min(RealSegments, math.max(2, (int)(math.distance(start, end) / LineSegLength)));
                LinePoint[] pList = new LinePoint[count];

                for (int i = 0; i < count; i++) {
                    float3 p = start + dir * (i * LineSegLength);
                    pList[count - (i+1)] = new LinePoint { pos = p, prevPos = p };
                } points = pList.ToList();
            }

            private float3 holderTension;
            private float3 hookTension;
            public void Update(MonoBehaviour _) {
                if (HookingEntity == null || !HookingEntity.active) Release();
                if (Holder == null || !Holder.active) Release();
                holderTension = float3.zero;
                hookTension = float3.zero;

                PreventProjectileDecay();
                Integrate(Time.deltaTime);
                SolveConstraints();
                ApplyTensionAndExtendLine();
                MatchLineRenderer();
            }

            private void Integrate(float dt) {
                for (int i = 1; i < points.Count - 1; i++) {
                    LinePoint p = points[i];

                    float3 velocity = (p.pos - p.prevPos) * damping;
                    p.prevPos = p.pos;
                    p.pos += velocity;

                    // Gravity (optional)
                    p.pos += dt * dt * (float3)Physics.gravity;

                    // Ground collision
                    p.pos += TerrainCollider.TrilinearDisplacement(
                        p.pos, EntityJob.cxt.mapContext
                    );

                    points[i] = p;
                }

                // Hard endpoints
                SetEndpoint(points.Count - 1, Holder.position);
                SetEndpoint(0, HookingEntity.position + hookedOffset);
                void SetEndpoint(int index, float3 position) {
                    LinePoint p = points[index];
                    p.pos = position;
                    p.prevPos = position;
                    points[index] = p;
                }
            }
            private void SolveConstraints(bool ignoreLast = false) {
                for (int iter = 0; iter < SolveIters; iter++) {
                    int count = points.Count;
                    if (ignoreLast) count--;
                    for (int i = 0; i < count - 1; i++) {
                        LinePoint a = points[i];
                        LinePoint b = points[i + 1];

                        float3 delta = b.pos - a.pos;
                        float dist = math.length(delta);
                        float SegmentLength = LineSegLength;
                        SegmentLength *= math.min(1, LineSegments - (i+1));

                        if (dist <= SegmentLength)
                            continue;

                        float stretch = dist - SegmentLength;
                        float3 dir = delta / dist;

                        // Break line if overstretched
                        if (stretch > LineSegLength * 2f && iter >= SolveIters - 1) {
                            BreakLine();
                            return;
                        }

                        // Internal points
                        if (i == 0) hookTension += dir * stretch;
                        else a.pos += dir * (stretch * 0.5f);
                        if (i + 1 == count - 1) holderTension -= dir * stretch;
                        else b.pos -= dir * (stretch * 0.5f);
                        points[i] = a; points[i + 1] = b;
                    }
                }
            }

            private void ApplyTensionToEntity(Entity.Entity entity, float3 tension) {
                float mag = math.length(tension);
                if (mag < LineSegLength * 0.25f)
                    return;

                float3 dir = tension / mag;
                float massFactor = 1f - entity.weight;
                entity.velocity += mag * massFactor * dir;
            }

            private void PreventProjectileDecay() {
                if (HookingEntity is not Projectile.ProjectileEntity p) return;
                p.ResetDecomposition(); //Prevent hook from despawning
            }

            private void ApplyTensionAndExtendLine() {
                ApplyTensionToEntity(HookingEntity, hookTension);

                if (points.Count >= RealSegments) {
                    ApplyTensionToEntity(Holder, holderTension);
                    return;
                }
                
                float tension = math.length(holderTension);
                if (tension < tensionThreshold)
                    return;


                // Insert new segment near holder
                LinePoint a = points[^1];
                LinePoint b = points[^2];

                float3 dir = math.normalizesafe(a.pos - b.pos);
                a.pos += dir * LineSegLength;
                a.prevPos = a.pos;

                points.Add(new() {
                    pos     = Holder.position,
                    prevPos = Holder.position
                });
            }

            private void MatchLineRenderer() {
                if (renderer == null || points.Count == 0)
                    return;

                if (renderer.positionCount != points.Count)
                    renderer.positionCount = points.Count;

                for (int i = 0; i < points.Count; i++) {
                    float3 pos = CPUMapManager.GSToWS(points[i].pos);
                    renderer.SetPosition(i, pos);
                }
            }

            public void ReelLine(float reelSpeed, float dt) {
                // Reduce total allowed segments (continuous)
                LineSegments = math.min(LineSegments, points.Count()); //Make sure we don't extra reel
                LineSegments -= (reelSpeed * dt) / MaxLineLength * MaxLineSegs;
                LineSegments = math.clamp(LineSegments, 2f, MaxLineSegs);

                while (points.Count > RealSegments) {
                    points.RemoveAt(points.Count - 2);
                }
            }

            public void ReleaseLine(float reelSpeed, float dt) {
                LineSegments += (reelSpeed * dt) / MaxLineLength * MaxLineSegs;
                LineSegments = math.clamp(LineSegments, 2f, MaxLineSegs);
            }

            public void Release() {
                GameObject.Destroy(LineObject);
                active = false;
            }


            private void BreakLine() {
                Release();
            }
        }
    }
}
