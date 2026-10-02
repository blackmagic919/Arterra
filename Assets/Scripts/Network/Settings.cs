using System;
using UnityEngine;

namespace Arterra.Core.Network {
    [Serializable]
    public class NetworkSettings {
        public int MaxPlayers = 4;
        public bool IsPrivate = false;
        public string Password = null;
    }
}