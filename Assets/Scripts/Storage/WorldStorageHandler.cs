using System.Threading;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Threading.Tasks;
using System;
using Arterra.Configuration;
using Arterra.Core.Network;
using Newtonsoft.Json;

namespace Arterra.Core.Storage {
    /// <summary>
    /// Manages the access, loading, and saving of world configuration data as well as
    /// account specific information shared between worlds. Modification of this architecture
    /// may invalidate all previous world data and alter access of different worlds.
    /// </summary>
    public static class World {
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
        private static GalaxyType _galaxyType;
        /// <summary>The type of galaxy currently being viewed. Galaxy refers to network dimension</summary>
        public static GalaxyType GALAXY_TYPE => _galaxyType;


        /// <summary> The tail path name of the display chunk image for each world </summary>
        public const string DisplayChunkPath = "/display_chunk";

        /// <summary> The primary startup function for loading the user's game information. Loads the <see cref="Config.TEMPLATE"> template </see>
        /// world configuration(the default world configuration) as well as finding the user's world selection meta data from the file system
        /// to load the user's last selected world's configuration. </summary>
        private static Task initialization;

        /// <summary>Call on Unity's main thread. All consumers share one load; failed loads can be retried.</summary>
        public static Task EnsureInitializedAsync() {
            // With domain reload disabled, a completed task can outlive its Unity config object.
            if (initialization == null || initialization.IsFaulted || initialization.IsCanceled
                || (initialization.Status == TaskStatus.RanToCompletion && Config.CURRENT == null))
                initialization = InitializeAsync();
            return initialization;
        }

        private static void InitializeTemplates() {
            Config.TEMPLATE = Resources.Load<Config>("Config");
            SegmentedUIEditor.Initialize();
            PaginatedUIEditor.Initialize();
        }

        private static async Task InitializeAsync() {
            InitializeTemplates();
            await LoadGalaxy(GalaxyType.Local);
            if (WORLD_SELECTION.Count == 0) {
                WORLD_SELECTION.AddFirst(new WorldMeta(Guid.NewGuid().ToString()));
                await SaveMeta();
            }
            await LoadOptions();
        }

        /// <summary> Asynchronously loads the world selection meta data object from the corresponding file located
        /// at <see cref="META_LOCATION"/> in the file system. If the file does not exist, a new world selection
        /// meta data object is created and saved to the file system. See <see cref="WORLD_SELECTION"/> for more information. </summary>
        /// <returns>A threaded task that is responsible for loading the meta data.</returns>
        public static async Task LoadGalaxy(GalaxyType galaxy, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            LinkedList<WorldMeta> newSelection;
            switch (galaxy) {
                case GalaxyType.Local:
                    if (!File.Exists(META_LOCATION)) {
                        newSelection = new LinkedList<WorldMeta>(new[] { new WorldMeta(Guid.NewGuid().ToString()) });
                        await SaveMeta(newSelection, cancellationToken);
                    } else {
                        string data = await File.ReadAllTextAsync(META_LOCATION, cancellationToken);
                        newSelection = JsonConvert.DeserializeObject<LinkedList<WorldMeta>>(data)
                            ?? new LinkedList<WorldMeta>();
                    }
                    if (newSelection.First != null)
                        newSelection.First.Value.LastAccessTime = DateTime.Now;
                    break;
                case GalaxyType.DirectWAN:
                    newSelection = new LinkedList<WorldMeta>(
                        await NetworkManager.QueryWorldMetasAsync(cancellationToken));
                    break;
                default:
                    Debug.Log($"Galaxy type {galaxy} is not implemented yet.");
                    newSelection = WORLD_SELECTION;
                    break;
            }

            // Commit together, with no await between cancellation check and assignment.
            cancellationToken.ThrowIfCancellationRequested();
            WORLD_SELECTION = newSelection;
            _galaxyType = galaxy;
        }

        /// <summary> Asynchronously loads the world configuration of the currently selected world from the file system and
        /// copies it to the <see cref="Config.CURRENT"> current world configuration </see>. This is the world that is the first 
        /// element in the <see cref="WORLD_SELECTION"/> list. This function assumes that the <see cref="WORLD_SELECTION"/> has already 
        /// been loaded and is non-empty. If the world does not exist, a new world configuration is created and saved to the file system. </summary>
        /// <returns> A threaded task that is responsible for loading the world configuration. </returns>
        public static async Task LoadOptions(CancellationToken cancellationToken = default) {
            WorldMeta meta = WORLD_SELECTION?.First?.Value
                ?? throw new InvalidOperationException("Select a world before loading options.");
            Config loaded;
            switch (meta.Type) {
                case GalaxyType.Local:
                    await NetworkManager.DisconnectClientAsync();
                    string location = meta.Path + "/Config.json";
                    if (!File.Exists(location)) {
                        loaded = Config.Create();
                        await SaveOptions(meta, loaded, cancellationToken);
                    } else {
                        string data = await File.ReadAllTextAsync(location, cancellationToken);
                        loaded = JsonConvert.DeserializeObject<Config>(data) ?? Config.Create();
                    }
                    break;
                case GalaxyType.DirectWAN:
                    Config previous = Config.CURRENT;
                    await NetworkManager.ConnectSessionAsync(meta.SessionId, cancellationToken);
                    try {
                        loaded = await Config.CurrentVariable.GetAsync(cancellationToken)
                            ?? throw new InvalidOperationException("The host returned a null world configuration.");
                    } catch {
                        try { await NetworkManager.DisconnectClientAsync(); }
                        catch (Exception cleanupError) { Debug.LogException(cleanupError); }
                        if (!TransportManager.Instance.IsRunning && TransportManager.Instance.Configuration == null)
                            Config.CURRENT = previous;
                        throw;
                    }
                    break;
                default:
                    Debug.Log($"Galaxy type {meta.Type} is not implemented yet.");
                    loaded = Config.CURRENT;
                    break;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(meta, WORLD_SELECTION?.First?.Value))
                throw new OperationCanceledException("World selection changed during options loading.");
            // Remote reads already populate the network variable; do not send them back as writes.
            if (meta.Type == GalaxyType.Local) Config.CURRENT = loaded;
        }

        /// <summary> Asynchronously saves the world selection meta data object to the corresponding file located
        /// at <see cref="META_LOCATION"/> in the file system. See <see cref="WORLD_SELECTION"/> for more information. </summary>
        /// <returns> A threaded task that is responsible for saving the meta data. </returns>
        public static Task SaveMeta() {
            return GALAXY_TYPE == GalaxyType.Local ? SaveMeta(WORLD_SELECTION) : Task.CompletedTask;
        }

        // Initialization can save its pending selection without publishing it first.
        private static Task SaveMeta(LinkedList<WorldMeta> selection, CancellationToken token = default) {
            return File.WriteAllTextAsync(META_LOCATION, JsonConvert.SerializeObject(selection), token);
        }

        /// <summary> Same as <see cref="SaveMeta"/> but synchronous. See <see cref="SaveMeta"/> for more information. </summary>
        public static void SaveMetaSync() {
            if (GALAXY_TYPE != GalaxyType.Local) return;
            File.WriteAllText(META_LOCATION, JsonConvert.SerializeObject(WORLD_SELECTION));
        }

        /// <summary> Asynchronously saves the world configuration of the currently selected world to the file system.
        /// This is the world configuration referenced by the <see cref="Config.CURRENT"> current world configuration </see> 
        /// and simultaneously should be the first element in the <see cref="WORLD_SELECTION"/> list. Hence, this function
        /// copies the <see cref="Config.CURRENT">object</see> to the location specified by the first element of <see cref="WORLD_SELECTION"/>. </summary>
        /// <returns>A threaded task that is responsible for saving the world configuration.</returns>
        public static Task SaveOptions() {
            return GALAXY_TYPE == GalaxyType.Local
                ? SaveOptions(WORLD_SELECTION.First.Value, Config.CURRENT) : Task.CompletedTask;
        }

        // Loading saves the new config before assigning CURRENT; normal saves use the same path.
        private static Task SaveOptions(WorldMeta meta, Config config, CancellationToken token = default) {
            token.ThrowIfCancellationRequested();
            string location = PrepareConfigPath(meta);
            return File.WriteAllTextAsync(location, JsonConvert.SerializeObject(config), token);
        }

        private static string PrepareConfigPath(WorldMeta meta) {
            Directory.CreateDirectory(meta.Path);
            return meta.Path + "/Config.json";
        }

        /// <summary> Same as <see cref="SaveOptions"/> but synchronous. See <see cref="SaveOptions"/> for more information. </summary>
        public static void SaveOptionsSync() {
            if (GALAXY_TYPE != GalaxyType.Local) return;
            File.WriteAllText(PrepareConfigPath(WORLD_SELECTION.First.Value), JsonConvert.SerializeObject(Config.CURRENT));
        }

        /// <summary>
        /// Selects the world through the information specified in <paramref name="meta"/>. This involves
        /// moving the entry to the front of <see cref="WORLD_SELECTION"/>, since it now is the most recently
        /// selected world, and loading the world configuration from the file system to the 
        /// <see cref="Config.CURRENT"> current world configuration </see> static location. 
        /// </summary>
        /// <param name="meta">The meta data necessary to load the world. <paramref name="meta"/> should be
        /// an entry within <see cref="WORLD_SELECTION"/>, see <seealso cref="WorldMeta"/> for more info. </param>
        public static async Task SelectWorld(WorldMeta meta, CancellationToken cancellationToken = default) {
            WORLD_SELECTION.Remove(meta);
            WORLD_SELECTION.AddFirst(meta);
            meta.LastAccessTime = DateTime.Now;
            await LoadOptions(cancellationToken);
            await SaveMeta();
        }

        /// <summary> Creates a new world and selects it. This involves creating adding a first entry within <see cref="WORLD_SELECTION"/>
        /// since it is now the most recently selected world, and creating a new world configuration off the template
        /// configuration. The new world config is copied to the <see cref="Config.CURRENT"> current world configuration </see>
        /// static location. </summary>
        public static void CreateWorld() {
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
        public static void DeleteWorld() {
            if (WORLD_SELECTION.Count == 0) return;

            if (Directory.Exists(WORLD_SELECTION.First.Value.Path))
                Directory.Delete(WORLD_SELECTION.First.Value.Path, true);
            WORLD_SELECTION.RemoveFirst();
            if (WORLD_SELECTION.Count == 0) CreateWorld();
            else {
                _ = LoadOptions();
                _ = SaveMeta();
            }
        }

        /// <summary>The meta data object responsible for identifying a world in the file system. Only 
        /// information necessary to identify the world and display it in the world selection
        /// screen is stored here; this is to avoid loading large world configuration files 
        /// when viewing the user's created worlds. </summary>
        public class WorldMeta {
            /// <summary> The type of state the world being referenced is recorded in.</summary>
            [HideInInspector]
            public GalaxyType Type;

            /// <summary> The unique identifier of the world in the file system. Unlike the world's <see cref="Name"/>,
            /// this is an absolute unique identifier for the world that should not be changed. </summary>
            [HideInInspector]
            public string Id;
            [JsonProperty("Path")]
            private string _meta;

            /// <summary> The location of the directory containing the world-specific information in the file system. 
            /// This includes the world configuration, and any modified world data. </summary>
            [HideInInspector] [JsonIgnore]
            public string Path {
                get { return _meta; }
                set { _meta = value; }
            }
            
            /// <summary> The sessionId on the remote discovery network if the world is of <see cref="WorldType">WorldType.Remote</see> </summary>
            [HideInInspector] [JsonIgnore]
            public string SessionId {
                get { return _meta; }
                set { _meta = value; }
            }

            /// <summary> The user-assigned name of the world. This is the name that will be displayed 
            /// in-game and to the user. This does not need to be unique and may be customized for user 
            /// comfort and readbility. </summary>
            [HideInInspector]
            public string Name;

            /// <summary> The last time this world was selected. </summary>
            public DateTime LastAccessTime;
            /// <summary> The timestamp of when the world was created. </summary>
            public DateTime CreationTime;

            /// <summary> Creates a new world meta data object with the specified id.
            /// This action creates a new unique location for the world's information
            /// and provides a default name for the world. </summary>
            /// <param name="id">The absolute unique identifier for the world</param>
            public WorldMeta(string id) {
                this.Id = id;
                this.Path = BASE_LOCATION + "WorldData_" + id;
                this.Name = "New World";
                this.CreationTime = DateTime.Now;
                this.LastAccessTime = DateTime.Now;
            }
        }
        public enum GalaxyType {
            Local, 
            DirectWAN,
            DedicatedServer,
        }

        public static GalaxyType NextGalaxy(GalaxyType type) {
            return (GalaxyType)(((int)type + 1) % 3);
        }
    }
}

