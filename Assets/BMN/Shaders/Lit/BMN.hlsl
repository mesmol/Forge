#ifndef MYHLSLINCLUDE_INCLUDED
#define MYHLSLINCLUDE_INCLUDED
#include "NoiseShader\HLSL\ClassicNoise3D.hlsl"
#include "NoiseShader\HLSL\WhiteNoise3D.hlsl"
#include "NoiseShader\HLSL\NoiseUtils.hlsl"
#include "NoiseShader\HLSL\SimplexNoise3D.hlsl"



float Noise3D_float(float3 p,float scale){

    return  snoise_grad(p*scale);
}


float fbm_float(float3 p, int octaves,float scale,float amplitude)
{
    float value = 0.0;
    float e = 3.0;
    for (int i = 0; i < octaves; ++ i)
    {
        value += amplitude * Noise3D_float(p,scale); 
        p = p * e; 
        amplitude *= 0.5; 
        e *= 0.95;
    }
    return value ;
}

void BMN_float(float3 p, int octaves,float scale,float amplitude,float speed,out float Out)
{
    Out = fbm_float(p+fbm_float(p + speed, octaves,scale,amplitude), octaves,scale,amplitude);
}
void BMN3_float(float3 p, int octaves,float scale,float amplitude,float speed,out float Out)
{
    Out = fbm_float(p+fbm_float(p + fbm_float(p + speed, octaves,scale,amplitude) + speed, octaves,scale,amplitude), octaves,scale,amplitude);
}
#endif