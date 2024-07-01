using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PauseHandler : MonoBehaviour
{
    private static RectTransform sTransform;
    private static bool active = false;
    private void OnEnable() { 
        sTransform = this.gameObject.GetComponent<RectTransform>(); 
        Deactivate();
    }

    public void Update(){
        if(Input.GetKeyUp(KeyCode.Escape)) {
            if(active) Deactivate();
            else Activate();
        }
        if(active && Input.GetKeyDown(KeyCode.Return))
            Exit();
    }

    public static void Activate(){
        sTransform.localScale = new Vector3(1, 1, 1);
        active = true;
    }

    public static void Deactivate(){
        sTransform.localScale = new Vector3(0, 0, 0);
        active = false;
    }

    public static void Exit(){
        SceneManager.LoadScene("MainMenu");
        active = false;
    }
}
