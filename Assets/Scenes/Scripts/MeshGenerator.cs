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
        MeshData mesh = new MeshData(noiseMap.GetLength(0), noiseMap.GetLength(1), noiseMap.GetLength(2));
        for(int x = 0; x < noiseMap.GetLength(0) - 1; x++)
        {
            for (int y = 0; y < noiseMap.GetLength(1) - 1; y++)
            {
                for (int z = 0; z < noiseMap.GetLength(2) - 1; z++)
                {
                    int cubeindex = 0;
                    if (noiseMap[x, y + 1, z] < isoLevel) cubeindex |= 1;
                    if (noiseMap[x + 1, y + 1, z] < isoLevel) cubeindex |= 2;
                    if (noiseMap[x + 1, y, z] < isoLevel) cubeindex |= 4;
                    if (noiseMap[x, y, z] < isoLevel) cubeindex |= 8;
                    if (noiseMap[x, y + 1, z + 1] < isoLevel) cubeindex |= 16;
                    if (noiseMap[x + 1, y + 1, z + 1] < isoLevel) cubeindex |= 32;
                    if (noiseMap[x + 1, y, z + 1] < isoLevel) cubeindex |= 64;
                    if (noiseMap[x, y, z + 1] < isoLevel) cubeindex |= 128;

                    int vertCount = 0;
                    int originalLength = mesh.vertices.Count;
                    int[] vertPos = new int[12];


                    if (MarchCubeRef.edgeTable[cubeindex] != 0)
                    {
                        /* Find the vertices where the surface intersects the cube */
                        //Why does c# not take int as bools
                        if ((MarchCubeRef.edgeTable[cubeindex] & 1) != 0) {
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x, y + 1, z), new Vector3(x + 1, y + 1, z),
                                            noiseMap[x, y + 1, z], noiseMap[x + 1, y + 1, z]));
                            mesh.vertexParents.Add(new Vector3(x, y + 1, z));
                            mesh.vertexParents.Add(new Vector3(x + 1, y + 1, z));
                            vertPos[0] = vertCount;
                            vertCount++;
                        }
                        if ((MarchCubeRef.edgeTable[cubeindex] & 2) != 0) {
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x + 1, y + 1, z), new Vector3(x + 1, y, z),
                                            noiseMap[x + 1, y + 1, z], noiseMap[x + 1, y, z]));
                            mesh.vertexParents.Add(new Vector3(x + 1, y + 1, z));
                            mesh.vertexParents.Add(new Vector3(x + 1, y, z));
                            vertPos[1] = vertCount;
                            vertCount++;
                        }
                        if ((MarchCubeRef.edgeTable[cubeindex] & 4) != 0) { 
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x + 1, y, z), new Vector3(x, y, z),
                                            noiseMap[x + 1, y, z], noiseMap[x, y, z]));
                            mesh.vertexParents.Add(new Vector3(x + 1, y, z));
                            mesh.vertexParents.Add(new Vector3(x, y, z));
                            vertPos[2] = vertCount;
                            vertCount++;
                        }
                        if ((MarchCubeRef.edgeTable[cubeindex] & 8) != 0) { 
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x, y, z), new Vector3(x, y + 1, z),
                                            noiseMap[x, y, z], noiseMap[x, y + 1, z]));
                            mesh.vertexParents.Add(new Vector3(x, y, z));
                            mesh.vertexParents.Add(new Vector3(x, y + 1, z));
                            vertPos[3] = vertCount;
                            vertCount++;
                        }
                        if ((MarchCubeRef.edgeTable[cubeindex] & 16) != 0) { 
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x, y + 1, z + 1), new Vector3(x + 1, y + 1, z + 1),
                                            noiseMap[x, y + 1, z + 1], noiseMap[x + 1, y + 1, z + 1]));
                            mesh.vertexParents.Add(new Vector3(x, y + 1, z + 1));
                            mesh.vertexParents.Add(new Vector3(x + 1, y + 1, z + 1));
                            vertPos[4] = vertCount;
                            vertCount++;
                        }
                        if ((MarchCubeRef.edgeTable[cubeindex] & 32) != 0) { 
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x + 1, y + 1, z + 1), new Vector3(x + 1, y, z + 1),
                                            noiseMap[x + 1, y + 1, z + 1], noiseMap[x + 1, y, z + 1]));
                            mesh.vertexParents.Add(new Vector3(x + 1, y + 1, z + 1));
                            mesh.vertexParents.Add(new Vector3(x + 1, y, z + 1));
                            vertPos[5] = vertCount;
                            vertCount++;
                        }
                        if ((MarchCubeRef.edgeTable[cubeindex] & 64) != 0) { 
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x + 1, y, z + 1), new Vector3(x, y, z + 1),
                                            noiseMap[x + 1, y, z + 1], noiseMap[x, y, z + 1]));
                            mesh.vertexParents.Add(new Vector3(x + 1, y, z + 1));
                            mesh.vertexParents.Add(new Vector3(x, y, z + 1));
                            vertPos[6] = vertCount;
                            vertCount++;
                        }
                        if ((MarchCubeRef.edgeTable[cubeindex] & 128) != 0) { 
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x, y, z + 1), new Vector3(x, y + 1, z + 1),
                                            noiseMap[x, y, z + 1], noiseMap[x, y + 1, z + 1]));
                            mesh.vertexParents.Add(new Vector3(x, y, z + 1));
                            mesh.vertexParents.Add(new Vector3(x, y + 1, z + 1));
                            vertPos[7] = vertCount;
                            vertCount++;
                        }
                        if ((MarchCubeRef.edgeTable[cubeindex] & 256) != 0) { 
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x, y + 1, z), new Vector3(x, y + 1, z + 1),
                                            noiseMap[x, y + 1, z], noiseMap[x, y + 1, z + 1]));
                            mesh.vertexParents.Add(new Vector3(x, y + 1, z));
                            mesh.vertexParents.Add(new Vector3(x, y + 1, z + 1));
                            vertPos[8] = vertCount;
                            vertCount++;
                        }
                        if ((MarchCubeRef.edgeTable[cubeindex] & 512) != 0) { 
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x + 1, y + 1, z), new Vector3(x + 1, y + 1, z + 1),
                                            noiseMap[x + 1, y + 1, z], noiseMap[x + 1, y + 1, z     + 1]));
                            mesh.vertexParents.Add(new Vector3(x + 1, y + 1, z));
                            mesh.vertexParents.Add(new Vector3(x + 1, y + 1, z + 1));
                            vertPos[9] = vertCount;
                            vertCount++;
                        }
                        if ((MarchCubeRef.edgeTable[cubeindex] & 1024) != 0) { 
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x + 1, y, z), new Vector3(x + 1, y, z + 1),
                                            noiseMap[x + 1, y, z], noiseMap[x + 1, y, z + 1]));
                            mesh.vertexParents.Add(new Vector3(x + 1, y, z));
                            mesh.vertexParents.Add(new Vector3(x + 1, y, z + 1));
                            vertPos[10] = vertCount;
                            vertCount++;
                        }
                        if ((MarchCubeRef.edgeTable[cubeindex] & 2048) != 0) { 
                            mesh.vertices.Add(
                               VertexInterp(isoLevel, new Vector3(x, y, z), new Vector3(x, y, z + 1),
                                            noiseMap[x, y, z], noiseMap[x, y, z + 1]));
                            mesh.vertexParents.Add(new Vector3(x, y, z));
                            mesh.vertexParents.Add(new Vector3(x, y, z+1));
                            vertPos[11] = vertCount;

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

    public MeshData(int meshWidth, int meshLength, int meshHeight)
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

    public Mesh CreateMesh()
    {
        Mesh mesh = new Mesh();
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        return mesh;
    }
}
