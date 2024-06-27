using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using System.Reflection;

public static class WorldStorageHandler
{
    public static string BASE_LOCATION = Application.persistentDataPath + "/Worlds/";
    public static WorldData WORLD_OPTIONS;

    static WorldStorageHandler(){ 
        if(!Directory.Exists(BASE_LOCATION)) System.IO.Directory.CreateDirectory(BASE_LOCATION); 
        WORLD_OPTIONS.Create();
    }
    public static async Task<WorldData[]> LoadMeta(){
        string[] worldPaths = Directory.GetDirectories(BASE_LOCATION, "WorldData*", SearchOption.TopDirectoryOnly);
        WorldData[] worlds = new WorldData[worldPaths.Length];
        for(int i = 0; i < worldPaths.Length; i++){
            string data = await File.ReadAllTextAsync(worldPaths[i] + "/WorldMeta.json");
            worlds[i] = Newtonsoft.Json.JsonConvert.DeserializeObject<WorldData>(data); //Does not call constructor
        }
        return worlds;
    }

    public static async Task SaveMeta(WorldData world){
        if(!Directory.Exists(world.Path)) Directory.CreateDirectory(world.Path);
        using (FileStream fs = new FileStream(world.Path + "/WorldMeta.json", FileMode.Create, FileAccess.Write, FileShare.None)){
            using(StreamWriter writer = new StreamWriter(fs)){
                string data = Newtonsoft.Json.JsonConvert.SerializeObject(world);
                await writer.WriteAsync(data);
                await writer.FlushAsync();
            }
        };
    }

    public static void DeleteMeta(WorldData world){
        string worldDir = BASE_LOCATION + "WorldData_" + world.Id;
        if(Directory.Exists(worldDir)) Directory.Delete(worldDir, true);
    }

    public static void SetOptions(WorldData world){ WORLD_OPTIONS = world; }


    public struct WorldData{
        [HideInInspector]
        public string Id;
        [HideInInspector]
        public string Path;
        [HideInInspector]
        public string Name;

        public WorldOptions WorldOptions;

        [OnDeserialized]
        internal void OnDeserialized(StreamingContext context = default){
            object defaultOptions = Resources.Load<WorldOptions>("Prefabs/DefaultOptions"); 
            object worldOptions = WorldOptions;
            WorldOptions.SupplementTree(ref worldOptions, ref defaultOptions);
        }

        public WorldData(string id, string path, string name, int seed){
            this.Id = id;
            this.Path = path;
            this.Name = name;
            
            WorldOptions = new WorldOptions();
            OnDeserialized();
            
            WorldOptions.seed = seed;
            Save();
        }

        //Only to be used for testing
        public void Create(){
            this.Id = default;
            this.Path = default;
            this.Name = default;

            WorldOptions = new WorldOptions();
            WorldOptions.seed = 0;
            OnDeserialized();
        }

        public void Save(){ _ = SaveMeta(this); }
    }
}
