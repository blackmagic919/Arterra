using Arterra.Core.Network;
using Arterra.Core.Events;
using System;
using Arterra.Data.Entity.Behavior;
using System.Collections;
using Arterra.Core.Storage;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Data.Item;
using Arterra.Utils;
using Arterra.Data.Intrinsic.Mortar;
using Arterra.Configuration;
using Arterra.GamePlay.Interaction;
using Arterra.GamePlay.UI;
using Arterra.Editor;
using Arterra.GamePlay;


namespace Arterra.Data.Material
{
    /// <summary> A mortar material which can crush items put into it to produced
    /// crushed variants. </summary>
    [CreateAssetMenu(menuName = "Generation/MaterialData/Behaviors/MortarMat")]
    public class MortarMaterial : PlaceableStructureMat {
        /// <summary> The index within the <see cref="MaterialData.Names"> name registry </see> of the 
        /// texture within the texture registry of the icon displayed on the <see cref="PanelNavbarManager">Navbar</see>
        /// referring to the Container.  </summary>
        [RegistryReference("Textures")]
        public int DisplayIcon;
        /// <summary> The maximum amount of  slots this mortar will have for smashing items. Includes both inputs and outputs </summary>
        public int MaxSlotCount;
        /// <summary> The maximum amount of formulas to display in the help menu </summary>
        public int MaxRecipeSlotCount;
        /// <summary>The event that is triggered when this mortar is opened.</summary>
        public Core.Events.GameEvent OpenEvent = Core.Events.GameEvent.Action_OpenMortar;

        public override void Preset(MaterialData materialData) {
            materialData.Register(typeof(PlaceableStructureMat), this);
        }

        /// <summary>Returns a mortar inventory created using the meta constructor.</summary>
        /// <param name="GCoord">The coordinate in grid space of the material</param>
        /// <param name="constructor">The constructor used to populate the inventory</param>
        /// <returns>The container instance</returns>
        public override object ConstructMetaData(int3 GCoord, MaterialData.MetaConstructor constructor) {
            return new MortarInventory(constructor, GCoord, MaxSlotCount);
        }


        public override bool OnRemoving(int3 GCoord, Entity.Entity caller) {
            if (!NetworkManager.IsActingServer || caller == null || !caller.Is(out PlayerInventoriesBehavior player)) return false;
            MapData info = CPUMapManager.SampleMap(GCoord);
            if (info.IsNull || !info.IsSolid) return false;
            if (!VerifyStructurePlaceement(GCoord)) { DropInventoryContent(GCoord); return false; }
            if (!CPUMapManager.GetOrCreateMapMeta(GCoord, () => new MortarInventory(GCoord, MaxSlotCount), out var inventory)) return false;
            player.OpenMaterial(inventory);
            return true;
        }

        /// <summary> See <see cref="MaterialData.OnRemoved"/> for more information. </summary>
        /// <param name="amount">The map data indicating the amount of material removed
        /// and the state it was removed as</param>
        /// <param name="GCoord">The location of the map information being</param>
        /// <returns>The item to give.</returns>
        public override Item.IItem OnRemoved(int3 GCoord, in MapData amount) {
             MapData info = CPUMapManager.SampleMap(GCoord);
            if (info.IsNull || amount.IsNull) return null;
            if (!info.IsSolid) return null;

            int SolidDensity = info.SolidDensity - amount.SolidDensity;
            if (!VerifyStructurePlaceement(GCoord) || SolidDensity < CPUMapManager.IsoValue)
                DropInventoryContent(GCoord);

            return null;
        }

        private void DropInventoryContent(int3 GCoord) {
            if (!CPUMapManager.TryGetExistingMapMeta(GCoord, out MortarInventory cont))
                return;
            foreach (IItem item in cont.Inventory.Info)
                PlayerInventoriesBehavior.SpawnDrop(item, GCoord);
            CPUMapManager.SetExistingMapMeta<object>(GCoord, null);
        }


        public enum InvIndex {
            Input = 0,
            Output = 1,
        }


        // This object will be serialized into the map meta data to store the inventory of the Mortar
        public class MortarInventory : MaterialInventory {
            private static WaitForSeconds _waitForSeconds1_0 = new WaitForSeconds(1.0f);

            //public Inventory inv;
            public StackInventory Inventory;

            [JsonIgnore]
            public GameObject root;


            [JsonIgnore]
            private MortarMaterial settings;
            [JsonIgnore]
            private MortarRecipeSearch RecipeSearch;

            [JsonIgnore] protected override Inventory[] Inventories => new Inventory[] { Inventory };
            protected override void InitializeSimulation() {
                Authoring.Is(out PlaceableStructureMat material);
                settings = (MortarMaterial)material;
                if (!NetworkManager.IsActingServer) return;
                Events.AddEventHandler(GameEvent.Apply_MaterialRecipe, Match);
                Events.AddEventHandler(GameEvent.Apply_MaterialGrind, Grind);
            }

            private void Match(object actor, object target, object context) {
                if (!GetPlayer(actor, out var player) || target is not int index) return;
                var recipes = Config.CURRENT.System.MortarFormulas.Reg;
                if ((uint)index >= recipes.Count) return;
                MortarRecipeSearch.MatchRecipe(recipes[index], Inventory, player);
                FlushChanges();
            }

            private void Grind(object actor, object target, object context) {
                if (!GetPlayer(actor, out _) || context is not float elapsed) return;
                TryGrind(elapsed);
                FlushChanges();
            }

            [JsonConstructor]
            public MortarInventory() {
                //Do nothing: this is for newtonsoft deserializer
            }

            internal MortarInventory(int3 GCoord, int MaxSlotCount) : base(GCoord){
                Inventory = new StackInventory(MaxSlotCount);
                position = GCoord;
            }

            public MortarInventory(MaterialData.MetaConstructor constructor, int3 GCoord,  int MaxSlotCount) : base(GCoord){
                Inventory = new StackInventory(constructor, GCoord, MaxSlotCount);
                position = GCoord;
            }

            //Auto Grinding: To Be Added in Future
            public void StartAutoCrushingProcess() {
                return;/* Add once adding ether crystals
                if (MortarFormula.GetMatchingFormula(Inventory) == null)
                    return;
                OctreeTerrain.MainCoroutines.Enqueue(UpdateRoutine());*/
            }

            private IEnumerator UpdateRoutine() {
                while (true) {
                    yield return _waitForSeconds1_0; // Update every second
                    if (!TryGrind(1.0f)) yield break;
                }
            }

            protected override void CreateDisplay() {
                root = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Mortar/Mortar"));
                Transform mortRegion = root.transform.GetChild(0).GetChild(0);
                GameObject center = mortRegion.Find("Input").gameObject;
                RecipeSearch = new MortarRecipeSearch(settings, root.transform, settings.MaxRecipeSlotCount, recipe => RequestRecipe(Config.CURRENT.System.MortarFormulas.Reg.IndexOf(recipe)));

                Animator mortarAnimator = root.GetComponent<Animator>();
                HoldDownUI holdTrigger = mortRegion.Find("Pestle").GetComponent<HoldDownUI>();

                holdTrigger.ClearAllListeners();
                mortarAnimator.SetBool("IsGrinding", false); 
                holdTrigger.AddHeldListener(() => {
                    RequestGrind(Time.deltaTime);
                });
                holdTrigger.AddDownListener(() => {
                   mortarAnimator.SetBool("IsGrinding", true); 
                });
                holdTrigger.AddUpListener(() => {
                   mortarAnimator.SetBool("IsGrinding", false); 
                });

                Inventory.InitializeHorizontalDisplay(center, false);

            }

            protected override void ReleaseDisplay() {
                if (root == null) return;
                Inventory.ReleaseStackDisplay();
                RecipeSearch?.Deactivate();
                GameObject.Destroy(root);
                root = null;
            }

            public bool TryGrind(float deltaTime) {
                var formula = MortarFormula.GetMatchingFormula(Inventory);
                if (formula == null) return false;

                int delta;
                foreach (var input in formula.Inputs.value) {
                    IItem item = Config.CURRENT.Generation.Items.Retrieve(formula.Index(input)).Item;
                    delta = CustomUtility.GetStaggeredDelta(item.UnitSize * input.Rate * deltaTime);
                    delta = Inventory.RemoveStackableKey(formula.Index(input), delta);
                }

                foreach(var output in formula.Outputs.value) {
                    IItem item = Config.CURRENT.Generation.Items.Retrieve(formula.Index(output)).Item;
                    int amount = CustomUtility.GetStaggeredDelta(item.UnitSize * output.Rate * deltaTime);
                    item.Create(formula.Index(output),(int)math.min(amount, item.StackLimit));
                    Inventory.AddStackable(item);
                    //Run out of output space
                    if (item.AmountRaw > 0)
                        return false;
                }
                return true;
            }


            protected override GameEvent OpenEvent => settings.OpenEvent;
            public override Sprite GetNavIcon() => Config.CURRENT.Generation.Textures.Retrieve(
                settings.Names.value[settings.DisplayIcon]).self;
            public override GameObject GetDispContent() => root;

        }
    }

}