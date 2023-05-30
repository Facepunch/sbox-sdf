using System;
using System.Collections.Generic;

namespace Sandbox.Sdf;

/// <summary>
/// Main entity for creating a 3D surface that can be added to and subtracted from.
/// Multiple volumes can be added to this entity with different materials.
/// </summary>
public partial class Sdf3DWorld : SdfWorld<Sdf3DWorld, Sdf3DChunk, Sdf3DVolume, (int X, int Y, int Z), Sdf3DArray, ISdf3D>
{
	protected override IEnumerable<(int X, int Y, int Z)> GetAffectedChunks<T>( T sdf, WorldQuality quality )
	{
		var bounds = sdf.Bounds;
		var unitSize = quality.UnitSize;

		var min = (bounds.Mins - quality.MaxDistance - unitSize) / quality.ChunkSize;
		var max = (bounds.Maxs + quality.MaxDistance + unitSize) / quality.ChunkSize;

		var minX = (int) MathF.Floor( min.x );
		var minY = (int) MathF.Floor( min.y );
		var minZ = (int) MathF.Floor( min.z );

		var maxX = (int) MathF.Ceiling( max.x );
		var maxY = (int) MathF.Ceiling( max.y );
		var maxZ = (int) MathF.Ceiling( max.z );

		for ( var z = minZ; z < maxZ; ++z )
		for ( var y = minY; y < maxY; ++y )
		for ( var x = minX; x < maxX; ++x )
		{
			yield return (x, y, z);
		}
	}
}
