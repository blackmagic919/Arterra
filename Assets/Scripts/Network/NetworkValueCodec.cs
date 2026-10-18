using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Core.Network {
    // Select once per closed generic type. BinaryWriter/Reader use little-endian
    // numbers; unsupported types retain their existing JSON representation.
    internal static class NetworkValueCodec<T> {
        public static readonly Action<BinaryWriter, T> Write;
        public static readonly Func<BinaryReader, T> Read;

        static NetworkValueCodec() => (Write, Read) = Create();

        private static (Action<BinaryWriter, T>, Func<BinaryReader, T>) Create() =>
            Match<bool>((w, v) => w.Write(v), r => r.ReadBoolean()) ??
            Match<byte>((w, v) => w.Write(v), r => r.ReadByte()) ??
            Match<sbyte>((w, v) => w.Write(v), r => r.ReadSByte()) ??
            Match<short>((w, v) => w.Write(v), r => r.ReadInt16()) ??
            Match<ushort>((w, v) => w.Write(v), r => r.ReadUInt16()) ??
            Match<int>((w, v) => w.Write(v), r => r.ReadInt32()) ??
            Match<uint>((w, v) => w.Write(v), r => r.ReadUInt32()) ??
            Match<long>((w, v) => w.Write(v), r => r.ReadInt64()) ??
            Match<ulong>((w, v) => w.Write(v), r => r.ReadUInt64()) ??
            Match<float>((w, v) => w.Write(v), r => r.ReadSingle()) ??
            Match<double>((w, v) => w.Write(v), r => r.ReadDouble()) ??
            Match<char>((w, v) => w.Write((ushort)v), r => (char)r.ReadUInt16()) ??
            Match<decimal>((w, v) => w.Write(v), r => r.ReadDecimal()) ??

            Match<float3>(
                (w, v) => { w.Write(v.x); w.Write(v.y); w.Write(v.z); },
                r => new float3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle())) ??

            Match<double3>(
                (w, v) => { w.Write(v.x); w.Write(v.y); w.Write(v.z); },
                r => new double3(r.ReadDouble(), r.ReadDouble(), r.ReadDouble())) ??

            Match<Quaternion>(
                (w, v) => { w.Write(v.x); w.Write(v.y); w.Write(v.z); w.Write(v.w); },
                r => new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle())) ??

            (WriteJson, ReadJson);

        private static (Action<BinaryWriter, T>, Func<BinaryReader, T>)? Match<TValue>(
            Action<BinaryWriter, TValue> write, Func<BinaryReader, TValue> read)
            => typeof(T) == typeof(TValue)
                ? ((Action<BinaryWriter, T>)(object)write, (Func<BinaryReader, T>)(object)read)
                : null;

        private static void WriteJson(BinaryWriter writer, T value)
            => writer.Write(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value, typeof(T), null)));

        private static T ReadJson(BinaryReader reader)
            => JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(
                reader.ReadBytes(checked((int)(reader.BaseStream.Length - reader.BaseStream.Position)))));
    }
}
