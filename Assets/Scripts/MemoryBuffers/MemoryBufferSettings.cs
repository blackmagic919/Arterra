using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

[CreateAssetMenu(menuName = "BufferMemory/Memory Heap")]
public class MemoryBufferSettings : ScriptableObject
{
    public int _BufferSize4Bytes = (int)5E7; //200 mB
    public int _MaxHeapSize = (int)5E3; //5k references
    public int _MaxAddressSize = (int)5E4; //50k addresses
}
