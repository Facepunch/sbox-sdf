using System;
using System.Collections.Generic;

namespace Sandbox.Sdf;

/// <summary>
/// Controls the appearance and physical properties of a layer in a <see cref="Sdf2DWorld"/>.
/// </summary>
[GameResource( "SDF 2D Layer", "sdflayer", $"Properties of a layer in a {nameof( Sdf2DWorld )}", Icon = "layers" )]
public class Sdf2DLayer : SdfResource<Sdf2DLayer>
{
	/// <summary>
	/// How wide this layer is in the z-axis. This can help prevent
	/// z-fighting for overlapping layers.
	/// </summary>
	[HideIf( nameof( IsTextureSourceOnly ), true )]
	public float Depth { get; set; } = 64f;

	/// <summary>
	/// How far to offset this layer in the z-axis.
	/// Useful for things like background / foreground layers.
	/// </summary>
	[HideIf( nameof( IsTextureSourceOnly ), true )]
	public float Offset { get; set; } = 0f;

	/// <summary>
	/// How wide a single tile of the texture should be.
	/// </summary>
	[HideIf( nameof( IsTextureSourceOnly ), true )]
	public float TexCoordSize { get; set; } = 256f;

	/// <summary>
	/// Material used by the front face of this layer.
	/// </summary>
	[HideIf( nameof( IsTextureSourceOnly ), true )]
	public Material FrontFaceMaterial { get; set; }

	/// <summary>
	/// Material used by the back face of this layer.
	/// </summary>
	[HideIf( nameof( IsTextureSourceOnly ), true )]
	public Material BackFaceMaterial { get; set; }

	/// <summary>
	/// Material used by the cut face connecting the front and
	/// back of this layer.
	/// </summary>
	[HideIf( nameof( IsTextureSourceOnly ), true )]
	public Material CutFaceMaterial { get; set; }

	internal override WorldQuality GetQualityFromPreset( WorldQualityPreset preset )
	{
		switch ( preset )
		{
			case WorldQualityPreset.Low:
				return new( 8, 256f, 48f );

			case WorldQualityPreset.Medium:
				return new( 16, 256f, 24f );

			case WorldQualityPreset.High:
				return new( 32, 256f, 12f );

			case WorldQualityPreset.Extreme:
				return new( 32, 128f, 6f );

			default:
				throw new NotImplementedException();
		}
	}
}
