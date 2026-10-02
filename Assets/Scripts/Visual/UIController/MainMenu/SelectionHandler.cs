using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Arterra.Core.Storage;
using static Arterra.Core.Storage.World;
using Arterra.Utils;
using Arterra.GamePlay.UI;
using System.Threading;
using System.Collections.Generic;


public class SelectionHandler : MonoBehaviour
{
    private static Animator sAnimator;
    private static RectTransform infoContent;
    private static Sprite DefaultChunkIcon;
    private static TextMeshProUGUI TitleButton;
    private static CancellationTokenSource cancel;
    private static bool active = false;

    static readonly Dictionary<GalaxyType, string> GalaxyTitles = new Dictionary<GalaxyType, string>{
        {GalaxyType.Local, "Private Worlds"},
        {GalaxyType.DirectWAN, "Personal Realms"},
        {GalaxyType.DedicatedServer, "Nexus Realms"},
    };

    private void OnEnable() { 
        HaltPendingTasks();
        sAnimator = this.gameObject.GetComponent<Animator>(); 
        infoContent = this.gameObject.transform.GetChild(0).GetChild(0).GetComponent<ScrollRect>().content.GetComponent<RectTransform>();
        TitleButton = this.gameObject.transform.Find("Title").GetComponent<TextMeshProUGUI>();
        DefaultChunkIcon = Resources.Load<Sprite>("Prefabs/SelectScreen/DefaultChunk");
        active = false;
    }


    private void OnDisable() {
        active = false;
        HaltPendingTasks();
    }

    private static void CreateSelections(){
        ReleaseSelectionInfo();
        foreach(WorldMeta meta in WORLD_SELECTION) 
            CreateWorldSelection(meta, infoContent);
    }

    public static async void Activate(Action callback = null){
        if(active) return;
        active = true;
        var animator = sAnimator;
        try { await World.EnsureInitializedAsync(); }
        catch (Exception exception) { active = false; Debug.LogException(exception); return; }
        if (!active || !animator || animator != sAnimator) return;
        TitleButton.text = GalaxyTitles[GALAXY_TYPE];

        sAnimator.SetTrigger("Unmask");
        new AnimatorAwaitTask(sAnimator, "MaskRockBreak", CreateSelections).Invoke();
        new AnimatorAwaitTask(sAnimator, "UnmaskedAnimation", () => {
            sAnimator.ResetTrigger("Unmask");
            callback?.Invoke();
        }).Invoke(); 
    }
    public static void Deactivate(Action callback = null){
        HaltPendingTasks();
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
        HaltPendingTasks();
        Deactivate(() => MenuHandler.Activate()); 
    }

    // Unity UI event entry point; await resumes on Unity's main thread.
    public static async void SwitchGalaxy() {
        if (!active) return;
        HaltPendingTasks();
        var request = new CancellationTokenSource();
        cancel = request;
        try {
            await LoadGalaxy(NextGalaxy(GALAXY_TYPE), request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (!active || cancel != request) return;
            TitleButton.text = GalaxyTitles[GALAXY_TYPE];
            CreateSelections();
        } catch (OperationCanceledException) when (request.IsCancellationRequested) {
            // A newer request or menu closure superseded this load.
        } catch (Exception exception) {
            Debug.LogException(exception);
        } finally {
            if (cancel == request) cancel = null;
            request.Dispose();
        }
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
        CreateSelections();
    }

    public static void DeleteSelected(){
        DeleteWorld();
        if(active) CreateSelections();
    }

    private static GameObject CreateWorldSelection(WorldMeta meta, RectTransform content){
        GameObject newSelection = Instantiate(Resources.Load<GameObject>("Prefabs/SelectScreen/WorldSelect"), content);
        RectTransform transform = newSelection.GetComponent<RectTransform>();

        Button info = newSelection.GetComponent<Button>();
        transform.Find("Name").GetComponent<TextMeshProUGUI>().text = meta.Name;

        string timeElapsed = TimeTextUtilty.ToTimeAgo(meta.LastAccessTime);
        transform.Find("AccessTime").GetComponent<TextMeshProUGUI>().text = timeElapsed;

        string creationTime = meta.CreationTime.ToString("dd/MM/yyyy HH:mm");
        transform.Find("CreationTime").GetComponent<TextMeshProUGUI>().text = creationTime;

        string iconPath = meta.Path + DisplayChunkPath;
        Sprite chunkIcon = meta.Type == GalaxyType.Local
            ? SaveTextureToFileUtility.LoadImageToSprite(iconPath) : null;
        if (chunkIcon == null) chunkIcon = DefaultChunkIcon;
        transform.Find("ChunkDIsplay").GetComponent<Image>().sprite = chunkIcon;

        newSelection.GetComponent<Button>().onClick.AddListener(async () => {
            if (!active) return;
            HaltPendingTasks();
            var request = new CancellationTokenSource();
            cancel = request;
            try {
                await SelectWorld(meta, request.Token);
                request.Token.ThrowIfCancellationRequested();
                if (!active || cancel != request) return;
                Deactivate(() => {
                    OptionsHandler.Activate();
                    MenuHandler.Activate();
                });
            } catch (OperationCanceledException) 
            when (request.IsCancellationRequested) {
            } catch (Exception exception) {
                Debug.LogException(exception);
            } finally {
                if (cancel == request) cancel = null;
                request.Dispose();
            }
        });
        
        return newSelection;
    }

    private static void HaltPendingTasks() {
        var pending = cancel;
        cancel = null;
        pending?.Cancel(); // The owning SwitchGalaxy call disposes it in finally.
    }
}
