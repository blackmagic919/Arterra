using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using Arterra.Config;

public class PauseHandler
{
    private static GameObject PauseMenu;
    private static GameObject PauseContent => PauseMenu.transform.Find("Content").gameObject;
    public static void Initialize() { 
        PauseMenu = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Pause"), GameUIManager.UIHandle.transform);
        PauseMenu.SetActive(false);

        InputPoller.AddBinding(new ActionBind("Pause", Activate), "PauseMenu:OPN", "1.0::Menu");
    }

    public static void Activate(float _null_){
        PauseMenu.SetActive(true);

        InputPoller.AddStackPoll(new ActionBind("Frame:Pause", (float _) => InputPoller.SetCursorLock(false)), "CursorLock");
        InputPoller.AddKeyBindChange(() => {
            InputPoller.AddContextFence("PauseMenu", "1.0::Menu", ActionBind.Exclusion.ExcludeAll);
            InputPoller.AddBinding(new ActionBind("Pause", Deactivate), "PauseMenu:CLS", "1.0::Menu");
        });

        Option<Config.GamePlaySettings> settings = Config.CURRENT._GamePlay;
        PaginatedUIEditor.CreateProceduralPagination(settings.value, PauseContent, (ChildUpdate cb) => { 
            settings.Clone(); object obj = settings.value;
            cb.Invoke(ref obj); 
            settings.value = (Config.GamePlaySettings)obj;
            Config.CURRENT._GamePlay = settings;
            Arterra.Core.Storage.World.SaveOptionsSync();
        }, () => {Exit();});

        GameObject ExitButton = PauseContent.transform.GetChild(0).Find("TopPanel").Find("Return").GetChild(0).gameObject;
        TextMeshProUGUI ExitText = ExitButton.GetComponent<TextMeshProUGUI>();
        ExitText.text = "Quit";
        ExitText.color = Color.red;

    }

    public static void Deactivate(float _null_){
        PaginatedUIEditor.ReleaseAllChildren(PauseContent);
        InputPoller.RemoveStackPoll("Frame:Pause", "CursorLock");
        InputPoller.AddKeyBindChange(() => InputPoller.RemoveContextFence("PauseMenu", "1.0::Menu"));
        PauseMenu.SetActive(false);
    }

    public static void Exit(){
        SceneManager.LoadScene("MainMenu");
    }
}
