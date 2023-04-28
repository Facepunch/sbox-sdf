//=========================================================================================================================
// Optional
//=========================================================================================================================
HEADER
{
	CompileTargets = ( IS_SM_50 && ( PC || VULKAN ) );
	Description = "Shader for geometry generated by Voxels.VoxelMeshWriter";
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

	float4 vColor : COLOR0 < Semantic( Color ); >;
};

//=========================================================================================================================

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

//=========================================================================================================================

VS
{
	#include "common/vertex.hlsl"
	//
	// Main
	//
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );

		o.vVertexColor.rgb = SrgbGammaToLinear( i.vColor.rgb );
		o.vVertexColor.a =  i.vColor.a;

		return FinalizeVertex( o );
	}
}

//=========================================================================================================================

PS
{
    #include "common/pixel.hlsl"

	float3 unpackNormal(float4 n) {
		return normalize(n.xyz * 2 - 1);
	}

	float3 rnmBlendUnpacked(float3 n1, float3 n2)
	{
		n1 += float3( 0,  0, 1);
		n2 *= float3(-1, -1, 1);
		return n1*dot(n1, n2)/n1.z - n2;
	}

	//
	// Main
	//
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 worldPos = (i.vPositionWithOffsetWs.xyz + g_vCameraPositionWs.xyz) / 128.0;

		float2 uvX = worldPos.zy;
        float2 uvY = worldPos.xz;
        float2 uvZ = worldPos.xy;

		float3 triblend = saturate(pow(i.vNormalWs.xyz, 4));
        triblend /= max(dot(triblend, half3(1,1,1)), 0.0001);

		half3 absVertNormal = abs(i.vNormalWs);
		half3 axisSign = i.vNormalWs < 0 ? -1 : 1;

		uvX.x *= axisSign.x;
		uvY.x *= axisSign.y;
		uvZ.x *= -axisSign.z;

		float4 colX = Tex2DS( g_tColor, TextureFiltering, uvX );
        float4 colY = Tex2DS( g_tColor, TextureFiltering, uvY );
        float4 colZ = Tex2DS( g_tColor, TextureFiltering, uvZ );
        float4 col = colX * triblend.x + colY * triblend.y + colZ * triblend.z;

		col.rgb *= i.vVertexColor.rgb;

		float3 tnormalX = unpackNormal(Tex2DS( g_tNormal, TextureFiltering, uvX ));
        float3 tnormalY = unpackNormal(Tex2DS( g_tNormal, TextureFiltering, uvY ));
        float3 tnormalZ = unpackNormal(Tex2DS( g_tNormal, TextureFiltering, uvZ ));

		tnormalX.x *= axisSign.x;
		tnormalY.x *= axisSign.y;
		tnormalZ.x *= -axisSign.z;

		tnormalX = half3(tnormalX.xy + i.vNormalWs.zy, i.vNormalWs.x);
		tnormalY = half3(tnormalY.xy + i.vNormalWs.xz, i.vNormalWs.y);
		tnormalZ = half3(tnormalZ.xy + i.vNormalWs.xy, i.vNormalWs.z);

		// Triblend normals and add to world normal
		float3 norm = normalize(
			tnormalX.zyx * triblend.x +
			tnormalY.xzy * triblend.y +
			tnormalZ.xyz * triblend.z +
			i.vNormalWs
		);
		
		float4 rmaX = Tex2DS( g_tRma, TextureFiltering, uvX );
        float4 rmaY = Tex2DS( g_tRma, TextureFiltering, uvY );
        float4 rmaZ = Tex2DS( g_tRma, TextureFiltering, uvZ );
        float4 rma = rmaX * triblend.x + rmaY * triblend.y + rmaZ * triblend.z;

		Material m = ToMaterial( i, col, float4( 0.5, 0.5, 1.0, 1.0 ), rma, g_flTintColor  );

		m.Normal = norm;

		return FinalizePixelMaterial( i, m );
	}
}
