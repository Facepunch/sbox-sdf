using System.Collections.Generic;

namespace Sandbox.Sdf;

/// <summary>
/// References a volume that will be used as a texture when rendering.
/// </summary>
public class VolumeTexture
{
	/// <summary>
	/// Material attribute name to set for the materials used by this volume.
	/// </summary>
	public string TargetAttribute { get; set; }

	/// <summary>
	/// Source volume that will provide the texture. The texture will have a single channel,
	/// with 0 representing -<see cref="Sdf3DVolume.MaxDistance"/> of the source layer,
	/// and 1 representing +<see cref="Sdf3DVolume.MaxDistance"/>.
	/// </summary>
	public Sdf3DVolume SourceVolume { get; set; }
}

/// <summary>
/// Controls the appearance and physical properties of a volume in a <see cref="Sdf3DWorld"/>.
/// </summary>
[GameResource( "SDF 3D Volume", "sdfvol", $"Properties of a volume in a {nameof( Sdf3DWorld )}", Icon = "view_in_ar" )]
public class Sdf3DVolume : SdfResource
{
	/// <summary>
	/// Material used to render this volume.
	/// </summary>
	[HideIf( nameof( IsTextureSourceOnly ), true )]
	public Material Material { get; set; }

	/// <summary>
	/// References to volumes that will be used as textures when rendering this volume.
	/// All referenced volumes must have the same chunk size as this volume.
	/// </summary>
	[HideIf( nameof( IsTextureSourceOnly ), true )]
	public List<VolumeTexture> VolumeTextures { get; set; }
}
