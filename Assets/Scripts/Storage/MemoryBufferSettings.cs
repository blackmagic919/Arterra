using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

[CreateAssetMenu(menuName = "BufferMemory/Memory Heap")]
public class MemoryBufferSettings : ScriptableObject
{
    [UISetting(Message = "Amount of Gross GPU Memory Allocated. Please be Aware of Device Capabilities")]
    public int _BufferSize4Bytes = (int)5E7; //200 mB
    public int _MaxHeapSize = (int)5E4; //50k references
    public int _MaxAddressSize = (int)5E4; //50k addresses
}
