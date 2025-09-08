#ifndef HEAP_OPERATIONS
#define HEAP_OPERATIONS

const static uint InitBlockSize = 3;

RWStructuredBuffer<uint> _SourceMemory;
RWStructuredBuffer<uint2> _Heap;

void Swap(uint a, uint b){
    uint2 temp = _Heap[a];
    _Heap[a] = _Heap[b];
    _Heap[b] = temp;

    //Update Linked List for positions
    _SourceMemory[_Heap[a].x - 2] = a;
    _SourceMemory[_Heap[b].x - 2] = b;
}

void SinkBlock(uint node, uint bStart){
    while(2*node <= _Heap[bStart].x){
        uint cur = node + bStart;
        uint maxChild = 2*node + bStart;
        uint sibling = 2*node + 1 + bStart;
        if(2*node + 1 <= _Heap[0].x && _Heap[sibling].y > _Heap[maxChild].y)
            maxChild = sibling;

        if(_Heap[cur].y >= _Heap[maxChild].y)
            break;

        Swap(cur, maxChild);
        node = maxChild - bStart;
    }
}

void SwimBlock(uint node, uint bStart){
    while(node > 1){
        uint cur = node + bStart;
        uint parent = (node >> 1) + bStart; //Better to be explicit

        if(_Heap[parent].y >= _Heap[cur].y)
            break;

        Swap(cur, parent);
        node = parent - bStart;
    }
}

void RemoveBlock(uint node, uint bStart){
    Swap(node, _Heap[bStart].x);
    _Heap[bStart].x--;

    //Sort the last node at this new position
    //If it does swim, the new block at this position won't sink
    //If it doesn't swim, the same node will be at this position
    SwimBlock(node, bStart);
    SinkBlock(node, bStart);
}

uint PrevBlockIndex(uint blockIndex){
    uint ret = 0;

    if(blockIndex > 2){ //Not Head of LinkedList
        uint pBlockEnd = blockIndex - InitBlockSize;
        uint pBlockSize = _SourceMemory[pBlockEnd];
        uint pBlockIndex = pBlockEnd - pBlockSize;

        uint pBlockHeapIndex = _SourceMemory[pBlockIndex - 2];

        if(pBlockHeapIndex != 0) //Is not allocated 
            ret = pBlockIndex;
    }
    return ret;
}

uint NextBlockIndex(uint blockIndex){
    uint ret = 0;

    //It's not possible for an allocated block to be the tail of the LL
    uint nBlockIndex = blockIndex + _SourceMemory[blockIndex-1] + InitBlockSize;
    uint nBlockHeapIndex = _SourceMemory[nBlockIndex - 2];

    if(nBlockHeapIndex != 0) //Is not allocated
        ret = nBlockIndex;

    return ret;
}


//Allocate at smallest(not guaranteed) block to prevent memory fracture
uint FindSmallestBlock(uint size, uint bStart){
    uint node = 1;
    while(2*node <= _Heap[bStart].x){
        uint maxChild = 2*node + bStart;
        uint sibling = 2*node + 1 + bStart;
        if(2*node + 1 <= _Heap[bStart].x && _Heap[sibling].y > _Heap[maxChild].y)
            maxChild = sibling;
        
        if(size > _Heap[maxChild].y)
            break;
        
        node = maxChild - bStart;
    }
    return node + bStart;
}
#endif