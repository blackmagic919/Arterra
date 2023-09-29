using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MeshGenerator
{
    /*
     *  
     *    4---4----5
     *   7|       5|
     *  / 8      / 9
     * 7--+-6---6  |
     * |  |     10 |
     *11  0---0-+--1
     * | 3      | 1
     * 3----2---2/
     * 
     * z
     * ^     .--------.
     * |    /|       /|
     * |   / |      / |    y
     * |  .--+-----.  |   /\
     * |  |  |     |  |   /
     * |  |  .-----+--.  /
     * |  | /      | /  /
     * | xyz_______./  /
     * +---------> x  /
     */
    public static MeshData GenerateMesh(float[,,] noiseMap, float isoLevel)
    {

        MeshData mesh = new MeshData();

        int IncSize = 1; //will add more later
        for (int x = 0; x < noiseMap.GetLength(0)-1; x += IncSize)
        {
            for (int y = 0; y < noiseMap.GetLength(1)-1; y += IncSize)
            {
                for (int z = 0; z < noiseMap.GetLength(2)-1; z += IncSize)
                {
                    int cubeindex = 0;
                    if (noiseMap[x, y + IncSize, z] > isoLevel) cubeindex |= 1;
                    if (noiseMap[x + IncSize, y + IncSize, z] > isoLevel) cubeindex |= 2;
                    if (noiseMap[x + IncSize, y, z] > isoLevel) cubeindex |= 4;
                    if (noiseMap[x, y, z] > isoLevel) cubeindex |= 8;
                    if (noiseMap[x, y + IncSize, z + IncSize] > isoLevel) cubeindex |= 16;
                    if (noiseMap[x + IncSize, y + IncSize, z + IncSize] > isoLevel) cubeindex |= 32;
                    if (noiseMap[x + IncSize, y, z + IncSize] > isoLevel) cubeindex |= 64;
                    if (noiseMap[x, y, z + IncSize] > isoLevel) cubeindex |= 128;

                    int vertCount = 0;
                    int originalLength = mesh.vertices.Count;
                    int[] vertPos = new int[12];


                    if (MarchCubeRef.edgeTable[cubeindex] != 0)
                    {
                        /* Find the vertices where the surface intersects the cube */
                        //Why does c# not take int as bools
                        if ((MarchCubeRef.edgeTable[cubeindex] & 1) != 0) {
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x, y + IncSize, z), new Vector3(x + IncSize, y + IncSize, z),
                                            noiseMap[x, y + IncSize, z], noiseMap[x + IncSize, y + IncSize, z]));
                            mesh.vertexParents.Add(new Vector3(x, y + IncSize, z));
                            mesh.vertexParents.Add(new Vector3(x + IncSize, y + IncSize, z));
                            vertPos[0] = vertCount;
                            vertCount++;
                        }
                        if ((MarchCubeRef.edgeTable[cubeindex] & 2) != 0) {
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x + IncSize, y + IncSize, z), new Vector3(x + IncSize, y, z),
                                            noiseMap[x + IncSize, y + IncSize, z], noiseMap[x + IncSize, y, z]));
                            mesh.vertexParents.Add(new Vector3(x + IncSize, y + IncSize, z));
                            mesh.vertexParents.Add(new Vector3(x + IncSize, y, z));
                            vertPos[1] = vertCount;
                            vertCount++;
                        }
                        if ((MarchCubeRef.edgeTable[cubeindex] & 4) != 0) { 
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x + IncSize, y, z), new Vector3(x, y, z),
                                            noiseMap[x + IncSize, y, z], noiseMap[x, y, z]));
                            mesh.vertexParents.Add(new Vector3(x + IncSize, y, z));
                            mesh.vertexParents.Add(new Vector3(x, y, z));
                            vertPos[2] = vertCount;
                            vertCount++;
                        }
                        if ((MarchCubeRef.edgeTable[cubeindex] & 8) != 0) { 
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x, y, z), new Vector3(x, y + IncSize, z),
                                            noiseMap[x, y, z], noiseMap[x, y + IncSize, z]));
                            mesh.vertexParents.Add(new Vector3(x, y, z));
                            mesh.vertexParents.Add(new Vector3(x, y + IncSize, z));
                            vertPos[3] = vertCount;
                            vertCount++;
                        }
                        if ((MarchCubeRef.edgeTable[cubeindex] & 16) != 0) { 
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x, y + IncSize, z + IncSize), new Vector3(x + IncSize, y + IncSize, z + IncSize),
                                            noiseMap[x, y + IncSize, z + IncSize], noiseMap[x + IncSize, y + IncSize, z + IncSize]));
                            mesh.vertexParents.Add(new Vector3(x, y + IncSize, z + IncSize));
                            mesh.vertexParents.Add(new Vector3(x + IncSize, y + IncSize, z + IncSize));
                            vertPos[4] = vertCount;
                            vertCount++;
                        }
                        if ((MarchCubeRef.edgeTable[cubeindex] & 32) != 0) { 
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x + IncSize, y + IncSize, z + IncSize), new Vector3(x + IncSize, y, z + IncSize),
                                            noiseMap[x + IncSize, y + IncSize, z + IncSize], noiseMap[x + IncSize, y, z + IncSize]));
                            mesh.vertexParents.Add(new Vector3(x + IncSize, y + IncSize, z + IncSize));
                            mesh.vertexParents.Add(new Vector3(x + IncSize, y, z + IncSize));
                            vertPos[5] = vertCount;
                            vertCount++;
                        }
                        if ((MarchCubeRef.edgeTable[cubeindex] & 64) != 0) { 
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x + IncSize, y, z + IncSize), new Vector3(x, y, z + IncSize),
                                            noiseMap[x + IncSize, y, z + IncSize], noiseMap[x, y, z + IncSize]));
                            mesh.vertexParents.Add(new Vector3(x + IncSize, y, z + IncSize));
                            mesh.vertexParents.Add(new Vector3(x, y, z + IncSize));
                            vertPos[6] = vertCount;
                            vertCount++;
                        }
                        if ((MarchCubeRef.edgeTable[cubeindex] & 128) != 0) { 
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x, y, z + IncSize), new Vector3(x, y + IncSize, z + IncSize),
                                            noiseMap[x, y, z + IncSize], noiseMap[x, y + IncSize, z + IncSize]));
                            mesh.vertexParents.Add(new Vector3(x, y, z + IncSize));
                            mesh.vertexParents.Add(new Vector3(x, y + IncSize, z + IncSize));
                            vertPos[7] = vertCount;
                            vertCount++;
                        }
                        if ((MarchCubeRef.edgeTable[cubeindex] & 256) != 0) { 
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x, y + IncSize, z), new Vector3(x, y + IncSize, z + IncSize),
                                            noiseMap[x, y + IncSize, z], noiseMap[x, y + IncSize, z + IncSize]));
                            mesh.vertexParents.Add(new Vector3(x, y + IncSize, z));
                            mesh.vertexParents.Add(new Vector3(x, y + IncSize, z + IncSize));
                            vertPos[8] = vertCount;
                            vertCount++;
                        }
                        if ((MarchCubeRef.edgeTable[cubeindex] & 512) != 0) { 
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x + IncSize, y + IncSize, z), new Vector3(x + IncSize, y + IncSize, z + IncSize),
                                            noiseMap[x + IncSize, y + IncSize, z], noiseMap[x + IncSize, y + IncSize, z + IncSize]));
                            mesh.vertexParents.Add(new Vector3(x + IncSize, y + IncSize, z));
                            mesh.vertexParents.Add(new Vector3(x + IncSize, y + IncSize, z + IncSize));
                            vertPos[9] = vertCount;
                            vertCount++;
                        }
                        if ((MarchCubeRef.edgeTable[cubeindex] & 1024) != 0) { 
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x + IncSize, y, z), new Vector3(x + IncSize, y, z + IncSize),
                                            noiseMap[x + IncSize, y, z], noiseMap[x + IncSize, y, z + IncSize]));
                            mesh.vertexParents.Add(new Vector3(x + IncSize, y, z));
                            mesh.vertexParents.Add(new Vector3(x + IncSize, y, z + IncSize));
                            vertPos[10] = vertCount;
                            vertCount++;
                        }
                        if ((MarchCubeRef.edgeTable[cubeindex] & 2048) != 0) { 
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x, y, z), new Vector3(x, y, z + IncSize),
                                            noiseMap[x, y, z], noiseMap[x, y, z + IncSize]));
                            mesh.vertexParents.Add(new Vector3(x, y, z));
                            mesh.vertexParents.Add(new Vector3(x, y, z + IncSize));
                            vertPos[11] = vertCount;
                            vertCount++;
                        }
                    }

                    for (int i = 0; MarchCubeRef.triTable[cubeindex, i] != -1; i += 3)
                    {
                        mesh.AddTriangle(originalLength + vertPos[MarchCubeRef.triTable[cubeindex, i]],
                                         originalLength + vertPos[MarchCubeRef.triTable[cubeindex, i + 1]],
                                         originalLength + vertPos[MarchCubeRef.triTable[cubeindex, i + 2]]);
                    }

                }
            }
        }

        return mesh;
    }

    public static Vector3 VertexInterp(float isoLevel, Vector3 p1, Vector3 p2, float p1Val, float p2Val)
    {
        if (Mathf.Abs(isoLevel - p1Val) < 0.00001)
            return (p1);
        if (Mathf.Abs(isoLevel - p2Val) < 0.00001)
            return (p2);
        if (Mathf.Abs(isoLevel - p2Val) < 0.00001)
            return (p1);
        Vector3 p;

        p = p1 + ((isoLevel - p1Val) / (p2Val - p1Val) * (p2 - p1));
        return p;
    }   
}

public class MeshData
{
    public List<Vector3> vertices;
    public List<Vector3> vertexParents;
    public List<int> triangles;

    public MeshData()
    {
        vertices = new List<Vector3>();
        vertexParents = new List<Vector3>();
        triangles = new List<int>();
    }

    public void AddTriangle(int a, int b, int c)
    {
        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);   
    }

}
