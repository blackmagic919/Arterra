using System.Collections.Generic;
using UnityEngine;
using WorldConfig;

//Strategy: Sort to find the buffers from most space to least space
//Reserve the first buffer as an overflow buffer only if its size is greater than 
//BlockAllocationSize * OverflowHandlerSizeReq; otherwise create new buffer and designate it
//the overflow buffer. Then we put all new allocations into the second largest buffer and dynamically
//change our estimate of what is the second largest buffer 
public class MemoryOccupancyBalancer {
    public class Settings {
        public int BlockAllocationSize;
        //Percentage of BlockAllocationSize
        public float OverflowHandlerSizeReq;
    }

    private List<WorldConfig.Quality.MemoryBufferHandler> MemoryBlocks;
    private Settings settings;

    public void Allocate() {
        
    }

}

