using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Utils { 
    public static class CustomUtility
    {
        static int[] xThetaRot = new int[] { 0, 2, 0, 2 };
        static int[] xThetaDir = new int[] { 1, -1, -1, 1 };
        static int[] zThetaRot = new int[] { 2, 0, 2, 0 };
        static int[] zThetaDir = new int[] { 1, 1, -1, -1 };

        public static int irregularIndexFromCoord(int x, int y, int z, int sizeX, int sizeY)
        {
            return x + y * sizeX + z * sizeX * sizeY;
        }

        public static int indexFromCoord(int x, int y, int z, int numPointsAxis)
        {
            return x * numPointsAxis * numPointsAxis + y * numPointsAxis + z;
        }

        public static int indexFromCoord2D(int x, int y, int numPointsAxis)
        {
            return x * numPointsAxis + y;
        }

        public static int indexFromCoordV(Vector3 coord, int numPointsPerAxis)
        {

            return (int)coord.x * numPointsPerAxis * numPointsPerAxis + (int)coord.y * numPointsPerAxis + (int)coord.z;
        }

        public static Vector3 FloorVector(Vector3 vector)
        {
            return new Vector3(Mathf.FloorToInt(vector.x), Mathf.FloorToInt(vector.y), Mathf.FloorToInt(vector.z));
        }

        public static float GetArea(Vector2 a, Vector2 b)
        {
            float width = Mathf.Abs(a.x - b.x);
            float length = Mathf.Abs(a.y - b.y);

            return width * length;
        }

        public static T[,,] InitializeArray3D<T>(T value, uint sizeX, uint sizeY, uint sizeZ)
        {
            T[,,] array = new T[sizeX, sizeY, sizeZ];

            for (int x = 0; x < sizeX; x++)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    for (int z = 0; z < sizeZ; z++)
                    {
                        array[x, y, z] = value;
                    }
                }
            }
            return array;
        }

        public static T[] SortArray<T>(T[] unsorted, Func<object, object, int> compare)
        {
            List<T> temp = new List<T>(unsorted);
            temp.Sort((a, b) => compare(a, b));
            return temp.ToArray();
        }

        public static int[] RotateAxis(int theta90, int phi90)
        {
            int[] rotateDir = new int[]{ 1, 2, 3 };
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

        public static float GetElement(Vector3 vector, int direction)
        {
            if (direction == 0)
                return vector.x;
            if (direction == 1)
                return vector.y;
            if (direction == 2)
                return vector.z;
            return -1.0f;
        }

        public static float GetDistanceToSquare(Vector2 center, float sideLength, Vector2 position)
        {
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

        public static int BinarySearch<T>(T target, ref T[] array, Func<T, T, int> compare)
        {
            int left = 0;
            int right = array.Length - 1;
            int mid = (right + left) / 2;

            while (left <= right)
            {
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
    }


    public enum priorities
    {
        planning = 0,
        structure = 1,
        generation = 2,
    };
}