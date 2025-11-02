using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SegmentedUIEditor;
using WorldConfig;
using System.Linq;
public class SegmentCategorySerializer : IConverter
{
    public bool CanConvert(Type iType, Type fType)
    {
        if (iType.BaseType == null) return false;
        if (!iType.BaseType.IsGenericType) return false;
        if (iType.BaseType.GetGenericTypeDefinition() != typeof(Category<>)) return false;
        //If its first argument is itself, then it's a leaf object being contained not a container
        return iType.BaseType.GetGenericArguments().First() != iType;
    }

    public void Serialize(GameObject parent, FieldInfo field, object value, ParentUpdate OnUpdate)
    {
        object cValue = value;
        Type type = cValue.GetType();
        //Get Children returns an Option<List<Option<Category<T>>>> where T is something
        object child = type.GetMethod("GetChildren", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(cValue, null);
        FieldInfo listField = child.GetType().GetField("value");
        object listObj = listField.GetValue(child);

        string name = (string)type.GetField("Name").GetValue(cValue);
        //parent should have this if it is an option which it must be based on config rules
        GetSegmentName(parent).text = name; 

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

        new SegmentListSerializer().Serialize(parent, listField, listObj, ChildRequest);
    }

    public void Deserialize(ref object destination, ref object source)
    {
        SupplementTree(ref destination, ref source); 
    }
    
}
