using System;
using System.Collections.Generic;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Editor;
using Unity.Mathematics;

namespace Arterra.Configuration
{
    [CreateAssetMenu(menuName = "ShaderData/MiniMesh/Setting")]
    public class MiniMeshSetting : Category<MiniMeshSetting>
    {
        /// <summary>The registry names of all entries referencing registries within <see cref="info"/>. When an element such as
        /// a material, structure, or entry needs to reference an entry in an external registry, they can indicate the index
        /// within this list of the name of the entry within the registry that they are referencing. </summary>
        public Option<List<string>> Names;
        //This is seperated so we have just the raw data independent of 
        //anything we might have inherited and so-on
        public Option<List<MiniMeshLevel>> info;

        public void GetInfo(List<MiniMeshLevelCompact> levels, List<MiniMeshCompact> meshes, List<MeshVertex> meshData, int levelCount)
        {
            List<MiniMeshLevel> rawLevels = info.value ?? new List<MiniMeshLevel>();
            int safeLevelCount = math.max(levelCount, 1);
            int fallbackLevel = math.max(rawLevels.Count - 1, 0);

            for (int levelIndex = 0; levelIndex < safeLevelCount; levelIndex++)
            {
                int sourceLevel = rawLevels.Count == 0 ? 0 : math.min(levelIndex, fallbackLevel);
                MiniMeshLevel levelInfo = rawLevels.Count == 0
                    ? new MiniMeshLevel { MMeshOptions = new Option<List<MiniMeshLevel.MiniMesh>> { value = new List<MiniMeshLevel.MiniMesh>() }, frequency = 0f, sizeMultiplier = 1f, sizeMultiplierRange = Vector2.one, TextureIndex = 0, inheritSurfaceNormals = false }
                    : rawLevels[sourceLevel];

                List<MiniMeshLevel.MiniMesh> options = levelInfo.MMeshOptions.value ?? new List<MiniMeshLevel.MiniMesh>();
                int optionsStart = meshes.Count;
                int textureIndex = ResolveTextureIndex(levelInfo.TextureIndex);

                if (options.Count > 0)
                {
                    float[] normalized = NormalizeProbabilities(options);
                    float cumulative = 0f;

                    for (int optionIndex = 0; optionIndex < options.Count; optionIndex++)
                    {
                        MiniMeshLevel.MiniMesh option = options[optionIndex];
                        int vertexStart = meshData.Count;
                        AppendMeshVertices(option.mesh, meshData);
                        int vertexCount = meshData.Count - vertexStart;

                        cumulative = math.min(1f, cumulative + normalized[optionIndex]);
                        meshes.Add(new MiniMeshCompact
                        {
                            vertexRange = new int2(vertexStart, vertexCount),
                            probability = cumulative,
                            texIndex = textureIndex
                        });
                    }
                }

                float2 sizeRange = ResolveSizeMultiplierRange(levelInfo);
                levels.Add(new MiniMeshLevelCompact
                {
                    optionsRange = new int2(optionsStart, meshes.Count - optionsStart),
                    frequency = levelInfo.frequency,
                    sizeMultiplierRange = sizeRange,
                    inheritSurfaceNormals = levelInfo.inheritSurfaceNormals ? 1 : 0
                });
            }
        }

        private static float2 ResolveSizeMultiplierRange(MiniMeshLevel levelInfo)
        {
            Vector2 rawRange = levelInfo.sizeMultiplierRange;
            bool hasRange = rawRange.x > 0f || rawRange.y > 0f;

            if (!hasRange)
            {
                float legacy = levelInfo.sizeMultiplier <= 0f ? 1f : levelInfo.sizeMultiplier;
                return new float2(legacy, legacy);
            }

            float minSize = math.max(math.min(rawRange.x, rawRange.y), 1e-4f);
            float maxSize = math.max(math.max(rawRange.x, rawRange.y), minSize);
            return new float2(minSize, maxSize);
        }

        private int ResolveTextureIndex(int localTextureIndex)
        {
            if (Names.value == null || localTextureIndex < 0 || localTextureIndex >= Names.value.Count)
            {
                return 0;
            }

            Catalogue<TextureContainer> texReg = Config.CURRENT.Generation.Textures;
            string texName = Names.value[localTextureIndex];
            return texReg.Contains(texName) ? texReg.RetrieveIndex(texName) : 0;
        }

        private static float[] NormalizeProbabilities(List<MiniMeshLevel.MiniMesh> options)
        {
            float[] normalized = new float[options.Count];
            float sum = 0f;
            for (int i = 0; i < options.Count; i++)
            {
                float p = math.max(options[i].probability, 0f);
                normalized[i] = p;
                sum += p;
            }

            if (sum <= 1e-6f)
            {
                float uniform = 1f / options.Count;
                for (int i = 0; i < options.Count; i++)
                {
                    normalized[i] = uniform;
                }
                return normalized;
            }

            float inv = 1f / sum;
            for (int i = 0; i < options.Count; i++)
            {
                normalized[i] *= inv;
            }

            return normalized;
        }

        private static void AppendMeshVertices(Mesh mesh, List<MeshVertex> meshData)
        {
            if (mesh == null)
            {
                return;
            }

            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uv0 = mesh.uv;
            Color[] colors = mesh.colors;
            bool hasWindChannel = colors != null && colors.Length > 0;
            
            int[] triangles = mesh.triangles;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                AppendPackedVertex(vertices, normals, uv0, colors, hasWindChannel, triangles[i], meshData);
                AppendPackedVertex(vertices, normals, uv0, colors, hasWindChannel, triangles[i + 1], meshData);
                AppendPackedVertex(vertices, normals, uv0, colors, hasWindChannel, triangles[i + 2], meshData);
            }
        }

        private static void AppendPackedVertex(Vector3[] vertices, Vector3[] normals, Vector2[] uv0, Color[] colors, bool hasWindChannel, int srcIndex, List<MeshVertex> meshData)
        {
            if (srcIndex < 0 || srcIndex >= vertices.Length)
            {
                return;
            }

            Vector3 n = (normals != null && srcIndex < normals.Length) ? normals[srcIndex] : Vector3.up;
            if (n.sqrMagnitude < 1e-8f)
            {
                n = Vector3.up;
            }

            Vector2 uv = (uv0 != null && srcIndex < uv0.Length) ? uv0[srcIndex] : Vector2.zero;
            float windStrength = (hasWindChannel && srcIndex < colors.Length) ? colors[srcIndex].r : 1f;
            meshData.Add(new MeshVertex
            {
                position = vertices[srcIndex],
                normal = n.normalized,
                uv = uv,
                windStrength = windStrength
            });
        }
    }

    [Serializable]
    public struct MiniMeshLevel
    {
        public Option<List<MiniMesh>> MMeshOptions;
        [Serializable]
        public struct MiniMesh {
            public Mesh mesh;
            public float probability;
        } 
        //Positive -> Per Face, Negative -> PerGridUnit^2
        public float frequency;
        public float sizeMultiplier; //default 1
        public Vector2 sizeMultiplierRange;
        public bool inheritSurfaceNormals;
        [RegistryReference("Textures")]
        public int TextureIndex;
    }

    public struct MiniMeshLevelCompact {
        public static int DataSize => sizeof(float) * 3 + sizeof(int) * 3;
        public int2 optionsRange;
        public float frequency;
        public float2 sizeMultiplierRange;
        public int inheritSurfaceNormals;
    }

    public struct MiniMeshCompact {
        public static int DataSize => sizeof(float) + sizeof(int) * 3;
        public int2 vertexRange;
        public float probability;
        public int texIndex;
    }

    public struct MeshVertex {
        public static int DataSize => sizeof(float) * 9;
        public float3 position;
        public float3 normal;
        public float2 uv;
        public float windStrength;
    }
}
