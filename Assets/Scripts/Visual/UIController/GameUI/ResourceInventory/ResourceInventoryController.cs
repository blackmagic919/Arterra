using Arterra.Configuration;
using Arterra.Data.Intrinsic;
using Arterra.Data.Item;
using Arterra.GamePlay.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Arterra.GamePlay.UI {
    public sealed class ResourceInventoryController : PanelNavbarManager.INavPanel {
        private static ResourceInventoryController Instance {get; set;}
        private RegistrySearchDisplay<Authoring> ItemSearch;
        private ResourceInventory settings;
        private GameObject InventoryRoot;

        private Transform Menu;
        private Transform SearchContainer;
        private TMP_InputField SearchInput;
        private static bool Enabled;

        public static void Initialize() {
            Enabled = false;
            Instance = null;
            Config.CURRENT.System.AddHook("Gamemode:ResourceInventory", OnResourceInventoryRuleChanged);
            object EnableInventory = Config.CURRENT.GamePlay.Gamemodes.value.ResourceInventory;
            OnResourceInventoryRuleChanged(ref EnableInventory);
        }

        private static void OnResourceInventoryRuleChanged(ref object rule) {
            bool EnableInventory = (bool)rule; 
            if (Enabled == EnableInventory) return;
            Enabled = EnableInventory;
            Instance ??= new ResourceInventoryController(Config.CURRENT.System.ResourceInventory);

            if (Enabled) PanelNavbarManager.Add(Instance, "ResourceInventory");
            else {
                Instance.Deactivate();
                PanelNavbarManager.Remove("ResourceInventory");
            }
        }

        public ResourceInventoryController(ResourceInventory settings) {
            this.settings = settings;
            InventoryRoot = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/ResourceInventory/Menu"));
            Menu = InventoryRoot.transform.Find("SearchArea");
            SearchInput = Menu.Find("SearchBar").GetComponentInChildren<TMP_InputField>();
            SearchContainer = Menu.Find("RecipeShelf").GetChild(0).GetChild(0);
            InventoryRoot.SetActive(false);

            Registry<Authoring> registry = Registry<Authoring>.FromCatalogue(Config.CURRENT.Generation.Items);
            GridUIManager RecipeContainer = new GridUIManager(SearchContainer.gameObject,
                Indicators.ItemSlots.Get,
                Indicators.ItemSlots.Release,
                settings.MaxResourceSearchDisplay
            );
            
            ItemSearch = new RegistrySearchDisplay<Authoring>(
                registry, Menu, SearchInput, RecipeContainer
            );
            Button prevButton = Menu.Find("PreviousPage").GetComponent<Button>();
            Button nextButton = Menu.Find("NextPage").GetComponent<Button>();
            ItemSearch.AddPaginateButtons(prevButton, nextButton);
        }

        private void ResourceInputSelect(float _) {
            if (!SearchContainer.gameObject.activeSelf) return;
            if (!ItemSearch.GridContainer.GetMouseSelected(out int index))
                return;
            Authoring author = ItemSearch.SlotEntries[index];
            IItem item = author.Item;
            item.Create(Config.CURRENT.Generation.Items.RetrieveIndex(author.Name), item.StackLimit);
            InventoryController.Cursor.ClearCursor(InventoryController.AddEntry);
            InventoryController.Cursor.HoldItem(item);
            InputPoller.SuspendKeybindPropogation("Select");
        }

        public void Activate() {
            InputPoller.AddKeyBindChange(() => {
                InputPoller.AddBinding(new ActionBind("Select",
                    ResourceInputSelect, ActionBind.Exclusion.None),
                    "ResourceInventory:SEL", "3.5::Window");
            });
            InventoryRoot.SetActive(true);
            ItemSearch.Activate();
        }

        public void Deactivate() {
            InputPoller.AddKeyBindChange(() => InputPoller.RemoveBinding("ResourceInventory:SEL", "3.5::Window"));
            InventoryRoot.SetActive(false);
            ItemSearch.Deactivate();
        }
        

        public void Release(){}
        public Sprite GetNavIcon() => Config.CURRENT.Generation.Textures.Retrieve(settings.DisplayIcon).self;
        public GameObject GetDispContent() => InventoryRoot;
    }
}