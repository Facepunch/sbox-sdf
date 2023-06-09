using System;
using System.Linq;

namespace Sandbox.Sdf.Noise
{
	public record struct CellularNoiseSdf3D( int Seed, Vector3 CellSize, float DistanceOffset, Vector3 InvCellSize ) : ISdf3D
	{
		public CellularNoiseSdf3D( int seed, Vector3 cellSize, float distanceOffset )
		: this( seed, cellSize, distanceOffset, new Vector3( 1f / cellSize.x, 1f / cellSize.y, 1f / cellSize.z ) )
		{
		}

		public BBox? Bounds => null;

		public float this[ Vector3 pos ]
		{
			get
			{
				var localPos = pos * InvCellSize;
				var cell = (
					X: (int)Math.Floor( localPos.x ),
					Y: (int)Math.Floor( localPos.y ),
					Z: (int)Math.Floor( localPos.z ));

				var cellPos = new Vector3( cell.X, cell.Y, cell.Z ) * CellSize;
				var cellLocalPos = pos - cellPos;

				var minDistSq = float.PositiveInfinity;

				foreach ( var offset in PointOffsets )
				{
					var feature = GetFeature( cell.X + offset.X, cell.Y + offset.Y, cell.Z + offset.Z ) + new Vector3( offset.X, offset.Y, offset.Z ) * CellSize;
					var distSq = (feature - cellLocalPos).LengthSquared;

					minDistSq = Math.Min( minDistSq, distSq );
				}

				return MathF.Sqrt( minDistSq ) - DistanceOffset;
			}
		}

		Vector3 GetFeature( int x, int y, int z )
		{
			var hashX = HashCode.Combine( Seed, x, y, z );
			var hashY = HashCode.Combine( z, Seed, x, y );
			var hashZ = HashCode.Combine( y, z, Seed, x );

			return new Vector3( (hashX & 0xffff) / 65536f, (hashY & 0xffff) / 65536f, (hashZ & 0xffff) / 65536f ) * CellSize;
		}
		
		[ThreadStatic] private static Vector3[] NearestPoints;

		private static (int X, int Y, int Z)[] PointOffsets { get; } = Enumerable.Range( -1, 3 ).SelectMany( z =>
			Enumerable.Range( -1, 3 ).SelectMany( y => Enumerable.Range( -1, 3 ).Select( x => (x, y, z) ) ) ).ToArray();

		/*
		void ISdf3D.SampleRange( BBox bounds, float[] output, (int X, int Y, int Z) outputSize )
		{
			var invCellSize = new Vector3( 1f / CellSize.x, 1f / CellSize.y, 1f / CellSize.z );
			var sampleSize = new Vector3( outputSize.X - 1, outputSize.Y - 1, outputSize.Z - 1 );
			var invSampleSize = new Vector3(
				bounds.Size.x / (outputSize.X - 1),
				bounds.Size.y / (outputSize.Y - 1),
				bounds.Size.z / (outputSize.Z - 1) );

			var minCell = (
				X: (int)MathF.Floor( bounds.Mins.x * invCellSize.x ),
				Y: (int)MathF.Floor( bounds.Mins.y * invCellSize.y ),
				Z: (int)MathF.Floor( bounds.Mins.z * invCellSize.z ));

			var maxCell = (
				X: (int) MathF.Ceiling( bounds.Maxs.x * invCellSize.x ),
				Y: (int) MathF.Ceiling( bounds.Maxs.y * invCellSize.y ),
				Z: (int) MathF.Ceiling( bounds.Maxs.z * invCellSize.z ));

			var offsets = PointOffsets;
			var nearest = NearestPoints ??= new Vector3[offsets.Length];

			for ( var cellZ = minCell.Z; cellZ < maxCell.Z; ++cellZ )
			{
				for ( var cellY = minCell.Y; cellY < maxCell.Y; ++cellY )
				{
					for ( var cellX = minCell.X; cellX < maxCell.X; ++cellX )
					{
						var cellMins = new Vector3( cellX, cellY, cellZ ) * CellSize;
						var cellLocalBounds = new BBox( bounds.Mins - cellMins, bounds.Maxs - cellMins );

						var minX = Math.Max( (int)MathF.Ceiling( cellLocalBounds.Mins.x * invSampleSize.x ), 0 );
						var maxX = Math.Min( (int)MathF.Ceiling( cellLocalBounds.Maxs.x * invSampleSize.x ), outputSize.X );

						var minY = Math.Max( (int) MathF.Ceiling( cellLocalBounds.Mins.y * invSampleSize.y ), 0 );
						var maxY = Math.Min( (int) MathF.Ceiling( cellLocalBounds.Maxs.y * invSampleSize.y ), outputSize.Y );

						var minZ = Math.Max( (int) MathF.Ceiling( cellLocalBounds.Mins.z * invSampleSize.z ), 0 );
						var maxZ = Math.Min( (int) MathF.Ceiling( cellLocalBounds.Maxs.z * invSampleSize.z ), outputSize.Z );

						if ( maxX <= minX || maxY <= minY || maxZ <= minZ )
						{
							continue;
						}

						for ( var i = 0; i < offsets.Length; ++i )
						{
							var offset = offsets[i];
							nearest[i] = GetPoint( (cellX + offset.X, cellY + offset.Y, cellZ + offset.Z) ) + new Vector3( offset.X, offset.Y, offset.Z ) * CellSize;
						}

						var baseSamplePos = (cellMins - bounds.Mins) * invSampleSize;
						var baseSample = (
							X: (int)MathF.Floor( baseSamplePos.x ),
							Y: (int)MathF.Floor( baseSamplePos.y ),
							Z: (int)MathF.Floor( baseSamplePos.z ));

						var baseSampleIndex = baseSample.X + (baseSample.Y + baseSample.Z * outputSize.Y) * outputSize.X;

						for ( var sampleZ = minZ; sampleZ < maxZ; ++sampleZ )
						{
							for ( var sampleY = minY; sampleY < maxY; ++sampleY )
							{
								for ( var sampleX = minX; sampleX < maxX; ++sampleX )
								{
									var minSq = float.PositiveInfinity;
									var pos = new Vector3( sampleX, sampleY, sampleZ ) * sampleSize;

									foreach ( var point in nearest)
									{
										var distSq = (point - pos).LengthSquared;

										if ( distSq < minSq )
										{
											minSq = distSq;
										}
									}

									var dist = MathF.Sqrt( minSq );
									output[baseSampleIndex + sampleX + (sampleY + sampleZ * outputSize.Y) * outputSize.X] = dist;
								}
							}
						}
					}
				}
			}
		}
		*/
	}
}
