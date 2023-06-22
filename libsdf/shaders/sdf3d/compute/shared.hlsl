
RWTexture3D<float> g_tOutput < Attribute( "OutputTexture" ); >;

float4x4 g_mTransform < Attribute( "Transform" ); >;

float Sample( float3 vPos );

[numthreads(8, 8, 1)] 
void MainCs( uint uGroupIndex : SV_GroupIndex, uint3 vThreadId : SV_DispatchThreadID )
{
    g_tOutput[vThreadId.xyz] = Sample( mul( g_mTransform, float4( vThreadId.xyz, 1.0 ) ).xyz );
}
