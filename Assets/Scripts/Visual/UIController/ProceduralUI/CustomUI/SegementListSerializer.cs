using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SegmentedUIEditor;
using Arterra.Configuration;
using System.Linq;

public class SegmentListSerializer : IConverter{
    public bool CanConvert(Type iType, Type fType){ return iType.IsGenericType && iType.GetGenericTypeDefinition() == typeof(List<>); }
    public void Serialize(GameObject parent, FieldInfo field, object value, ParentUpdate OnUpdate){
        object cValue = value; //Capture the object to streamline changes
        void ChildRequest(ChildUpdate childCallback) {
            void ParentReceive(ref object parentObject) {
                cValue = field.GetValue(parentObject);
                childCallback(ref cValue);
                field.SetValue(parentObject, cValue);
            }
            OnUpdate(ParentReceive);
        }

        Transform optionContent = GetSegmentContent(parent);
        Button buttonField1 = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/SegmentedUI/Drop_Arrow"), optionContent).GetComponent<Button>(); bool isOpen1 = false;
        Button listAdd = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/SegmentedUI/ListAdd"), optionContent).GetComponent<Button>();
        Button listRemove = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/SegmentedUI/ListRemove"), optionContent).GetComponent<Button>();
        buttonField1.onClick.AddListener(() => { 
            isOpen1 = !isOpen1;
            if(isOpen1) CreateList((IList)cValue, parent, ChildRequest);
            else ReleaseDisplay(parent); 
            ForceLayoutRefresh(parent.transform);
        }); 
        listAdd.onClick.AddListener(() => {
            ChildRequest((ref object parentObj) => {
                cValue = parentObj; ((IList)cValue).Add(CreateInstance(field.FieldType.GetGenericArguments()[0]));
                if (isOpen1) ReleaseDisplay(parent);
                CreateList((IList)cValue, parent, ChildRequest);
                ForceLayoutRefresh(parent.transform);
            }); isOpen1 = true;
        });
        listRemove.onClick.AddListener(() => {
            if(!isOpen1) isOpen1 = true;
            if(((IList)cValue).Count == 0) return;
            ChildRequest((ref object parentObj) => {
                cValue = (IList)parentObj; ((IList)cValue).RemoveAt(((IList)cValue).Count - 1);
                if (isOpen1) ReleaseDisplay(parent);
                CreateList((IList)cValue, parent, ChildRequest);
                ForceLayoutRefresh(parent.transform);
            }); isOpen1 = true;
        });
    }

    public void Deserialize(ref object destination, ref object source){
        IList dest = (IList)destination; IList src = (IList)source;
        for(int i = 0; i < dest.Count && i < src.Count; i++){
            object srcEl = src[i]; object destEl = dest[i];
            Type destType = destEl.GetType();
            if(destType.GetInterfaces().Contains(typeof(IOption))){
                FieldInfo field = destType.GetField("value");
                object vDest = field.GetValue(destEl); object vSrc = field.GetValue(srcEl);
                if(((IOption)destEl).IsDirty) {
                    SupplementTree(ref vDest, ref vSrc);
                } else {
                    destEl = srcEl;
                    if(destType.GetInterfaces().Contains(typeof(IDirtyOption))){
                        (destEl as IDirtyOption).Prime();
                        vDest = field.GetValue(destEl);
                        SupplementTree(ref vDest, ref vSrc);
                    }
                } field.SetValue(destEl, vDest); dest[i] = destEl;
            } 
            else if(destType.IsPrimitive || destType == typeof(string)) continue;
            else if (destType.IsEnum) continue;
            else if(destType.IsValueType){
                SupplementTree(ref destEl, ref srcEl);
                dest[i] = destEl;
            } else throw new Exception("Settings objects must contain either only value types or options");
        }
    }

    private static object TryGetName(object value) {
        var member = value.GetType().GetMember("Name",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static)
                .FirstOrDefault(m =>
                    m.MemberType == MemberTypes.Field ||
                    m.MemberType == MemberTypes.Property);
        return member switch {
            FieldInfo f => f.GetValue(value),
            PropertyInfo p => p.GetValue(value),
            _ => null
        };
    }

    private static void CreateList(IList list, GameObject parent, ParentUpdate OnUpdate){
        SetUpLayout(parent);
        for(int i = 0; i < list.Count; i++) {
            GameObject key = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/SegmentedUI/Option"), parent.transform);
            object cObject = list[i]; object value = cObject; Type cObjType = cObject.GetType();

            TextMeshProUGUI elementText = GetSegmentName(key);
            object name = TryGetName(value);

            FieldInfo field = null; ParentUpdate nUpdate = OnUpdate; 
            int index = i; //this is not useless--index is captured to streamline changes
            if (cObjType.GetInterfaces().Contains(typeof(IOption))) {
                BuildOptionListElementBinding(cObject, index, OnUpdate, out field, out value, out nUpdate);
                name = TryGetName(value);
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
            
            if(name != null){ elementText.text = name.ToString(); }
            else elementText.text = "Element " + i.ToString() + ": ";
            CreateInputField(field, key, value, nUpdate);
        }
    }

    private static void BuildOptionListElementBinding(object listOption, int index, ParentUpdate OnUpdate,
        out FieldInfo field, out object value, out ParentUpdate nUpdate) {
        field = listOption.GetType().GetField("value");
        value = field.GetValue(listOption);
        if (value == null) {
            value = CreateInstance(field.FieldType);
            field.SetValue(listOption, value);
        }

        // List elements are not fields, so updates are routed through list index replacement.
        void ChildRequest(ChildUpdate childCallback) {
            void ParentReceive(ref object parentObject){
                IList newList = (IList)parentObject;
                ((IOption)listOption).Clone();
                childCallback(ref listOption);
                newList[index] = (IOption)listOption;
            }
            OnUpdate(ParentReceive);
        }

        nUpdate = ChildRequest;
        if (field.FieldType.GetInterfaces().Contains(typeof(IOption))) {
            HandleOptionClosure(field, listOption, ChildRequest, out field, out value, out nUpdate);
            return;
        }
    }
}
