using System;

namespace Sandbox.Sdf;

internal record struct SdfArray2DData( byte[] Samples, int BaseIndex, int RowStride )
{
	public byte this[int x, int y] => Samples[BaseIndex + x + y * RowStride];
}

internal partial class Sdf2DArray : BaseNetworkable, INetworkSerializer
{
	private const byte MaxEncoded = 255;
	public const int Margin = 1;

	public Sdf2DWorldQuality Quality { get; private set; }

	private byte[] _samples;

	private int _arraySize;
	private float _unitSize;
	private float _invUnitSize;
	private float _invMaxDistance;

	private bool _textureInvalid = true;
	private Texture _texture;

	public int ModificationCount { get; set; }

	public Sdf2DArray()
	{
	}

	public Sdf2DArray( Sdf2DWorldQuality quality )
	{
		Init( quality );
	}

	public Texture Texture
	{
		get
		{
			if ( !_textureInvalid && _texture != null ) return _texture;

			_textureInvalid = false;

			if ( _texture == null )
				_texture = new Texture2DBuilder()
					.WithFormat( ImageFormat.I8 )
					.WithSize( _arraySize, _arraySize )
					.WithData( _samples )
					.WithAnonymous( true )
					.Finish();
			else
				_texture.Update( _samples );

			return _texture;
		}
	}

	private void Init( Sdf2DWorldQuality quality )
	{
		Quality = quality;

		_arraySize = Quality.ChunkResolution + Margin * 2 + 1;
		_unitSize = Quality.ChunkSize / Quality.ChunkResolution;
		_invUnitSize = Quality.ChunkResolution / Quality.ChunkSize;
		_invMaxDistance = 1f / Quality.MaxDistance;

		_samples = new byte[_arraySize * _arraySize];

		Clear( false );
	}

	private byte Encode( float distance )
	{
		return (byte)((int)((distance * _invMaxDistance * 0.5f + 0.5f) * MaxEncoded)).Clamp( 0, 255 );
	}

	private float Decode( byte encoded )
	{
		return (encoded * (1f / MaxEncoded) - 0.5f) * Quality.MaxDistance * 2f;
	}

	public void Clear( bool solid )
	{
		Array.Fill( _samples, solid ? (byte)0 : (byte)255 );
		++ModificationCount;
	}

	private (int MinX, int MinY, int MaxX, int MaxY) GetSampleRange( Rect bounds )
	{
		var min = (bounds.TopLeft - Quality.MaxDistance) * _invUnitSize;
		var max = (bounds.BottomRight + Quality.MaxDistance) * _invUnitSize;

		var minX = Math.Max( 0, (int)MathF.Ceiling( min.x ) + Margin );
		var minY = Math.Max( 0, (int)MathF.Ceiling( min.y ) + Margin );

		var maxX = Math.Min( _arraySize, (int)MathF.Ceiling( max.x ) + Margin );
		var maxY = Math.Min( _arraySize, (int)MathF.Ceiling( max.y ) + Margin );

		return (minX, minY, maxX, maxY);
	}

	public bool Add<T>( in T sdf )
		where T : ISdf2D
	{
		var (minX, minY, maxX, maxY) = GetSampleRange( sdf.Bounds );
		var maxDist = Quality.MaxDistance;

		var changed = false;

		for ( var y = minY; y < maxY; ++y )
		{
			var worldY = (y - Margin) * _unitSize;

			for ( int x = minX, index = minX + y * _arraySize; x < maxX; ++x, ++index )
			{
				var worldX = (x - Margin) * _unitSize;
				var sampled = sdf[new Vector2( worldX, worldY )];

				if ( sampled >= maxDist ) continue;

				var encoded = Encode( sampled );

				var oldValue = _samples[index];
				var newValue = Math.Min( encoded, oldValue );

				_samples[index] = newValue;

				changed |= oldValue != newValue;
			}
		}

		if ( changed )
		{
			++ModificationCount;
			_textureInvalid = true;
		}

		return changed;
	}

	public bool Subtract<T>( in T sdf )
		where T : ISdf2D
	{
		var (minX, minY, maxX, maxY) = GetSampleRange( sdf.Bounds );
		var maxDist = Quality.MaxDistance;

		var changed = false;

		for ( var y = minY; y < maxY; ++y )
		{
			var worldY = (y - Margin) * _unitSize;

			for ( int x = minX, index = minX + y * _arraySize; x < maxX; ++x, ++index )
			{
				var worldX = (x - Margin) * _unitSize;
				var sampled = sdf[new Vector2( worldX, worldY )];

				if ( sampled >= maxDist ) continue;

				var encoded = Encode( sampled );

				var oldValue = _samples[index];
				var newValue = Math.Max( (byte)(MaxEncoded - encoded), oldValue );

				_samples[index] = newValue;

				changed |= oldValue != newValue;
			}
		}

		if ( changed )
		{
			++ModificationCount;
			_textureInvalid = true;
		}

		return changed;
	}

	public void WriteTo( Sdf2DMeshWriter writer, Sdf2DLayer layer, bool renderMesh, bool collisionMesh )
	{
		writer.Write( new SdfArray2DData( _samples, Margin * _arraySize + Margin, _arraySize ),
			layer, renderMesh, collisionMesh );
	}

	public void Read( ref NetRead net )
	{
		var quality = new Sdf2DWorldQuality(
			net.Read<int>(),
			net.Read<float>(),
			net.Read<float>() );

		Init( quality );

		_samples = net.ReadUnmanagedArray( _samples );

		++ModificationCount;
		_textureInvalid = true;
	}

	public void Write( NetWrite net )
	{
		net.Write( Quality.ChunkResolution );
		net.Write( Quality.ChunkSize );
		net.Write( Quality.MaxDistance );

		net.WriteUnmanagedArray( _samples );
	}
}
