using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;
namespace NSerializable
{
    [System.Serializable]
    public struct Vec2{
        public float x; public float y;
        public Vec2(float x, float y){ this.x = x; this.y = y; }
        public Vec2(Vector2 v){ this.x = v.x; this.y = v.y;}
        public Vector2 GetVector(){ return new Vector2(x, y); }
    }

    [System.Serializable]
    public struct Vec3{
        public float x; public float y; public float z;
        public Vec3(float x, float y, float z){ this.x = x; this.y = y; this.z = z; }
        public Vec3(Vector3 v){ this.x = v.x; this.y = v.y; this.z = v.z;}
        public Vector3 GetVector(){ return new Vector3(x, y, z); }
    }

    [System.Serializable]
    public struct Vec4{
        public float x; public float y; public float z; public float w;
        public Vec4(float x, float y, float z, float w){ this.x = x; this.y = y; this.z = z; this.w = w; }
        public Vec4(Vector4 v){ this.x = v.x; this.y = v.y; this.z = v.z; this.w = v.w;}
        public Vec4(Quaternion v){ this.x = v.x; this.y = v.y; this.z = v.z; this.w = v.w;}
        public Vector4 GetVector(){ return new Vector4(x, y, z);}
        public Quaternion GetQuaternion(){ return new Quaternion(x, y, z, w); }
        public Color GetColor(){ return new Color(x,y,z,w); }
    }
}