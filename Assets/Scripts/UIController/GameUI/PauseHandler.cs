using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PauseHandler
{
    private static GameObject PauseMenu;
    private static GameObject PauseContent => PauseMenu.transform.Find("Content").gameObject;
    private static Queue<(string, uint)> Fences;
    public static void Initialize() { 
        PauseMenu = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Pause"), PlayerHandler.UIHandle.transform);
        PauseMenu.SetActive(false);
        InputPoller.SetCursorLock(true);
        Fences = new Queue<(string, uint)>();

        InputPoller.AddBinding("Pause", "Master", (_) => {Activate();});
        
    }

    public static void Activate(){
        PauseMenu.SetActive(true);
        InputPoller.SetCursorLock(false);

        InputPoller.AddKeyBindChange(() => {
            Fences.Enqueue(("GamePlay", InputPoller.AddContextFence("GamePlay")));
            Fences.Enqueue(("UI", InputPoller.AddContextFence("UI")));
            Fences.Enqueue(("Control", InputPoller.AddContextFence("Control")));
            Fences.Enqueue(("Master", InputPoller.AddContextFence("Master")));
            InputPoller.AddBinding("Pause", "Master", (_) => {Deactivate();});
        });

        Option<WorldOptions.GamePlaySettings> settings = WorldStorageHandler.WORLD_OPTIONS._GamePlay;
        PaginatedUIEditor.CreateProceduralPagination(settings.value, PauseContent, (ChildUpdate cb) => { 
            settings.Clone(); object obj = settings.value;
            cb.Invoke(ref obj); 
            settings.value = (WorldOptions.GamePlaySettings)obj;
            WorldStorageHandler.WORLD_OPTIONS._GamePlay = settings;
            WorldStorageHandler.SaveOptionsSync();
        }, () => {Exit();});

        GameObject ExitButton = PauseContent.transform.GetChild(0).Find("TopPanel").Find("Return").GetChild(0).gameObject;
        TextMeshProUGUI ExitText = ExitButton.GetComponent<TextMeshProUGUI>();
        ExitText.text = "Quit";
        ExitText.color = Color.red;

    }

    public static void Deactivate(){
        PaginatedUIEditor.ReleaseAllChildren(PauseContent);
        PauseMenu.SetActive(false);
        InputPoller.SetCursorLock(true);
        InputPoller.AddKeyBindChange(() => {
            while(Fences.Count > 0){
                var (context, fence) = Fences.Dequeue();
                InputPoller.RemoveContextFence(fence, context);
            }
        });
    }

    public static void Exit(){
        SceneManager.LoadScene("MainMenu");
    }
}
