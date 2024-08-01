using System;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;
using UnityEngine.UI;
using TMPro;
using static WorldStorageHandler;
using UnityEditor;

public class SelectionHandler : MonoBehaviour
{
    private static RectTransform sTransform;
    private static RectTransform infoContent;
    private static bool initialized = true;

    private void OnEnable() { 
        sTransform = this.gameObject.GetComponent<RectTransform>(); 
        infoContent = this.gameObject.transform.GetChild(0).GetComponent<ScrollRect>().content.GetComponent<RectTransform>();
        WorldStorageHandler.Activate();
        initialized = true;
    }

    private void Update(){
        if(initialized) return;

        ReleaseSelectionInfo();
        foreach(WorldMeta meta in WORLD_SELECTION) 
            CreateWorldSelection(meta, infoContent);
        initialized = true;
    }

    private static float hidePos = -1680; //px
    private static float showPos = 0; //px
    private static float deltaP = 2400;

    public static void Activate(Action callback = null){
        SetPanel(true, callback);
        initialized = false;
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
            position.y = Mathf.Clamp(position.y + deltaP * (active ? 1 : -1) * Time.deltaTime, hidePos, showPos);
            sTransform.anchoredPosition = position;
            await Task.Yield();
        }
        callback?.Invoke();
    }

    public static void Return() { Deactivate(() => MenuHandler.Activate()); }

    private static void ReleaseSelectionInfo(){
        foreach(Transform child in infoContent){ 
            Destroy(child.gameObject); 
        }
        infoContent.sizeDelta = new Vector2(infoContent.sizeDelta.x, 0);
    }

    public static void AddWorld(){
        CreateWorld();
        initialized = false;
    }

    public static void DeleteSelected(){
        DeleteWorld();
        initialized = false;
    }

    private static GameObject CreateWorldSelection(WorldMeta meta, RectTransform content){
        GameObject newSelection = Instantiate(Resources.Load<GameObject>("Prefabs/WorldSelect"), content);
        RectTransform transform = newSelection.GetComponent<RectTransform>();

        Button info = newSelection.GetComponent<Button>();
        info.GetComponentInChildren<TextMeshProUGUI>().text = meta.Name;

        newSelection.GetComponent<Button>().onClick.AddListener(() => { 
            SelectWorld(meta);
            Deactivate(() => {OptionsHandler.Activate(); MenuHandler.Activate();});
        });
        
        return newSelection;
    }


}
