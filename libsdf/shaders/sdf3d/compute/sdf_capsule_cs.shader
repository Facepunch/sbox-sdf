HEADER
{
	DevShader = true;
	Description = "Capsule SDF Compute Shader";
}

MODES
{
	Default()
}

COMMON
{
	#include "common/shared.hlsl"
}

CS
{
	#include "shaders/sdf3d/compute/shared.hlsl"
    
	float3 g_vPointA < Attribute( "PointA" ); >;
	float3 g_vPointB < Attribute( "PointB" ); >;
	float3 g_vAlong < Attribute( "Along" ); >;
	float g_fRadius < Attribute( "Radius" ); >;
	
	float Sample( float3 vPos )
	{
        float t = dot( vPos - g_vPointA, g_vAlong );
        float3 closest = lerp( g_vPointA, g_vPointB, t );

        return length(vPos - closest) - g_fRadius;
	}
}
