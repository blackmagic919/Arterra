using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static ProceduralUIEditor;

public class AbstractUISerializer : UIConverter
{
    public bool CanConvert(Type obj)
    {
        return obj.IsAbstract || obj.IsInterface;
    }

    //Type is the abstract type's type, value is the object, OnUpdate is the callback to parent
    public void Serialize(GameObject parent, FieldInfo field, object value, ParentUpdate OnUpdate)
    {
        object cValue = value; 
        void ChildRequest(ChildUpdate childCallback) { 
            void ParentReceive(ref object parentObject){
                cValue = field.GetValue(parentObject);
                childCallback(ref cValue); 
                //this is necessary because the child may be a value type
                field.SetValue(parentObject, cValue);
            }
            OnUpdate(ParentReceive);
        }

        //Capture the object to streamline changes
        void PropogateChild(ChildUpdate childCallback) { 
            void ParentReceive(ref object parentObject){
                childCallback(ref parentObject); 
                cValue = field.GetValue(parentObject);
            }
            OnUpdate(ParentReceive);
        }

        bool isTypeOpen = false; bool isFieldOpen = false;
        Button buttonField = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/Drop_Arrow"), parent.transform.GetChild(0)).GetComponent<Button>(); 
        buttonField.onClick.AddListener(() => {
            isFieldOpen = !isFieldOpen;
            if(isTypeOpen) {
                isTypeOpen = false;
                isFieldOpen = true;
                ReleaseDisplay(parent);
            } 
            if(isFieldOpen) CreateOptionDisplay(cValue, parent, ChildRequest);
            else ReleaseDisplay(parent);
            ForceLayoutRefresh(parent.transform);
        });

        GameObject typeUI = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/ExtendedType"), parent.transform.GetChild(0));
        Button tButton = typeUI.GetComponent<Button>();
        TextMeshProUGUI tText = typeUI.GetComponentInChildren<TextMeshProUGUI>();
        tText.text = cValue.GetType().Name;  
        
        tButton.onClick.AddListener(() => {
            isTypeOpen = !isTypeOpen;
            if(isFieldOpen) {
                isFieldOpen = false;
                isTypeOpen = true;
                ReleaseDisplay(parent);
            } 
            if(isTypeOpen) CreateTypeList(parent, tText, field, PropogateChild);
            else ReleaseDisplay(parent);
            ForceLayoutRefresh(parent.transform);
        });
    }

    private void CreateTypeList(GameObject parent, TextMeshProUGUI tText, FieldInfo field, ParentUpdate OnUpdate){
        SetUpLayout(parent);
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        Type[] ConcretTypes = assemblies.SelectMany(x => x.GetTypes()).Where(x => field.FieldType.IsAssignableFrom(x) && !x.IsAbstract).ToArray();

        foreach(Type cType in ConcretTypes){
            GameObject newOption = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/Option"), parent.transform);
            TextMeshProUGUI typeText = newOption.GetComponent<TextMeshProUGUI>();
            typeText.text = cType.Name;
            typeText.color = Color.green;

            GameObject select = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/ListAdd"), newOption.transform);
            Button selectButton = select.GetComponent<Button>();
            selectButton.onClick.AddListener(() => {
                object newObject = CreateInstance(cType);
                OnUpdate((ref object parentObj) => {
                    field.SetValue(parentObj, newObject);
                    tText.text = cType.Name;
                });
            });
        }
    }

    public void Deserialize(ref object destination, ref object source)
    { //Default Behavior
        SupplementTree(ref destination, ref source); 
    }
}
