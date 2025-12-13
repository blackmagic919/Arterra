using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static TerrainGeneration.OctreeTerrain;
using WorldConfig;
using System.Threading.Tasks;
using Utils;

public static class LoadingHandler
{
    public static GameObject LoadingScreen;
    public static Image Background;
    public static Slider slider;
    public static TextMeshProUGUI taskText;
    private static IUpdateSubscriber eventTask;
    private static float finishedLoad;
    private static float progress;
    public static string[] taskDescriptions = {
        "Generating Surface Data",
        "Planning Structures",
        "Combining Map Infomation",
        "Filling Hills and Clouds",
        "Generating Terrain",
        "Facilitating Generation",
        "Plating Grass and Growing Leaves"
    };

    //Please improve loading screen one day
    public static void Initialize(){
        LoadingScreen = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Loading"), GameUIManager.UIHandle.transform);
        Background = LoadingScreen.transform.GetComponent<Image>();
        slider = LoadingScreen.transform.GetChild(0).GetComponent<Slider>();
        taskText = LoadingScreen.transform.GetChild(1).GetComponent<TextMeshProUGUI>();
        Background.sprite = Resources.Load<Sprite>($"Textures/BackgroundImages/Background_{Random.Range(1, 10)}");
        Activate();
    }

    public static void Release() => AudioManager.Instance.UpdateAmbience(0);

    public static void Activate(){
        if(eventTask != null && eventTask.Active) 
            eventTask.Active = false;
        eventTask = new IndirectUpdate(Update);
        MainLoopUpdateTasks.Enqueue(eventTask);
        LoadingScreen.SetActive(true);
        finishedLoad = 0;
        progress = 0;
    }

    public static void Update(MonoBehaviour mono)
    {
        if(RequestQueue.IsEmpty){
            LoadingScreen.SetActive(false);
            eventTask.Active = false;
            return;
        }
        float totalRemainingLoad = 0; 
        foreach(GenTask task in RequestQueue){
            totalRemainingLoad += taskLoadTable[task.id];
        }

        RequestQueue.TryPeek(out GenTask fTask);
        taskText.text = taskDescriptions[fTask.id];
        progress = finishedLoad / (totalRemainingLoad + finishedLoad);
        AudioManager.Instance.UpdateAmbience(progress);

        slider.value = progress;
        finishedLoad += Config.CURRENT.Quality.Terrain.value.maxFrameLoad;
    }
}
