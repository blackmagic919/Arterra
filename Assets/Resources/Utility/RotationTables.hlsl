static const float3x3 RotationLookupTable[4][3] = {
    {
        // Rotation matrix for theta = 0, phi = 0
        float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1),
        // Rotation matrix for theta = 0, phi = 90
        float3x3(1, 0, 0, 0, 0, 1, 0, -1, 0),
        // Rotation matrix for theta = 0, phi = 180
        float3x3(1, 0, 0, 0, -1, 0, 0, 0, -1)
    },
    {
        // Rotation matrix for theta = 90, phi = 0
        float3x3(0, 0, 1, 0, 1, 0, -1, 0, 0),
        // Rotation matrix for theta = 90, phi = 90
        float3x3(0, 0, 1, -1, 0, 0, 0, -1, 0),
        // Rotation matrix for theta = 90, phi = 180
        float3x3(0, 0, 1, 0, -1, 0, 1, 0, 0)
    },
    {
        // Rotation matrix for theta = 180, phi = 0
        float3x3(-1, 0, 0, 0, 1, 0, 0, 0, -1),
        // Rotation matrix for theta = 180, phi = 90
        float3x3(-1, 0, 0, 0, 0, -1, 0, -1, 0),
        // Rotation matrix for theta = 180, phi = 180
        float3x3(-1, 0, 0, 0, -1, 0, 0, 0, 1)
    },
    {
        // Rotation matrix for theta = 270, phi = 0
        float3x3(0, 0, -1, 0, 1, 0, 1, 0, 0),
        // Rotation matrix for theta = 270, phi = 90
        float3x3(0, 0, -1, 1, 0, 0, 0, -1, 0),
        // Rotation matrix for theta = 270, phi = 180
        float3x3(0, 0, -1, 0, -1, 0, -1, 0, 0)
    }
};