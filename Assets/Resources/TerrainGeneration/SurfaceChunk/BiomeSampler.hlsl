struct RNode{
    float minCorner[6];
    float maxCorner[6];
    int biome;
};

StructuredBuffer<RNode> _BiomeRTree;

bool contains(RNode node, float mapData[6]){
    for (int i = 0; i < 6; i++)
    {
        if (mapData[i] < node.minCorner[i] || mapData[i] > node.maxCorner[i])
            return false;
    }
    return true;
}

//No recursion
int GetBiome(float mapData[6]){

    uint curInd = 1;
    uint checkedChild = 0; //0<-unvisited, 1<-visited first child, 2 <- fully visited

    //    if not found     if biome is found
    while(curInd > 0 && _BiomeRTree[curInd-1].biome == -1){

        if(checkedChild == 2){
            checkedChild = curInd % 2 + 1;
            curInd = floor(curInd / 2);
        }
        else if(checkedChild == 0){
            if(contains(_BiomeRTree[curInd * 2 - 1], mapData)){
                curInd = curInd * 2;
                checkedChild = 0;
            }
            else
                checkedChild = 1;
        }
        else{
            if(contains(_BiomeRTree[curInd * 2], mapData)){
                curInd = curInd * 2 + 1;
                checkedChild = 0;
            }
            else
                checkedChild = 2;
        }
    }

    return (curInd == 0) ? 0 : _BiomeRTree[curInd-1].biome;
}


