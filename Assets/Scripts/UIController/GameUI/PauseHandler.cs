using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PauseHandler
{
    private static GameObject PauseMenu;
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
            InputPoller.AddBinding("Quit", "Master", (_) => {Exit();});
        });
    }

    public static void Deactivate(){
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
