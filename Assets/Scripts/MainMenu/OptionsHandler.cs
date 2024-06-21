using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading.Tasks;
using static WorldStorageHandler;
using UnityEngine.UI;
using TMPro;
using System.Reflection;
using UnityEditor.UIElements;
using UnityEngine.PlayerLoop;

public class OptionsHandler : MonoBehaviour
{
    private static RectTransform sTransform;
    private static GameObject infoContent;
    private void OnEnable() { 
        sTransform = this.gameObject.GetComponent<RectTransform>(); 
        infoContent = sTransform.GetChild(1).GetComponent<ScrollRect>().content.gameObject;
    }

    private static readonly float hidePos = 640; //px
    private static readonly float showPos = -100; //px
    private static readonly float deltaP = 10;
    private static bool active = false;

    public static void Activate(Action callback = null){ 
        if(active) return;
        SetPanel(true, callback); 
        InitializeDisplay(); 
        active = true;
    }
    public static void Deactivate(Action callback = null){ 
        if(!active) return;
        SelectionHandler.GetSelected().Save();
        SetPanel(false, () => {
            ReleaseDisplay(infoContent); 
            callback?.Invoke();
        });
        active = false;
    }

    public static void TogglePanel(){
        if(active) Deactivate();
        else Activate();
    }

    public async static void SetPanel(bool active, Action callback = null){
        Vector2 position = sTransform.anchoredPosition;
        while(active ? position.x > showPos : position.x < hidePos){
            position.x = Mathf.Clamp(position.x + deltaP * (active ? -1 : 1), showPos, hidePos);
            sTransform.anchoredPosition = position;
            await Task.Yield();
        }
        callback?.Invoke();
    }


    public static void EditName(){
        TMP_InputField inputField = sTransform.GetChild(0).GetComponent<TMP_InputField>();
        WorldData world = SelectionHandler.GetSelected(); world.Name = inputField.text;
    }

    public static void Delete(){
        Deactivate(() => MenuHandler.Activate());
        SelectionHandler.DeleteSelected(); 
    }

    private static void ReleaseDisplay(GameObject content){
        foreach(Transform child in content.transform){ 
            if(child.gameObject.name.Contains("Option")) Destroy(child.gameObject); 
        }
    }


    //https://forum.unity.com/threads/layoutgroup-does-not-refresh-in-its-current-frame.458446/
    private static async void ForceLayoutRefresh(Transform content){
        await Task.Yield(); //For some reason layout refresh must be delayed
        while(content != null && content.GetComponent<VerticalLayoutGroup>() != null){
            LayoutRebuilder.ForceRebuildLayoutImmediate(content.GetComponent<RectTransform>());
            content = content.transform.parent;
        }
    }

    public static void InitializeDisplay(){
        WorldData cWorld = SelectionHandler.GetSelected();
        TMP_InputField inputField = sTransform.GetChild(0).GetComponent<TMP_InputField>();
        inputField.text = cWorld.Name;
        inputField.onEndEdit.AddListener((string value) => {
            cWorld.Name = value;
            inputField.text = value;
            SelectionHandler.SetSelected(cWorld);
        });

        CreateOptionDisplay(cWorld.WorldOptions, infoContent);
    }
    


    static void CreateOptionDisplay(object setting,  GameObject content, Func<object> OnUpdate = null){
        System.Reflection.FieldInfo[] fields = setting.GetType().GetFields();
        SetUpLayout(content);

        for(int i = 0; i < fields.Length; i++){
            if(Attribute.IsDefined(fields[i], typeof(HideInInspector))) continue;
            
            System.Reflection.FieldInfo field = fields[i]; FieldInfo oField = field;
            object value = field.GetValue(setting); object cObject = setting; Func<object> nUpdate = OnUpdate;
            GameObject newOption = Instantiate(Resources.Load<GameObject>("Prefabs/Option"), content.transform);
            RectTransform transform = newOption.GetComponent<RectTransform>();
            newOption.GetComponent<TextMeshProUGUI>().text = field.Name;
            
            //Extract the value of the option
            //cObject is the Option Field(value type)
            //setting is the class containing the option
            //value is the class held by the option
            if(field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(WorldOptions.Option<>)) {
                cObject = field.GetValue(setting); //Would prefer if GetValueDirect was implemented 
                field = value.GetType().GetField("value"); 
                value = field.GetValue(value);
                object UpdateCopy() {
                    if(OnUpdate != null) setting = OnUpdate();
                    ((WorldOptions.IOption)cObject).Clone(); 
                    oField.SetValue(setting, cObject);
                    return field.GetValue(cObject); 
                    //because value is a class, the value set to setting from cObject is a reference
                    //which can be directly achieved from cObject
                } nUpdate = UpdateCopy;
            }

            CreateInputField(field, newOption, value, nUpdate);
        }  
    }

    public static void CreateInputField(FieldInfo field, GameObject parent, object value, Func<object> OnUpdate = null){
        switch (field.FieldType){
            case Type t when t == typeof(int):
                TMP_InputField inputField = Instantiate(Resources.Load<GameObject>("Prefabs/Text_Input"), parent.transform).GetComponent<TMP_InputField>(); inputField.text = value.ToString();
                inputField.onEndEdit.AddListener((string value) => { field.SetValue(OnUpdate(), int.Parse(value)); });
                break;
            case Type t when t == typeof(float):
                TMP_InputField inputField2 = Instantiate(Resources.Load<GameObject>("Prefabs/Text_Input"), parent.transform).GetComponent<TMP_InputField>(); inputField2.text = value.ToString();
                inputField2.onEndEdit.AddListener((string value) => { field.SetValue(OnUpdate(), float.Parse(value)); });
                break;
            case Type t when t == typeof(string):
                TMP_InputField inputField3 = Instantiate(Resources.Load<GameObject>("Prefabs/Text_Input"), parent.transform).GetComponent<TMP_InputField>(); inputField3.text = value.ToString();
                inputField3.onEndEdit.AddListener((string value) => { field.SetValue(OnUpdate(), value); });
                break;
            case Type t when t == typeof(bool):
                Toggle toggleField = Instantiate(Resources.Load<GameObject>("Prefabs/Bool_Input"), parent.transform).GetComponent<Toggle>(); toggleField.isOn = (bool)value;
                toggleField.onValueChanged.AddListener((bool value) => { field.SetValue(OnUpdate(), value); });
                break;
            case Type t when t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>):
                Button buttonField1 = Instantiate(Resources.Load<GameObject>("Prefabs/Drop_Arrow"), parent.transform).GetComponent<Button>(); bool isOpen1 = false;
                buttonField1.onClick.AddListener(() => { 
                    isOpen1 = !isOpen1;
                    if(isOpen1) CreateList(field, (IList)value, parent, OnUpdate);
                    else ReleaseDisplay(parent); 
                    ForceLayoutRefresh(parent.transform);
                });
                break;
            default:
                Button buttonField = Instantiate(Resources.Load<GameObject>("Prefabs/Drop_Arrow"), parent.transform).GetComponent<Button>(); bool isOpen = false;
                buttonField.onClick.AddListener(() => {
                    isOpen = !isOpen;
                    if(isOpen) CreateOptionDisplay(value, parent, OnUpdate);
                    else { ReleaseDisplay(parent); }
                    ForceLayoutRefresh(parent.transform);
                });
                break;
        }
    }

    private static void SetUpLayout(GameObject content){
        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        ContentSizeFitter filter = content.GetComponent<ContentSizeFitter>();
        if(layout == null) layout = content.AddComponent<VerticalLayoutGroup>();
        if(filter == null) filter = content.AddComponent<ContentSizeFitter>();
        layout.childControlHeight = false;
        layout.childAlignment = TextAnchor.UpperCenter;
        filter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private static void CreateList(FieldInfo oField, IList list, GameObject parent, Func<object> OnUpdate){
        SetUpLayout(parent);
        for(int i = 0; i < list.Count; i++) {
            GameObject key = Instantiate(Resources.Load<GameObject>("Prefabs/Option"), parent.transform);
            key.GetComponent<TextMeshProUGUI>().text = "Element " + i.ToString() + ": ";

            object cObject = list[i]; int index = i;
            FieldInfo field = cObject.GetType().GetField("value");
            object value = field.GetValue(cObject);

            object NewUpdate() {
                object setting = OnUpdate();
                WorldOptions.IOption lOption = (WorldOptions.IOption)cObject; lOption.Clone(); 
                list[index] = lOption; oField.SetValue(setting, list);

                return field.GetValue(lOption); 
            }

            CreateInputField(field, key, value, NewUpdate);
        }
    }

}
