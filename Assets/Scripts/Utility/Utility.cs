using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using System.Threading.Tasks;
using UnityEditorInternal;

namespace Utils {
    public static class CustomUtility {
        public readonly static int3[] dP = new int3[6]{
            new (0,1,0),
            new (0,-1,0),
            new (1,0,0),
            new (0,0,-1),
            new (-1,0,0),
            new (0,0,1),
        };

        public readonly static int3x3[,,] RotationLookupTable = new int3x3[,,]
        {
            // y = 0
            {
                // x = 0
                {
                    new(1,0,0, 0,1,0, 0,0,1),
                    new(0,-1,0, 1,0,0, 0,0,1),
                    new(-1,0,0, 0,-1,0, 0,0,1),
                    new(0,1,0, -1,0,0, 0,0,1)
                },
                // x = 1
                {
                    new(1,0,0, 0,0,-1, 0,1,0),
                    new(0,0,-1, -1,0,0, 0,1,0),
                    new(-1,0,0, 0,0,1, 0,1,0),
                    new(0,0,1, 1,0,0, 0,1,0)
                },
                // x = 2
                {
                    new(1,0,0, 0,-1,0, 0,0,-1),
                    new(0,-1,0, -1,0,0, 0,0,-1),
                    new(-1,0,0, 0,1,0, 0,0,-1),
                    new(0,1,0, 1,0,0, 0,0,-1)
                },
                // x = 3
                {
                    new(1,0,0, 0,0,1, 0,-1,0),
                    new(0,0,1, -1,0,0, 0,-1,0),
                    new(-1,0,0, 0,0,-1, 0,-1,0),
                    new(0,0,-1, 1,0,0, 0,-1,0)
                }
            },

            // y = 1
            {
                // x = 0
                {
                    new(0,0,1, 0,1,0, -1,0,0),
                    new(0,1,0, -1,0,0, 0,0,-1),
                    new(0,0,-1, 0,1,0, 1,0,0),
                    new(0,-1,0, 1,0,0, 0,0,-1)
                },
                // x = 1
                {
                    new(0,0,1, -1,0,0, 0,-1,0),
                    new(-1,0,0, 0,0,-1, 0,-1,0),
                    new(0,0,-1, 1,0,0, 0,-1,0),
                    new(1,0,0, 0,0,1, 0,-1,0)
                },
                // x = 2
                {
                    new(0,0,1, 0,-1,0, 1,0,0),
                    new(1,0,0, 0,-1,0, 0,0,-1),
                    new(0,0,-1, 0,-1,0, -1,0,0),
                    new(-1,0,0, 0,-1,0, 0,0,1)
                },
                // x = 3
                {
                    new(0,0,1, 1,0,0, 0,1,0),
                    new(1,0,0, 0,0,-1, 0,1,0),
                    new(0,0,-1, -1,0,0, 0,1,0),
                    new(-1,0,0, 0,0,1, 0,1,0)
                }
            },

            // y = 2
            {
                // x = 0
                {
                    new(-1,0,0, 0,1,0, 0,0,-1),
                    new(0,1,0, 1,0,0, 0,0,-1),
                    new(1,0,0, 0,-1,0, 0,0,-1),
                    new(0,-1,0, -1,0,0, 0,0,-1)
                },
                // x = 1
                {
                    new(-1,0,0, 0,0,-1, 0,1,0),
                    new(0,0,-1, 1,0,0, 0,1,0),
                    new(1,0,0, 0,0,1, 0,1,0),
                    new(0,0,1, -1,0,0, 0,1,0)
                },
                // x = 2
                {
                    new(-1,0,0, 0,-1,0, 0,0,1),
                    new(0,-1,0, 1,0,0, 0,0,1),
                    new(1,0,0, 0,1,0, 0,0,1),
                    new(0,1,0, -1,0,0, 0,0,1)
                },
                // x = 3
                {
                    new(-1,0,0, 0,0,1, 0,-1,0),
                    new(0,0,1, 1,0,0, 0,-1,0),
                    new(1,0,0, 0,0,-1, 0,-1,0),
                    new(0,0,-1, -1,0,0, 0,-1,0)
                }
            },

            // y = 3
            {
                // x = 0
                {
                    new(0,0,-1, 0,1,0, 1,0,0),
                    new(0,1,0, 1,0,0, 0,0,1),
                    new(0,0,1, 0,1,0, -1,0,0),
                    new(0,-1,0, -1,0,0, 0,0,1)
                },
                // x = 1
                {
                    new(0,0,-1, 1,0,0, 0,-1,0),
                    new(1,0,0, 0,0,-1, 0,-1,0),
                    new(0,0,1, -1,0,0, 0,-1,0),
                    new(-1,0,0, 0,0,1, 0,-1,0)
                },
                // x = 2
                {
                    new(0,0,-1, 0,-1,0, -1,0,0),
                    new(-1,0,0, 0,-1,0, 0,0,1),
                    new(0,0,1, 0,-1,0, 1,0,0),
                    new(1,0,0, 0,-1,0, 0,0,-1)
                },
                // x = 3
                {
                    new(0,0,-1, -1,0,0, 0,1,0),
                    new(-1,0,0, 0,0,-1, 0,1,0),
                    new(0,0,1, 1,0,0, 0,1,0),
                    new(1,0,0, 0,0,1, 0,1,0)
                }
            }
        };


        static int[] xThetaRot = new int[] { 0, 2, 0, 2 };
        static int[] xThetaDir = new int[] { 1, -1, -1, 1 };
        static int[] zThetaRot = new int[] { 2, 0, 2, 0 };
        static int[] zThetaDir = new int[] { 1, 1, -1, -1 };

        public static int irregularIndexFromCoord(int x, int y, int z, int sizeY, int sizeZ) {
            return x * sizeY * sizeZ + y * sizeZ + z;
        }

        public static int irregularIndexFromCoord(int3 pos, int2 size) {
            return pos.x * size.x * size.y + pos.y * size.y + pos.z;
        }

        public static int indexFromCoord(int x, int y, int z, int numPointsAxis) {
            return x * numPointsAxis * numPointsAxis + y * numPointsAxis + z;
        }

        public static int indexFromCoord2D(int x, int y, int numPointsAxis) {
            return x * numPointsAxis + y;
        }

        public static int indexFromCoord(int3 coord, int numPointsPerAxis) {

            return coord.x * numPointsPerAxis * numPointsPerAxis + coord.y * numPointsPerAxis + coord.z;
        }

        public static Vector3 FloorVector(Vector3 vector) {
            return new Vector3(Mathf.FloorToInt(vector.x), Mathf.FloorToInt(vector.y), Mathf.FloorToInt(vector.z));
        }

        public static float GetArea(Vector2 a, Vector2 b) {
            float width = Mathf.Abs(a.x - b.x);
            float length = Mathf.Abs(a.y - b.y);

            return width * length;
        }

        public static uint EncodeMorton2(uint2 v) {
            uint Part1By1(uint x) {
                x &= 0x0000ffff;                // x = ---- ---- ---- ---- fedc ba98 7654 3210
                x = (x | (x << 8)) & 0x00FF00FF; // spread by 8 bits
                x = (x | (x << 4)) & 0x0F0F0F0F; // spread by 4 bits
                x = (x | (x << 2)) & 0x33333333; // spread by 2 bits
                x = (x | (x << 1)) & 0x55555555; // spread by 1 bit
                return x;
            }
            return Part1By1(v.x) | (Part1By1(v.y) << 1);
        }
        public static uint EncodeMorton3(uint3 v) {
            uint Part1By2(uint x) {
                x &= 0x000003ff;                  // x = ---- ---- ---- ---- ---- --98 7654 3210
                x = (x ^ (x << 16)) & 0xff0000ff; // x = ---- --98 ---- ---- ---- ---- 7654 3210
                x = (x ^ (x << 8)) & 0x0300f00f; // x = ---- --98 ---- ---- 7654 ---- ---- 3210
                x = (x ^ (x << 4)) & 0x030c30c3; // x = ---- --98 ---- 76-- --54 ---- 32-- --10
                x = (x ^ (x << 2)) & 0x09249249; // x = ---- 9--8 --7- -6-- 5--4 --3- -2-- 1--0
                return x;
            }
            return Part1By2(v.x) | (Part1By2(v.y) << 1) | (Part1By2(v.z) << 2);
        }


        public static T[,,] InitializeArray3D<T>(T value, uint sizeX, uint sizeY, uint sizeZ) {
            T[,,] array = new T[sizeX, sizeY, sizeZ];

            for (int x = 0; x < sizeX; x++) {
                for (int y = 0; y < sizeY; y++) {
                    for (int z = 0; z < sizeZ; z++) {
                        array[x, y, z] = value;
                    }
                }
            }
            return array;
        }

        public static T[] SortArray<T>(T[] unsorted, Func<object, object, int> compare) {
            List<T> temp = new List<T>(unsorted);
            temp.Sort((a, b) => compare(a, b));
            return temp.ToArray();
        }

        public static int[] RotateAxis(int theta90, int phi90) {
            int[] rotateDir = new int[] { 1, 2, 3 };
            int[] rotatedDir = new int[] { 1, 2, 3 };

            rotatedDir[0] = rotateDir[xThetaRot[theta90]] * xThetaDir[theta90];
            rotatedDir[2] = rotateDir[zThetaRot[theta90]] * zThetaDir[theta90];

            rotatedDir.CopyTo(rotateDir, 0);
            int transformedX = Mathf.Abs(rotateDir[0]) - 1;

            int[] yPhiRot = new int[] { 1, transformedX, 1 };
            int[] yPhiDir = new int[] { 1, -1, -1 };
            int[] xPhiRot = new int[] { transformedX, 1, transformedX };
            int[] xPhiDir = new int[] { 1, 1, -1 };

            rotatedDir[transformedX] = rotateDir[xPhiRot[phi90]] * xPhiDir[phi90];
            rotatedDir[1] = rotateDir[yPhiRot[phi90]] * yPhiDir[phi90];

            return rotatedDir;
        }

        public static float GetElement(Vector3 vector, int direction) {
            if (direction == 0)
                return vector.x;
            if (direction == 1)
                return vector.y;
            if (direction == 2)
                return vector.z;
            return -1.0f;
        }

        public static float GetDistanceToSquare(Vector2 center, float sideLength, Vector2 position) {
            float halfLength = sideLength / 2;

            float xZero = position.x;
            float yZero = position.y;

            float xLeft = center.x - halfLength;
            float xRight = center.x + halfLength;
            float yTop = center.y + halfLength;
            float yBottom = center.y - halfLength;

            float xClosest = Mathf.Clamp(xZero, xLeft, xRight);
            float yClosest = Mathf.Clamp(yZero, yBottom, yTop);

            return Vector2.Distance(new Vector2(xClosest, yClosest), position);
        }

        public static int BinarySearch<T>(T target, ref T[] array, Func<T, T, int> compare) {
            int left = 0;
            int right = array.Length - 1;
            int mid = (right + left) / 2;

            while (left <= right) {
                int compareResult = compare(array[mid], target);

                if (compareResult == 0)
                    return mid;
                else if (compareResult < 0)
                    right = mid - 1;
                else
                    left = mid + 1;

                mid = (right + left) / 2;
            }

            return right; //returns closest before it
        }
        public static float Frac(float value) { return value - (float)Math.Truncate(value); }

        public static Vector3 AsVector(int3 vector) {
            return new Vector3(vector.x, vector.y, vector.z);
        }

        public static Vector2 AsVector(int2 vector) {
            return new Vector2(vector.x, vector.y);
        }

        public static Bounds TransformBounds(Transform transform, Bounds boundsOS) {
            var center = transform.TransformPoint(boundsOS.center);

            var size = boundsOS.size;
            var axisX = transform.TransformVector(size.x, 0, 0);
            var axisY = transform.TransformVector(0, size.y, 0);
            var axisZ = transform.TransformVector(0, 0, size.z);

            size.x = Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x);
            size.y = Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y);
            size.z = Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z);

            return new Bounds(center, size);
        }

        public static void OrderedLoop(int radius, Action<int3> cb) {
            static void SignDecode(int count, Action<int3> cb) {
                for (int i = 0; i < count; i++) {
                    cb.Invoke(new int3(
                        (i & 0x1) == 0 ? -1 : 1,
                        (i & 0x2) == 0 ? -1 : 1,
                        (i & 0x4) == 0 ? -1 : 1
                    ));
                }
            }

            static void PlaceDecodeSmall(int3 v, Action<int3> cb) {
                cb(new(v.x, v.y, v.z));
                cb(new(v.y, v.x, v.z));
                cb(new(v.y, v.z, v.x));
            }

            static void PlaceDecodeLarge(int3 v, Action<int3> cb) {
                cb(new(v.x, v.y, v.z));
                cb(new(v.x, v.z, v.y));
                cb(new(v.y, v.x, v.z));
                cb(new(v.z, v.x, v.y));
                cb(new(v.y, v.z, v.x));
                cb(new(v.z, v.y, v.x));
            }

            for (int majorAxis = 0; majorAxis <= radius; majorAxis++) {
                for (int intAxis = 0; intAxis <= majorAxis; intAxis++) {
                    for (int minorAxis = 0; minorAxis <= intAxis; minorAxis++) {
                        if (majorAxis == minorAxis) {
                            if (majorAxis == 0) cb(int3.zero);
                            else SignDecode(8, s => cb(s * new int3(majorAxis, intAxis, minorAxis)));
                        } else if (majorAxis == intAxis) {
                            if (minorAxis == 0) SignDecode(4, s => PlaceDecodeSmall(new int3(minorAxis, s.x * majorAxis, s.y * intAxis), cb));
                            else SignDecode(8, s => PlaceDecodeSmall(new int3(s.x * minorAxis, s.y * majorAxis, s.z * intAxis), cb));
                        } else if (intAxis == minorAxis) {
                            if (minorAxis == 0) SignDecode(2, s => PlaceDecodeSmall(new int3(majorAxis * s.x, intAxis, minorAxis), cb));
                            else SignDecode(8, s => PlaceDecodeSmall(new int3(s.x * majorAxis, s.y * intAxis, s.z * minorAxis), cb));
                        } else {
                            if (minorAxis == 0) SignDecode(4, s => PlaceDecodeLarge(new int3(minorAxis, s.x * intAxis, s.y * majorAxis), cb));
                            else SignDecode(8, s => PlaceDecodeLarge(new int3(s.x * minorAxis, s.y * intAxis, s.z * majorAxis), cb));
                        }
                    }
                }
            }
        }
    }
    
    public class TwoWayDict<TKey, TValue>
    {
        private readonly Dictionary<TKey, TValue> keyToValue = new();
        private readonly Dictionary<TValue, TKey> valueToKey = new();

        // Regular indexer: access by key
        public TValue this[TKey key] {
            get => keyToValue[key];
            set {
                if (keyToValue.TryGetValue(key, out var oldValue))
                    valueToKey.Remove(oldValue);

                keyToValue[key] = value;
                valueToKey[value] = key;
            }
        }
        //ToDo: Add reverse indexer
        public void Add(TKey key, TValue value)
        {
            if (keyToValue.ContainsKey(key))
                throw new ArgumentException($"Duplicate key: {key}");
            if (valueToKey.ContainsKey(value))
                throw new ArgumentException($"Duplicate value: {value}");

            keyToValue[key] = value;
            valueToKey[value] = key;
        }

        public TValue GetByKey(TKey key)
        {
            if (!keyToValue.TryGetValue(key, out var value))
                throw new KeyNotFoundException($"Key not found: {key}");
            return value;
        }

        public TKey GetByValue(TValue value)
        {
            if (!valueToKey.TryGetValue(value, out var key))
                throw new KeyNotFoundException($"Value not found: {value}");
            return key;
        }

        public bool TryGetByKey(TKey key, out TValue value) => keyToValue.TryGetValue(key, out value);
        public bool TryGetByValue(TValue value, out TKey key) => valueToKey.TryGetValue(value, out key);
        public bool ContainsKey(TKey key) => keyToValue.ContainsKey(key);
        public bool ContainsValue(TValue value) => valueToKey.ContainsKey(value);

        public bool RemoveByKey(TKey key)
        {
            if (keyToValue.TryGetValue(key, out var value))
            {
                keyToValue.Remove(key);
                valueToKey.Remove(value);
                return true;
            }
            return false;
        }

        public bool RemoveByValue(TValue value)
        {
            if (valueToKey.TryGetValue(value, out var key))
            {
                valueToKey.Remove(value);
                keyToValue.Remove(key);
                return true;
            }
            return false;
        }

        public void Clear()
        {
            keyToValue.Clear();
            valueToKey.Clear();
        }

        public int Count => keyToValue.Count;
    }

    public enum priorities {
        planning = 0,
        structure = 1,
        generation = 2,
        assignment = 3,
        mesh = 4,
        propogation = 5,
        geoshader = 6,
    };
}