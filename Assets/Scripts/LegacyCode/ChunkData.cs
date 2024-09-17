using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

public abstract class ChunkData
{
    public virtual void UpdateVisibility(int3 CCoord, float chunksVisibleInViewDistance){
        //Check if should be disabled
    }
    public virtual void DestroyChunk(){
        //Destroy Chunk
    }
}
