using UnityEngine;
using System;
using System.Threading.Tasks;
using UnityEngine.UI;
using TMPro;
using static SegmentedUIEditor;
using Arterra.Config;
using Unity.Mathematics;
using Arterra.Core.Terrain;
using Arterra.Core.Storage;
using System.Collections;
using static Arterra.Core.Storage.World;

namespace Arterra.Config.Intrinsic {
    /// <summary> Settings controlling how the world appears in 
    /// the menu when selecting and viewing the world 
    /// before entering </summary>
    [Serializable]
    public struct WorldApperance {
        /// <summary> The coordinate in Chunk Space of the chunk that will be
        /// used to generate the chunk display. If this chunk is not saved,
        /// any arbitrary saved chunk will be selected to be displayed. </summary>
        public int3 DisplayedChunk;
        /// <summary> The speed at which the camera rotates around this display chunk. </summary>
        public float3 RotateSpeed;
        /// <summary>
        /// The amount of 
        /// </summary>
        public float GridScale;
        public float GridThickness;
    }
}

public class OptionsHandler : MonoBehaviour
{
    private static Animator sAnimator;
    private static GameObject infoContent;
    private static TMP_InputField WorldName; 
    private static SingleChunkDisplay spinChunk;
    private static bool active = false;
    private void OnEnable() { 
        SystemProtocol.Reset();
        sAnimator = this.gameObject.GetComponent<Animator>();
        infoContent = transform.GetChild(0).Find("Settings").GetComponent<ScrollRect>().content.gameObject;
        WorldName = transform.GetChild(0).Find("WorldName").GetComponent<TMP_InputField>();
        spinChunk = new SingleChunkDisplay();
        active = false;
    }    

    public static void Activate(Action callback = null){ 
        if(active) return;
        active = true;
        
        sAnimator.SetTrigger("Unmask");
        new AnimatorAwaitTask(sAnimator, "MaskRockBreak", InitializeDisplay).Invoke();
        new AnimatorAwaitTask(sAnimator,  "UnmaskedAnimation", () => {
            sAnimator.ResetTrigger("Unmask");
            callback?.Invoke();
        }).Invoke(); 
    }
    public static void Deactivate(Action callback = null){ 
        if(!active) return;
        active = false;
        
        _ = SaveOptions();
        sAnimator.SetTrigger("Mask");
        new AnimatorAwaitTask(sAnimator, "MaskedAnimation", () => {
            sAnimator.ResetTrigger("Mask");
            ReleaseDisplay(infoContent); 
            callback?.Invoke();
        }).Invoke();
    }

    public static void TogglePanel(){
        if(active) Deactivate();
        else Activate();
    }


    public static void EditName(){
        WORLD_SELECTION.First.Value.Name = WorldName.text; 
        Task.Run(() => SaveMeta());
    }

    public static void Delete(){
        if(!active) return;
        SelectionHandler.DeleteSelected(); 
        //Don't call deactivate because it will save the options
        //Which Delete already does
        sAnimator.SetTrigger("Mask");
        new AnimatorAwaitTask(sAnimator, "MaskedAnimation", () => {
            ReleaseDisplay(infoContent);
            MenuHandler.Activate();
        }).Invoke();
        active = false;
    }

    public static void InitializeDisplay(){
        WorldMeta cWorld = WORLD_SELECTION.First.Value;
        WorldName.text = cWorld.Name;
        WorldName.onEndEdit.AddListener((string value) => {
            cWorld.Name = value;
            WorldName.text = value;
        });

        spinChunk.InitializeDisplay();
        ReleaseDisplay(infoContent);
        CreateOptionDisplay(Config.CURRENT, infoContent, (ChildUpdate cb) => { object wo = Config.CURRENT; cb.Invoke(ref wo);});
        infoContent.GetComponent<VerticalLayoutGroup>().padding.left = 0;
    }

    public void LateUpdate() => spinChunk.Render(this);
    
    private class SingleChunkDisplay {
        private string shownWorld;
        private GameObject ChunkDisplay;
        private GameObject CameraController;
        private ModelManager ChunkModel;
        private GridManager ChunkGrid;
        private bool active = false;
        private bool updatedIcon = false;
        public SingleChunkDisplay() {
            ChunkDisplay = GameObject.Find("ChunkDisplay");
            CameraController = ChunkDisplay.transform.Find("Controller").gameObject;
            shownWorld = null;
            updatedIcon = false;
            active = false;
        }

        public void InitializeDisplay() {
            if (WORLD_SELECTION.First.Value.Id == shownWorld) return;
            shownWorld = WORLD_SELECTION.First.Value.Id;
            LoadWorldChunkDisplay();
        }

        private void Release() {
            if (!active) return;
            active = false;
            updatedIcon = false;

            SystemProtocol.Shutdown(); 
            CameraController.transform.localRotation = Quaternion.identity;
            Transform grid = ChunkDisplay.transform.Find("Grid");
            grid.localPosition = Vector3.zero;
            grid.localScale = Vector3.one;

            Transform model = ChunkDisplay.transform.Find("Model");
            model.localPosition = Vector3.one;
            
            ChunkModel?.Release();
            ChunkGrid?.Release();
        }

        public void Render(MonoBehaviour self) {
            if (!active) return;
            float3 rotSpeed = Config.CURRENT.System.WorldApperance.value.RotateSpeed;
            CameraController.transform.Rotate(rotSpeed * Time.deltaTime);
            ChunkModel?.Render();
            ChunkGrid?.Render();

            if (updatedIcon) return;
            updatedIcon = true;
            self.StartCoroutine(CaptureAtEndOfFrame());
        }

        private IEnumerator CaptureAtEndOfFrame()
        {
            yield return new WaitForEndOfFrame();

            var cam = CameraController.GetComponentInChildren<Camera>();
            string savePath = WORLD_SELECTION.First.Value.Path + DisplayChunkPath;

            Utils.SaveTextureToFileUtility.SaveRenderTextureToFile(
                cam.targetTexture,
                savePath
            );
        }


        private void LoadWorldChunkDisplay() {
            Release();
            SystemProtocol.MinimalStartup(); 
            active = true;

            Arterra.Config.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain;
            Arterra.Config.Intrinsic.WorldApperance wSettings = Config.CURRENT.System.WorldApperance;
            uint chunkSize = (uint)rSettings.mapChunkSize + 2;

            uint3 gridSize = (uint3)Mathf.CeilToInt(chunkSize * wSettings.GridScale);
            Transform grid = ChunkDisplay.transform.Find("Grid");
            grid.position -= (Vector3)(float3)chunkSize/2.0f;
            grid.localScale = chunkSize / (float3)(gridSize-1);

            ChunkGrid = new GridManager(gridSize, grid.transform, UtilityBuffers.GenerationBuffer,  0);
            ChunkGrid.GridMaterial.SetFloat("_WireframeWidth", ChunkGrid.GridMaterial.GetFloat("_WireframeWidth") * wSettings.GridThickness);
            ChunkGrid.GridMaterial.SetFloat("_VertexSize", ChunkGrid.GridMaterial.GetFloat("_VertexSize") *  wSettings.GridThickness);
            ChunkGrid.GenerateModel();

            if (!GetDisplayChunkFromDisk(out MapData[] chunk)) return;
            chunk = Utils.CustomUtility.RescaleLinearMap(chunk, rSettings.mapChunkSize, 2, 1);

            Transform model = ChunkDisplay.transform.Find("Model");
            model.position -= (Vector3)(float3)chunkSize/2.0f;
            ChunkModel = new ModelManager(
                chunkSize, model,
                rSettings.IsoLevel, UtilityBuffers.TransferBuffer,
                UtilityBuffers.GenerationBuffer, ChunkGrid.offsets.bufferEnd
            );
            
            UtilityBuffers.TransferBuffer.SetData(chunk);
            ChunkModel.GenerateModel();
        }

        private bool GetDisplayChunkFromDisk(out MapData[] chunk) {
            int3 CCoord = Config.CURRENT.System.WorldApperance.value.DisplayedChunk;
            Chunk.ReadbackInfo info = Chunk.ReadChunkMap(CCoord, 0);
            chunk = info.map;

            if (chunk != null) return true;
            if (!Chunk.TryFindSavedMapChunk(out CCoord)) 
                return false;
            //Update the display chunk coord saved.
            Config.CURRENT.System.WorldApperance.value.DisplayedChunk = CCoord;
            Config.CURRENT.System.WorldApperance.IsDirty = true;
            info = Chunk.ReadChunkMap(CCoord, 0);
            chunk = info.map;
            return chunk != null;
        }
    }

}
