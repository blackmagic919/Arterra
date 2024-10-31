using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static OctreeTerrain;

public class LoadingHandler : UpdateTask
{
    public static GameObject LoadingScreen;
    public static Image Background;
    public static Slider slider;
    public static TextMeshProUGUI taskText;
    private static float finishedLoad;
    public static string[] taskDescriptions = {
        "Generating Surface Data",
        "Planning Structures",
        "Combining Map Infomation",
        "Generating Terrain",
        "Filling Hills and Clouds",
        "Facilitating Generation"
    };

    //Please improve loading screen one day
    public static void Initialize(){
        LoadingScreen = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Loading"), UIOrigin.UIHandle.transform);
        Background = LoadingScreen.transform.GetComponent<Image>();
        slider = LoadingScreen.transform.GetChild(0).GetComponent<Slider>();
        taskText = LoadingScreen.transform.GetChild(1).GetComponent<TextMeshProUGUI>();
        LoadingScreen.SetActive(true);

        Background.sprite = Resources.Load<Sprite>($"Textures/BackgroundImages/Background_{Random.Range(1, 10)}");
        finishedLoad = 0;

        MainLoopUpdateTasks.Enqueue(new LoadingHandler{active = true});
    }

    public override void Update(MonoBehaviour mono)
    {
        if(RequestQueue.IsEmpty){
            LoadingScreen.SetActive(false);
            active = false;
            return;
        }

        float totalRemainingLoad = 0; 
        foreach(GenTask task in RequestQueue){
            totalRemainingLoad += taskLoadTable[task.id];
        }

        RequestQueue.TryPeek(out GenTask fTask);
        taskText.text = taskDescriptions[fTask.id];
        slider.value = finishedLoad / (totalRemainingLoad + finishedLoad);
        finishedLoad += maxFrameLoad;
    }
}
