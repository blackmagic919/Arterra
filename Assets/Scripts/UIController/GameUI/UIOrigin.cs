using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class UIOrigin
{
    public static GameObject UIHandle;
    // Start is called before the first frame update
    public static void Initialize()
    {
        UIHandle = GameObject.Find("MainUI");
        LoadingHandler.Initialize();
        PauseHandler.Initialize();
        CraftingMenuController.Initialize();
    }

    public static void Release(){
        CraftingMenuController.Release();
    }
}
