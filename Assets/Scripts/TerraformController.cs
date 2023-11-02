using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TerrainUtils;

public class TerraformController : MonoBehaviour
{

    public float terraformRadius = 5;
    public LayerMask terrainMask;

    public float terraformSpeedNear = 0.1f;
    public float terraformSpeedFar = 0.25f;
    public float maxTerraformDistance = 60;
    public float materialCapacity = 20;

    float cutOff;
    float weight;
    bool isAdding;
    public float selected = -1.0f;
    int selectPosition = 0;


    MaterialBarController barController;
    EndlessTerrain terrain;
    Transform cam;
    int numIterations = 5;
    bool hasHit;
    Vector3 hitPoint;


    [HideInInspector]
    public Dictionary<float, float> materialInventory = new Dictionary<float, float>();

    public float[] getInventoryKeys
    {
        get{
            float[] keys = new float[materialInventory.Count];
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
        cutOff = terrain.IsoLevel;
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
            terrain.Terraform(terraformPoint, terraformRadius, handleTerraform);
        }
        // Subtract terrain
        else if (Input.GetMouseButton(0))
        {
            isAdding = false;
            terrain.Terraform(terraformPoint, terraformRadius, handleTerraform);
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

    Vector2 handleTerraform(Vector2 pointInfo, float brushStrength)
    {
        if (isAdding)
        {

            if (pointInfo.y < cutOff || pointInfo.x == selected)
            {
                float deltaDensity = Mathf.Clamp(pointInfo.y + brushStrength * weight, 0, 1) - pointInfo.y;
                pointInfo.x = selected;
                pointInfo.y += RemoveMaterialFromInventory((int)pointInfo.x, deltaDensity);
            }

        }
        else if(pointInfo.y > cutOff)
        {
            float deltaDensity = pointInfo.y - Mathf.Clamp(pointInfo.y - brushStrength * weight, 0, 1);
            pointInfo.y -= AddMaterialToInventory((int)pointInfo.x, deltaDensity);
        }

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
