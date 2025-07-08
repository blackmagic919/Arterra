using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class GameUIManager
{
    public static GameObject UIHandle;
    public static void Initialize()
    {
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
