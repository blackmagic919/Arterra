using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.UI;

[ExecuteInEditMode]
public class MaterialBarController : MonoBehaviour
{
    TerraformController terraform;
    public RectTransform panelRectTransform;
    public Image inventoryMat;

    private ComputeBuffer MainInventoryData;
    private ComputeBuffer AuxInventoryData;

    [Range(0, 1)]
    public float size;

    private void OnEnable()
    {
        MainInventoryData = new ComputeBuffer(100, sizeof(int) + sizeof(float));
        AuxInventoryData = new ComputeBuffer(100, sizeof(int) + sizeof(float));
    }

    private void OnDisable()
    {
        MainInventoryData?.Release();
        AuxInventoryData?.Release();
    }

    void Start()
    {
        // Get the RectTransform component of the UI panel.
        panelRectTransform = GetComponent<RectTransform>();
        terraform = FindFirstObjectByType<TerraformController>();
        inventoryMat.materialForRendering.SetBuffer("MainInventoryMaterial", MainInventoryData);
        inventoryMat.materialForRendering.SetBuffer("AuxInventoryMaterial", AuxInventoryData);
    }

    // Update is called once per frame
    void Update()
    {
        if (Application.isPlaying)
            size = (float)terraform.MainInventory.totalMaterialAmount / terraform.MainInventory.materialCapacity;
        else
            OnInventoryChanged();

        panelRectTransform.transform.localScale = new Vector3(size, 1, 1);

    }

    public void OnInventoryChanged() //DO NOT DO THIS IN ONENABLE, Shader is compiled later than OnEnabled is called
    {
        int totalmaterials_M = terraform.MainInventory.inventory.Count;
        int totalMaterials_A = terraform.AuxInventory.inventory.Count;
        MainInventoryData.SetData(MaterialData.GetInventoryData(terraform.MainInventory), 0, 0, totalmaterials_M);
        AuxInventoryData.SetData(MaterialData.GetInventoryData(terraform.AuxInventory), 0, 0, totalMaterials_A);
        inventoryMat.materialForRendering.SetInt("MainMaterialCount", totalmaterials_M);
        inventoryMat.materialForRendering.SetInt("AuxMaterialCount", totalMaterials_A);
        inventoryMat.materialForRendering.SetInt("UseSolid", terraform.useSolid ? 1 : 0);

        inventoryMat.materialForRendering.SetInt("selectedMat", (int)terraform.MainInventory.selected);
        inventoryMat.materialForRendering.SetFloat("InventorySize", size);
    }

    struct MaterialData
    {
        public int index;
        public float percentage;

        public static MaterialData[] GetInventoryData(MaterialInventory inventory){
            int[] indexes = inventory.GetInventoryKeys;
            int[] amounts = inventory.GetInventoryValues;
            int totalMaterials = inventory.inventory.Count;

            MaterialData[] materialInfo = new MaterialData[totalMaterials+1];
            materialInfo[0].percentage = 0;
            for (int i = 1; i <= totalMaterials; i++)
            {
                materialInfo[i-1].index = indexes[i-1];
                materialInfo[i].percentage = ((float)amounts[i-1]) / inventory.totalMaterialAmount + materialInfo[i - 1].percentage;
            }
            return materialInfo;
        }
    }

}
