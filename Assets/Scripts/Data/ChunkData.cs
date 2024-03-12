using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class ChunkData
{
    public virtual void UpdateVisibility(Vector3 CCoord, float chunksVisibleInViewDistance){
        //Check if should be disabled
    }
    public virtual void DestroyChunk(){
        //Destroy Chunk
    }
}
