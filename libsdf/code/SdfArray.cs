using System;

namespace Sandbox.Sdf;

public abstract partial class SdfArray<TSdf> : BaseNetworkable, INetworkSerializer
{
#pragma warning disable SB3000
	protected const byte MaxEncoded = 255;
	public const int Margin = 1;
#pragma warning restore SB3000

	public int Dimensions { get; }

	public WorldQuality Quality { get; private set; }
	public byte[] Samples { get; private set; }
	public int ModificationCount { get; set; }
	public int ArraySize { get; private set; }
	public int SampleCount { get; private set; }

	protected float UnitSize { get; private set; }
	protected float InvUnitSize { get; private set; }
	protected float InvMaxDistance { get; private set; }

	private bool _textureInvalid = true;
	private Texture _texture;

	protected SdfArray( int dimensions )
	{
		Dimensions = dimensions;
	}
	
	protected (int Min, int Max) GetSampleRange( float localMin, float localMax )
	{
		return (Math.Max( 0, (int) MathF.Ceiling( (localMin - Quality.MaxDistance) * InvUnitSize ) + Margin ),
			Math.Min( ArraySize, (int) MathF.Ceiling( (localMax + Quality.MaxDistance) * InvUnitSize ) + Margin ));
	}

	protected byte Encode( float distance )
	{
		return (byte) ((int) ((distance * InvMaxDistance * 0.5f + 0.5f) * MaxEncoded)).Clamp( 0, 255 );
	}

	protected float Decode( byte encoded )
	{
		return (encoded * (1f / MaxEncoded) - 0.5f) * Quality.MaxDistance * 2f;
	}

	public Texture Texture
	{
		get
		{
			if ( !_textureInvalid && _texture != null ) return _texture;

			ThreadSafe.AssertIsMainThread();

			_textureInvalid = false;

			if ( _texture == null )
				_texture = CreateTexture();
			else
				UpdateTexture( _texture );

			return _texture;
		}
	}

	protected abstract Texture CreateTexture();

	protected abstract void UpdateTexture( Texture texture );

	public abstract bool Add<T>( in T sdf )
		where T : TSdf;

	public abstract bool Subtract<T>( in T sdf )
		where T : TSdf;

	internal void Init( WorldQuality quality )
	{
		if ( Quality.Equals( quality ) )
		{
			return;
		}

		Quality = quality;

		ArraySize = Quality.ChunkResolution + Margin * 2 + 1;
		UnitSize = Quality.ChunkSize / Quality.ChunkResolution;
		InvUnitSize = Quality.ChunkResolution / Quality.ChunkSize;
		InvMaxDistance = 1f / Quality.MaxDistance;

		SampleCount = 1;

		for ( var i = 0; i < Dimensions; ++i )
		{
			SampleCount *= ArraySize;
		}

		Samples = new byte[SampleCount];

		Clear( false );
	}

	public void Clear( bool solid )
	{
		Array.Fill( Samples, solid ? (byte) 0 : (byte) 255 );
		++ModificationCount;
	}

	protected void MarkChanged()
	{
		++ModificationCount;
		_textureInvalid = true;
	}

	public void Read( ref NetRead net )
	{
		Init( WorldQuality.Read( ref net ) );

		Samples = net.ReadUnmanagedArray( Samples );

		MarkChanged();
	}

	public void Write( NetWrite net )
	{
		Quality.Write( net );

		net.WriteUnmanagedArray( Samples );
	}
}
