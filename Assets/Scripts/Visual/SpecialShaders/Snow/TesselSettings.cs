using System;
using UnityEngine;
using Arterra.Configuration;

namespace Arterra.Configuration
{
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

    [Serializable]
    public struct TesselLevel
    {
        public int tesselReduction;
        public static int DataSize => sizeof(int);
    }
}
