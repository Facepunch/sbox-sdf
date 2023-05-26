using System.Collections.Generic;

namespace Sandbox.Sdf;

/// <summary>
/// References a layer that will be used as a texture when rendering.
/// </summary>
public class LayerTexture
{
	/// <summary>
	/// Material attribute name to set for the materials used by this layer.
	/// </summary>
	public string TargetAttribute { get; set; }

	/// <summary>
	/// Source layer that will provide the texture. The texture will have a single channel,
	/// with 0 representing -<see cref="Sdf2DLayer.MaxDistance"/> of the source layer,
	/// and 1 representing +<see cref="Sdf2DLayer.MaxDistance"/>.
	/// </summary>
	public Sdf2DLayer SourceLayer { get; set; }
}

/// <summary>
/// Controls the appearance and physical properties of a layer in a <see cref="Sdf2DWorld"/>.
/// </summary>
[GameResource( "SDF 2D Layer", "sdflayer", $"Properties of a layer in a {nameof( Sdf2DWorld )}", Icon = "layers" )]
public class Sdf2DLayer : SdfResource
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

	/// <summary>
	/// References to layers that will be used as textures when rendering this layer.
	/// All referenced layers must have the same chunk size as this layer.
	/// </summary>
	[HideIf( nameof( IsTextureSourceOnly ), true )]
	public List<LayerTexture> LayerTextures { get; set; }
}
