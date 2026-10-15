using System;
using UnityEngine;

namespace Arterra.Core.Network {
    [Serializable]
    public class NetworkSettings {
        public string ServiceUrl = "https://alpha.api.playarterra.com";
        //Whether or not the server will send map and entity data
        //To the client even if it isn't dirty (modified).
        public bool Authoritative = false; 
    }
}