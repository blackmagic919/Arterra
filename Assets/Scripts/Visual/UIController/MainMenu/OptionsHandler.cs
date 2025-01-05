using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading.Tasks;
using static WorldStorageHandler;
using UnityEngine.UI;
using TMPro;
using static SegmentedUIEditor;

public class OptionsHandler : MonoBehaviour
{
    private static Animator sAnimator;
    private static GameObject infoContent;
    private static TMP_InputField WorldName; 
    private static bool active = false;
    private void OnEnable() { 
        sAnimator = this.gameObject.GetComponent<Animator>();
        infoContent = transform.GetChild(0).GetChild(1).GetComponent<ScrollRect>().content.gameObject;
        WorldName = transform.GetChild(0).GetChild(0).GetComponent<TMP_InputField>();
        active = false;
    }    

    public static void Activate(Action callback = null){ 
        if(active) return;
        active = true;
        
        sAnimator.SetTrigger("Unmask");
        TestState("MaskRockBreak", InitializeDisplay);
        TestState("UnmaskedAnimation", () => {
            sAnimator.ResetTrigger("Unmask");
            callback?.Invoke();
        }); 
    }
    public static void Deactivate(Action callback = null){ 
        if(!active) return;
        active = false;
        
        _ = SaveOptions();
        sAnimator.SetTrigger("Mask");
        TestState("MaskedAnimation", () => {
            sAnimator.ResetTrigger("Mask");
            ReleaseDisplay(infoContent); 
            callback?.Invoke();
        });
    }

    public static void TogglePanel(){
        if(active) Deactivate();
        else Activate();
    }

    private async static void TestState(string state, Action callback = null){
        while(true) {
            if(sAnimator == null) return;
            if(sAnimator.GetCurrentAnimatorStateInfo(0).IsName(state)) break;
            await Task.Yield();
        }
        callback?.Invoke();
    }

    public static void EditName(){
        WORLD_SELECTION.First.Value = new WorldMeta{
            Id = WORLD_SELECTION.First.Value.Id,
            Name = WorldName.text, 
            Path = WORLD_SELECTION.First.Value.Path
        }; Task.Run(() => SaveMeta());
    }

    public static void Delete(){
        if(!active) return;
        SelectionHandler.DeleteSelected(); 
        //Don't call deactivate because it will save the options
        //Which Delete already does
        sAnimator.SetTrigger("Mask");
        TestState("MaskedAnimation", () => {
            ReleaseDisplay(infoContent); 
            MenuHandler.Activate();
        });
        active = false;
    }

    public static void InitializeDisplay(){
        WorldMeta cWorld = WORLD_SELECTION.First.Value;
        WorldName.text = cWorld.Name;
        WorldName.onEndEdit.AddListener((string value) => {
            cWorld.Name = value;
            WorldName.text = value;
        });

        ReleaseDisplay(infoContent);
        CreateOptionDisplay(WorldOptions.CURRENT, infoContent, (ChildUpdate cb) => { object wo = WorldOptions.CURRENT; cb.Invoke(ref wo);});
        infoContent.GetComponent<VerticalLayoutGroup>().padding.left = 0;
    }

}
