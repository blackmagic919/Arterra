using System.Collections;
using System.Collections.Generic;
using Unity.Burst.CompilerServices;
using UnityEngine;
using UnityEngine.TerrainUtils;

[ExecuteInEditMode]
public class TerraformController : MonoBehaviour
{

    public float terraformRadius = 5;
    public LayerMask terrainMask;

    public float terraformSpeedNear = 0.1f;
    public float terraformSpeedFar = 0.25f;

    public float maxTerraformDistance = 60;

    EndlessTerrain terrain;
    Transform cam;
    int numIterations = 5;
    bool hasHit;
    Vector3 hitPoint;

    // Start is called before the first frame update
    void Start()
    {
        cam = Camera.main.transform;
        terrain = FindAnyObjectByType<EndlessTerrain>();
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
    }

    void Terraform(Vector3 terraformPoint)
    {
        hasHit = true;
        hitPoint = terraformPoint; //For visualization

        float dstFromCam = (terraformPoint - cam.position).magnitude;
        float weight01 = Mathf.InverseLerp(0, maxTerraformDistance, dstFromCam);
        float weight = Mathf.Lerp(terraformSpeedNear, terraformSpeedFar, weight01);

        if (Input.GetMouseButton(1))
        {
            terrain.Terraform(terraformPoint, -weight, terraformRadius);
        }
        // Subtract terrain
        else if (Input.GetMouseButton(0))
        {
            terrain.Terraform(terraformPoint, weight, terraformRadius);
        }

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
