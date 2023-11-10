using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;

namespace Sandbox.Sdf;

/// <summary>
/// Main entity for creating a set of 2D surfaces that can be added to and subtracted from.
/// Each surface is aligned to the same plane, but can have different offsets, depths, and materials.
/// </summary>
public partial class Sdf2DWorld : SdfWorld<Sdf2DWorld, Sdf2DChunk, Sdf2DLayer, (int X, int Y), Sdf2DArray, ISdf2D>
{
	internal Sdf2DWorld( ISdfWorldImpl impl )
		: base( impl )
	{

	}

	/// <summary>
	/// Create a <see cref="SceneObject"/>-based 2D surface that can be added to and subtracted from.
	/// This won't use any entities, so is safe for use in menus and editors.
	/// See <see cref="Sdf2DWorld()"/> for creating networked worlds with collision.
	/// </summary>
	public Sdf2DWorld( SceneWorld sceneWorld )
		: base( sceneWorld )
	{

	}

	/// <summary>
	/// Create an <see cref="Entity"/>-based 2D surface that can be added to and subtracted from.
	/// See <see cref="Sdf2DWorld(SceneWorld)"/> for creating worlds in menus and editors.
	/// </summary>
	public Sdf2DWorld()
	{

	}

	/// <inheritdoc />
	public override int Dimensions => 2;

	private (int MinX, int MinY, int MaxX, int MaxY) GetChunkRange( Rect bounds, WorldQuality quality )
	{
		var unitSize = quality.UnitSize;

		var min = (bounds.TopLeft - quality.MaxDistance - unitSize) / quality.ChunkSize;
		var max = (bounds.BottomRight + quality.MaxDistance + unitSize) / quality.ChunkSize;

		var minX = (int) MathF.Floor( min.x );
		var minY = (int) MathF.Floor( min.y );

		var maxX = (int) MathF.Ceiling( max.x );
		var maxY = (int) MathF.Ceiling( max.y );

		return (minX, minY, maxX, maxY);
	}

	/// <inheritdoc />
	protected override IEnumerable<(int X, int Y)> GetAffectedChunks<T>( T sdf, WorldQuality quality )
	{
		var (minX, minY, maxX, maxY) = GetChunkRange( sdf.Bounds, quality );

		for ( var y = minY; y < maxY; ++y )
		for ( var x = minX; x < maxX; ++x )
		{
			yield return (x, y);
		}
	}

	protected override bool AffectsChunk<T>( T sdf, WorldQuality quality, (int X, int Y) chunkKey )
	{
		var (minX, minY, maxX, maxY) = GetChunkRange( sdf.Bounds, quality );

		return chunkKey.X >= minX && chunkKey.X < maxX
			&& chunkKey.Y >= minY && chunkKey.Y < maxY;
	}
}
