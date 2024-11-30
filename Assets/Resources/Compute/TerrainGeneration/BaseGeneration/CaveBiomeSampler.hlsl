struct RNode4{
    float minCorner[4];
    float maxCorner[4];
    int biome;
};

StructuredBuffer<RNode4> _BiomeCaveTree;

bool contains(RNode4 node, float mapData[4]){
    for (int i = 0; i < 4; i++)
    {
        if (mapData[i] < node.minCorner[i] || mapData[i] > node.maxCorner[i])
            return false;
    }
    return true;
}

//No recursion
int GetBiome(float mapData[4], int offset){

    uint curInd = 1;
    uint checkedChild = 0; //0<-unvisited, 1<-visited first child, 2 <- fully visited

    //    if not found     if biome is found
    while(curInd > 0 && _BiomeCaveTree[curInd - 1 + offset].biome == -1){

        if(checkedChild == 2){
            checkedChild = curInd % 2 + 1;
            curInd = floor(curInd / 2);
        }
        else if(checkedChild == 0){
            if(contains(_BiomeCaveTree[curInd * 2 - 1 + offset], mapData)){
                curInd = curInd * 2;
                checkedChild = 0;
            }
            else
                checkedChild = 1;
        }
        else{
            if(contains(_BiomeCaveTree[curInd * 2 + offset], mapData)){
                curInd = curInd * 2 + 1;
                checkedChild = 0;
            }
            else
                checkedChild = 2;
        }
    };
    return curInd <= 0 ? offset : _BiomeCaveTree[(curInd - 1) + offset].biome;
}


