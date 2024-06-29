using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading.Tasks;
using static WorldStorageHandler;
using UnityEngine.UI;
using TMPro;
using System.Reflection;

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
    private static readonly float deltaP = 2400;//px/s
    private static bool active = false;

    public static void Activate(Action callback = null){ 
        if(active) return;
        SetPanel(true, callback); 
        InitializeDisplay(); 
        active = true;
    }
    public static void Deactivate(Action callback = null){ 
        if(!active) return;
        _ = SaveOptions();
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
            position.x = Mathf.Clamp(position.x + deltaP * (active ? -1 : 1) * Time.deltaTime, showPos, hidePos);
            sTransform.anchoredPosition = position;
            await Task.Yield();
        }
        callback?.Invoke();
    }


    public static void EditName(){
        TMP_InputField inputField = sTransform.GetChild(0).GetComponent<TMP_InputField>();
        WORLD_SELECTION.First.Value = new WorldMeta{
            Id = WORLD_SELECTION.First.Value.Id,
            Name = inputField.text, 
            Path = WORLD_SELECTION.First.Value.Path
        }; Task.Run(() => SaveMeta());
    }

    public static void Delete(){
        SelectionHandler.DeleteSelected(); 
        Deactivate(() => MenuHandler.Activate());
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
        WorldMeta cWorld = WORLD_SELECTION.First.Value;
        TMP_InputField inputField = sTransform.GetChild(0).GetComponent<TMP_InputField>();
        inputField.text = cWorld.Name;
        inputField.onEndEdit.AddListener((string value) => {
            cWorld.Name = value;
            inputField.text = value;
        });

        CreateOptionDisplay(WORLD_OPTIONS, infoContent);
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

    public delegate void ChildUpdate(ref object obj);
    public delegate void ParentUpdate(ChildUpdate cb);

    static void CreateOptionDisplay(object setting,  GameObject content, ParentUpdate OnUpdate = null){
        System.Reflection.FieldInfo[] fields = setting.GetType().GetFields();
        SetUpLayout(content);

        for(int i = 0; i < fields.Length; i++){
            if(Attribute.IsDefined(fields[i], typeof(UIgnore))) continue;
            
            System.Reflection.FieldInfo field = fields[i]; 
            object value = field.GetValue(setting); object cObject = setting; ParentUpdate nUpdate = OnUpdate;
            GameObject newOption = Instantiate(Resources.Load<GameObject>("Prefabs/Option"), content.transform);
            RectTransform transform = newOption.GetComponent<RectTransform>();
            newOption.GetComponent<TextMeshProUGUI>().text = field.Name;
            
            //Extract the value of the option
            //cObject is the Option Field(value type)
            //setting is the class containing the option
            //value is the class held by the option
            if(field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(Option<>)) {
                FieldInfo oField = field;
                cObject = field.GetValue(setting); //Would prefer if GetValueDirect was implemented 
                field = value.GetType().GetField("value"); 
                value = field.GetValue(value);
                if(value == null){ //If it's null, create a new object and mark field as dirty
                    ((IOption)cObject).Clone(); //clones nothing and marks dirty
                    value = Activator.CreateInstance(field.FieldType);
                    field.SetValue(cObject, value);
                    oField.SetValue(setting, cObject);
                }
                
                void ChildRequest(ChildUpdate childCallback) { 
                    void ParentReceive(ref object parentObject){
                        ((IOption)cObject).Clone();
                        value = field.GetValue(cObject);
                        childCallback(ref value);

                        field.SetValue(cObject, value); //if class this does nothing
                        oField.SetValue(parentObject, cObject);
                    }
                    if(OnUpdate == null) ParentReceive(ref setting);
                    else OnUpdate(ParentReceive);
                } nUpdate = ChildRequest;
            } else if(field.FieldType.IsValueType && !field.FieldType.IsPrimitive){
                void ChildRequest(ChildUpdate childCallback) { 
                    void ParentReceive(ref object parentObject){
                        value = field.GetValue(parentObject);
                        childCallback(ref value);
                        field.SetValue(parentObject, value); 
                    }
                    if(OnUpdate == null) ParentReceive(ref setting);
                    else OnUpdate(ParentReceive);
                } nUpdate = ChildRequest;
            } else if(!field.FieldType.IsPrimitive && field.FieldType != typeof(string)) 
                throw new Exception("Settings objects must contain either only value types or options");

            CreateInputField(field, newOption, value, nUpdate);
        }  
    }

    public static void CreateInputField(FieldInfo field, GameObject parent, object value, ParentUpdate OnUpdate = null){
        switch (field.FieldType){
            case Type t when t == typeof(int):
                TMP_InputField inputField = Instantiate(Resources.Load<GameObject>("Prefabs/Text_Input"), parent.transform).GetComponent<TMP_InputField>(); inputField.text = value.ToString();
                inputField.onEndEdit.AddListener((string value) => { OnUpdate((ref object parent) => {field.SetValue(parent, int.Parse(value));}); });
                break;
            case Type t when t == typeof(uint):
                inputField = Instantiate(Resources.Load<GameObject>("Prefabs/Text_Input"), parent.transform).GetComponent<TMP_InputField>(); inputField.text = value.ToString();
                inputField.onEndEdit.AddListener((string value) => { OnUpdate((ref object parent) => {field.SetValue(parent, uint.Parse(value));}); });
                break;
            case Type t when t == typeof(float):
                inputField = Instantiate(Resources.Load<GameObject>("Prefabs/Text_Input"), parent.transform).GetComponent<TMP_InputField>(); inputField.text = value.ToString();
                inputField.onEndEdit.AddListener((string value) => { OnUpdate((ref object parent) => {field.SetValue(parent, float.Parse(value));}); });
                break;
            case Type t when t == typeof(string):
                inputField = Instantiate(Resources.Load<GameObject>("Prefabs/Text_Input"), parent.transform).GetComponent<TMP_InputField>(); inputField.text = value.ToString();
                inputField.onEndEdit.AddListener((string value) => { OnUpdate((ref object parent) => {field.SetValue(parent, value);}); });
                break;
            case Type t when t == typeof(bool):
                Toggle toggleField = Instantiate(Resources.Load<GameObject>("Prefabs/Bool_Input"), parent.transform).GetComponent<Toggle>(); toggleField.isOn = (bool)value;
                toggleField.onValueChanged.AddListener((bool value) => { OnUpdate((ref object parent) => {field.SetValue(parent, value);}); });
                break;
            case Type t when t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>):
                Button buttonField1 = Instantiate(Resources.Load<GameObject>("Prefabs/Drop_Arrow"), parent.transform.GetChild(0)).GetComponent<Button>(); bool isOpen1 = false;
                IList list = (IList)value; //capture list so all updates will be streamed to all event handlers
                buttonField1.onClick.AddListener(() => { 
                    isOpen1 = !isOpen1;
                    if(isOpen1) CreateList(list, parent, OnUpdate);
                    else ReleaseDisplay(parent); 
                    ForceLayoutRefresh(parent.transform);
                }); 
                Button listAdd = Instantiate(Resources.Load<GameObject>("Prefabs/ListAdd"), parent.transform.GetChild(0)).GetComponent<Button>();
                Button listRemove = Instantiate(Resources.Load<GameObject>("Prefabs/ListRemove"), parent.transform.GetChild(0)).GetComponent<Button>();
                listAdd.onClick.AddListener(() => {
                    if(!isOpen1) return;
                    OnUpdate((ref object parentObj) => {
                        list = (IList)parentObj; list.Add(Activator.CreateInstance(t.GetGenericArguments()[0]));
                        ReleaseDisplay(parent); CreateList(list, parent, OnUpdate);
                        ForceLayoutRefresh(parent.transform);
                    });
                });
                listRemove.onClick.AddListener(() => {
                    if(!isOpen1) return;
                    if(list.Count == 0) return;
                    OnUpdate((ref object parentObj) => {
                        list = (IList)parentObj; list.RemoveAt(list.Count - 1);
                        ReleaseDisplay(parent); CreateList(list, parent, OnUpdate);
                        ForceLayoutRefresh(parent.transform);
                    }); list.RemoveAt(list.Count - 1);
                });
                break;
            default:
                Button buttonField = Instantiate(Resources.Load<GameObject>("Prefabs/Drop_Arrow"), parent.transform.GetChild(0)).GetComponent<Button>(); bool isOpen = false;
                buttonField.onClick.AddListener(() => {
                    isOpen = !isOpen;
                    if(isOpen) CreateOptionDisplay(value, parent, OnUpdate);
                    else { ReleaseDisplay(parent); }
                    ForceLayoutRefresh(parent.transform);
                });
                break;
        }
    }

    private static void CreateList(IList list, GameObject parent, ParentUpdate OnUpdate){
        SetUpLayout(parent);
        for(int i = 0; i < list.Count; i++) {
            GameObject key = Instantiate(Resources.Load<GameObject>("Prefabs/Option"), parent.transform);
            key.GetComponent<TextMeshProUGUI>().text = "Element " + i.ToString() + ": ";

            object cObject = list[i]; object value = cObject; 
            FieldInfo field = null; ParentUpdate nUpdate = OnUpdate; 
            int index = i;
            if(cObject.GetType().IsGenericType && cObject.GetType().GetGenericTypeDefinition() == typeof(Option<>)){
                field = cObject.GetType().GetField("value"); 
                value = field.GetValue(cObject);
                if(value == null){
                    value = Activator.CreateInstance(field.FieldType);
                    field.SetValue(cObject, value);
                } 

                void ChildRequest(ChildUpdate childCallback) { 
                    void ParentReceive(ref object parentObject){
                        IList newList = (IList)parentObject;
                        IOption lOption = (IOption)cObject; 
                        lOption.Clone(); value = field.GetValue(lOption);

                        childCallback(ref value);
                        field.SetValue(lOption, value);
                        newList[index] = lOption; 
                    }
                    OnUpdate(ParentReceive);
                } nUpdate = ChildRequest;
            } 
            //You can't get a field to represent an index in a list so we create a
            //temporary option to fake a field so that the architecture is consistent
            else if(cObject.GetType().IsPrimitive || cObject.GetType() == typeof(string)){ 
                cObject = Activator.CreateInstance(typeof(Option<>).MakeGenericType(cObject.GetType()));
                field = cObject.GetType().GetField("value");
                
                void ChildRequest(ChildUpdate childCallback) { 
                    void ParentReceive(ref object parentObject){
                        IList newList = (IList)parentObject;
                        ((IOption)cObject).Clone();

                        childCallback(ref cObject); 
                        newList[index] = field.GetValue(cObject); 
                    }
                    OnUpdate(ParentReceive);
                } nUpdate = ChildRequest;
            } else if(cObject.GetType().IsValueType){
                cObject = Activator.CreateInstance(typeof(Option<>).MakeGenericType(cObject.GetType()));
                field = cObject.GetType().GetField("value");

                void ChildRequest(ChildUpdate childCallback) { 
                    void ParentReceive(ref object parentObject){
                        IList newList = (IList)parentObject;

                        childCallback(ref value); 
                        newList[index] = value; 
                    }
                    OnUpdate(ParentReceive);
                } nUpdate = ChildRequest;
            }
            
            else if(!field.FieldType.IsPrimitive && field.FieldType != typeof(string)) 
                throw new Exception("Setting objects must contain either only value types or options");

            CreateInputField(field, key, value, nUpdate);
        }
    }


}
