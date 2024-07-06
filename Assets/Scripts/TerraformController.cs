using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using System.Linq;


[System.Serializable]
public class TerraformSettings{
    public float terraformRadius = 5;
    public float terraformSpeed = 4;
    public float maxTerraformDistance = 60;
    public int materialCapacity = 51000;
    public float minInvMatThresh = 1f;
}
[System.Serializable]
public class TerraformController
{
    private TerraformSettings settings;
    public LayerMask objectLayer;
    private bool shiftPressed = false;

    int IsoLevel;


    MaterialBarController barController;
    Transform cam;
    Vector3 hitPoint;


    [HideInInspector]
    public MaterialInventory MainInventory;

    // Start is called before the first frame update
    public void Activate()
    {
        cam = Camera.main.transform;
        barController = UnityEngine.Object.FindAnyObjectByType<MaterialBarController>();
        IsoLevel = Mathf.RoundToInt(WorldStorageHandler.WORLD_OPTIONS.Rendering.value.IsoLevel * 255);
        barController.OnInventoryChanged(this);
        settings = WorldStorageHandler.WORLD_OPTIONS.Terraforming.value;
    }

    public void Update()
    {
        if(Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift)) shiftPressed = true;
        if(Input.GetKeyUp(KeyCode.LeftShift) || Input.GetKeyUp(KeyCode.RightShift)) shiftPressed = false;
        
        Terraform();
        HandleMaterialSwitch();
    }

    void HandleMaterialSwitch()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
            MainInventory.PreviousMaterial();
        else if (Input.GetKeyDown(KeyCode.Alpha2))
            MainInventory.NextMaterial();
        else return;
        barController.OnInventoryChanged(this);
    }

    bool RayTestSolid(CPUDensityManager.MapData pointInfo){ return (pointInfo.density * pointInfo.viscosity / 255.0f) >= IsoLevel; }
    bool RayTestLiquid(CPUDensityManager.MapData pointInfo){ return (pointInfo.density * (1 - pointInfo.viscosity / 255.0f)) >= IsoLevel || (pointInfo.density * pointInfo.viscosity / 255.0f) >= IsoLevel;}

    void Terraform()
    {
        if(Input.GetMouseButtonUp(1) || Input.GetMouseButtonUp(0)) MainInventory.ClearSmallMaterials(settings.minInvMatThresh);
        else if (Input.GetMouseButton(1)) //Don't add if there is an object in the way
        {
            if(MainInventory.selected.isSolid){
                if(CPUDensityManager.RayCastTerrain(cam.position, cam.forward, settings.maxTerraformDistance, RayTestSolid, out hitPoint))
                    if(!Physics.CheckSphere(hitPoint, settings.terraformRadius, objectLayer)) CPUDensityManager.Terraform(hitPoint, settings.terraformRadius, HandleAddSolid);
            } else{ 
                if(CPUDensityManager.RayCastTerrain(cam.position, cam.forward, settings.maxTerraformDistance, RayTestLiquid, out hitPoint))
                    CPUDensityManager.Terraform(hitPoint, settings.terraformRadius, HandleAddLiquid);
            }
        }
        // Subtract terrain
        else if (Input.GetMouseButton(0))
        {
            if(shiftPressed) {
                if(CPUDensityManager.RayCastTerrain(cam.position, cam.forward, settings.maxTerraformDistance, RayTestLiquid, out hitPoint))
                    CPUDensityManager.Terraform(hitPoint, settings.terraformRadius, HandleRemoveLiquid);
            }else {
                if(CPUDensityManager.RayCastTerrain(cam.position, cam.forward, settings.maxTerraformDistance, RayTestSolid, out hitPoint))
                    CPUDensityManager.Terraform(hitPoint, settings.terraformRadius, HandleRemoveSolid);
            }
        }
        else return; 
        barController.OnInventoryChanged(this);
    }

    int GetStaggeredDelta(int baseDensity, float deltaDensity){
        int staggeredDelta = Mathf.FloorToInt(deltaDensity);
        staggeredDelta += (deltaDensity % 1) == 0 ? 0 : (Time.frameCount % Mathf.CeilToInt(1 / (deltaDensity % 1))) == 0 ? 1 : 0;
        staggeredDelta = Mathf.Abs(Mathf.Clamp(baseDensity + staggeredDelta, 0, 255) - baseDensity);

        return staggeredDelta;
    }

    CPUDensityManager.MapData HandleAddSolid(CPUDensityManager.MapData pointInfo, float brushStrength){
        brushStrength *= settings.terraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;

        int selected = MainInventory.selected.material;
        int solidDensity = Mathf.RoundToInt(pointInfo.density * (pointInfo.viscosity / 255.0f));
        if(solidDensity < IsoLevel || pointInfo.material == selected){
            //If adding solid density, override water
            int deltaDensity = GetStaggeredDelta(solidDensity, brushStrength);
            deltaDensity = MainInventory.RemoveMaterialFromInventory(deltaDensity);

            solidDensity += deltaDensity;
            pointInfo.density = Mathf.Min(pointInfo.density + deltaDensity, 255);
            if(solidDensity >= IsoLevel) pointInfo.material = selected;

            pointInfo.viscosity = Mathf.RoundToInt(((float)solidDensity) / pointInfo.density * 255);
        }
        return pointInfo;
    }

    CPUDensityManager.MapData HandleAddLiquid(CPUDensityManager.MapData pointInfo, float brushStrength){
        brushStrength *= settings.terraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;

        int selected = MainInventory.selected.material;
        int liquidDensity = Mathf.RoundToInt(pointInfo.density * (1 - (pointInfo.viscosity / 255.0f)));
        if(liquidDensity < IsoLevel || pointInfo.material == selected){
            //If adding liquid density, only change if not solid
            int deltaDensity = GetStaggeredDelta(pointInfo.density, brushStrength);
            deltaDensity = MainInventory.RemoveMaterialFromInventory(deltaDensity);

            if(pointInfo.density + deltaDensity > 255) Debug.Log(pointInfo.density + " " + deltaDensity);
            pointInfo.density += deltaDensity;
            liquidDensity += deltaDensity;
            if(liquidDensity >= IsoLevel) pointInfo.material = selected;

            pointInfo.viscosity = Mathf.RoundToInt((1 - (((float)liquidDensity) / pointInfo.density)) * 255);
        }
        return pointInfo;
    }



    CPUDensityManager.MapData HandleRemoveSolid(CPUDensityManager.MapData pointInfo, float brushStrength){
        brushStrength *= settings.terraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;

        int solidDensity = Mathf.RoundToInt(pointInfo.density * (pointInfo.viscosity / 255.0f));
        if(solidDensity >= IsoLevel){
            int deltaDensity = GetStaggeredDelta(solidDensity, -brushStrength);
            deltaDensity = MainInventory.AddMaterialToInventory(new MaterialInventory.InvMat{material = pointInfo.material, isSolid = true}, deltaDensity);

            pointInfo.density -= deltaDensity;
            solidDensity -= deltaDensity;
            
            if(pointInfo.density != 0) pointInfo.viscosity = Mathf.RoundToInt(((float)solidDensity) / pointInfo.density * 255);
        }
        return pointInfo;
    }

    CPUDensityManager.MapData HandleRemoveLiquid(CPUDensityManager.MapData pointInfo, float brushStrength){
        brushStrength *= settings.terraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;

        int liquidDensity = Mathf.RoundToInt(pointInfo.density * (1 - (pointInfo.viscosity / 255.0f)));
        if (liquidDensity > IsoLevel){
            int deltaDensity = GetStaggeredDelta(liquidDensity, -brushStrength);
            deltaDensity = MainInventory.AddMaterialToInventory(new MaterialInventory.InvMat{material = pointInfo.material, isSolid = false}, deltaDensity);

            pointInfo.density -= deltaDensity;
            liquidDensity -= deltaDensity;
            
            if(pointInfo.density != 0) pointInfo.viscosity = Mathf.RoundToInt((1 - (((float)liquidDensity) / pointInfo.density)) * 255);
        }
        return pointInfo;
    }

    public void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(hitPoint, 0.25f);
    }
}

//This logic is beyond me XD, spent a while thinking of how to implement this
//Slightly more efficient than ElementAt

public struct MaterialInventory{
    public Dictionary<uint, int> inventory;
    public int totalMaterialAmount;
    public int materialCapacity;
    public int selectedPos;
    public InvMat selected;

    public MaterialInventory(int capacity){
        inventory = new Dictionary<uint, int>();
        materialCapacity = capacity;
        totalMaterialAmount = 0;
        selectedPos = 0;
        selected = new InvMat{isNull = true};
    }

    public uint[] GetInventoryKeys
    {
        get{
            uint[] keys = new uint[inventory.Count];
            inventory.Keys.CopyTo(keys, 0);
            return keys;
        }
    }

    public int[] GetInventoryValues
    {
        get
        {
            int[] values = new int[inventory.Count];
            inventory.Values.CopyTo(values, 0);
            return values;
        }
    }


    public void NextMaterial(){
        selectedPos++;
        selectedPos %= inventory.Count;
        selected = new InvMat{key = this.inventory.ElementAt(selectedPos).Key};
    }

    public void PreviousMaterial(){
        selectedPos--;
        if(selectedPos < 0) selectedPos = inventory.Count - 1;
        if(selectedPos >= 0) selected = new InvMat{key = this.inventory.ElementAt(selectedPos).Key};
        else selected.isNull = true;
    }

    public void ClearSmallMaterials(float threshold){
        uint[] keys = GetInventoryKeys;
        int[] values = GetInventoryValues;
        for(int i = 0; i < keys.Length; i++){
            if(values[i] < threshold){
                totalMaterialAmount -= values[i];
                inventory.Remove(keys[i]);
                if(selected.key == keys[i]) PreviousMaterial();
            }
        }
    }

    public int AddMaterialToInventory(InvMat materialIndex, int delta)
    {
        delta = Mathf.Min(totalMaterialAmount + delta, materialCapacity) - totalMaterialAmount;
        uint key = materialIndex.key;

        if (inventory.ContainsKey(key))
            inventory[key] += delta;
        else{
            inventory.Add(key, delta);
            if(selected.isNull) selected = materialIndex;
        }

        totalMaterialAmount += delta;
        return delta;
    }

    public int RemoveMaterialFromInventory(int delta)
    {
        delta = totalMaterialAmount - Mathf.Max(totalMaterialAmount - delta, 0);
        uint key = selected.key;

        if (inventory.ContainsKey(key)) {
            int amount = inventory[key];
            delta = amount - Mathf.Max(amount - delta, 0);
            inventory[key] -= delta;

            if (inventory[key] == 0){
                inventory.Remove(key);
                PreviousMaterial();
            }
        }
        else
            delta = 0;

        totalMaterialAmount -= delta;
        return delta;
    }

    public struct InvMat{
        public uint key;
        public int material {
            get => (int)(key & 0x7FFFFFFF);
            set => key = (uint)(value & 0x7FFFFFFF) | 0x80000000;
        }
        public bool isSolid {
            readonly get => (key & 0x80000000) != 0;
            set => key = value ? key | 0x80000000 : key & 0x7FFFFFFF;
        }

        public bool isNull{
            readonly get => key == 0xFFFFFFFF;
            set => key = value ? 0xFFFFFFFF : key;
        }
    }
}