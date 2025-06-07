using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WorldConfig;

[AttributeUsage(AttributeTargets.Field)]
public sealed class UISetting : Attribute{
    public bool Ignore{get; set;}
    public string Message{get; set;}
    public string Warning{get; set;}
    public string Alias{get; set;}
}

//Only applicable for WorldOptions
[AttributeUsage(AttributeTargets.Field)]
public sealed class UIModifiable : Attribute {
    public string CallbackName{get; set;}
}

public delegate void ChildUpdate(ref object obj);
public delegate void ParentUpdate(ChildUpdate cb);



public static class SegmentedUIEditor
{
    public interface IConverter{
        //Instance Type is concrete, fieldType can be more abstract
        public abstract bool CanConvert(Type instanceType, Type fieldType);
        public abstract void Serialize(GameObject parent, FieldInfo field, object value, ParentUpdate OnUpdate);
        public abstract void Deserialize(ref object destination, ref object source);
    }
    public static List<IConverter> UIConverterSettings;

    public static void Initialize(){
        UIConverterSettings = new List<IConverter>
        {
            new SegmentAbstractSerializer(),
            new SegmentListSerializer(),
            new SegmentCategorySerializer(),
        };
    }
    //
    public static IConverter GetCustomSerializerSetting(Type instanceType, Type fieldType)
    {
        if (UIConverterSettings == null) return null;
        for (int i = 0; i < UIConverterSettings.Count; i++)
        {
            if (UIConverterSettings[i].CanConvert(instanceType, fieldType))
                return UIConverterSettings[i];
        }
        return null;
    }

    public static void ReleaseDisplay(GameObject content){
        foreach(Transform child in content.transform){ 
            if(child.gameObject.name.Contains("Option")) GameObject.Destroy(child.gameObject); 
        }
        content.GetComponent<VerticalLayoutGroup>().padding.left = 0;
    }

    //To Do: Flatten Options into a list and store index if it isn't dirty to the template list
    public static void SupplementTree(ref object dest, ref object src){
        System.Reflection.FieldInfo[] fields = src.GetType().GetFields();
        foreach(FieldInfo field in fields){
            if (Attribute.IsDefined(field, typeof(UISetting)) && (Attribute.GetCustomAttribute(field, typeof(UISetting)) as UISetting).Ignore)
                continue;
            if (field.IsStatic) continue; //Ignore static fields
            if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(Option<>))
            {
                if (((IOption)field.GetValue(dest)).IsDirty)
                {
                    FieldInfo nField = field.FieldType.GetField("value");
                    object oDest = field.GetValue(dest), oSrc = field.GetValue(src);
                    object nDest = nField.GetValue(oDest), nSrc = nField.GetValue(oSrc);

                    IConverter CustomConvertor = GetCustomSerializerSetting(nDest.GetType(), nField.FieldType);
                    if (CustomConvertor != null) CustomConvertor.Deserialize(ref nDest, ref nSrc);
                    else SupplementTree(ref nDest, ref nSrc);

                    nField.SetValue(oDest, nDest); field.SetValue(dest, oDest);
                }
                else field.SetValue(dest, field.GetValue(src)); //This is the only line that actually fills in anything
            }
            else if (field.FieldType.IsPrimitive || field.FieldType == typeof(string)) continue;
            else if (field.FieldType.IsEnum) continue;
            else if (field.FieldType.IsValueType)
            {
                object nDest = field.GetValue(dest), nSrc = field.GetValue(src);
                SupplementTree(ref nDest, ref nSrc);
                field.SetValue(dest, nDest);
            }
            else throw new Exception("Settings objects must contain either only value types or options");
        }
    }


    //https://forum.unity.com/threads/layoutgroup-does-not-refresh-in-its-current-frame.458446/
    public static async void ForceLayoutRefresh(Transform content){
        await Task.Yield(); //For some reason layout refresh must be delayed
        while(content != null && content.GetComponent<VerticalLayoutGroup>() != null){
            LayoutRebuilder.ForceRebuildLayoutImmediate(content.GetComponent<RectTransform>());
            content = content.transform.parent;
        }
    }

    public static object CreateInstance(Type type){
        if (type.IsInterface || type.IsAbstract)
        {
            //this is pretty expensive so try not to have too many abstract classes
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            type = assemblies.SelectMany(x => x.GetTypes()).First(x => type.IsAssignableFrom(x) && !x.IsAbstract);
            //If no concrete template is found, we will not be able to create an instance
            //without meta-coding the class during runtime which is difficult
            if (type == null) throw new Exception("No concrete class found for interface/abstract class");
        } else if (type.IsSubclassOf(typeof(ScriptableObject)) && type.IsGenericType){
            var assemblies = AppDomain.CurrentDomain.GetAssemblies(); 
            //Extra condition that the new type cannot be generic
            type = assemblies.SelectMany(x => x.GetTypes()).First(x => type.IsAssignableFrom(x) && !x.IsAbstract && !x.IsGenericType);
            if (type == null) throw new Exception("No concrete class found for interface/abstract class");
        }
        if(type.IsSubclassOf(typeof(ScriptableObject))) return ScriptableObject.CreateInstance(type);
        return Activator.CreateInstance(type);
    }

   public static void SetUpLayout(GameObject content){
        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        ContentSizeFitter filter = content.GetComponent<ContentSizeFitter>();
        if(layout == null) layout = content.AddComponent<VerticalLayoutGroup>();
        if(filter == null) filter = content.AddComponent<ContentSizeFitter>();
        layout.childControlHeight = false;
        layout.childControlWidth = false;
        layout.padding.left = 25;
        layout.childAlignment = TextAnchor.UpperCenter;
        filter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    public static void CreateOptionDisplay(object setting, GameObject content, ParentUpdate OnUpdate = null){
        System.Reflection.FieldInfo[] fields = setting.GetType().GetFields();
        SetUpLayout(content);

        for(int i = 0; i < fields.Length; i++){
            if(fields[i].IsStatic) continue;
            string name = fields[i].Name;
            if(Attribute.IsDefined(fields[i], typeof(UISetting))){
                UISetting UITag = Attribute.GetCustomAttribute(fields[i], typeof(UISetting)) as UISetting;
                if(UITag.Ignore) continue;
                if(UITag.Warning != null) {
                    GameObject message = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/SegmentedUI/Option_Warning"), content.transform);
                    message.GetComponent<TextMeshProUGUI>().text = UITag.Warning;
                }
                if(UITag.Message != null) {
                    GameObject message = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/SegmentedUI/Option_Message"), content.transform);
                    message.GetComponent<TextMeshProUGUI>().text = UITag.Message;
                }
                if(UITag.Alias != null){
                    name = UITag.Alias;
                }
            } 
            
            System.Reflection.FieldInfo field = fields[i]; 
            object value = field.GetValue(setting); object cObject = setting; ParentUpdate nUpdate = OnUpdate;
            GameObject newOption = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/SegmentedUI/Option"), content.transform);
            RectTransform transform = newOption.GetComponent<RectTransform>();
            newOption.GetComponent<TextMeshProUGUI>().text = name;

            //Extract the value of the option
            //cObject is the Option Field(value type)
            //setting is the class containing the option
            //value is the class held by the option
            if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(Option<>))
            {
                FieldInfo oField = field;
                cObject = field.GetValue(setting); //Would prefer if GetValueDirect was implemented 
                field = value.GetType().GetField("value");
                value = field.GetValue(value);
                if (value == null)
                { //If it's null, create a new object and mark field as dirty
                    ((IOption)cObject).Clone(); //clones nothing and marks dirty
                    value = CreateInstance(field.FieldType);
                    field.SetValue(cObject, value);
                    oField.SetValue(setting, cObject);
                }

                void ChildRequest(ChildUpdate childCallback)
                {
                    void ParentReceive(ref object parentObject)
                    {
                        ((IOption)cObject).Clone();
                        childCallback(ref cObject);
                        oField.SetValue(parentObject, cObject);
                    }
                    OnUpdate(ParentReceive);
                }
                nUpdate = ChildRequest;
            }
            else if (field.FieldType == typeof(string) && value == null) value = "New " + field.Name;
            else if (!field.FieldType.IsValueType && !field.FieldType.IsPrimitive && field.FieldType != typeof(string))
            {
                Debug.LogWarning($"Encountered unexpected {field.FieldType} ");
                throw new Exception("Settings objects must contain either only value types or options");
            } 

            CreateInputField(field, newOption, value, nUpdate);
        }  
    }

    //Field -> The fieldInfo of the input field, 
    //Value -> The raw value of the field, 
    //Parent -> UI Parent of the field
    //OnUpdate -> Callback to parent to obtain new parent path when editing
    public static void CreateInputField(FieldInfo field, GameObject parent, object value, ParentUpdate OnUpdate = null){
        IConverter CustomConverter = GetCustomSerializerSetting(value.GetType(), field.FieldType); //Don't use FieldType because it could be more generic than the value type
        if(CustomConverter != null){
            CustomConverter.Serialize(parent, field, value, OnUpdate);
            return;
        }

        object cValue = value; //Capture the object to streamline changes
        void ChildRequest(ChildUpdate childCallback) { 
            void ParentReceive(ref object parentObject){
                cValue = field.GetValue(parentObject);
                childCallback(ref cValue); 
                //this is necessary because the child may be a value type
                field.SetValue(parentObject, cValue);
            }
            OnUpdate(ParentReceive);
        }

        switch (field.FieldType){
            case Type t when t == typeof(int):
                TMP_InputField inputField = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/SegmentedUI/Text_Input"), parent.transform).GetComponent<TMP_InputField>(); inputField.text = value.ToString();
                inputField.onEndEdit.AddListener((string value) => { OnUpdate((ref object parent) => {field.SetValue(parent, int.Parse(value));}); });
                break;
            case Type t when t == typeof(uint):
                inputField = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/SegmentedUI/Text_Input"), parent.transform).GetComponent<TMP_InputField>(); inputField.text = value.ToString();
                inputField.onEndEdit.AddListener((string value) => { OnUpdate((ref object parent) => {field.SetValue(parent, uint.Parse(value));}); });
                break;
            case Type t when t == typeof(float):
                inputField = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/SegmentedUI/Text_Input"), parent.transform).GetComponent<TMP_InputField>(); inputField.text = value.ToString();
                inputField.onEndEdit.AddListener((string value) => { OnUpdate((ref object parent) => {field.SetValue(parent, float.Parse(value));}); });
                break;
            case Type t when t == typeof(string):
                inputField = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/SegmentedUI/Text_Input"), parent.transform).GetComponent<TMP_InputField>(); inputField.text = value == null ? "New Entry" : value.ToString();
                inputField.onEndEdit.AddListener((string value) => { OnUpdate((ref object parent) => {field.SetValue(parent, value);}); });
                break;
            case Type t when t == typeof(bool):
                Toggle toggleField = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/SegmentedUI/Bool_Input"), parent.transform).GetComponent<Toggle>(); toggleField.isOn = (bool)value;
                toggleField.onValueChanged.AddListener((bool value) => { OnUpdate((ref object parent) => {field.SetValue(parent, value);}); });
                break;
            case Type t when t.IsEnum:
                TMP_Dropdown dropdownField = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/SegmentedUI/Dropdown_Input"), parent.transform).GetComponent<TMP_Dropdown>();
                dropdownField.ClearOptions(); 
                dropdownField.AddOptions(Enum.GetNames(t).ToList());

                Array enumValues = Enum.GetValues(t);
                dropdownField.value = Array.IndexOf(enumValues, value);
                dropdownField.onValueChanged.AddListener((int value) => { OnUpdate((ref object parent) => { field.SetValue(parent, enumValues.GetValue(value)); }); });
                break;
            default:
                Button buttonField = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/SegmentedUI/Drop_Arrow"), parent.transform.GetChild(0)).GetComponent<Button>(); bool isOpen = false;
                buttonField.onClick.AddListener(() => {
                    isOpen = !isOpen;
                    if(isOpen) CreateOptionDisplay(cValue, parent, ChildRequest);
                    else ReleaseDisplay(parent);
                    ForceLayoutRefresh(parent.transform);
                });
                break;
        }
    }
}
