using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static PaginatedUIEditor;
using WorldConfig;


//List must be held by an Option which only holds it
//Therefore it already has its own page and doesn't need to create another
public class PageListSerializer : IConverter{
    public bool CanConvert(Type type){ return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>); }
    public void Serialize(GameObject page, GameObject parent, FieldInfo field, object value, ParentUpdate OnUpdate){
        object cValue = value; //Capture the object to streamline changes
        void ChildRequest(ChildUpdate childCallback) { 
            void ParentReceive(ref object parentObject){
                cValue = field.GetValue(parentObject);
                childCallback(ref cValue); 
                field.SetValue(parentObject, cValue);
            }
            OnUpdate(ParentReceive);
        }
        
        Button PaginateButton = parent.AddComponent<Button>();
        PaginateButton.onClick.AddListener(() => {
            GameObject newPage = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/PaginatedUI/Page"), page.transform.parent);
            Button ReturnButton = GetPageReturn(newPage).GetComponent<Button>();
            page.SetActive(false);

            ReturnButton.onClick.AddListener(() => { 
                GameObject.Destroy(newPage); 
                page.SetActive(true);
            });
            CreateList((IList)cValue, newPage, ChildRequest);
            ForceLayoutRefresh(GetPageContent(newPage));
            
            Button listAdd = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/PaginatedUI/ListAdd"), GetPageHeader(newPage)).GetComponent<Button>();
            Button listRemove = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/PaginatedUI/ListRemove"), GetPageHeader(newPage)).GetComponent<Button>();
            ForceLayoutRefresh(GetPageHeader(newPage));
            listAdd.onClick.AddListener(() => {
                ChildRequest((ref object parentObj) => {
                    cValue = parentObj; 
                    ((IList)cValue).Add(CreateInstance(field.FieldType.GetGenericArguments()[0]));
                    ReleaseAllChildren(GetPageContent(newPage).gameObject);
                    CreateList((IList)cValue, newPage, ChildRequest);
                    ForceLayoutRefresh(GetPageContent(newPage));
                });
            });
            listRemove.onClick.AddListener(() => {
                if(((IList)cValue).Count == 0) return;
                ChildRequest((ref object parentObj) => {
                    cValue = parentObj; 
                    ((IList)cValue).RemoveAt(((IList)cValue).Count - 1);
                    ReleaseAllChildren(GetPageContent(newPage).gameObject); 
                    CreateList((IList)cValue, newPage, ChildRequest);
                    ForceLayoutRefresh(GetPageContent(newPage));
                });
            });
        });
    }

    private static void CreateList(IList list, GameObject page, ParentUpdate OnUpdate){
        for(int i = 0; i < list.Count; i++) {
            object cObject = list[i]; object value = cObject; Type cObjType = cObject.GetType();

            GameObject key = GetOptionContent(UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/PaginatedUI/Option"), GetPageContent(page))).gameObject;
            TextMeshProUGUI elementText = key.transform.Find("Name").GetComponent<TextMeshProUGUI>();
            FieldInfo name = value.GetType().GetField("Name");
            if(name != null && name.GetValue(value) != null){ elementText.text = name.GetValue(value).ToString(); }
            else elementText.text = "Element " + i.ToString() + ": ";

            FieldInfo field = null; ParentUpdate nUpdate = OnUpdate; 
            int index = i; //Capture the index to streamline changes
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

            CreateInputField(field, value, key, page, nUpdate);
        }
    }
}
