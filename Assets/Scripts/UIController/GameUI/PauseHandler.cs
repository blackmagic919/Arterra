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
    private static uint Fence;
    public static void Initialize() { 
        PauseMenu = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Pause"), PlayerHandler.UIHandle.transform);
        PauseMenu.SetActive(false);

        InputPoller.AddBinding(new InputPoller.ActionBind("Pause", Activate), "1.0::Menu");
    }

    public static void Activate(float _null_){
        PauseMenu.SetActive(true);

        InputPoller.AddStackPoll(new InputPoller.ActionBind("Frame:Pause", (float _) => InputPoller.SetCursorLock(false)), "CursorLock");
        InputPoller.AddKeyBindChange(() => {
            Fence = InputPoller.AddContextFence("1.0::Menu", InputPoller.ActionBind.Exclusion.ExcludeAll);
            InputPoller.AddBinding(new InputPoller.ActionBind("Pause", Deactivate), "1.0::Menu");
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

    public static void Deactivate(float _null_){
        PaginatedUIEditor.ReleaseAllChildren(PauseContent);
        InputPoller.RemoveStackPoll("Frame:Pause", "CursorLock");
        InputPoller.AddKeyBindChange(() => InputPoller.RemoveContextFence(Fence, "1.0::Menu"));
        PauseMenu.SetActive(false);
    }

    public static void Exit(){
        SceneManager.LoadScene("MainMenu");
    }
}
