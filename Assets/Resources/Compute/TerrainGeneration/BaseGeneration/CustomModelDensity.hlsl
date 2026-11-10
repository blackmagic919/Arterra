#ifndef CUSTOM_MODEL_DENSITY
#define CUSTOM_MODEL_DENSITY
float CustomModelDensity(float density, float isoLevel) {
    return density >= isoLevel ? (1.0 - density) / (1.0 - isoLevel) * isoLevel : density;
}
#endif
