using UnityEngine;

public static class GameUIManager {
    public static GameObject UIHandle;
    public static void Initialize() {
        UIHandle = GameObject.Find("MainUI");
        LoadingHandler.Initialize();
        PauseHandler.Initialize();
        GameOverHandler.Initialize();

        PanelNavbarManager.Initialize();
        InventoryController.Initialize();
        DayNightContoller.Initialize();
        PlayerStatDisplay.Initialize();
    }

}


/// <summary> An interface to abstractify the creation and 
/// handling of UI 'slots' </summary>
public interface ISlot {
    /// <summary> Attaches the UI panel to be displayed representing the Item to the UI object. </summary>
    public void AttachDisplay(Transform pSlot);
    /// <summary> This handle is called when the Display UI is to be cleared  </summary>
    public void ClearDisplay();
}