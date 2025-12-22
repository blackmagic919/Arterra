using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Arterra.Core.Storage;
using static Arterra.Core.Storage.World;


public class SelectionHandler : MonoBehaviour
{
    private static Animator sAnimator;
    private static RectTransform infoContent;
    private static Sprite DefaultChunkIcon;
    private static bool active = false;

    private void OnEnable() { 
        sAnimator = this.gameObject.GetComponent<Animator>(); 
        infoContent = this.gameObject.transform.GetChild(0).GetChild(0).GetComponent<ScrollRect>().content.GetComponent<RectTransform>();
        DefaultChunkIcon = Resources.Load<Sprite>("Prefabs/SelectScreen/DefaultChunk");
        World.Activate();
        active = false;
    }


    private static void InitializeDisplay(){
        ReleaseSelectionInfo();
        foreach(WorldMeta meta in WORLD_SELECTION) 
            CreateWorldSelection(meta, infoContent);
    }

    public static void Activate(Action callback = null){
        if(active) return;
        active = true;

        sAnimator.SetTrigger("Unmask");
        new AnimatorAwaitTask(sAnimator, "MaskRockBreak", InitializeDisplay).Invoke();
        new AnimatorAwaitTask(sAnimator, "UnmaskedAnimation", () => {
            sAnimator.ResetTrigger("Unmask");
            callback?.Invoke();
        }).Invoke(); 
    }
    public static void Deactivate(Action callback = null){
        if(!active) return;
        active = false;

        sAnimator.SetTrigger("Mask");
        new AnimatorAwaitTask(sAnimator,
            "MaskedAnimation", () => {
            sAnimator.ResetTrigger("Mask");
            ReleaseSelectionInfo();
            callback?.Invoke();
        }).Invoke();
    }

    public static void Return() { 
        if(!active) return;
        Deactivate(() => MenuHandler.Activate()); 
    }

    private static void ReleaseSelectionInfo(){
        foreach(Transform child in infoContent){ 
            Destroy(child.gameObject); 
        }
        infoContent.sizeDelta = new Vector2(infoContent.sizeDelta.x, 0);
    }

    public static void AddWorld(){
        if (!active) return;
        CreateWorld();
        InitializeDisplay();
    }

    public static void DeleteSelected(){
        DeleteWorld();
        if(active) InitializeDisplay();
    }

    private static GameObject CreateWorldSelection(WorldMeta meta, RectTransform content){
        GameObject newSelection = Instantiate(Resources.Load<GameObject>("Prefabs/SelectScreen/WorldSelect"), content);
        RectTransform transform = newSelection.GetComponent<RectTransform>();

        Button info = newSelection.GetComponent<Button>();
        transform.Find("Name").GetComponent<TextMeshProUGUI>().text = meta.Name;

        string timeElapsed = Utils.TimeUtility.ToTimeAgo(meta.LastAccessTime);
        transform.Find("AccessTime").GetComponent<TextMeshProUGUI>().text = timeElapsed;

        string creationTime = meta.CreationTime.ToString("dd/MM/yyyy HH:mm");
        transform.Find("CreationTime").GetComponent<TextMeshProUGUI>().text = creationTime;

        string iconPath = meta.Path + DisplayChunkPath;
        Sprite chunkIcon = Utils.SaveTextureToFileUtility.LoadImageToSprite(iconPath);
        if (chunkIcon == null) chunkIcon = DefaultChunkIcon;
        transform.Find("ChunkDIsplay").GetComponent<Image>().sprite = chunkIcon;

        newSelection.GetComponent<Button>().onClick.AddListener(() => { 
            if(!active) return;
            SelectWorld(meta);
            Deactivate(() => {
                OptionsHandler.Activate();
                MenuHandler.Activate();
            });
        });
        
        return newSelection;
    }

}
