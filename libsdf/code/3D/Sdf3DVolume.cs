using System;
using System.Collections.Generic;

namespace Sandbox.Sdf;

/// <summary>
/// Controls the appearance and physical properties of a volume in a <see cref="Sdf3DWorld"/>.
/// </summary>
[GameResource( "SDF 3D Volume", "sdfvol", $"Properties of a volume in a Sdf3DWorld", Icon = "view_in_ar" )]
public class Sdf3DVolume : SdfResource<Sdf3DVolume>
{
	/// <summary>
	/// Material used to render this volume.
	/// </summary>
	[HideIf( nameof( IsTextureSourceOnly ), true )]
	public Material Material { get; set; }

	internal override WorldQuality GetQualityFromPreset( WorldQualityPreset preset )
	{
		switch ( preset )
		{
			case WorldQualityPreset.Low:
				return new( 8, 256f, 32f );

			case WorldQualityPreset.Medium:
				return new( 16, 256f, 64f );

			case WorldQualityPreset.High:
				return new( 32, 256f, 96f );

			case WorldQualityPreset.Extreme:
				return new( 16, 128f, 32f );

			default:
				throw new NotImplementedException();
		}
	}
}
