RWStructuredBuffer<uint> _SourceMemory;
//x->block address, y -> parent & rb, z -> leqChild, w -> geqChild
RWStructuredBuffer<uint4> _Heap;


uint Parent(uint node){ return _Heap[node].y & 0x7FFFFFFF; }
bool IsRed(uint node){ return (_Heap[node].y & 0x80000000) != 0; }
void SetParent(uint node, uint parent) { 
    if(node != 0) {
        _Heap[node].y = (_Heap[node].y & 0x80000000) 
            | (parent & 0x7FFFFFFF); 
}}


//Moves a to b and b to a while maintaining same relationships
void SwapPositions(uint a, uint b) {
    uint parent = Parent(a);
    if(_Heap[parent].w == a) {
        _Heap[parent].w = b;
    } else _Heap[parent].z = b;
    
    SetParent(_Heap[a].z, b);
    SetParent(_Heap[a].w, b);

    parent = Parent(b);
    if(_Heap[parent].w == b) {
        _Heap[parent].w = a;
    } else _Heap[parent].z = a;

    SetParent(_Heap[b].z, a);
    SetParent(_Heap[b].w, a);
    
    //Swap values
    uint4 temp = _Heap[a];
    _Heap[a] = _Heap[b];
    _Heap[b] = temp;

    //Update LL Block
    _SourceMemory[_Heap[a].x - 2] = a;
    _SourceMemory[_Heap[b].x - 2] = b;
}

void SwapChildren(uint node, uint newChild){
    uint parent = Parent(node);
    if(_Heap[parent].z == node) {
        _Heap[parent].z = newChild;
    } else _Heap[parent].w = newChild;
}


void PropogateBalance(uint offender){
    while(Parent(offender) != 0){
        uint lChild = _Heap[offender].z;
        uint rChild = _Heap[offender].w;
        if(IsRed(rChild) && IsRed(lChild)) {
            _Heap[rChild].y &= 0x7FFFFFFF;
            _Heap[lChild].y &= 0x7FFFFFFF;
            _Heap[offender].y |= 0x80000000;
            offender = Parent(offender);
            continue;
        } if(IsRed(rChild)){ //RotateLeft
            SwapChildren(offender, rChild);
            SetParent(rChild, Parent(offender));

            _Heap[offender].w = _Heap[rChild].z;
            SetParent(_Heap[rChild].z, offender);

            _Heap[rChild].z = offender;
            SetParent(offender, rChild);
        }  else if(IsRed(lChild) && IsRed(_Heap[lChild].z)) {
            SwapChildren(offender, lChild);
            SetParent(lChild, Parent(offender));

            _Heap[offender].z = _Heap[lChild].w;
            SetParent(_Heap[lChild].w, offender);

            _Heap[lChild].w = offender;
            SetParent(offender, lChild);

            _Heap[lChild].y |= 0x80000000; //red
            _Heap[offender].y &= 0x7FFFFFFF; //black
        } else break;
        offender = Parent(offender);
    } 
    //Root is always black
    if(Parent(offender) == 0) _Heap[offender].y &= 0x7FFFFFFF;
} 

//Remove Block can move around blocks
//Remove Block will also not read any allocation 
//information(e.g. size, heapPtr) about the node to be removed
void RemoveBlock(uint node){
    if(node > _Heap[0].x || node == 0) 
        return;
    //Find next successor
    uint successor = node;
    if(_Heap[node].z == _Heap[node].w) { //Both are 0
        successor = node;
    } else if(_Heap[node].z != 0 && _Heap[node].w != 0){
        uint successor = _Heap[node].w;
        uint next = _Heap[successor].z;
        while(next != 0){
            successor = next;
            next = _Heap[successor].z;
        }
    } else successor = _Heap[node].z != 0 ? _Heap[node].z : _Heap[node].w;
    
    //Swap to next successor
    _Heap[node].x = _Heap[successor].x;
    //Update LL-Block
    _SourceMemory[_Heap[node].x - 2] = node; 
    
    //Place other node to fill in hole
    SwapPositions(successor, _Heap[0].x);

    //Remove node
    successor = _Heap[0].x;
    uint child = _Heap[successor].z == 0 ? 
        _Heap[successor].w : _Heap[successor].z;
    SwapChildren(successor, child);
    SetParent(child, Parent(successor));
    _Heap[0].x--;

    if(child != 0) PropogateBalance(Parent(child));
}

//Add Block will not move around blocks
void AddBlock(uint blockAddr) {
    uint size = _SourceMemory[blockAddr-1];
    uint cur = 0; uint next = _Heap[0].z;
    //Find leaf node
    while(next != 0){
        cur = next;
        if(_SourceMemory[_Heap[next].x - 1] >= size) {
            next = _Heap[next].z;
        } else next = _Heap[next].w;
    }

    //Add child block
    _Heap[++_Heap[0].x] = uint4(
        blockAddr,
        cur | 0x80000000,
        0, 0
    );
    if(_SourceMemory[_Heap[cur].x - 1] >= size) {
        _Heap[cur].z = _Heap[0].x;
    } else _Heap[cur].w = _Heap[0].x;
    PropogateBalance(_Heap[0].x);
}

//Allocate at guaranteed smallest block to prevent memory fracture
//The use of this makes the exact focus of the heap ambiguous
uint FindSmallestBlock(uint size){
    uint node = 0; uint child = _Heap[0].z;
    while(child != 0){
        if(_SourceMemory[_Heap[child].x - 1] >= size){
            node = child;
            child = _Heap[node].z;
        } else child = _Heap[node].w;
    } return node;
}