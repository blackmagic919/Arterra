using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Threading.Tasks;
using System;
using WorldConfig;

namespace MapStorage{
/// <summary>
/// Manages the access, loading, and saving of world configuration data as well as
/// account specific information shared between worlds. Modification of this architecture
/// may invalidate all previous world data and alter access of different worlds.
/// </summary>
public static class World
{
    /// <summary> The relative location in the user's file system of the file containing the "World Selection Meta Data"
    /// object responsible for identifying <b>all worlds</b> accessible in-game. See <see cref="WORLD_SELECTION"/> for more information. </summary>
    public static string META_LOCATION = Application.persistentDataPath + "/WorldMeta.json";
    /// <summary> The relative base location of all world-specific data in the user's file system. Each world should be able to source
    /// its non-meta, instance information completely within this directory, preferable in a sub-directory seperating itself 
    /// from other worlds. </summary>
    public static string BASE_LOCATION = Application.persistentDataPath + "/Worlds/";
    /// <summary> The MetaData Linked List responsible for locating and identifying all worlds in the system. All worlds maintain an entry in
    /// this list which is stored in the file at <see cref="META_LOCATION"/>. The order of elements in this list follows the 
    /// order in which worlds were last selected by the user; and this is the precise order of worlds shown in World Selection. 
    /// Modification or deletion of this list from storage can/will result in the (reversible) loss of <b>all</b> world data even if
    /// the world itself is not deleted. </summary>
    public static LinkedList<WorldMeta> WORLD_SELECTION;

    /// <summary> The primary startup function for loading the user's game information. Loads the <see cref="Config.TEMPLATE"> template </see>
    /// world configuration(the default world configuration) as well as finding the user's world selection meta data from the file system
    /// to load the user's last selected world's configuration. </summary>
    public static void Activate(){
        Config.TEMPLATE = Resources.Load<Config>("Config");
        Config.CURRENT = Config.Create();
        SegmentedUIEditor.Initialize();
        PaginatedUIEditor.Initialize();
        LoadMetaSync(); LoadOptionsSync();
    }

    /// <summary> Asynchronously loads the world selection meta data object from the corresponding file located
    /// at <see cref="META_LOCATION"/> in the file system. If the file does not exist, a new world selection
    /// meta data object is created and saved to the file system. See <see cref="WORLD_SELECTION"/> for more information. </summary>
    /// <returns>A threaded task that is responsible for loading the meta data.</returns>
    public static async Task LoadMeta(){
        if(!File.Exists(META_LOCATION)) {
            WORLD_SELECTION = new LinkedList<WorldMeta>(new WorldMeta[]{new (Guid.NewGuid().ToString())});
            await SaveMeta();
            return;
        }
        string data = await File.ReadAllTextAsync(META_LOCATION);
        WORLD_SELECTION = Newtonsoft.Json.JsonConvert.DeserializeObject<LinkedList<WorldMeta>>(data); //Does not call constructor
    }

    /// <summary> Asynchronously saves the world selection meta data object to the corresponding file located
    /// at <see cref="META_LOCATION"/> in the file system. See <see cref="WORLD_SELECTION"/> for more information. </summary>
    /// <returns> A threaded task that is responsible for saving the meta data. </returns>
    public static async Task SaveMeta(){
        using (FileStream fs = new FileStream(META_LOCATION, FileMode.Create, FileAccess.Write, FileShare.None)){
            using(StreamWriter writer = new StreamWriter(fs)){
                string data = Newtonsoft.Json.JsonConvert.SerializeObject(WORLD_SELECTION);
                await writer.WriteAsync(data);
                await writer.FlushAsync();
            };
        };
    }

    /// <summary> Asynchronously loads the world configuration of the currently selected world from the file system and
    /// copies it to the <see cref="Config.CURRENT"> current world configuration </see>. This is the world that is the first 
    /// element in the <see cref="WORLD_SELECTION"/> list. This function assumes that the <see cref="WORLD_SELECTION"/> has already 
    /// been loaded and is non-empty. If the world does not exist, a new world configuration is created and saved to the file system. </summary>
    /// <returns> A threaded task that is responsible for loading the world configuration. </returns>
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

    /// <summary> Asynchronously saves the world configuration of the currently selected world to the file system.
    /// This is the world configuration referenced by the <see cref="Config.CURRENT"> current world configuration </see> 
    /// and simultaneously should be the first element in the <see cref="WORLD_SELECTION"/> list. Hence, this function
    /// copies the <see cref="Config.CURRENT">object</see> to the location specified by the first element of <see cref="WORLD_SELECTION"/>. </summary>
    /// <returns>A threaded task that is responsible for saving the world configuration.</returns>
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

    /// <summary> Same as <see cref="LoadMeta"/> but synchronous. See <see cref="LoadMeta"/> for more information. </summary>
    public static void LoadMetaSync(){
        if(!File.Exists(META_LOCATION)) {
            WORLD_SELECTION = new LinkedList<WorldMeta>(new WorldMeta[]{new (Guid.NewGuid().ToString())});
            SaveMetaSync();
            return;
        }
        string data = File.ReadAllText(META_LOCATION);
        WORLD_SELECTION = Newtonsoft.Json.JsonConvert.DeserializeObject<LinkedList<WorldMeta>>(data); //Does not call constructor
    }

    /// <summary> Same as <see cref="SaveMeta"/> but synchronous. See <see cref="SaveMeta"/> for more information. </summary>
    public static void SaveMetaSync(){
        using (FileStream fs = new FileStream(META_LOCATION, FileMode.Create, FileAccess.Write, FileShare.None)){
            using(StreamWriter writer = new StreamWriter(fs)){
                string data = Newtonsoft.Json.JsonConvert.SerializeObject(WORLD_SELECTION);
                writer.Write(data);
                writer.Flush();
            };
        };
    }

    /// <summary> Same as <see cref="LoadOptions"/> but synchronous. See <see cref="LoadOptions"/> for more information. </summary>
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

    /// <summary> Same as <see cref="SaveOptions"/> but synchronous. See <see cref="SaveOptions"/> for more information. </summary>
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

    /// <summary>
    /// Selects the world through the information specified in <paramref name="meta"/>. This involves
    /// moving the entry to the front of <see cref="WORLD_SELECTION"/>, since it now is the most recently
    /// selected world, and loading the world configuration from the file system to the 
    /// <see cref="Config.CURRENT"> current world configuration </see> static location. 
    /// </summary>
    /// <param name="meta">The meta data necessary to load the world. <paramref name="meta"/> should be
    /// an entry within <see cref="WORLD_SELECTION"/>, see <seealso cref="WorldMeta"/> for more info. </param>
    public static void SelectWorld(WorldMeta meta){
        WORLD_SELECTION.Remove(meta);
        WORLD_SELECTION.AddFirst(meta);
        _ = LoadOptions(); //Don't use Task.Run because we want it to be on main thread until await
        _ = SaveMeta();
    }

    /// <summary> Creates a new world and selects it. This involves creating adding a first entry within <see cref="WORLD_SELECTION"/>
    /// since it is now the most recently selected world, and creating a new world configuration off the template
    /// configuration. The new world config is copied to the <see cref="Config.CURRENT"> current world configuration </see>
    /// static location. </summary>
    public static void CreateWorld(){
        WORLD_SELECTION.AddFirst(new WorldMeta(Guid.NewGuid().ToString()));
        Config.CURRENT = Config.Create();
        _ = SaveOptions();
        _ = SaveMeta();
    }

    /// <summary> Deletes the currently selected world. This involves removing the first entry in <see cref="WORLD_SELECTION"/>
    /// and deleting the corresponding information associated with the world in the file system indicated by this entry. Note, 
    /// doing this is absolute and irreversible. A deleted world will have all of its information removed irretrievably.
    /// This function then loads then selects the next consecutive world in <see cref="WORLD_SELECTION"/> and loads its configuration,
    /// creating a new world if there are no worlds left. </summary>
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

    /// <summary>The meta data object responsible for identifying a world in the file system. Only 
    /// information necessary to identify the world and display it in the world selection
    /// screen is stored here; this is to avoid loading large world configuration files 
    /// when viewing the user's created worlds. </summary>
    public struct WorldMeta{
        /// <summary> The unique identifier of the world in the file system. Unlike the world's <see cref="Name"/>,
        /// this is an absolute unique identifier for the world that should not be changed. </summary>
        [HideInInspector]
        public string Id;

        /// <summary> The location of the directory containing the world-specific information in the file system. 
        /// This includes the world configuration, and any modified world data. </summary>
        [HideInInspector]
        public string Path;

        /// <summary> The user-assigned name of the world. This is the name that will be displayed 
        /// in-game and to the user. This does not need to be unique and may be customized for user 
        /// comfort and readbility. </summary>
        [HideInInspector]
        public string Name;

        /// <summary> Creates a new world meta data object with the specified id.
        /// This action creates a new unique location for the world's information
        /// and provides a default name for the world. </summary>
        /// <param name="id">The absolute unique identifier for the world</param>
        public WorldMeta(string id){
            this.Id = id;
            this.Path = BASE_LOCATION + "WorldData_" + id;
            this.Name = "New World";
        }
    }
}}

