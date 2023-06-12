using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Sdf;

internal record struct Sdf3DArrayData( byte[] Samples, int Margin, int ArraySize, int BaseIndex )
{
	public Sdf3DArrayData( byte[] samples, int margin, int arraySize )
		: this( samples, margin, arraySize, margin * (1 + arraySize + arraySize * arraySize) )
	{
		
	}

	public byte this[int x, int y, int z] => Samples[BaseIndex + x + (y + z * ArraySize) * ArraySize];

	public float this[ float x, int y, int z ]
	{
		get
		{
			var x0 = (int) MathF.Floor( x );
			var x1 = x0 + 1;

			var a = this[x0, y, z];
			var b = this[x1, y, z];

			return a + (b - a) * (x - x0);
		}
	}

	public float this[int x, float y, int z]
	{
		get
		{
			var y0 = (int) MathF.Floor( y );
			var y1 = y0 + 1;

			var a = this[x, y0, z];
			var b = this[x, y1, z];

			return a + (b - a) * (y - y0);
		}
	}

	public float this[int x, int y, float z]
	{
		get
		{
			var z0 = (int) MathF.Floor( z );
			var z1 = z0 + 1;

			var a = this[x, y, z0];
			var b = this[x, y, z1];

			return a + (b - a) * (z - z0);
		}
	}
}

/// <summary>
/// Networked array containing raw SDF samples for a <see cref="Sdf3DChunk"/>.
/// </summary>
public partial class Sdf3DArray : SdfArray<ISdf3D>
{
	/// <summary>
	/// Networked array containing raw SDF samples for a <see cref="Sdf3DChunk"/>.
	/// </summary>
	public Sdf3DArray()
		: base( 3 )
	{
	}

	/// <inheritdoc />
	protected override Texture CreateTexture()
	{
		return new Texture3DBuilder()
			.WithFormat( ImageFormat.I8 )
			.WithSize( ArraySize, ArraySize, ArraySize )
			.WithData( Samples )
			.WithAnonymous( true )
			.Finish();
	}

	/// <inheritdoc />
	protected override void UpdateTexture( Texture texture )
	{
		texture.Update3D( Samples );
	}

	private ((int X, int Y, int Z) Min, (int X, int Y, int Z) Max, Transform Transform) GetSampleRange( BBox? bounds )
	{
		if ( bounds is not {} b )
		{
			return ((0, 0, 0), (ArraySize, ArraySize, ArraySize), new Transform(
				-Margin * UnitSize, Rotation.Identity, UnitSize ));
		}

		var (minX, maxX, minLocalX, maxLocalX) = GetSampleRange( b.Mins.x, b.Maxs.x );
		var (minY, maxY, minLocalY, maxLocalY) = GetSampleRange( b.Mins.y, b.Maxs.y );
		var (minZ, maxZ, minLocalZ, maxLocalZ) = GetSampleRange( b.Mins.z, b.Maxs.z );

		var min = new Vector3( minLocalX, minLocalY, minLocalZ );
		var max = new Vector3( maxLocalX, maxLocalY, maxLocalZ );

		return ((minX, minY, minZ), (maxX, maxY, maxZ), new Transform(
			min, Rotation.Identity, UnitSize ));
	}

	/// <inheritdoc />
	public override bool Add<T>( in T sdf )
	{
		var (min, max, transform) = GetSampleRange( sdf.Bounds );
		var size = (X: max.X - min.X, Y: max.Y - min.Y, Z: max.Z - min.Z);
		var maxDist = Quality.MaxDistance;

		var changed = false;

		var samples = ArrayPool<float>.Shared.Rent( size.X * size.Y * size.Z );

		try
		{
			sdf.SampleRange( transform, samples, size );

			for ( var z = min.Z; z < max.Z; ++z )
			{
				for ( var y = min.Y; y < max.Y; ++y )
				{
					var srcIndex = (y - min.Y) * size.X + (z - min.Z) * size.X * size.Y;
					var dstIndex = min.X + y * ArraySize + z * ArraySize * ArraySize;

					for ( var x = min.X; x < max.X; ++x, ++srcIndex, ++dstIndex )
					{
						var sampled = samples[srcIndex];

						if ( sampled >= maxDist ) continue;

						var encoded = Encode( sampled );

						var oldValue = Samples[dstIndex];
						var newValue = Math.Min( encoded, oldValue );

						Samples[dstIndex] = newValue;

						changed |= oldValue != newValue;
					}
				}
			}
		}
		finally
		{
			ArrayPool<float>.Shared.Return( samples );
		}

		if ( changed )
		{
			MarkChanged();
		}

		return changed;
	}

	/// <inheritdoc />
	public override bool Subtract<T>( in T sdf )
	{
		var (min, max, transform) = GetSampleRange( sdf.Bounds );
		var size = (X: max.X - min.X, Y: max.Y - min.Y, Z: max.Z - min.Z);
		var maxDist = Quality.MaxDistance;

		var changed = false;

		var samples = ArrayPool<float>.Shared.Rent( size.X * size.Y * size.Z );

		try
		{
			sdf.SampleRange( transform, samples, size );

			for ( var z = min.Z; z < max.Z; ++z )
			{
				for ( var y = min.Y; y < max.Y; ++y )
				{
					var srcIndex = (y - min.Y) * size.X + (z - min.Z) * size.X * size.Y;
					var dstIndex = min.X + y * ArraySize + z * ArraySize * ArraySize;

					for ( var x = min.X; x < max.X; ++x, ++srcIndex, ++dstIndex )
					{
						var sampled = samples[srcIndex];

						if ( sampled >= maxDist ) continue;

						var encoded = Encode( sampled );

						var oldValue = Samples[dstIndex];
						var newValue = Math.Max( (byte)(byte.MaxValue - encoded), oldValue );

						Samples[dstIndex] = newValue;

						changed |= oldValue != newValue;
					}
				}
			}
		}
		finally
		{
			ArrayPool<float>.Shared.Return( samples );
		}

		if ( changed )
		{
			MarkChanged();
		}

		return changed;
	}

	internal Task WriteToAsync( Sdf3DMeshWriter writer, Sdf3DVolume volume, CancellationToken token )
	{
		if ( writer.Samples == null || writer.Samples.Length < Samples.Length )
		{
			writer.Samples = new byte[Samples.Length];
		}

		Array.Copy( Samples, writer.Samples, Samples.Length );

		return writer.WriteAsync( new Sdf3DArrayData( writer.Samples, Margin, ArraySize ), volume, token );
	}
}
