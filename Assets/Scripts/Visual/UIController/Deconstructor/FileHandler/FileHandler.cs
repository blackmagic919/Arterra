using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using System.IO;
using System;
using Unity.Mathematics;

public static class FileHandler
{
    public static void SaveToJson<T>(T toSave, string fileName, string filePath)
    {
        Debug.Log(GetPath(fileName, filePath));
        string content = JsonUtility.ToJson(toSave);
        WriteFile(GetPath(fileName, filePath), content);
    }

    public static void SaveListToJson<T>(List<T> toSave, string fileName, string filePath)
    {
        string content = JsonHelper.ToJson<T>(toSave.ToArray());
        WriteFile(GetPath(fileName, filePath), content);
    }

    public static T ReadFromJson<T>(string filePath)
    {
        string content = ReadFile(filePath);
        if(string.IsNullOrEmpty(content) || content == "{}")
        {
            return default(T);
        }
        T res = JsonUtility.FromJson<T>(content);
        return res;
    }

    public static List<T> ReadDirectoryFromJSON<T>(string dirPath)
    {
        string[] files = Directory.GetFiles(dirPath);
        List<T> dirData = new List<T>();
        foreach (string filePath in files)
        {
            dirData.Add(ReadFromJson<T>(filePath));
        }
        return dirData;
    }

    private static string GetPath(string fileName, string filePath)
    {
        string fullPath = Application.persistentDataPath + "/" + filePath + "/";
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        return fullPath + fileName;
    }

    private static void WriteFile(string path, string content)
    {
        FileStream fileStream = new FileStream(path, FileMode.Create);

        using (StreamWriter writer = new StreamWriter(fileStream))
        {
            writer.Write(content);
        }
    }

    private static string ReadFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            using(StreamReader reader = new StreamReader(filePath))
            {
                string content = reader.ReadToEnd();
                return content;
            }
        }
        return "";
    }
}

public static class JsonHelper
{
    public static T[] FromJson<T>(string json)
    {
        Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(json);
        return wrapper.Items;
    }

    public static string ToJson<T>(T[] array)
    {
        Wrapper<T> wrapper = new Wrapper<T>();
        wrapper.Items = array;
        return JsonUtility.ToJson(wrapper);
    }

    public static string ToJson<T>(T[] array, bool prettyPrint)
    {
        Wrapper<T> wrapper = new Wrapper<T>();
        wrapper.Items = array;
        return JsonUtility.ToJson(wrapper, prettyPrint);
    }

    [Serializable]
    private class Wrapper<T>
    {
        public T[] Items;
    }
}
