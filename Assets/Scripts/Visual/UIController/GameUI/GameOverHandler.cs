using System;
using System.Threading.Tasks;
using TerrainGeneration;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using WorldConfig;

public class GameOverHandler
{
    private static GameObject EndMenu;
    private static Button RespawnButton => EndMenu.transform.Find("Respawn").gameObject.GetComponent<Button>();
    private static Button QuitButton => EndMenu.transform.Find("Quit").gameObject.GetComponent<Button>();
    private static uint Fence;
    
    public static void Initialize() { 
        EndMenu = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/GameOver"), GameUIManager.UIHandle.transform);
        EndMenu.SetActive(false);

        RespawnButton.onClick.AddListener(Deactivate);
        QuitButton.onClick.AddListener(Exit);
    }

    public static void Activate(){
        InputPoller.AddStackPoll(new ActionBind("Frame:GameOver", (float _) => InputPoller.SetCursorLock(false)), "CursorLock");
        InputPoller.AddKeyBindChange(() =>InputPoller.AddContextFence("GameOver", "1.0::Menu", ActionBind.Exclusion.ExcludeAll));
        EndMenu.SetActive(true);
    }

    public static void Deactivate(){
        InputPoller.RemoveStackPoll("Frame:GameOver", "CursorLock");
        InputPoller.AddKeyBindChange(() => InputPoller.RemoveContextFence("GameOver", "1.0::Menu"));
        EndMenu.SetActive(false);
        PlayerHandler.RespawnPlayer(LoadingHandler.Activate);
    }

    public static void Exit(){
        PlayerHandler.RespawnPlayer(() => {
            SceneManager.LoadScene("MainMenu");
        });
    }
}
