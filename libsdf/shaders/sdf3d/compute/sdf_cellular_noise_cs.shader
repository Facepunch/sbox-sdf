HEADER
{
	DevShader = true;
	Description = "Cellular Worley Noise SDF Compute Shader";
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

	int g_iSeed < Attribute( "Seed" ); >;
	float3 g_vCellSize < Attribute( "CellSize" ); >;
	float g_fDistanceOffset < Attribute( "DistanceOffset" ); >;
	
	float Sample( float3 vPos )
	{
		return 0.0;
	}
}
