using System.Collections.Generic;
using System.IO;
using Arterra.Core.Network.Authentication;
using Arterra.Data.Entity.Behavior;
using Arterra.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Arterra.Core.Storage {
    /// <summary>World-local player snapshots. Call on the main thread with entity jobs stopped.</summary>
    public static class PlayerStorage {
        private const int FormatVersion = 1;
        private static Dictionary<string, JToken> players;
        public static bool IsInitialized => players != null;

        public static void Initialize() {
            Release();
            string path = Path.Combine(World.WORLD_SELECTION.First.Value.Path, "PlayerData.json");
            if (!File.Exists(path)) { players = new(); return; }
            var json = JObject.Parse(File.ReadAllText(path));
            if ((int?)json["version"] != FormatVersion) throw new InvalidDataException("Unsupported player save version.");
            players = json["players"]?.ToObject<Dictionary<string, JToken>>()
                ?? throw new InvalidDataException("Missing player save dictionary.");
        }

        public static void Release() => players = null;

        public static BehaviorEntity.Animal Load(AccountId account) {
            return players.TryGetValue(account.SaveKey, out var saved)
                ? saved.ToObject<Registerable<BehaviorEntity.Animal>>().Value : null;
        }

        public static void Capture(AccountId account, BehaviorEntity.Animal player) {
            if (player == null) return;
            players[account.SaveKey] = JToken.FromObject(
                new Registerable<BehaviorEntity.Animal>(player)
            );
        }
        public static void Save() {
            string path = Path.Combine(World.WORLD_SELECTION.First.Value.Path, "PlayerData.json");
            var json = new JObject { ["version"] = FormatVersion, ["players"] = JObject.FromObject(players) };
            string temporary = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(temporary, json.ToString(Formatting.None));
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
    }
}
