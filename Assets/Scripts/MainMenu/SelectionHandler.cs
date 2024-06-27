using System;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;
using UnityEngine.UI;
using TMPro;
using static WorldStorageHandler;

public class SelectionHandler : MonoBehaviour
{
    private static RectTransform sTransform;
    private static RectTransform infoContent;
    private static WorldData[] worlds;
    private static int selected = -1;
    private static bool initialized = false;

    private void OnEnable() { 
        sTransform = this.gameObject.GetComponent<RectTransform>(); 
        infoContent = this.gameObject.transform.GetChild(0).GetComponent<ScrollRect>().content.GetComponent<RectTransform>();
    }

    private void Update(){
        if(!initialized && worlds != null){
            ReleaseSelectionInfo();
            for(int i = 0; i < worlds.Length; i++) CreateWorldSelection(worlds[i], infoContent, i);
            initialized = true;
        }
    }

    private static float hidePos = -1680; //px
    private static float showPos = 0; //px
    private static float deltaP = 10;

    public static void Activate(Action callback = null){
        SetPanel(true, callback);
        InitializeSelectionInfo();
    }
    public static void Deactivate(Action callback = null){
        SetPanel(false, () => {
            ReleaseSelectionInfo(); 
            callback?.Invoke();
        });
    }


    private static async void SetPanel(bool active, Action callback = null){
        Vector2 position = sTransform.anchoredPosition;
        while(active ? position.y < showPos : position.y > hidePos){
            position.y = Mathf.Clamp(position.y + deltaP * (active ? 1 : -1), hidePos, showPos);
            sTransform.anchoredPosition = position;
            await Task.Yield();
        }
        callback?.Invoke();
    }

    public static void Return() { Deactivate(() => MenuHandler.Activate()); }

    public static WorldData GetSelected(){
        if(selected == -1) {
            WorldData newWorld = CreateWorld();
            
            if(worlds == null) worlds = new WorldData[1];
            else Array.Resize(ref worlds, worlds.Length + 1);
            worlds[^1] = newWorld; 
            selected = worlds.Length - 1;
        };

        return worlds[selected];
    }

    public static void SetSelected(WorldData data){
        if(selected == -1) return;
        worlds[selected] = data;
    }

    public static void SaveWorld(WorldData world){
        worlds[selected] = world;
        Task.Run(() => SaveMeta(world));
    }

    private static void InitializeSelectionInfo(){
        Task<WorldData[]> loadTask = LoadMeta();

        loadTask.ContinueWith(task => {
            worlds = task.Result;
            initialized = false;
        });
    }

    private static void ReleaseSelectionInfo(){
        foreach(Transform child in infoContent){ 
            Destroy(child.gameObject); 
        }
        infoContent.sizeDelta = new Vector2(infoContent.sizeDelta.x, 0);
    }

    public static void AddWorld(){
        WorldData newWorld = CreateWorld();
        Array.Resize(ref worlds, worlds.Length + 1);
        worlds[^1] = newWorld;
        initialized = false;
    }

    static WorldData CreateWorld(){
        string id = Guid.NewGuid().ToString();
        return new WorldData(
            id,
            WorldStorageHandler.BASE_LOCATION + "WorldData_" + id,
            "New World",
            UnityEngine.Random.Range(int.MinValue, int.MaxValue)
        );
    }

    public static void DeleteSelected(){
        if(selected == -1) return;
        DeleteMeta(worlds[selected]);

        ReleaseSelectionInfo();
        List<WorldData> temp = new List<WorldData>(worlds); temp.RemoveAt(selected);
        worlds = temp.ToArray(); initialized = false; selected = -1;
    }

    private static GameObject CreateWorldSelection(WorldData data, RectTransform content, int index){
        GameObject newSelection = Instantiate(Resources.Load<GameObject>("Prefabs/WorldSelect"), content);
        RectTransform transform = newSelection.GetComponent<RectTransform>();
        transform.anchoredPosition = new Vector2(0, -index * transform.sizeDelta.y);

        Button info = newSelection.GetComponent<Button>();
        info.GetComponentInChildren<TextMeshProUGUI>().text = data.Name;

        newSelection.GetComponent<Button>().onClick.AddListener(() => { 
            Deactivate(() => {OptionsHandler.Activate(); MenuHandler.Activate();});
            selected = index;
        });

        content.sizeDelta += new Vector2(0, transform.sizeDelta.y);
        return newSelection;
    }

    public static void LoadWorld(){
        WorldStorageHandler.SetOptions(GetSelected());
    }


}
