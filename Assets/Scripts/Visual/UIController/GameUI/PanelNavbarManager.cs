using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using WorldConfig;

public static class PanelNavbarManager {
    private static GameObject Menu;
    private static GameObject Content;
    private static GameObject NavBar;
    private static Dictionary<string, NavPanel> NavPanels;
    private static GameObject PanelTemp;
    private static string SelectedPanel = null;
    public static void Initialize() {
        Menu = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Navigation/NavBar"), GameUIManager.UIHandle.transform);
        PanelTemp = Resources.Load<GameObject>("Prefabs/GameUI/Navigation/BarPanel");
        Menu.SetActive(false);

        NavPanels = new Dictionary<string, NavPanel>();
        NavBar = Menu.transform.Find("Bar").Find("Viewport").Find("Content").gameObject;
        Content = Menu.transform.Find("Content").gameObject;
        SelectedPanel = null;

        Add(new CraftingMenuController(Config.CURRENT.System.Crafting), "Crafting");
    }

    public static void Release() {
        foreach (NavPanel panel in NavPanels.Values) {
            panel.Release();
        }
    }

    public static void Add(INavPanel panel, string name) {
        if (name == null) return;
        NavPanel newPanel = new NavPanel(panel, name);
        NavPanels.TryAdd(name, newPanel);
    }

    public static void Remove(string name) {
        if (name == null) return;
        if (!TryGetValue(name, out NavPanel panel)) return;

        panel.Release();
        NavPanels.Remove(name);
        if (!name.Equals(SelectedPanel)) return;
        SelectedPanel = null;
        if (NavPanels.Count == 0) return;
        SelectedPanel = NavPanels.First().Key;
        NavPanels[SelectedPanel].Activate();
    }

    public static void Activate(string selPanel = null) {
        if (NavPanels.Count == 0) return;
        if (ContainsKey(SelectedPanel))
            NavPanels[SelectedPanel].Deactivate();

        SelectedPanel = selPanel;
        if (!ContainsKey(SelectedPanel))
            SelectedPanel = NavPanels.First().Key;

        NavPanels[SelectedPanel].Activate();
        Menu.SetActive(true);
    }

    public static void Deactivate() {
        if (SelectedPanel != null && NavPanels.ContainsKey(SelectedPanel))
            NavPanels[SelectedPanel].Deactivate();
        Menu.SetActive(false);
        SelectedPanel = null;
    }

    private static bool TryGetValue(string key, out NavPanel Value) {
        Value = default;
        if (key == null) return false;
        return NavPanels.TryGetValue(key, out Value);
    }

    private static bool ContainsKey(string key) {
        if (key == null) return false;
        return NavPanels.ContainsKey(key);
    }

    public interface INavPanel {
        public void Release();
        public void Activate();
        public void Deactivate();
        public Sprite GetNavIcon();
        public GameObject GetDispContent();
    }

    private struct NavPanel {
        private GameObject PanelItem;
        private INavPanel NavHandler;
        public NavPanel(INavPanel NavHandler, string name) {
            this.NavHandler = NavHandler;
            NavHandler.GetDispContent().transform.SetParent(Content.transform, false);

            PanelItem = GameObject.Instantiate(PanelTemp, NavBar.transform);
            PanelItem.GetComponent<Image>().sprite = NavHandler.GetNavIcon();
            PanelItem.GetComponent<Button>().onClick.AddListener(() => PanelNavbarManager.Activate(name));
        }

        public void Activate() {
            NavHandler?.Activate();
            PanelItem.GetComponent<Button>().Select();
        }
        public void Deactivate() {
            NavHandler?.Deactivate();
        }
        public void Release() {
            if (PanelItem != null)
                GameObject.Destroy(PanelItem);
            NavHandler.Release();
        }
    }

}
