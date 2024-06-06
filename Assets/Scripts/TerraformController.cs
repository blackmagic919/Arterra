using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using System.Linq;

public class TerraformController : MonoBehaviour
{
    public float terraformRadius = 5;
    public LayerMask objectLayer;

    public float terraformSpeedNear = 0.1f;
    public float terraformSpeedFar = 0.25f;
    public float maxTerraformDistance = 60;
    public float minInvMatThresh = 1f;
    const int materialCapacity = 51000;

    int IsoLevel;
    float weight;
    public bool useSolid = true;


    MaterialBarController barController;
    Transform cam;
    Vector3 hitPoint;


    [HideInInspector]
    public MaterialInventory MainInventory = new MaterialInventory(materialCapacity);
    [HideInInspector]
    public MaterialInventory AuxInventory = new MaterialInventory(materialCapacity);

    // Start is called before the first frame update
    void Start()
    {
        cam = Camera.main.transform;
        barController = FindAnyObjectByType<MaterialBarController>();
        EndlessTerrain terrain = FindAnyObjectByType<EndlessTerrain>();
        IsoLevel = Mathf.RoundToInt(terrain.settings.IsoLevel * 255);
        useSolid = true;
    }

    bool RayTestSolid(CPUDensityManager.MapData pointInfo){
        return (pointInfo.density * pointInfo.viscosity / 255.0f) >= IsoLevel;
    }

    bool RayTestLiquid(CPUDensityManager.MapData pointInfo){
        return (pointInfo.density * (1 - pointInfo.viscosity / 255.0f)) >= IsoLevel || (pointInfo.density * pointInfo.viscosity / 255.0f) >= IsoLevel;
    }

    void Update()
    {
        if(CPUDensityManager.RayCastTerrain(cam.position, cam.forward, maxTerraformDistance, useSolid ? RayTestSolid : RayTestLiquid, out hitPoint))
            Terraform(hitPoint);

        HandleMaterialSwitch();
    }

    void HandleMaterialSwitch()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
            MainInventory.PreviousMaterial();
        if (Input.GetKeyDown(KeyCode.Alpha2))
            MainInventory.NextMaterial();
        if(Input.GetKeyDown(KeyCode.Alpha3)){
            useSolid = !useSolid;

            MaterialInventory temp = MainInventory;
            MainInventory = AuxInventory;
            AuxInventory = temp;
        }
    }

    void Terraform(Vector3 terraformPoint)
    {
        float dstFromCam = (terraformPoint - cam.position).magnitude;
        float weight01 = Mathf.InverseLerp(0, maxTerraformDistance, dstFromCam);
        weight = Mathf.Lerp(terraformSpeedNear, terraformSpeedFar, weight01);

        if (Input.GetMouseButton(1)) //Don't add if there is an object in the way
        {
            if(useSolid){
                if(!Physics.CheckSphere(terraformPoint, terraformRadius, objectLayer)) CPUDensityManager.Terraform(terraformPoint, terraformRadius, HandleAddSolid);
            }else CPUDensityManager.Terraform(terraformPoint, terraformRadius, HandleAddLiquid);
        }
        // Subtract terrain
        else if (Input.GetMouseButton(0))
        {
            if(useSolid) CPUDensityManager.Terraform(terraformPoint, terraformRadius, HandleRemoveSolid);
            else CPUDensityManager.Terraform(terraformPoint, terraformRadius, HandleRemoveLiquid);
        }
        else if(!MainInventory.cleared) MainInventory.ClearSmallMaterials(minInvMatThresh);

        barController.OnInventoryChanged();
    }

    int GetStaggeredDelta(int baseDensity, float deltaDensity){
        deltaDensity = Mathf.Abs(Mathf.Clamp(baseDensity + deltaDensity, 0, 255) - baseDensity);
        int staggeredDelta = Mathf.FloorToInt(deltaDensity) + (Time.frameCount % Mathf.CeilToInt(1 / (deltaDensity % 1)) == 0 ? 1 : 0);

        return staggeredDelta;
    }

    CPUDensityManager.MapData HandleAddSolid(CPUDensityManager.MapData pointInfo, float brushStrength){
        if(weight * brushStrength == 0) return pointInfo;

        int selected = MainInventory.selected;
        int solidDensity = Mathf.RoundToInt(pointInfo.density * (pointInfo.viscosity / 255.0f));
        if(solidDensity < IsoLevel || pointInfo.material == selected){
            //If adding solid density, override water
            int deltaDensity = GetStaggeredDelta(solidDensity, brushStrength * weight);
            deltaDensity = MainInventory.RemoveMaterialFromInventory(deltaDensity);

            solidDensity += deltaDensity;
            pointInfo.density = Mathf.Min(pointInfo.density + deltaDensity, 255);
            if(solidDensity >= IsoLevel) pointInfo.material = selected;

            pointInfo.viscosity = Mathf.RoundToInt(((float)solidDensity) / pointInfo.density) * 255;
        }
        return pointInfo;
    }

    CPUDensityManager.MapData HandleAddLiquid(CPUDensityManager.MapData pointInfo, float brushStrength){
        if(weight * brushStrength == 0) return pointInfo;

        int selected = MainInventory.selected;
        int liquidDensity = Mathf.RoundToInt(pointInfo.density * (1 - (pointInfo.viscosity / 255.0f)));
        if(pointInfo.density <= IsoLevel || pointInfo.material == selected){
            //If adding liquid density, only change if not solid
            int deltaDensity = GetStaggeredDelta(pointInfo.density, brushStrength * weight);
            deltaDensity = MainInventory.RemoveMaterialFromInventory(deltaDensity);

            pointInfo.density += deltaDensity;
            liquidDensity += deltaDensity;
            if(pointInfo.density >= IsoLevel) pointInfo.material = selected;

            pointInfo.viscosity = Mathf.RoundToInt(1 - (((float)liquidDensity) / pointInfo.density) * 255);
        }
        return pointInfo;
    }



    CPUDensityManager.MapData HandleRemoveSolid(CPUDensityManager.MapData pointInfo, float brushStrength){
        if(weight * brushStrength == 0) return pointInfo;

        int solidDensity = Mathf.RoundToInt(pointInfo.density * (pointInfo.viscosity / 255.0f));
        if(useSolid && (solidDensity >= IsoLevel)){
            int deltaDensity = GetStaggeredDelta(solidDensity, -brushStrength * weight);
            deltaDensity = MainInventory.AddMaterialToInventory(pointInfo.material, deltaDensity);

            pointInfo.density -= deltaDensity;
            solidDensity -= deltaDensity;
            
            if(pointInfo.density != 0) pointInfo.viscosity = Mathf.RoundToInt(((float)solidDensity) / pointInfo.density * 255);
        }
        return pointInfo;
    }

    CPUDensityManager.MapData HandleRemoveLiquid(CPUDensityManager.MapData pointInfo, float brushStrength){
        if(weight * brushStrength == 0) return pointInfo;

        int liquidDensity = Mathf.RoundToInt(pointInfo.density * (1 - (pointInfo.viscosity / 255.0f)));
        if (!useSolid && (liquidDensity > IsoLevel)){
            int deltaDensity = GetStaggeredDelta(liquidDensity, -brushStrength * weight);
            deltaDensity = MainInventory.AddMaterialToInventory(pointInfo.material, deltaDensity);

            pointInfo.density -= deltaDensity;
            liquidDensity -= deltaDensity;
            
            if(pointInfo.density != 0) pointInfo.viscosity = Mathf.RoundToInt((1 - (((float)liquidDensity) / pointInfo.density)) * 255);
        }
        return pointInfo;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        /*
        TerrainChunk[] chunks = new TerrainChunk[EndlessTerrain.terrainChunkDict.Values.Count];
        EndlessTerrain.terrainChunkDict.Values.CopyTo(chunks, 0);
        foreach(TerrainChunk chunk in chunks){
            if(chunk == null) continue;
            Gizmos.DrawWireCube(chunk.meshObject.transform.position, 2.5f * 64 * Vector3.one);
        }*/
        
        Gizmos.DrawSphere(hitPoint, 0.25f);
    }
}

//This logic is beyond me XD, spent a while thinking of how to implement this
//Slightly more efficient than ElementAt
public class MaterialInventory{
    public Dictionary<int, int> inventory = new Dictionary<int, int>();
    public int totalMaterialAmount = 0;
    public int materialCapacity = 0;

    private int selectedPos = 0;
    public int selected = -1;
    public bool cleared = true;

    public MaterialInventory(int capacity){
        materialCapacity = capacity;
    }

    public int[] GetInventoryKeys
    {
        get{
            int[] keys = new int[inventory.Count];
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
        selected = this.inventory.ElementAt(selectedPos).Key;
    }

    public void PreviousMaterial(){
        selectedPos--;
        if(selectedPos < 0) selectedPos = inventory.Count - 1;
        if(selectedPos >= 0) selected = this.inventory.ElementAt(selectedPos).Key;
        else selected = -1;
    }

    public void ClearSmallMaterials(float threshold){
        int[] keys = GetInventoryKeys;
        int[] values = GetInventoryValues;
        for(int i = 0; i < keys.Length; i++){
            if(values[i] < threshold){
                totalMaterialAmount -= values[i];
                inventory.Remove(keys[i]);
                if(selected == keys[i]) PreviousMaterial();
            }
        }
        cleared = true;
    }

    public int AddMaterialToInventory(int materialIndex, int delta)
    {
        delta = Mathf.Min(totalMaterialAmount + delta, materialCapacity) - totalMaterialAmount;
        cleared = false;

        if (inventory.ContainsKey(materialIndex))
            inventory[materialIndex] += delta;
        else{
            inventory.Add(materialIndex, delta);
            if(selected == -1) selected = materialIndex;
        }

        totalMaterialAmount += delta;
        return delta;
    }

    public int RemoveMaterialFromInventory(int delta)
    {
        delta = totalMaterialAmount - Mathf.Max(totalMaterialAmount - delta, 0);
        cleared = false;

        if (inventory.ContainsKey(selected)) {
            int amount = inventory[selected];
            delta = amount - Mathf.Max(amount - delta, 0);
            inventory[selected] -= delta;

            if (inventory[selected] == 0){
                inventory.Remove(selected);
                PreviousMaterial();
            }
        }
        else
            delta = 0;

        totalMaterialAmount -= delta;
        return delta;
    }
}