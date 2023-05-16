//=========================================================================================================================
// Optional
//=========================================================================================================================
HEADER
{
	Description = "Template Shader for S&box";
}

//=========================================================================================================================
// Optional
//=========================================================================================================================
FEATURES
{
    #include "common/features.hlsl"
}

//=========================================================================================================================
COMMON
{
	#include "common/shared.hlsl"
}

//=========================================================================================================================

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

//=========================================================================================================================

struct PixelInput
{
	#include "common/pixelinput.hlsl"

	float3 vPositionOs : TEXCOORD15;
};

//=========================================================================================================================

VS
{
	#include "common/vertex.hlsl"
	//
	// Main
	//
	PixelInput MainVs( INSTANCED_SHADER_PARAMS( VertexInput i ) )
	{
		PixelInput o = ProcessVertex( i );
		
		o.vPositionOs = i.vPositionOs;

		return FinalizeVertex( o );
	}
}

//=========================================================================================================================

PS
{
    #include "common/pixel.hlsl"

	CreateTexture2D( g_tTestLayer ) <
		Attribute( "TestLayer" );
		SrgbRead( false );
		Filter( BILINEAR );
		AddressU( CLAMP );
		AddressV( CLAMP );
		Default( 1.0 );
	>;

	float4 g_flTestLayerRect < Default4( 0.0, 0.0, 1.0, 1.0 ); Attribute( "TestLayerRect" ); >;

	//
	// Main
	//
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = GatherMaterial( i );
		
		float signedDist = Tex2D( g_tTestLayer, i.vPositionOs.xy * g_flTestLayerRect.zw + g_flTestLayerRect.xy ).r - 0.5;

		m.Albedo.rgb *= 1.0 - clamp( (signedDist + 0.125) * 8.0, 0.0, 1.0 );

		ShadingModelValveStandard sm;
		return FinalizePixelMaterial( i, m, sm );
	}
}
