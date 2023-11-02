using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class GrassRenderer
{
    [HideInInspector]
    public GenerationHeightData GenerationData;
    /*Loop through materials, keep array for mesh instance for each material
     * 
     *For a mesh, go through every triangle
     *For each triangle, there will be 3 vertices, and 6 materials
     *Keep list of material influences 
     *For each of 6 parent points
     *  add influence or 1-influence to material index
     *  
     *Define "Amount" as
     *(numInstancesPerMeter2)*(AreaOfTriangle)*(MaterialInfluence)
     *Run loop for(Floor(Amount += Random(0,1))) <- Deals with probabilities
     *  Find a random point on the triangle
     *  render object
     */
    /*
    public MeshFilter filter;
    private Mesh mesh;

    public object generatePoints(int[] triangles, Vector3[] vertex, Color[] material, Vector3[] normalV, Vector3 offset, float lerpScale)
    {
        List<Vector3> positions = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Color> colors = new List<Color>();

        System.Random random = new System.Random();//Running on thread
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Dictionary<int, float> influence = new Dictionary<int, float>();
            float triangelArea = CalculateTriangleArea(vertex[triangles[i]], vertex[triangles[i + 1]], vertex[triangles[i + 2]]);

            for (int w = i; w < i + 3; w++)
            {
                Color curMaterial = material[triangles[w]];

                if (!influence.TryAdd((int)curMaterial.r, curMaterial.b / 255f / 3f)) //3 points per triangle
                    influence[(int)curMaterial.r] += curMaterial.b / 255f / 3f;
                if (!influence.TryAdd((int)curMaterial.g, (1.0f - curMaterial.b / 255f) / 3f))
                    influence[(int)curMaterial.g] += (1.0f - curMaterial.b / 255f) / 3f;
            }

            foreach (KeyValuePair<int, float> genMat in influence)
            {
                int matIndex = genMat.Key;
                float matInfluence = genMat.Value;
                int Amount = Mathf.FloorToInt(GenerationData.Materials[matIndex].instancesPerMSq * triangelArea * matInfluence + (float)random.NextDouble()); //if value is not int, this runs as a remainder probability of happening
                for (int m = 0; m < Amount; m++)
                {
                    Vector3 origin = (getRandomPoint(vertex[triangles[i]], vertex[triangles[i + 1]], vertex[triangles[i + 2]]) + offset) * lerpScale;
                    Vector3 normal = Vector3.Normalize(normalV[triangles[i]] + normalV[triangles[i + 1]] + normalV[triangles[i + 2]]);
                    colors.Add(new Color(1.0f, 1.0f, 1.0f));
                    positions.Add(origin);
                    normals.Add(normal);

                    //Quaternion meshOrientation = Quaternion.Euler(new Vector3(90, 0, 0));//some meshes aren't oriented the correct way
                    //genPoints[genMat.Key].Add(Matrix4x4.TRS(origin, Quaternion.LookRotation(normal) * meshFaceRotation, Vector3.one * materialData[matIndex].genObjScale));
                }
            }
        }
        mesh = new Mesh();
        mesh.SetVertices(positions);
        mesh.SetColors(colors);
        mesh.SetIndices(Enumerable.Range(0, positions.Count).ToArray(), MeshTopology.Points, 0);
        mesh.SetNormals(normals);
        filter.sharedMesh = mesh;

        return null;
    }

    //Chatgpt is so smart wtf
    private Vector3 getRandomPoint(Vector3 vet1, Vector3 vet2, Vector3 vet3)
    {
        // Generate two random numbers between 0 and 1
        System.Random random = new System.Random();
        float r1 = (float)random.NextDouble();
        float r2 = (float)random.NextDouble();

        // Ensure the two random numbers sum to no more than 1
        if (r1 + r2 > 1)
        {
            r1 = 1 - r1;
            r2 = 1 - r2;
        }

        // Calculate the third random number
        float r3 = 1 - r1 - r2;

        // Calculate the coordinates of the random point using barycentric coordinates
        Vector3 randomPoint = r1 * vet1 + r2 * vet2 + r3 * vet3;

        return randomPoint;
    }

    private float CalculateTriangleArea(Vector3 v1, Vector3 v2, Vector3 v3)
    {
        Vector3 side1 = v2 - v1;
        Vector3 side2 = v3 - v1;

        //Cross product = area of parallelogram from sides = |s1||s2|sin(theta) (Calc3 lol)
        Vector3 crossProduct = Vector3.Cross(side1, side2);

        float area = 0.5f * crossProduct.magnitude;

        return area;
    }*/
}
