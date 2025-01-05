using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuHandler : MonoBehaviour
{
    private static RectTransform sTransform;
    private static Animator sAnimator;
    private static bool active = false;
    private void OnEnable() { 
        sTransform = this.gameObject.GetComponent<RectTransform>(); 
        sAnimator = this.gameObject.GetComponent<Animator>();
        active = true; sAnimator.SetTrigger("Unmask");
    }

    public static void Activate(Action callback = null){ 
        if(active) return;
        active = true;

        TestState("MaskedAnimation", () => {
            sAnimator.SetTrigger("Unmask");
            TestState("UnmaskedAnimation", callback); 
        });
    }
    public static void Deactivate(Action callback = null){ 
        if(!active) return;
        active = false;

        TestState("UnmaskedAnimation", () => {
            sAnimator.SetTrigger("Mask");
            TestState("MaskedAnimation", callback);
        });
    }

    private async static void TestState(string state, Action callback = null){
        while(sAnimator != null && !sAnimator.GetCurrentAnimatorStateInfo(0).IsName(state)) {
            await Task.Yield();
        }
        callback?.Invoke();
    }

    public void Quit(){ 
        if(!active) return; 
        Application.Quit(); 
    }
    public void Play() { 
        if(!active) return;
        _ = WorldStorageHandler.SaveOptions(); 
        SceneManager.LoadScene("GameScene");
    }
    public void Select(){ 
        if(!active) return;
        OptionsHandler.Deactivate(); 
        Deactivate(() => SelectionHandler.Activate()); 
    }
    
    public void Options(){ 
        if(!active) return; 
        OptionsHandler.TogglePanel(); 
    }
    
}
