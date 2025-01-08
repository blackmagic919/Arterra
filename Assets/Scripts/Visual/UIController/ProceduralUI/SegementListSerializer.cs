using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SegmentedUIEditor;
using WorldConfig;

public class SegmentListSerializer : IConverter{
    public bool CanConvert(Type type){ return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>); }
    public void Serialize(GameObject parent, FieldInfo field, object value, ParentUpdate OnUpdate){
        object cValue = value; //Capture the object to streamline changes
        void ChildRequest(ChildUpdate childCallback) { 
            void ParentReceive(ref object parentObject){
                cValue = field.GetValue(parentObject);
                childCallback(ref cValue); 
                field.SetValue(parentObject, cValue);
            }
            OnUpdate(ParentReceive);
        }
        
        Button buttonField1 = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/SegmentedUI/Drop_Arrow"), parent.transform.GetChild(0)).GetComponent<Button>(); bool isOpen1 = false;
        Button listAdd = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/SegmentedUI/ListAdd"), parent.transform.GetChild(0)).GetComponent<Button>();
        Button listRemove = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/SegmentedUI/ListRemove"), parent.transform.GetChild(0)).GetComponent<Button>();
        buttonField1.onClick.AddListener(() => { 
            isOpen1 = !isOpen1;
            if(isOpen1) CreateList((IList)cValue, parent, ChildRequest);
            else ReleaseDisplay(parent); 
            ForceLayoutRefresh(parent.transform);
        }); 
        listAdd.onClick.AddListener(() => {
            if(!isOpen1) return;
            ChildRequest((ref object parentObj) => {
                cValue = parentObj; ((IList)cValue).Add(CreateInstance(field.FieldType.GetGenericArguments()[0]));
                ReleaseDisplay(parent); CreateList((IList)cValue, parent, ChildRequest);
                ForceLayoutRefresh(parent.transform);
            });
        });
        listRemove.onClick.AddListener(() => {
            if(!isOpen1) return;
            if(((IList)cValue).Count == 0) return;
            ChildRequest((ref object parentObj) => {
                cValue = (IList)parentObj; ((IList)cValue).RemoveAt(((IList)cValue).Count - 1);
                ReleaseDisplay(parent); CreateList((IList)cValue, parent, ChildRequest);
                ForceLayoutRefresh(parent.transform);
            });
        });
    }

    public void Deserialize(ref object destination, ref object source){
        IList dest = (IList)destination; IList src = (IList)source;
        for(int i = 0; i < dest.Count && i < src.Count; i++){
            object srcEl = src[i]; object destEl = dest[i]; Type destType = destEl.GetType();
            if(destType.IsGenericType && destType.GetGenericTypeDefinition() == typeof(Option<>)){
                if(((IOption)destEl).IsDirty) {
                    object nDest, nSrc; 
                    FieldInfo field = destType.GetField("value");
                    nDest = field.GetValue(destEl); nSrc = field.GetValue(srcEl);
                    SupplementTree(ref nDest, ref nSrc);
                    field.SetValue(destEl, nDest); dest[i] = destEl;
                } else dest[i] = src[i];
            } 
            else if(destType.IsPrimitive || destType == typeof(string)) continue;
            else if (destType.IsEnum) continue;
            else if(destType.IsValueType){
                SupplementTree(ref destEl, ref srcEl);
                dest[i] = destEl;
            } else throw new Exception("Settings objects must contain either only value types or options");
        }
    }

    private static void CreateList(IList list, GameObject parent, ParentUpdate OnUpdate){
        SetUpLayout(parent);
        for(int i = 0; i < list.Count; i++) {
            GameObject key = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/SegmentedUI/Option"), parent.transform);
            object cObject = list[i]; object value = cObject; Type cObjType = cObject.GetType();

            TextMeshProUGUI elementText = key.GetComponent<TextMeshProUGUI>();
            FieldInfo name = value.GetType().GetField("Name");
            if(name != null && name.GetValue(value) != null){ elementText.text = name.GetValue(value).ToString(); }
            else elementText.text = "Element " + i.ToString() + ": ";

            FieldInfo field = null; ParentUpdate nUpdate = OnUpdate; 
            int index = i; //this is not useless--index is captured to streamline changes
            if(cObjType.IsGenericType && cObjType.GetGenericTypeDefinition() == typeof(Option<>)){
                field = cObjType.GetField("value"); 
                value = field.GetValue(cObject);
                if(value == null){
                    value = CreateInstance(field.FieldType);
                    field.SetValue(cObject, value);
                } 

                void ChildRequest(ChildUpdate childCallback) { 
                    void ParentReceive(ref object parentObject){
                        IList newList = (IList)parentObject;
                        ((IOption)cObject).Clone();
                        childCallback(ref cObject);
                        newList[index] = (IOption)cObject; 
                    }
                    OnUpdate(ParentReceive);
                } nUpdate = ChildRequest;
            } 
            //You can't get a field to represent an index in a list so we create a
            //temporary option to fake a field so that the architecture is consistent
            else if(cObjType.IsValueType || cObjType.IsPrimitive || cObjType == typeof(string)){
                cObject = CreateInstance(typeof(Option<>).MakeGenericType(cObjType));
                field = cObject.GetType().GetField("value");
                field.SetValue(cObject, value);

                void ChildRequest(ChildUpdate childCallback) { 
                    void ParentReceive(ref object parentObject){
                        IList newList = (IList)parentObject;
                        ((IOption)cObject).Clone();
                        childCallback(ref cObject); 
                        newList[index] = field.GetValue(cObject); 
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
