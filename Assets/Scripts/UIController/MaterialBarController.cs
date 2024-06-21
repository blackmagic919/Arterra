using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.UI;

public class MaterialBarController : MonoBehaviour
{
    public RectTransform panelRectTransform;
    public Image inventoryMat;

    private ComputeBuffer MainInventoryData;

    [Range(0, 1)]
    public float size;

    private void OnEnable() { MainInventoryData = new ComputeBuffer(100, sizeof(int) + sizeof(float)); }

    private void OnDisable() { MainInventoryData?.Release(); }

    void Start()
    {
        // Get the RectTransform component of the UI panel.
        panelRectTransform = GetComponent<RectTransform>();
        inventoryMat.materialForRendering.SetBuffer("MainInventoryMaterial", MainInventoryData);
        size = 0;
    }

    // Update is called once per frame
    void Update() { panelRectTransform.transform.localScale = new Vector3(size, 1, 1); }

    public void OnInventoryChanged(TerraformController terraform) //DO NOT DO THIS IN ONENABLE, Shader is compiled later than OnEnabled is called
    {
        size = (float)terraform.MainInventory.totalMaterialAmount / terraform.MainInventory.materialCapacity;
        int totalmaterials_M = terraform.MainInventory.inventory.Count;
        int totalMaterials_A = terraform.AuxInventory.inventory.Count;
        MainInventoryData.SetData(MaterialData.GetInventoryData(terraform.MainInventory), 0, 0, totalmaterials_M);
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
