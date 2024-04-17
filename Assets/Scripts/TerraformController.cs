using System;
using System.Collections.Generic;
using UnityEngine;

public class TerraformController : MonoBehaviour
{

    public float terraformRadius = 5;
    public LayerMask terrainMask;

    public float terraformSpeedNear = 0.1f;
    public float terraformSpeedFar = 0.25f;
    public float maxTerraformDistance = 60;
    public float materialCapacity = 20;

    float IsoLevel;
    float weight;
    bool isAdding;
    public int selected = -1;
    private int selectPosition = 0;
    private bool useSolid = true;


    MaterialBarController barController;
    EndlessTerrain terrain;
    Transform cam;
    int numIterations = 5;
    bool hasHit;
    Vector3 hitPoint;


    [HideInInspector]
    public Dictionary<int, float> materialInventory = new Dictionary<int, float>();

    public int[] getInventoryKeys
    {
        get{
            int[] keys = new int[materialInventory.Count];
            materialInventory.Keys.CopyTo(keys, 0);
            return keys;
        }
    }

    public float[] getInventoryValues
    {
        get
        {
            float[] values = new float[materialInventory.Count];
            materialInventory.Values.CopyTo(values, 0);
            return values;
        }
    }

    [HideInInspector]
    public float totalMaterialAmount = 0;

    // Start is called before the first frame update
    void Start()
    {
        cam = Camera.main.transform;
        terrain = FindAnyObjectByType<EndlessTerrain>();
        barController = FindAnyObjectByType<MaterialBarController>();
        IsoLevel = terrain.settings.IsoLevel;
        useSolid = true;
    }

    // Update is called once per frame
    void Update()
    {
        RaycastHit hit;

        hasHit = false;

        for (int i = 0; i < numIterations; i++)
        {
            float rayRadius = terraformRadius * Mathf.Lerp(0.01f, 1, i / (numIterations - 1f));
            if (Physics.SphereCast(cam.position, rayRadius, cam.forward, out hit, maxTerraformDistance, terrainMask))
            {
                Terraform(hit.point);
                break;
            }
        }
        HandleMaterialSwitch();
    }

    void HandleMaterialSwitch()
    {
        int inventorySize = materialInventory.Count;
        if (inventorySize == 0)
            return;

        if (Input.GetKeyDown(KeyCode.Alpha1))
            selectPosition--;
        if (Input.GetKeyDown(KeyCode.Alpha2))
            selectPosition++;
        if(Input.GetKeyDown(KeyCode.Alpha3))
            useSolid = !useSolid;

        if (selectPosition < 0)
            selectPosition = inventorySize + selectPosition;
        selectPosition %= inventorySize;

        selected = getInventoryKeys[(int)MathF.Min(selectPosition, inventorySize- 1)];
    }

    void Terraform(Vector3 terraformPoint)
    {
        hasHit = true;
        hitPoint = terraformPoint; //For visualization

        float dstFromCam = (terraformPoint - cam.position).magnitude;
        float weight01 = Mathf.InverseLerp(0, maxTerraformDistance, dstFromCam);
        weight = Mathf.Lerp(terraformSpeedNear, terraformSpeedFar, weight01);

        if (Input.GetMouseButton(1))
        {
            isAdding = true;
            terrain.Terraform(terraformPoint, terraformRadius, HandleTerraform);
        }
        // Subtract terrain
        else if (Input.GetMouseButton(0))
        {
            isAdding = false;
            terrain.Terraform(terraformPoint, terraformRadius, HandleTerraform);
        }

        barController.OnInventoryChanged();
    }

    float AddMaterialToInventory(int materialIndex, float delta)
    {
        delta = Mathf.Min(totalMaterialAmount + delta, materialCapacity) - totalMaterialAmount;

        if (materialInventory.ContainsKey(materialIndex))
            materialInventory[materialIndex] += delta;
        else
            materialInventory.Add(materialIndex, delta);

        totalMaterialAmount += delta;
        return delta;
    }

    float RemoveMaterialFromInventory(int materialIndex, float delta)
    {
        delta = totalMaterialAmount - Mathf.Max(totalMaterialAmount - delta, 0);

        if (materialInventory.ContainsKey(materialIndex)) {
            float amount = materialInventory[materialIndex];
            delta = amount - Mathf.Max(amount - delta, 0);
            materialInventory[materialIndex] -= delta;

            if (materialInventory[materialIndex] == 0)
                materialInventory.Remove(materialIndex);
        }
        else
            delta = 0;

        totalMaterialAmount -= delta;
        return delta;
    }

    void HandleAdd(ref TerrainChunk.MapData pointInfo, float brushStrength){
        float solidDensity = pointInfo.density * pointInfo.viscosity;
        float liquidDensity = pointInfo.density * (1 - pointInfo.viscosity);
        if(useSolid && (solidDensity < IsoLevel || pointInfo.material == selected)){
            //If adding solid density, remove all water there first
            float deltaDensity = RemoveMaterialFromInventory(selected, Mathf.Min(solidDensity + brushStrength * weight, 1) - solidDensity);

            solidDensity += deltaDensity;
            pointInfo.density = Mathf.Min(pointInfo.density + deltaDensity, 1);
            if(solidDensity >= IsoLevel){ pointInfo.material = selected;}

            pointInfo.viscosity = solidDensity / pointInfo.density;
        }
        else if(!useSolid && (pointInfo.density < IsoLevel || pointInfo.material == selected)){
            //If adding liquid density, only change if not solid
            float deltaDensity = RemoveMaterialFromInventory(selected, Mathf.Min(pointInfo.density + brushStrength * weight, 1) - pointInfo.density);

            pointInfo.density += deltaDensity;
            liquidDensity += deltaDensity;
            pointInfo.material = selected;

            pointInfo.viscosity = 1 - (liquidDensity / pointInfo.density);
        }
        //This is the most expected behavior I can reproduce...
    }

    void HandleRemove(ref TerrainChunk.MapData pointInfo, float brushStrength){
        float solidDensity = pointInfo.density * pointInfo.viscosity;
        float liquidDensity = pointInfo.density * (1 - pointInfo.viscosity);
        if(useSolid && (solidDensity > IsoLevel)){
            float deltaDensity = AddMaterialToInventory(pointInfo.material, solidDensity - Mathf.Max(solidDensity - brushStrength * weight, 0));

            pointInfo.density -= deltaDensity;
            solidDensity -= deltaDensity;
            
            if(pointInfo.density != 0) pointInfo.viscosity = solidDensity / pointInfo.density;
        } else if (!useSolid && (liquidDensity > IsoLevel)){
            float deltaDensity = AddMaterialToInventory(pointInfo.material, liquidDensity - Mathf.Max(liquidDensity - brushStrength * weight, 0));

            pointInfo.density -= deltaDensity;
            liquidDensity -= deltaDensity;
            
            if(pointInfo.density != 0) pointInfo.viscosity = 1 - (liquidDensity / pointInfo.density);
        }
    }

    TerrainChunk.MapData HandleTerraform(TerrainChunk.MapData pointInfo, float brushStrength)
    {
        if(weight * brushStrength == 0) return pointInfo;

        if (isAdding) HandleAdd(ref pointInfo, brushStrength);
        else HandleRemove(ref pointInfo, brushStrength);
        return pointInfo;
    }

    void OnDrawGizmos()
    {
        if (hasHit)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(hitPoint, 0.25f);
        }
    }
}
