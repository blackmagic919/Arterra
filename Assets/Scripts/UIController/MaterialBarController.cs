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
    public Material inventoryMat;

    private ComputeBuffer indexBuffer;
    private ComputeBuffer percentageBuffer;

    const int numThreadsPerAxis = 8;
    const int textureWidth = 800;
    const int textureHeight = 130;

    [Range(0, 1)]
    public float size;

    private void OnEnable()
    {
        indexBuffer = new ComputeBuffer(100, sizeof(int));
        percentageBuffer = new ComputeBuffer(100, sizeof(float));
    }

    private void OnDisable()
    {
        indexBuffer.Release();
        percentageBuffer.Release();
    }

    void Start()
    {
        // Get the RectTransform component of the UI panel.
        panelRectTransform = GetComponent<RectTransform>();
        terraform = FindFirstObjectByType<TerraformController>();

        inventoryMat.SetBuffer("inventoryMaterialIndexes", indexBuffer);
        inventoryMat.SetBuffer("inventoryMaterialPercents", percentageBuffer);

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
        int[] indexes = terraform.getInventoryKeys;
        float[] amounts = terraform.getInventoryValues;

        float[] percentageCumulative = new float[totalMaterials];
        percentageCumulative[0] = 0;

        for (int i = 1; i < totalMaterials; i++)
            percentageCumulative[i] = (amounts[i-1]) / terraform.totalMaterialAmount + percentageCumulative[i - 1];

        indexBuffer.SetData(indexes, 0, 0, indexes.Count());
        percentageBuffer.SetData(percentageCumulative, 0, 0, percentageCumulative.Count());
        inventoryMat.SetInt("materialCount", totalMaterials);
        inventoryMat.SetInt("selectedMat", (int)terraform.selected);
        inventoryMat.SetFloat("InventorySize", size);
    }

}
