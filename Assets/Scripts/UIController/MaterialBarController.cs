using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.UI;

[ExecuteInEditMode]
public class MaterialBarController : MonoBehaviour
{
    TerraformController terraform;
    public TextureData textureData;
    public RectTransform panelRectTransform;
    public ComputeShader BarTextureCompute;
    public Material inventoryMat;

    public RenderTexture result;

    const int numThreadsPerAxis = 8;
    const int textureWidth = 800;
    const int textureHeight = 130;

    [Range(0, 1)]
    public float size;

    private void OnEnable()
    {
        result = new RenderTexture(textureWidth, textureHeight, 1);
        result.enableRandomWrite = true;
        result.Create();
    }

    void Start()
    {
        // Get the RectTransform component of the UI panel.
        panelRectTransform = GetComponent<RectTransform>();
        terraform = FindFirstObjectByType<TerraformController>();
        result.filterMode = FilterMode.Point; //Prevent weird ghost interpolated materials -> Materials index not interpolated
    }

    // Update is called once per frame
    void Update()
    {
        if (Application.isPlaying)
            size = terraform.totalMaterialAmount / terraform.materialCapacity;
        else
            OnInventoryChanged();

        panelRectTransform.transform.localScale = new Vector3(size, 1, 1);

    }

    public void OnInventoryChanged() //DO NOT DO THIS IN ONENABLE, Shader is compiled later than OnEnabled is called
    {
        int totalMaterials = terraform.materialInventory.Count;

        if (totalMaterials == 0)
            return;
        //
        float[] indexes = terraform.getInventoryKeys;
        float[] amounts = terraform.getInventoryValues;
        int maxMaterials = textureData.MaterialDictionary.Count;

        float[] percentageCumulative = new float[totalMaterials];
        percentageCumulative[0] = 0;
        for (int i = 1; i < totalMaterials; i++)
            percentageCumulative[i] = (amounts[i-1]) / terraform.totalMaterialAmount + percentageCumulative[i - 1];

        ComputeBuffer indexBuffer = new ComputeBuffer(totalMaterials, sizeof(float));
        indexBuffer.SetData(indexes);

        ComputeBuffer percentageBuffer = new ComputeBuffer(totalMaterials, sizeof(float));
        percentageBuffer.SetData(percentageCumulative);

        BarTextureCompute.SetInt("materialCount", totalMaterials);
        BarTextureCompute.SetInt("maxMaterialCount", maxMaterials);
        BarTextureCompute.SetBuffer(0, "inventoryMaterialIndexes", indexBuffer);
        BarTextureCompute.SetBuffer(0, "inventoryMaterialPercents", percentageBuffer);

        BarTextureCompute.SetTexture(0, "Result", result); 
        BarTextureCompute.Dispatch(0, textureWidth / numThreadsPerAxis + 1, textureHeight/ numThreadsPerAxis + 1, 1);


        inventoryMat.SetInt("maxMatCount", maxMaterials);
        inventoryMat.SetInt("selectedMat", (int)terraform.selected);
        inventoryMat.SetFloat("InventorySize", size);
        inventoryMat.SetTexture("_MaterialData", result);

        indexBuffer.Release();
        percentageBuffer.Release();
    }

}
