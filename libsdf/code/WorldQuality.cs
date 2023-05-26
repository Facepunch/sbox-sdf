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
	
public record struct WorldQuality( int ChunkResolution, float ChunkSize, float MaxDistance )
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
