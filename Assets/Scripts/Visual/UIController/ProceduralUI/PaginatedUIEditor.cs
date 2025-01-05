using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class PaginatedUIEditor 
{
    public interface IConverter{
        public abstract bool CanConvert(Type obj);
        public abstract void Serialize(GameObject page, GameObject parent, FieldInfo field, object value, ParentUpdate OnUpdate);
    }

    public static List<IConverter> UIConverterSettings;

    public static void Initialize(){
        UIConverterSettings = new List<IConverter>{
            new PageListSerializer(),
            new PageKeybindSerializer()
        };
    }
    public static IConverter GetCustomSerializerSetting(Type type){
        if(UIConverterSettings == null) return null;
        for(int i = 0; i < UIConverterSettings.Count; i++){
            if(UIConverterSettings[i].CanConvert(type)) 
                return UIConverterSettings[i];
        } return null;
    }


    public static async void ForceLayoutRefresh(Transform content){
        await Task.Yield(); 
        while(content != null && content.GetComponent<VerticalLayoutGroup>() != null){
            LayoutRebuilder.ForceRebuildLayoutImmediate(content.GetComponent<RectTransform>());
            content = content.transform.parent;
        }
    }

    public static object CreateInstance(Type type){
        if(type.IsInterface || type.IsAbstract) { 
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            type = assemblies.SelectMany(x => x.GetTypes()).First(x => type.IsAssignableFrom(x) && !x.IsAbstract);
            if(type == null) throw new Exception("No concrete class found for interface/abstract class");
        } 
        if(type.IsSubclassOf(typeof(ScriptableObject))) return ScriptableObject.CreateInstance(type);
        return Activator.CreateInstance(type);
    }

    public static void ReleaseAllChildren(GameObject content){
        foreach(Transform child in content.transform){ 
            GameObject.Destroy(child.gameObject); 
        }
    }

    public static void CreateProceduralPagination(object setting, GameObject content, ParentUpdate OnUpdate, Action OnReturn = null){
        GameObject page = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/PaginatedUI/Page"), content.transform);
        GameObject ret = page.transform.Find("TopPanel").Find("Return").gameObject;
        if(OnReturn == null) ret.transform.parent.gameObject.SetActive(false);
        else ret.GetComponent<Button>().onClick.AddListener(() => { 
            GameObject.Destroy(page);
            OnReturn(); 
        });

        CreatePageDisplay(setting, page, OnUpdate);
    }

    public static Transform GetPageContent(GameObject page){ return page.transform.Find("ScrollView").GetChild(0).GetChild(0); }
    public static Transform GetPageReturn(GameObject page){ return page.transform.Find("TopPanel").Find("Return"); }
    public static Transform GetOptionContent(GameObject option){ return option.transform.Find("Viewport").GetChild(0); }
    public static Transform GetPageHeader(GameObject page){ return page.transform.Find("TopPanel"); }
    public static void CreatePageDisplay(object setting, GameObject page, ParentUpdate OnUpdate = null){
        FieldInfo[] fields = setting.GetType().GetFields();

        for(int i = 0; i < fields.Length; i++){
            string name = fields[i].Name;
            if(Attribute.IsDefined(fields[i], typeof(UISetting))){
                UISetting UITag = Attribute.GetCustomAttribute(fields[i], typeof(UISetting)) as UISetting;
                if(UITag.Ignore) continue;
                if(UITag.Alias != null){
                    name = UITag.Alias;
                }
            } 
            
            FieldInfo field = fields[i]; 
            object value = field.GetValue(setting); object cObject = setting; ParentUpdate nUpdate = OnUpdate;
            GameObject newOption = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/PaginatedUI/Option"), GetPageContent(page));
            newOption = GetOptionContent(newOption).gameObject;
            newOption.transform.Find("Name").GetComponent<TextMeshProUGUI>().text = name;

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
                    value = CreateInstance(field.FieldType);
                    field.SetValue(cObject, value);
                    oField.SetValue(setting, cObject);
                }
                
                void ChildRequest(ChildUpdate childCallback) { 
                    void ParentReceive(ref object parentObject){
                        ((IOption)cObject).Clone();
                        childCallback(ref cObject);
                        oField.SetValue(parentObject, cObject);
                    }
                    OnUpdate(ParentReceive);
                } nUpdate = ChildRequest;
            } else if(!field.FieldType.IsValueType && !field.FieldType.IsPrimitive && field.FieldType != typeof(string)) 
                throw new Exception("Settings objects must contain either only value types or options");
            CreateInputField(field, value, newOption, page, nUpdate);
        }  
        ForceLayoutRefresh(GetPageContent(page));
    }

    //Field -> The fieldInfo of the input field, 
    //Value -> The raw value of the field, 
    //Parent -> UI Parent of the field
    //OnUpdate -> Callback to parent to obtain new parent path when editing
    public static void CreateInputField(FieldInfo field, object value, GameObject parent, GameObject page, ParentUpdate OnUpdate = null){
        IConverter CustomConverter = GetCustomSerializerSetting(field.FieldType);
        if(CustomConverter != null){
            CustomConverter.Serialize(page, parent, field, value, OnUpdate);
            return;
        }

        object cValue = value; //Capture the object to streamline changes
        void ChildRequest(ChildUpdate childCallback) { 
            void ParentReceive(ref object parentObject){
                cValue = field.GetValue(parentObject);
                childCallback(ref cValue); 
                field.SetValue(parentObject, cValue);
            }
            OnUpdate(ParentReceive);
        }

        switch (field.FieldType){
            case Type t when t == typeof(int):
                TMP_InputField inputField = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/PaginatedUI/Text_Input"), parent.transform).GetComponent<TMP_InputField>(); 
                inputField.text = value.ToString();
                inputField.onEndEdit.AddListener((string value) => { OnUpdate((ref object parent) => {field.SetValue(parent, int.Parse(value));}); });
                break;
            case Type t when t == typeof(uint):
                inputField = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/PaginatedUI/Text_Input"), parent.transform).GetComponent<TMP_InputField>(); 
                inputField.text = value.ToString();
                inputField.onEndEdit.AddListener((string value) => { OnUpdate((ref object parent) => {field.SetValue(parent, uint.Parse(value));}); });
                break;
            case Type t when t == typeof(float):
                inputField = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/PaginatedUI/Text_Input"), parent.transform).GetComponent<TMP_InputField>(); 
                inputField.text = value.ToString();
                inputField.onEndEdit.AddListener((string value) => { OnUpdate((ref object parent) => {field.SetValue(parent, float.Parse(value));}); });
                break;
            case Type t when t == typeof(string):
                inputField = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/PaginatedUI/Text_Input"), parent.transform).GetComponent<TMP_InputField>(); 
                inputField.text = value == null ? "New Entry" : value.ToString();
                inputField.onEndEdit.AddListener((string value) => { OnUpdate((ref object parent) => {field.SetValue(parent, value);}); });
                break;
            case Type t when t == typeof(bool):
                Toggle toggleField = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/PaginatedUI/Bool_Input"), parent.transform).GetComponent<Toggle>(); 
                toggleField.isOn = (bool)value;
                toggleField.onValueChanged.AddListener((bool value) => { OnUpdate((ref object parent) => {field.SetValue(parent, value);}); });
                break;
            case Type t when t.IsEnum:
                TMP_Dropdown dropdownField = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/PaginatedUI/Dropdown_Input"), parent.transform).GetComponent<TMP_Dropdown>();
                dropdownField.ClearOptions(); 
                dropdownField.AddOptions(Enum.GetNames(t).ToList());

                Array enumValues = Enum.GetValues(t);
                dropdownField.value = Array.IndexOf(enumValues, value);
                dropdownField.onValueChanged.AddListener((int value) => { OnUpdate((ref object parent) => { field.SetValue(parent, enumValues.GetValue(value)); }); });
                break;
            default: //create new page
                Button PaginateButton = parent.AddComponent<Button>();
                PaginateButton.onClick.AddListener(() => {
                    GameObject newPage = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/PaginatedUI/Page"), page.transform.parent);
                    Button ReturnButton = GetPageReturn(newPage).GetComponent<Button>();
                    page.SetActive(false);

                    ReturnButton.onClick.AddListener(() => { 
                        GameObject.Destroy(newPage); 
                        page.SetActive(true);
                    });
                    
                    CreatePageDisplay(cValue, newPage, ChildRequest);
                });
                break;
        }
    }
}