using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MenuHandler : MonoBehaviour
{
    private static RectTransform sTransform;
    private void OnEnable() { sTransform = this.gameObject.GetComponent<RectTransform>(); }
    private static readonly float hidePos = -640; //px
    private static readonly float showPos = 100; //px
    private static readonly float deltaP = 2400;

    public static void Activate(Action callback = null){ SetPanel(true, callback); }
    public static void Deactivate(Action callback = null){ SetPanel(false, callback); }

    private async static void SetPanel(bool active, Action callback = null){
        Vector2 position = sTransform.anchoredPosition;
        while(active ? position.x < showPos : position.x > hidePos){
            position.x = Mathf.Clamp(position.x + deltaP * (active ? 1 : -1) * Time.deltaTime, hidePos, showPos);
            sTransform.anchoredPosition = position;
            await Task.Yield();
        }
        callback?.Invoke();
    }

    public void Quit(){ Application.Quit(); }
    public void Play() { _ = WorldStorageHandler.SaveOptions(); SceneManager.LoadScene("GameScene"); }
    public void Select(){ OptionsHandler.Deactivate(); Deactivate(() => SelectionHandler.Activate()); }
    
    public void Options(){ OptionsHandler.TogglePanel(); }
    
}
