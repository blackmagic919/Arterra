using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Threading.Tasks;
using System;
using WorldConfig;

public static class WorldStorageHandler
{
    public static string META_LOCATION = Application.persistentDataPath + "/WorldMeta.json";
    public static string BASE_LOCATION = Application.persistentDataPath + "/Worlds/";
    public static LinkedList<WorldMeta> WORLD_SELECTION;

    public static void Activate(){ 
        Config.TEMPLATE = Resources.Load<Config>("Config");
        Config.CURRENT = Config.Create();
        SegmentedUIEditor.Initialize();
        PaginatedUIEditor.Initialize();
        LoadMetaSync(); LoadOptionsSync();
    }

    public static async Task LoadMeta(){
        if(!File.Exists(META_LOCATION)) {
            WORLD_SELECTION = new LinkedList<WorldMeta>(new WorldMeta[]{new (Guid.NewGuid().ToString())});
            await SaveMeta();
            return;
        }
        string data = await File.ReadAllTextAsync(META_LOCATION);
        WORLD_SELECTION = Newtonsoft.Json.JsonConvert.DeserializeObject<LinkedList<WorldMeta>>(data); //Does not call constructor
    }

    public static async Task SaveMeta(){
        using (FileStream fs = new FileStream(META_LOCATION, FileMode.Create, FileAccess.Write, FileShare.None)){
            using(StreamWriter writer = new StreamWriter(fs)){
                string data = Newtonsoft.Json.JsonConvert.SerializeObject(WORLD_SELECTION);
                await writer.WriteAsync(data);
                await writer.FlushAsync();
            };
        };
    }

    public static async Task LoadOptions(){
        string location = WORLD_SELECTION.First.Value.Path + "/Config.json";
        if(!Directory.Exists(WORLD_SELECTION.First.Value.Path) || !File.Exists(location)) {
            Config.CURRENT = Config.Create();
            await SaveOptions();
            return;
        }
        string data = await File.ReadAllTextAsync(location);
        Config.CURRENT = Newtonsoft.Json.JsonConvert.DeserializeObject<Config>(data); //Does not call constructor
        
    }

    public static async Task SaveOptions(){
        string location = WORLD_SELECTION.First.Value.Path + "/Config.json";
        if(!Directory.Exists(WORLD_SELECTION.First.Value.Path)) Directory.CreateDirectory(WORLD_SELECTION.First.Value.Path);
        using (FileStream fs = new FileStream(location, FileMode.Create, FileAccess.Write, FileShare.None)){
            using(StreamWriter writer = new StreamWriter(fs)){
                string data = Newtonsoft.Json.JsonConvert.SerializeObject(Config.CURRENT);
                await writer.WriteAsync(data);
                await writer.FlushAsync();
            };
        };
    }

    public static void LoadMetaSync(){
        if(!File.Exists(META_LOCATION)) {
            WORLD_SELECTION = new LinkedList<WorldMeta>(new WorldMeta[]{new (Guid.NewGuid().ToString())});
            SaveMetaSync();
            return;
        }
        string data = File.ReadAllText(META_LOCATION);
        WORLD_SELECTION = Newtonsoft.Json.JsonConvert.DeserializeObject<LinkedList<WorldMeta>>(data); //Does not call constructor
    }

    public static void SaveMetaSync(){
        using (FileStream fs = new FileStream(META_LOCATION, FileMode.Create, FileAccess.Write, FileShare.None)){
            using(StreamWriter writer = new StreamWriter(fs)){
                string data = Newtonsoft.Json.JsonConvert.SerializeObject(WORLD_SELECTION);
                writer.Write(data);
                writer.Flush();
            };
        };
    }

    public static void LoadOptionsSync(){
        string location = WORLD_SELECTION.First.Value.Path + "/Config.json";
        if(!Directory.Exists(WORLD_SELECTION.First.Value.Path) || !File.Exists(location)) {
            Config.CURRENT = Config.Create();
            SaveOptionsSync();
            return;
        }
        string data = File.ReadAllText(location);
        Config.CURRENT = Newtonsoft.Json.JsonConvert.DeserializeObject<Config>(data); //Does not call constructor
    }

    public static void SaveOptionsSync(){
        string location = WORLD_SELECTION.First.Value.Path + "/Config.json";
        if(!Directory.Exists(WORLD_SELECTION.First.Value.Path)) Directory.CreateDirectory(WORLD_SELECTION.First.Value.Path);
        using (FileStream fs = new FileStream(location, FileMode.Create, FileAccess.Write, FileShare.None)){
            using(StreamWriter writer = new StreamWriter(fs)){
                string data = Newtonsoft.Json.JsonConvert.SerializeObject(Config.CURRENT);
                writer.Write(data);
                writer.Flush();
            };
        };
    }
    public static void SelectWorld(WorldMeta meta){
        WORLD_SELECTION.Remove(meta);
        WORLD_SELECTION.AddFirst(meta);
        _ = LoadOptions(); //Don't use Task.Run because we want it to be on main thread until await
        _ = SaveMeta();
    }

    public static void CreateWorld(){
        WORLD_SELECTION.AddFirst(new WorldMeta(Guid.NewGuid().ToString()));
        Config.CURRENT = Config.Create();
        _ = SaveOptions();
        _ = SaveMeta();
    }

    public static void DeleteWorld(){
        if(WORLD_SELECTION.Count == 0) return;
        
        if(Directory.Exists(WORLD_SELECTION.First.Value.Path))
            Directory.Delete(WORLD_SELECTION.First.Value.Path, true);
        WORLD_SELECTION.RemoveFirst();
        if(WORLD_SELECTION.Count == 0) CreateWorld();
        else {
            _ = LoadOptions();
            _ = SaveMeta();
        }
    }


    public struct WorldMeta{
        [HideInInspector]
        public string Id;
        [HideInInspector]
        public string Path;
        [HideInInspector]
        public string Name;

        public WorldMeta(string id){
            this.Id = id;
            this.Path = WorldStorageHandler.BASE_LOCATION + "WorldData_" + id;
            this.Name = "New World";
        }
    }
}

