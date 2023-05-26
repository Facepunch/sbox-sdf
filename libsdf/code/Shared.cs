using System.Text.Json.Serialization;
using System;

namespace Sandbox.Sdf;

/// <summary>
/// Quality settings for <see cref="Sdf2DLayer"/>.
/// </summary>
public enum WorldQualityPreset
{
	/// <summary>
	/// Cheap and cheerful, suitable for frequent (per-frame) edits.
	/// </summary>
	Low,

	/// <summary>
	/// Recommended quality for most cases.
	/// </summary>
	Medium,

	/// <summary>
	/// More expensive to update and network, but a much smoother result.
	/// </summary>
	High,

	/// <summary>
	/// Only use this for small, detailed objects!
	/// </summary>
	Extreme,

	/// <summary>
	/// Manually tweak quality parameters.
	/// </summary>
	Custom = -1
}
	
internal record struct WorldQuality( int ChunkResolution, float ChunkSize, float MaxDistance )
{
	public float UnitSize => ChunkSize / ChunkResolution;

	public static WorldQuality Read( ref NetRead net )
	{
		return new WorldQuality( net.Read<int>(),
			net.Read<float>(),
			net.Read<float>() );
	}

	public void Write( NetWrite net )
	{
		net.Write( ChunkResolution );
		net.Write( ChunkSize );
		net.Write( MaxDistance );
	}
}

public abstract class SdfResource : GameResource
{
	private static char[] SplitChars { get; } = new[] { ' ' };

	/// <summary>
	/// If true, this resource is only used as a texture source by other resources.
	/// This will disable collision shapes and render mesh generation for this resource.
	/// </summary>
	public bool IsTextureSourceOnly { get; set; }

	/// <summary>
	/// Tags that physics shapes created by this resource should have, separated by spaces.
	/// If empty, no physics shapes will be created.
	/// </summary>
	[Editor( "tags" )]
	[HideIf( nameof( IsTextureSourceOnly ), true )]
	public string CollisionTags { get; set; } = "solid";

	[HideInEditor]
	[JsonIgnore]
	public string[] SplitCollisionTags => IsTextureSourceOnly
		? Array.Empty<string>()
		: CollisionTags?.Split( SplitChars, StringSplitOptions.RemoveEmptyEntries ) ?? Array.Empty<string>();

	/// <summary>
	/// Controls mesh visual quality, affecting performance and networking costs.
	/// </summary>
	public WorldQualityPreset QualityLevel { get; set; } = WorldQualityPreset.Medium;

	/// <summary>
	/// How many rows / columns of samples are stored per chunk.
	/// Higher means more needs to be sent over the network, and more work for the mesh generator.
	/// Medium quality is 16.
	/// </summary>
	[ShowIf( nameof( QualityLevel ), WorldQualityPreset.Custom )]
	public int ChunkResolution { get; set; } = 16;

	/// <summary>
	/// How wide / tall a chunk is in world space. If you'll always make small
	/// edits to this layer, you can reduce this to add detail.
	/// Medium quality is 256.
	/// </summary>
	[ShowIf( nameof( QualityLevel ), WorldQualityPreset.Custom )]
	public float ChunkSize { get; set; } = 256f;

	/// <summary>
	/// Largest absolute value stored in a chunk's SDF.
	/// Higher means more samples are written to when doing modifications.
	/// I'd arbitrarily recommend ChunkSize / ChunkResolution * 4.
	/// </summary>
	[ShowIf( nameof( QualityLevel ), WorldQualityPreset.Custom )]
	public float MaxDistance { get; set; } = 64f;

	[HideInEditor]
	[JsonIgnore]
	internal WorldQuality Quality => QualityLevel switch
	{
		WorldQualityPreset.Custom => new WorldQuality( ChunkResolution, ChunkSize, MaxDistance ),
		_ => Sdf2DWorld.QualityFromPreset( QualityLevel )
	};
}
