using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using WorldConfig;

namespace Utils.NSerializable{

public static class JsonCustomSettings
{
    public static void ConfigureJsonInternal()
    {
        JsonConvert.DefaultSettings = () =>
        {
            var settings = new JsonSerializerSettings{
                //So we track the name of the type with abstracts
                TypeNameHandling = TypeNameHandling.Auto 
            };

            settings.Converters.Add(new ColorConverter());
            settings.Converters.Add(new Vec4Converter());
            settings.Converters.Add(new Vec3Converter());
            settings.Converters.Add(new Vec2Converter());
            settings.Converters.Add(new QuaternionConverter());
            settings.Converters.Add(new SObjConverter());

            settings.Converters.Add(new UInt3Converter());
            settings.Converters.Add(new Int4Converter());
            settings.Converters.Add(new Int3Converter());
            settings.Converters.Add(new Int2Converter());
            settings.Converters.Add(new Float4Converter());
            settings.Converters.Add(new Float3Converter());
            settings.Converters.Add(new Float2Converter());
            return settings;
        };
    }
}

#if UNITY_EDITOR
// this must be inside an Editor/ folder
public static class EditorJsonSettings
{
    [InitializeOnLoadMethod]
    public static void ApplyCustomConverters()
    {
      JsonCustomSettings.ConfigureJsonInternal();
    }
}
#endif
public static class RuntimeJsonSettings
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void ApplyCustomConverters()
    {
        JsonCustomSettings.ConfigureJsonInternal();
    }
}

//Converters
public class ColorConverter : JsonConverter<Color>
{
    public override void WriteJson(JsonWriter writer, Color value, JsonSerializer serializer)
    {
        JArray array = new(value.r, value.g, value.b, value.a);
        array.WriteTo(writer);
    }

    public override Color ReadJson(JsonReader reader, Type objectType, Color existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        JArray array = JArray.Load(reader);
        return new Color((float)array[0], (float)array[1], (float)array[2], (float)array[3]);
    }
}

public class Vec4Converter : JsonConverter<Vector4>
{
    public override void WriteJson(JsonWriter writer, Vector4 value, JsonSerializer serializer)
    {
        JArray array = new(value.x, value.y, value.z, value.w);
        array.WriteTo(writer);
    }

    public override Vector4 ReadJson(JsonReader reader, Type objectType, Vector4 existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        JArray array = JArray.Load(reader);
        return new Vector4((float)array[0], (float)array[1], (float)array[2], (float)array[3]);
    }
}

public class Vec3Converter : JsonConverter<Vector3>
{
    public override void WriteJson(JsonWriter writer, Vector3 value, JsonSerializer serializer)
    {
        JArray array = new(value.x, value.y, value.z);
        array.WriteTo(writer);
    }

    public override Vector3 ReadJson(JsonReader reader, Type objectType, Vector3 existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        JArray array = JArray.Load(reader);
        return new Vector3((float)array[0], (float)array[1], (float)array[2]);
    }
}

public class Vec2Converter : JsonConverter<Vector2>
{
    public override void WriteJson(JsonWriter writer, Vector2 value, JsonSerializer serializer)
    {
        JArray array = new(value.x, value.y);
        array.WriteTo(writer);
    }

    public override Vector2 ReadJson(JsonReader reader, Type objectType, Vector2 existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        JArray array = JArray.Load(reader);
        return new Vector2((float)array[0], (float)array[1]);
    }
}

public class QuaternionConverter : JsonConverter<Quaternion>
{
    public override void WriteJson(JsonWriter writer, Quaternion value, JsonSerializer serializer)
    {
        JArray array = new(value.x, value.y, value.z, value.w);
        array.WriteTo(writer);
    }

    public override Quaternion ReadJson(JsonReader reader, Type objectType, Quaternion existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        JArray array = JArray.Load(reader);
        return new Quaternion((float)array[0], (float)array[1], (float)array[2], (float)array[3]);
    }
}


public class Int4Converter : JsonConverter<int4>
{
    public override void WriteJson(JsonWriter writer, int4 value, JsonSerializer serializer)
    {
        JArray array = new(value.x, value.y, value.z, value.w);
        array.WriteTo(writer);
    }

    public override int4 ReadJson(JsonReader reader, Type objectType, int4 existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        JArray array = JArray.Load(reader);
        return new int4((int)array[0], (int)array[1], (int)array[2], (int)array[3]);
    }
}

public class Int3Converter : JsonConverter<int3>
{
    public override void WriteJson(JsonWriter writer, int3 value, JsonSerializer serializer)
    {
        JArray array = new(value.x, value.y, value.z);
        array.WriteTo(writer);
    }

    public override int3 ReadJson(JsonReader reader, Type objectType, int3 existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        JArray array = JArray.Load(reader);
        return new int3((int)array[0], (int)array[1], (int)array[2]);
    }
}

public class Int2Converter : JsonConverter<int2>
{
    public override void WriteJson(JsonWriter writer, int2 value, JsonSerializer serializer)
    {
        JArray array = new(value.x, value.y);
        array.WriteTo(writer);
    }

    public override int2 ReadJson(JsonReader reader, Type objectType, int2 existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        JArray array = JArray.Load(reader);
        return new int2((int)array[0], (int)array[1]);
    }
}

public class Float4Converter : JsonConverter<float4>
{
    public override void WriteJson(JsonWriter writer, float4 value, JsonSerializer serializer)
    {
        JArray array = new(value.x, value.y, value.z, value.w);
        array.WriteTo(writer);
    }

    public override float4 ReadJson(JsonReader reader, Type objectType, float4 existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        JArray array = JArray.Load(reader);
        return new float4((float)array[0], (float)array[1], (float)array[2], (float)array[3]);
    }
}

public class Float3Converter : JsonConverter<float3>
{
    public override void WriteJson(JsonWriter writer, float3 value, JsonSerializer serializer)
    {
        JArray array = new(value.x, value.y, value.z);
        array.WriteTo(writer);
    }

    public override float3 ReadJson(JsonReader reader, Type objectType, float3 existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        JArray array = JArray.Load(reader);
        return new float3((float)array[0], (float)array[1], (float)array[2]);
    }
}

public class Float2Converter : JsonConverter<float2>
{
    public override void WriteJson(JsonWriter writer, float2 value, JsonSerializer serializer)
    {
        JArray array = new(value.x, value.y);
        array.WriteTo(writer);
    }

    public override float2 ReadJson(JsonReader reader, Type objectType, float2 existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        JArray array = JArray.Load(reader);
        return new float2((float)array[0], (float)array[1]);
    }
}

public class UInt3Converter : JsonConverter<uint3>
{
    public override void WriteJson(JsonWriter writer, uint3 value, JsonSerializer serializer)
    {
        JArray array = new(value.x, value.y, value.z);
        array.WriteTo(writer);
    }

    public override uint3 ReadJson(JsonReader reader, Type objectType, uint3 existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        JArray array = JArray.Load(reader);
        return new uint3((uint)array[0], (uint)array[1], (uint)array[2]);
    }
}

public class SObjConverter : JsonConverter<ScriptableObject>
{
    public override void WriteJson(JsonWriter writer, ScriptableObject value, JsonSerializer serializer)
    {
        serializer.Converters.Remove(this);
        JToken.FromObject(value, serializer).WriteTo(writer);
        serializer.Converters.Add(this);
    }

    public override ScriptableObject ReadJson(JsonReader reader, Type objectType, ScriptableObject existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if(objectType.IsAbstract || objectType.IsInterface || objectType.IsGenericType){
            JObject typeObject = JObject.Load(reader);
            objectType = typeObject["$type"].ToObject<Type>();
            reader = typeObject.CreateReader();
        }
        ScriptableObject instance = ScriptableObject.CreateInstance(objectType);
        serializer.Populate(reader, instance);
        return instance;
    }
}}
