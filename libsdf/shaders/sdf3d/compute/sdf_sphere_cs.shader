HEADER
{
	DevShader = true;
	Description = "Sphere SDF Compute Shader";
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
    
	float3 g_vCenter < Attribute( "Center" ); >;
	float g_fRadius < Attribute( "Radius" ); >;
	
	float Sample( float3 vPos )
	{
		return length( vPos - g_vCenter ) - g_fRadius;
	}
}
