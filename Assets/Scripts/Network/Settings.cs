using System;
using UnityEngine;

namespace Arterra.Core.Network {
    [Serializable]
    public class NetworkSettings {
        public int MaxPlayers = 4;
        public bool IsPrivate = false;
        public string Password = null;
        //Whether or not the server will send map and entity data
        //To the client even if it isn't dirty (modified).
        public bool Authoritative = false; 
    }
}