using System;
using UnityEngine;

[CreateAssetMenu(menuName = "ShaderData/Tesselation/Setting")]
public class TesselSettings : Category<TesselSettings>
{
    public static int DataSize => sizeof(int);
    public Data info;
    [Serializable]
    public struct Data
    {
        public uint tesselationFactor;//3
    }
}
