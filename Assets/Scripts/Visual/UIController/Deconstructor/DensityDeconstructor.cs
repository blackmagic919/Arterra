using System.Collections.Generic;
using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEditor;
using Utils;
using System.Linq;
using Arterra.Config.Generation.Structure;
using Arterra.Config;
using Arterra.Core.Storage;
using Arterra.Core.Terrain;

[ExecuteInEditMode]
public class DensityDeconstructor : MonoBehaviour
{
#if UNITY_EDITOR
    [Header("Misc")]
    public float IsoLevel = 0.5f;
    [SerializeField]
    public StructureData Structure;

    [Header("Create")]
    public string loadPath;
    public float3 SDFoffset;

    GridManager.SelectionArray SelectedArray;
    Queue<uint> Selected;
    GridManager gridManager;
    ModelManager modelManager;
    private bool initialized = false;
    private bool showGrid = false;
    private bool showModel = false;

    private StructureData.PointInfo curData;
    private StructureData.PointInfo prevData;

    //Don't ask me why the conversion is like this..
    private Vector2 _MousePos{ get{ return new Vector2(Event.current.mousePosition.x*2, Camera.current.scaledPixelHeight - Event.current.mousePosition.y*2); }}
    private uint3 GridSize{get{return Structure.settings.value.GridSize;}}
    readonly int3[] adjDelta = new int3[6] { 
        new int3(-1, 0, 0), 
        new int3(1, 0, 0),
        new int3(0, -1, 0),
        new int3(0, 1, 0),
        new int3(0, 0, -1),
        new int3(0, 0, 1) 
    };

    public void OnEnable(){
        SceneView.duringSceneGui += OnSceneGUI;
    }

    public void OnDisable(){
        SceneView.duringSceneGui -= OnSceneGUI;
        Release();
    }
    public void Release(){
        if(!initialized) return;
        initialized = false;

        SerializeMaterials();
        gridManager.Release();
        modelManager.Release();
        Selected.Clear();
    }

    private void InitializeGrid(){
        if(GridSize.x == 0 || GridSize.y == 0 || GridSize.z == 0)
            throw new Exception("Grid size cannot be zero");
        if(initialized) Release(); 
        if(Config.CURRENT == null) World.Activate();
        IRegister.Setup(Config.CURRENT); //Initialize Register LUTS
        SystemProtocol.MinimalStartup(); // Initialize Material Information
        Structure.Initialize();
        DeserializeMaterials();

        int numPoints = (int)(GridSize.x * GridSize.y * GridSize.z);
        SelectedArray = new GridManager.SelectionArray(numPoints);
        Selected = new Queue<uint>();

        gridManager = new GridManager(GridSize, this.gameObject.transform, UtilityBuffers.GenerationBuffer, numPoints, 0);
        modelManager = new ModelManager(GridSize, this.gameObject.transform, IsoLevel, UtilityBuffers.TransferBuffer, UtilityBuffers.GenerationBuffer, gridManager.offsets.bufferEnd);
        gridManager.GenerateModel();

        initialized = true;
        showGrid = true;
        showModel = true;
    }

    public void SaveData()
    {
        SerializeMaterials();
        EditorUtility.SetDirty(Structure);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    public void LoadData(){
        InitializeGrid();
        //Immediately Render Model
        this.UpdateMapData();
        this.modelManager.GenerateModel();
    }

    public void ResizeStructure() {
        int3 offset = (int3)math.floor(SDFoffset);
        int3 nGridSize = math.max((int3)GridSize + offset, int3.zero);
        StructureData.PointInfo[] NewStructure = CustomUtility.RescaleLinearMap(
            Structure.map.value.ToArray(),
            (int3)GridSize,
            offset, 0
        );
        
        Structure.map.value = NewStructure.ToList();
        Structure.settings.value.GridSize = (uint3)nGridSize;
        InitializeGrid();
        this.UpdateMapData();
        this.modelManager.GenerateModel();
    }

    public void ShiftStructure() {
        int3 offset = (int3)math.floor(SDFoffset);
        StructureData.PointInfo[] NewStructure = CustomUtility.RescaleLinearMap(
            Structure.map.value.ToArray(),
            (int3)GridSize,
            0, offset
        );

        Structure.map.value = NewStructure.ToList();
        InitializeGrid();
        this.UpdateMapData();
        this.modelManager.GenerateModel();
    }

    public void ConvertMesh() {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/" + loadPath + ".fbx").GetComponent<MeshFilter>().sharedMesh;
        if (mesh == null) throw new Exception("Mesh not found");
        InitializeGrid();

        GetDataFromMesh(mesh);
        this.UpdateMapData();
        this.modelManager.GenerateModel();
    }

    public void LoadChunk() {
        InitializeGrid();
        MapData[] map = Chunk.ReadChunkBin(loadPath, 0, out _);
        if (map == null) return;

        Arterra.Config.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain;
        Structure.map = Utils.CustomUtility.RescaleLinearMap(map, rSettings.mapChunkSize, 2, 1)
            .Select(s => new StructureData.PointInfo{data = s.data, preserve = false}).ToList();
        Structure.settings.value.GridSize = new uint3((uint)rSettings.mapChunkSize,
            (uint)rSettings.mapChunkSize, (uint)rSettings.mapChunkSize) + 2;
        this.UpdateMapData();
        this.modelManager.GenerateModel();
    }

    public void Start() {
        if(Application.isPlaying) this.gameObject.SetActive(false);
    }

    public void Update(){
        if(!initialized) return;

        if(showGrid) this.gridManager.Render(); 
        if(showModel) this.modelManager.Render();
    }
    
    public void UpdateMapData(){UtilityBuffers.TransferBuffer.SetData(Structure.map.value.ToArray()); }
    public void OnSceneGUI(SceneView sceneView){
        if(!initialized) return;

        Rect GUIBox = new Rect(10, 10, 200, 400);
        GUILayout.BeginArea(GUIBox);
        var headerStyle = new GUIStyle(GUI.skin.label){ fontStyle = FontStyle.Bold };

        if (Selected.Count > 0){ //Map Data Editor
            GUILayout.Label("MapData", headerStyle);
            GUILayout.BeginHorizontal();
            var reg = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
            string currentMaterial = reg.RetrieveName(curData.material);
            GUILayout.Label("Material");  currentMaterial = EditorGUILayout.TextArea(currentMaterial, GUILayout.Width(100));
            if (reg.Contains(currentMaterial)) curData.material = reg.RetrieveIndex(currentMaterial);
            GUILayout.EndHorizontal();GUILayout.BeginHorizontal();
            GUILayout.Label("Density"); curData.density = (int)(GUILayout.HorizontalSlider(curData.density/255.0f, 0.0f, 1.0f, GUILayout.Width(100)) * 255);
            GUILayout.EndHorizontal(); GUILayout.BeginHorizontal();
            GUILayout.Label("Viscosity"); curData.viscosity = (int)(GUILayout.HorizontalSlider(curData.viscosity/255.0f, 0.0f, 1.0f, GUILayout.Width(100)) * 255);
            GUILayout.EndHorizontal(); GUILayout.BeginHorizontal();
            GUILayout.Label("Preserve Viscosity"); curData.preserve = GUILayout.Toggle(curData.preserve, "");
            GUILayout.EndHorizontal();
        }
        GUILayout.Label("Editor Settings", headerStyle);
        showGrid = GUILayout.Button("Toggle Grid") ? !showGrid : showGrid;
        showModel = GUILayout.Button("Toggle Model") ? !showModel : showModel;

        GUILayout.EndArea();

        if(!GUIBox.Contains(_MousePos)) HandleInputs();
        if(Selected.Count > 0){ HandleMapChange(); }
    }

    private void HandleMapChange(){

        if (curData.data != prevData.data){
            int deltaDensity = curData.density - prevData.density;
            int deltaViscosity = curData.viscosity - prevData.viscosity;

            UpdateSelected((e) => {
                e.density = Mathf.Clamp(e.density + deltaDensity, 0, 255);
                e.viscosity = Mathf.Clamp(e.viscosity + deltaViscosity, 0, e.density);
                e.material = curData.material == prevData.material ? e.material : curData.material;
                e.preserve = curData.preserve == prevData.preserve ? e.preserve : curData.preserve;
                return e;
            });

            this.UpdateMapData();
            this.modelManager.GenerateModel();
            prevData = curData;
        }
    }

    void SetDisplayMapData(StructureData.PointInfo data){
        curData = data; prevData = data;
    }

    void HandleInputs(){
        if(Event.current.type == EventType.KeyDown && Event.current.command){
            if(Event.current.keyCode == KeyCode.I) InvertSelection();
            if(Event.current.keyCode == KeyCode.N) SwapSelection();
            if(Event.current.keyCode == KeyCode.D) SelectDensity();
            if(Event.current.keyCode == KeyCode.M) SelectMaterial();
            if(Event.current.keyCode == KeyCode.W) SelectWalkable();
        } else if(Event.current.type == EventType.MouseDown && Event.current.button == 0) SelectPoint();
        else return;

        Event.current.Use(); //Prevent propogation of default behavior
        this.gridManager.SetSelectionData(ref SelectedArray);
        FlushSelection();
    }

    bool RayIntersectsSphere(Ray ray, Vector3 sphereCenter, float sphereRadius)
    {
        Vector3 oc = ray.origin - sphereCenter;

        float a = Vector3.Dot(ray.direction, ray.direction);
        float b = 2.0f * Vector3.Dot(oc, ray.direction);
        float c = Vector3.Dot(oc, oc) - sphereRadius * sphereRadius;
        float discriminant = b * b - 4 * a * c;

        return discriminant >= 0;
    }

    private void SelectPoint(){
        Ray SelectionWS = Camera.current.ScreenPointToRay(_MousePos);
        if(!Event.current.shift) ReleaseSelection();

        Ray SelectionOS = new Ray(this.transform.worldToLocalMatrix.MultiplyPoint3x4(SelectionWS.origin), 
                                    this.transform.TransformDirection(SelectionWS.direction));

        int3 point; 
        for(point.x = 0; point.x < GridSize.x; point.x++){
            for(point.y = 0; point.y < GridSize.y; point.y++){
                for(point.z = 0; point.z < GridSize.z; point.z++){
                    if(RayIntersectsSphere(SelectionOS, new Vector3(point.x, point.y, point.z), 0.05f)){
                        int index = CustomUtility.irregularIndexFromCoord(point, new int2(GridSize.yz));
                        SelectedArray[index] = Event.current.shift ? !SelectedArray[index] : true;
                        SetDisplayMapData(Structure.map.value[index]);
                    }
                }
            }
        }
    }

    private void FlushSelection(){
        Selected.Clear();
        for(uint i = 0; i < Structure.map.value.Count; i++){
            if(SelectedArray[(int)i]) Selected.Enqueue(i);
        }
    }

    private void ReleaseSelection(){
        UpdateSelectionState((e) => { return false; });
        Selected.Clear();
    }
    
    private void InvertSelection(){
        for(int i = 0; i < Structure.map.value.Count; i++)
            SelectedArray[i] = !SelectedArray[i];
    }

    private void SwapSelection(){
        UpdateSelectionState((e) => {return false;}); 

        uint numPoints = GridSize.x * GridSize.y * GridSize.z;
        UpdateSelectionState((ind) =>{
            for(int i = 0; i < 6; i++){
                int newInd = (int)ind + CustomUtility.irregularIndexFromCoord(adjDelta[i], new int2(GridSize.yz));
                if(newInd >= numPoints || newInd < 0) return;
                SelectedArray[newInd] = Structure.map.value[newInd].material != Structure.map.value[(int)ind].material;
            }
        });
    }

    private void SelectDensity(){
        FloodFill((ind) => {
            if((Structure.map.value[ind].density < IsoLevel * 255) != (curData.density < IsoLevel * 255)) return false;
            if(SelectedArray[ind]) return false;
            SelectedArray[ind] = true;
            return true;
        });
    }
    private void SelectMaterial(){
        FloodFill((ind) => {
            if(Structure.map.value[ind].material != curData.material) return false;
            if(SelectedArray[ind]) return false;
            SelectedArray[ind] = true;
            return true;
        });
    }

    private void SelectWalkable(){
        bool VerifyProfile(Arterra.Config.Generation.Entity.Authoring info, int3 BaseCoord) {
            bool allC = true; bool anyC = false; bool any0 = false;
            uint3 dC = new (0);
            Arterra.Config.Generation.Entity.EntitySetting.ProfileInfo p = info.Setting.profile;
            int3 gridSize = (int3)Structure.settings.value.GridSize;
            for(dC.x = 0; dC.x < p.bounds.x; dC.x++){
                for(dC.y = 0; dC.y < p.bounds.y; dC.y++){
                    for(dC.z = 0; dC.z < p.bounds.z; dC.z++){
                        uint index = dC.x * p.bounds.y * p.bounds.z + dC.y * p.bounds.z + dC.z;
                        Arterra.Config.Generation.Entity.ProfileE profile = info.Profile.value[(int)index];
                        if (math.any(BaseCoord + (int3)dC >= gridSize)) return false;
                        int3 rCoord = BaseCoord + (int3)dC;
                        int rIndex = rCoord.x * gridSize.y * gridSize.z + rCoord.y * gridSize.z + rCoord.z;
                        bool valid = profile.bounds.Contains(new MapData{data = Structure.map.value[rIndex].data});
                        allC = allC && (valid || !profile.AndFlag);
                        anyC = anyC || (valid && profile.OrFlag);
                        any0 = any0 || profile.OrFlag;
                    }
                }
            } 
            if(allC && (!any0 || anyC)) return true;
            else return false;
        }
        var info = Config.CURRENT.Generation.Entities.Retrieve("Player");
        if (info == null) return;
        int3 gridSize = (int3)Structure.settings.value.GridSize;
        for(int ind = 0; ind < gridSize.x * gridSize.y * gridSize.z; ind++){
            int3 coord = new int3(ind / (gridSize.y * gridSize.z), (ind / gridSize.z) % gridSize.y, ind % gridSize.z);
            if (!VerifyProfile(info, coord)) continue;
            SelectedArray[ind] = true;
        }
    }

    void UpdateSelected(Func<StructureData.PointInfo, StructureData.PointInfo> action){
        for(int i = 0, count = Selected.Count; i < count; i++){
            uint index = Selected.Dequeue();
            Structure.map.value[(int)index] = action.Invoke(Structure.map.value[(int)index]);
            Selected.Enqueue(index);
        }
    }

    void UpdateSelectionState(Action<uint> action){
        for(int i = 0, count = Selected.Count; i < count; i++){
            uint index = Selected.Dequeue();
            action.Invoke(index);
            Selected.Enqueue(index);
        }
    }

    void UpdateSelectionState(Func<bool, bool> action){
        for(int i = 0, count = Selected.Count; i < count; i++){
            uint index = Selected.Dequeue();
            SelectedArray[(int)index] = action.Invoke(SelectedArray[(int)index]);
            Selected.Enqueue(index);
        }
    }

    void FloodFill(Func<int, bool> action){
        Queue<int3> queue = new Queue<int3>(Selected.Select(e => new int3((int)(e / (GridSize.y * GridSize.z)), (int)((e / GridSize.z) % GridSize.y), (int)(e % GridSize.z))));

        while(queue.Count > 0){
            int3 point = queue.Dequeue();
            for(int i = 0; i < 6; i++){
                int3 newPoint = point + adjDelta[i];
                if(newPoint.x < 0 || newPoint.x >= GridSize.x || 
                    newPoint.y < 0 || newPoint.y >= GridSize.y || 
                    newPoint.z < 0 || newPoint.z >= GridSize.z) continue;
                if(action.Invoke(CustomUtility.irregularIndexFromCoord(newPoint, new int2(GridSize.yz)))) 
                    queue.Enqueue(newPoint);
            }
        }
    }

    private void SerializeMaterials(){
        if(Structure.Names.value != null && Structure.Names.value.Count > 0)
            return; //Already serialized
        Dictionary<int, int> MaterialDict = new Dictionary<int, int>();
        for(int i = 0; i < Structure.map.value.Count; i++){
            StructureData.PointInfo p = Structure.map.value[i];
            MaterialDict.TryAdd(p.material, MaterialDict.Count);
            p.material = MaterialDict[p.material];
            Structure.map.value[i] = p;
        }
        string[] materials = new string[MaterialDict.Count];
        var reg = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        foreach(var pair in MaterialDict){
            materials[pair.Value] = reg.RetrieveName(pair.Key);
        }
        Structure.Names.value = materials.ToList();
    }

    private void DeserializeMaterials(){
        if (Structure.Names.value == null || Structure.Names.value.Count == 0)
            return;
        List<int> MaterialLUT = new List<int>();
        var reg = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        for(int i = 0; i < Structure.Names.value.Count; i++){
            MaterialLUT.Add(reg.RetrieveIndex(Structure.Names.value[i]));
        }  
        //Mark it as already deserialized so we don't double deserialize
        Structure.Names.value = null;
        for(int i = 0; i < Structure.map.value.Count; i++){
            StructureData.PointInfo p = Structure.map.value[i];
            p.material = MaterialLUT[math.clamp(p.material, 0, MaterialLUT.Count()-1)];
            Structure.map.value[i] = p;
        }
    }

    void GetDataFromMesh(Mesh mesh){
        ComputeShader SDFConstructor = Resources.Load<ComputeShader>("Compute/CGeometry/Deconstructor/MeshDeconstructor");
        ComputeBuffer vertexBuffer = new ComputeBuffer(mesh.vertexCount, sizeof(float)*3);
        ComputeBuffer indexBuffer = new ComputeBuffer(mesh.triangles.Length, sizeof(uint));
        vertexBuffer.SetData(mesh.vertices); indexBuffer.SetData(mesh.triangles);
        
        int kernel = SDFConstructor.FindKernel("GetSDF");
        SDFConstructor.SetBuffer(kernel, "Vertices", vertexBuffer); //Assume one data stream
        SDFConstructor.SetBuffer(kernel, "Indices", indexBuffer);
        SDFConstructor.SetInt("numInds", (int)mesh.GetIndexCount(0)); //Assume one submesh
        SDFConstructor.SetFloats("offset", SDFoffset.x, SDFoffset.y, SDFoffset.z);

        SDFConstructor.SetBuffer(kernel, "Distance", UtilityBuffers.TransferBuffer);
        SDFConstructor.SetInts("GridSize", new int[]{(int)GridSize.x, (int)GridSize.y, (int)GridSize.z});

        uint3 threadsPerAxis;
        SDFConstructor.GetKernelThreadGroupSizes(kernel, out threadsPerAxis.x, out threadsPerAxis.y, out threadsPerAxis.z);
        threadsPerAxis = new uint3(
            (uint)Mathf.CeilToInt((float)GridSize.x / threadsPerAxis.x),
            (uint)Mathf.CeilToInt((float)GridSize.y / threadsPerAxis.y), 
            (uint)Mathf.CeilToInt((float)GridSize.z / threadsPerAxis.z)
        );
        SDFConstructor.Dispatch(kernel, (int)threadsPerAxis.x, (int)threadsPerAxis.y, (int)threadsPerAxis.z);
        vertexBuffer.Dispose();
        indexBuffer.Dispose();

        kernel = SDFConstructor.FindKernel("GetMap");
        SDFConstructor.SetBuffer(kernel, "Distance", UtilityBuffers.TransferBuffer);
        SDFConstructor.SetBuffer(kernel, "MapData", UtilityBuffers.TransferBuffer);
        SDFConstructor.SetFloat("IsoLevel", IsoLevel);

        int numPoints = (int)(GridSize.x * GridSize.y * GridSize.z);
        SDFConstructor.GetKernelThreadGroupSizes(kernel, out threadsPerAxis.x, out _, out _);
        threadsPerAxis.x = (uint)Mathf.CeilToInt((float)numPoints/ threadsPerAxis.x);
        SDFConstructor.Dispatch(kernel, (int)threadsPerAxis.x, 1, 1);

        StructureData.PointInfo[] newMap =  Structure.map.value.ToArray();
        UtilityBuffers.TransferBuffer.GetData(newMap);
        Structure.map.value = newMap.ToList();
    }

#endif
}

public class ModelManager{
    private ComputeBuffer MapBuffer;
    private ComputeBuffer GeoBuffer;
    private ComputeShader ModelConstructor;
    private ComputeShader IndexLinker;
    private ComputeShader DrawArgsConstructor;
    public Arterra.Core.Terrain.Map.Generator.GeoGenOffsets offsets;
    private uint3 GridSize;
    private float IsoLevel;

    private Transform transform;
    private Material[] ModelMaterial =
    new Material[2];
    private Arterra.Core.Terrain.Readback.GeometryHandle[] GeoHandles = 
    new Arterra.Core.Terrain.Readback.GeometryHandle[2];

    public const int VERTEX_STRIDE_WORD = 3 + 2;
    public const int TRI_STRIDE_WORD = 3;

    public ModelManager(uint3 GridSize, Transform transform, float IsoLevel, ComputeBuffer MapBuffer, ComputeBuffer GeoBuffer, int bufferStart){
        this.ModelConstructor = Resources.Load<ComputeShader>("Compute/CGeometry/Deconstructor/ModelConstructor");
        this.IndexLinker = Resources.Load<ComputeShader>("Compute/CGeometry/Deconstructor/ModelIndexLinker");
        this.DrawArgsConstructor = Resources.Load<ComputeShader>("Compute/TerrainGeneration/Readback/MeshDrawArgs");
        this.GeoBuffer = GeoBuffer;
        this.MapBuffer = MapBuffer;
        this.GridSize = GridSize;
        this.IsoLevel = IsoLevel;
        this.transform = transform;

        this.offsets = new Arterra.Core.Terrain.Map.Generator.GeoGenOffsets(new int3(GridSize), 0, bufferStart, VERTEX_STRIDE_WORD);
        PresetData();
    }

    ~ModelManager(){ Release(); }

    public void Release(){
        for(int i = 0; i < 2; i++){ GameObject.DestroyImmediate(ModelMaterial[i]); }
        ReleaseHandles();
    }

    void ReleaseHandles(){ for(int i = 0; i < 2; i++){ GeoHandles[i]?.Release(); } }

    public void Render(){ for(int i = 0; i < 2; i++) {GeoHandles[i]?.Update();} }


    public void GenerateModel(Camera camera = null){
        ConstructModel();
        SetupRenderParams(camera);
        Render(); //Render to apply immediately
    }

    void SetupRenderParams(Camera camera){ 
        /*For some Ridiculous reason, unity's Update Loop can run on different thread than OnSceneGUI, 
            so releasing at the same time will cause errors in the way it's handled as it is trying to render
            between updates
        */
    
        GeoHandles[0]?.Release();
        GeoHandles[0] = SetupGeoHandle(camera, offsets.vertStart, offsets.baseTriStart, offsets.baseTriCounter, 0);
        GeoHandles[1]?.Release();
        GeoHandles[1] = SetupGeoHandle(camera, offsets.vertStart, offsets.waterTriStart, offsets.waterTriCounter, 1);
    }

    void ConstructModel(){
        //Construct Vertices
        UtilityBuffers.ClearRange(this.GeoBuffer, 3, offsets.bufferStart);
        int kernel = ModelConstructor.FindKernel("March");

        uint3 threadsPerAxis;
        ModelConstructor.GetKernelThreadGroupSizes(kernel, out threadsPerAxis.x, out threadsPerAxis.y, out threadsPerAxis.z);
        threadsPerAxis = new uint3(
            (uint)Mathf.CeilToInt((float)GridSize.x / threadsPerAxis.x),
            (uint)Mathf.CeilToInt((float)GridSize.y / threadsPerAxis.y), 
            (uint)Mathf.CeilToInt((float)GridSize.z / threadsPerAxis.z)
        );

        ModelConstructor.Dispatch(kernel, (int)threadsPerAxis.x, (int)threadsPerAxis.y, (int)threadsPerAxis.z);
        LinkTriangles(offsets.baseTriStart, offsets.baseTriCounter);
        LinkTriangles(offsets.waterTriStart, offsets.waterTriCounter);
    }

    void LinkTriangles(int start, int counter){
        int kernel = IndexLinker.FindKernel("CSMain");
        ComputeBuffer args = UtilityBuffers.CountToArgs(IndexLinker, this.GeoBuffer, counter);
        IndexLinker.SetInt("bCOUNT_Tri", counter);
        IndexLinker.SetInt("bSTART_Tri", start);
        IndexLinker.DispatchIndirect(kernel, args);
    }

    Arterra.Core.Terrain.Readback.GeometryHandle SetupGeoHandle(Camera camera, int vertStart, int indexStart, int indexCounter, int matInd){
        uint drawArgs = UtilityBuffers.DrawArgs.Allocate();

        int kernel = DrawArgsConstructor.FindKernel("CSMain");
        DrawArgsConstructor.SetBuffer(kernel, "counter", this.GeoBuffer);
        DrawArgsConstructor.SetInt("bCOUNTER", indexCounter);
        DrawArgsConstructor.SetInt("argOffset", (int)drawArgs);
        DrawArgsConstructor.SetBuffer(kernel, "_IndirectArgsBuffer", UtilityBuffers.DrawArgs.Get());
        DrawArgsConstructor.Dispatch(kernel, 1, 1, 1);

        Vector3 size = new Vector3(GridSize.x, GridSize.y, GridSize.z);
        Bounds BoundsWS = CustomUtility.TransformBounds(transform, new Bounds(size / 2f, size));

        RenderParams rp = new RenderParams(this.ModelMaterial[matInd]){
            worldBounds = BoundsWS,
            shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
            receiveShadows = false,
            matProps = new MaterialPropertyBlock(),
            camera = camera
        };

        rp.matProps.SetBuffer("Vertices", this.GeoBuffer);
        rp.matProps.SetBuffer("Triangles", this.GeoBuffer);
        rp.matProps.SetInt("triAddress", indexStart);
        rp.matProps.SetInt("vertAddress", vertStart);
        rp.matProps.SetMatrix("_LocalToWorld", transform.localToWorldMatrix);

        return new Arterra.Core.Terrain.Readback.GeometryHandle(rp, default, 0, drawArgs, matInd);
    }

    void PresetData(){
        this.ModelMaterial[0] = new Material(Shader.Find("Unlit/ModelTerrain")); 
        this.ModelMaterial[1] = new Material(Shader.Find("Unlit/ModelLiquid")); 

        int kernel = ModelConstructor.FindKernel("March");
        ModelConstructor.SetBuffer(kernel, "MapInfo", this.MapBuffer);
        ModelConstructor.SetBuffer(kernel, "vertexes", this.GeoBuffer);
        ModelConstructor.SetBuffer(kernel, "triangles", this.GeoBuffer);
        ModelConstructor.SetBuffer(kernel, "triangleDict", this.GeoBuffer);
        ModelConstructor.SetBuffer(kernel, "counter", this.GeoBuffer);

        ModelConstructor.SetInts("counterInd", new int[]{offsets.vertexCounter, offsets.baseTriCounter, offsets.waterTriCounter});
        ModelConstructor.SetInts("GridSize", new int[]{(int)GridSize.x, (int)GridSize.y, (int)GridSize.z});

        ModelConstructor.SetInt("bSTART_dict", offsets.dictStart);
        ModelConstructor.SetInt("bSTART_verts", offsets.vertStart);    
        ModelConstructor.SetInt("bSTART_baseT", offsets.baseTriStart);
        ModelConstructor.SetInt("bSTART_waterT", offsets.waterTriStart);
        ModelConstructor.SetFloat("IsoLevel", IsoLevel);

        kernel = IndexLinker.FindKernel("CSMain");
        IndexLinker.SetBuffer(kernel, "triDict", this.GeoBuffer);
        IndexLinker.SetBuffer(kernel, "counter", this.GeoBuffer);
        IndexLinker.SetBuffer(kernel, "BaseTriangles", this.GeoBuffer);
        IndexLinker.SetInt("bSTART_Dict", offsets.dictStart);
    }
}

public class GridManager{
    public Material GridMaterial;
    public GridOffsets offsets;
    private Transform transform;
    private ComputeBuffer GeoBuffer;
    private ComputeBuffer SelectionBuffer;
    private ComputeShader GridConstructor;
    private Bounds boundsOS;
    private RenderParams renderParams;
    private uint3 GridSize;
    private uint GridPlaneCount{ get{ 
        return (uint)(GridSize.x * (GridSize.y-1) * (GridSize.z-1) +
                    (GridSize.x-1) * GridSize.y * (GridSize.z-1) +
                    (GridSize.x-1) * (GridSize.y-1) * GridSize.z);
    } }

    public GridManager(uint3 GridSize, Transform transform, ComputeBuffer GeoBuffer, int bufferStart) {
        this.GridMaterial = new Material(Shader.Find("Unlit/GridShader"));
        this.GridConstructor = Resources.Load<ComputeShader>("Compute/CGeometry/Deconstructor/GridConstructor");
        this.offsets = new GridOffsets(new int3(GridSize), bufferStart, 4, 3);
        this.SelectionBuffer = null;
        this.GeoBuffer = GeoBuffer;
        this.GridSize = GridSize;
        this.transform = transform;

        Vector3 size = new Vector3(GridSize.x, GridSize.y, GridSize.z);
        boundsOS = new Bounds(size / 2f, size);
    }

    public GridManager(uint3 GridSize, Transform transform, ComputeBuffer GeoBuffer, int selLen, int bufferStart) {
        this.GridMaterial = new Material(Shader.Find("Unlit/GridShader"));
        this.GridConstructor = Resources.Load<ComputeShader>("Compute/CGeometry/Deconstructor/GridConstructor");
        this.offsets = new GridOffsets(new int3(GridSize), bufferStart, 4, 3);
        this.SelectionBuffer = new ComputeBuffer(math.max(selLen, 1), sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.Dynamic);
        this.GeoBuffer = GeoBuffer;
        this.GridSize = GridSize;
        this.transform = transform;

        Vector3 size = new Vector3(GridSize.x, GridSize.y, GridSize.z);
        boundsOS = new Bounds(size / 2f, size);
    }

    public void GenerateModel(Camera camera = null){
        ConstructGridGeometry();
        SetupRenderParams(camera, out renderParams, transform);
        Render(); //Render to apply immediately
    }

    ~GridManager(){ Release();}
    public void Release(){ 
        GameObject.DestroyImmediate(GridMaterial); 
        this.SelectionBuffer?.Release();
    }

    public void SetSelectionData(ref SelectionArray SelectionArray){ 
        if (this.SelectionBuffer == null) return;
        this.SelectionBuffer.SetData(SelectionArray.SelectionData);
    }

    public void Render(){ Graphics.RenderPrimitives(renderParams, MeshTopology.Triangles, (int)GridPlaneCount * 4, 1); }

    void SetupRenderParams(Camera camera, out RenderParams rp, Transform transform){
        Bounds BoundsWS = CustomUtility.TransformBounds(transform, boundsOS);
        rp = new RenderParams(GridMaterial){
            worldBounds = BoundsWS,
            shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
            receiveShadows = false,
            matProps = new MaterialPropertyBlock(),
            camera = camera
        };

        rp.matProps.SetBuffer("VertexBuffer", GeoBuffer);
        rp.matProps.SetBuffer("IndexBuffer", GeoBuffer);
        rp.matProps.SetInt("bSTART_index", offsets.indexStart);
        rp.matProps.SetInt("bSTART_vertex", offsets.vertexStart);
        if (SelectionBuffer != null) {
            rp.matProps.SetBuffer("SelectionBuffer", SelectionBuffer);
            rp.matProps.SetInt("_NoSelection", 0);
        } else rp.matProps.SetInt("_NoSelection", 1);
        rp.matProps.SetInt("MapSizeX", (int)GridSize.x); rp.matProps.SetInt("MapSizeY", (int)GridSize.y); rp.matProps.SetInt("MapSizeZ", (int)GridSize.z);
        rp.matProps.SetMatrix("_LocalToWorld", transform.localToWorldMatrix);
    }

    void ConstructGridGeometry(){
        UtilityBuffers.ClearRange(GeoBuffer, 1, offsets.bufferStart);
        int kernel = GridConstructor.FindKernel("CSMain");

        GridConstructor.SetBuffer(kernel, "counter", GeoBuffer);
        GridConstructor.SetBuffer(kernel, "VertexBuffer", GeoBuffer);
        GridConstructor.SetBuffer(kernel, "IndexBuffer", GeoBuffer);
        GridConstructor.SetInt("bCOUNTER_index", offsets.indexCounter);
        GridConstructor.SetInt("bSTART_index", offsets.indexStart);
        GridConstructor.SetInt("bSTART_vertex", offsets.vertexStart);

        GridConstructor.SetInts("GridSize", new int[]{(int)GridSize.x, (int)GridSize.y, (int)GridSize.z});

        uint3 threadsPerAxis;
        GridConstructor.GetKernelThreadGroupSizes(kernel, out threadsPerAxis.x, out threadsPerAxis.y, out threadsPerAxis.z);
        threadsPerAxis = new uint3(
            (uint)Mathf.CeilToInt((float)GridSize.x / threadsPerAxis.x),
            (uint)Mathf.CeilToInt((float)GridSize.y / threadsPerAxis.y), 
            (uint)Mathf.CeilToInt((float)GridSize.z / threadsPerAxis.z)
        );

        GridConstructor.Dispatch(kernel, (int)threadsPerAxis.x, (int)threadsPerAxis.y, (int)threadsPerAxis.z);
    }

    public struct SelectionArray{
        public uint[] SelectionData;
        public SelectionArray(int size)
        {
            // Initialize the array with the required size
            SelectionData = new uint[(size + 31) / 32];
        }

        public bool this[int index]{
            get{
                if(index >= SelectionData.Length * 32) throw new ArgumentOutOfRangeException(nameof(index));
                return (SelectionData[index / 32] & (1 << (index % 32))) != 0;
            }
            set{
                if(index >= SelectionData.Length * 32) throw new ArgumentOutOfRangeException(nameof(index));
                if(value) SelectionData[index / 32] |= (uint)(1 << (index % 32));
                else SelectionData[index / 32] &= ~(uint)(1 << (index % 32));
            }
        }
    }

    public struct GridOffsets : BufferOffsets{
        public int indexCounter;
        public int indexStart;
        public int vertexStart;
        private int offsetStart; private int offsetEnd;
        public int bufferStart{get{return offsetStart;}} public int bufferEnd{get{return offsetEnd;}}
        public GridOffsets(int3 GridSize, int bufferStart, int indexStride, int vertexStride){
            int numPoints = GridSize.x * GridSize.y * GridSize.z;
            int GridPlaneCount = (GridSize.x * (GridSize.y-1) * (GridSize.z-1) +
                    (GridSize.x-1) * GridSize.y * (GridSize.z-1) +
                    (GridSize.x-1) * (GridSize.y-1) * GridSize.z);

            this.offsetStart = bufferStart;
            this.indexCounter = bufferStart;

            this.indexStart = Mathf.CeilToInt((float)(indexCounter+1) / indexStride);
            int indexEnd = indexStart * indexStride + GridPlaneCount * indexStride;

            this.vertexStart = Mathf.CeilToInt((float)indexEnd / vertexStride);
            int vertexEnd = vertexStart * vertexStride + numPoints * vertexStride;

            this.offsetEnd = vertexEnd;
        }
    }
}
