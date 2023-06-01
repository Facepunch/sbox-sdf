using System;
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

public partial class Sdf3DArray : SdfArray<ISdf3D>
{
	public Sdf3DArray()
		: base( 3 )
	{
	}

	protected override Texture CreateTexture()
	{
		return new Texture3DBuilder()
			.WithFormat( ImageFormat.I8 )
			.WithSize( ArraySize, ArraySize, ArraySize )
			.WithData( Samples )
			.WithAnonymous( true )
			.Finish();
	}

	protected override void UpdateTexture( Texture texture )
	{
		texture.Update3D( Samples );
	}

	private (int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ) GetSampleRange( BBox bounds )
	{
		var (minX, maxX) = GetSampleRange( bounds.Mins.x, bounds.Maxs.x );
		var (minY, maxY) = GetSampleRange( bounds.Mins.y, bounds.Maxs.y );
		var (minZ, maxZ) = GetSampleRange( bounds.Mins.z, bounds.Maxs.z );

		return (minX, minY, minZ, maxX, maxY, maxZ);
	}

	public override bool Add<T>( in T sdf )
	{
		var (minX, minY, minZ, maxX, maxY, maxZ) = GetSampleRange( sdf.Bounds );
		var maxDist = Quality.MaxDistance;

		var changed = false;

		for ( var z = minZ; z < maxZ; ++z )
		{
			var worldZ = (z - Margin) * UnitSize;

			for ( var y = minY; y < maxY; ++y )
			{
				var worldY = (y - Margin) * UnitSize;

				for ( int x = minX, index = minX + y * ArraySize + z * ArraySize * ArraySize; x < maxX; ++x, ++index )
				{
					var worldX = (x - Margin) * UnitSize;
					var sampled = sdf[new Vector3( worldX, worldY, worldZ )];

					if ( sampled >= maxDist ) continue;

					var encoded = Encode( sampled );

					var oldValue = Samples[index];
					var newValue = Math.Min( encoded, oldValue );

					Samples[index] = newValue;

					changed |= oldValue != newValue;
				}
			}
		}

		if ( changed )
		{
			MarkChanged();
		}

		return changed;
	}

	public override bool Subtract<T>( in T sdf )
	{
		var (minX, minY, minZ, maxX, maxY, maxZ) = GetSampleRange( sdf.Bounds );
		var maxDist = Quality.MaxDistance;

		var changed = false;

		for ( var z = minZ; z < maxZ; ++z )
		{
			var worldZ = (z - Margin) * UnitSize;

			for ( var y = minY; y < maxY; ++y )
			{
				var worldY = (y - Margin) * UnitSize;

				for ( int x = minX, index = minX + y * ArraySize + z * ArraySize * ArraySize; x < maxX; ++x, ++index )
				{
					var worldX = (x - Margin) * UnitSize;
					var sampled = sdf[new Vector3( worldX, worldY, worldZ )];

					if ( sampled >= maxDist ) continue;

					var encoded = Encode( sampled );

					var oldValue = Samples[index];
					var newValue = Math.Max( (byte)(MaxEncoded - encoded), oldValue );

					Samples[index] = newValue;

					changed |= oldValue != newValue;
				}
			}
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
