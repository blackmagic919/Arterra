using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using Arterra.Config;
using System.Linq;
using static PaginatedUIEditor;
public class PaginatedCategorySerializer : IConverter
{
    public bool CanConvert(Type iType)
    {
        if (iType.BaseType == null) return false;
        if (!iType.BaseType.IsGenericType) return false;
        if (iType.BaseType.GetGenericTypeDefinition() != typeof(Category<>)) return false;
        //If its first argument is itself, then it's a leaf object being contained not a container
        return iType.BaseType.GetGenericArguments().First() != iType;
    }

    public void Serialize(GameObject page, GameObject parent, FieldInfo field, object value, ParentUpdate OnUpdate)
    {
        object cValue = value;
        Type type = cValue.GetType();
        //Get Children returns an Option<List<Option<Category<T>>>> where T is something
        object child = type.GetMethod("GetChildren", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(cValue, null);
        FieldInfo listField = child.GetType().GetField("value");
        object listObj = listField.GetValue(child);

        string name = (string)type.GetField("Name").GetValue(cValue);
        //parent should have this if it is an option which it must be based on config rules
        parent.transform.GetChild(0).gameObject.GetComponent<TextMeshProUGUI>().text = name; 

        MethodInfo setter = type.GetMethod("SetChildren", BindingFlags.Instance | BindingFlags.NonPublic);
        void ChildRequest(ChildUpdate childCallback)
        {
            void ParentReceive(ref object parentObject)
            {
                cValue = field.GetValue(parentObject);
                ((IOption)child).Clone();
                childCallback(ref child);
                setter.Invoke(cValue, new[] { child });
                field.SetValue(parentObject, cValue);
            }
            OnUpdate(ParentReceive);
        }

        new PageListSerializer().Serialize(page, parent, listField, listObj, ChildRequest);
    }
}
