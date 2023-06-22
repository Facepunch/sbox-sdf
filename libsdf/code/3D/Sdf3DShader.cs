using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sandbox.Diagnostics;

namespace Sandbox.Sdf;

public static class VolumeTexturePool
{
	private const int Capacity = 64;

	private static Dictionary<int, ConcurrentBag<Texture>> Buckets { get; } = new();

	private static int NextPowerOf2( int value )
	{
		var po2 = 1;
		while ( po2 < value )
		{
			po2 <<= 1;
		}

		return po2;
	}

	public static Task<Texture> RentAsync( (int X, int Y, int Z) size )
	{
		return RentAsync( Math.Max( size.X, Math.Max( size.Y, size.Z ) ) );
	}

	public static async Task<Texture> RentAsync( int minSize )
	{
		minSize = NextPowerOf2( minSize );

		if ( Buckets.TryGetValue( minSize, out var bucket ) && bucket.TryTake( out var texture ) )
		{
			return texture;
		}

		await GameTask.MainThread();

		return Texture.CreateVolume( minSize, minSize, minSize, ImageFormat.R32F )
			.WithUAVBinding()
			.WithDynamicUsage()
			.WithAnonymous( true )
			.Finish();
	}

	public static void Return( Texture texture )
	{
		Assert.AreEqual( texture.Width, texture.Height );
		Assert.AreEqual( texture.Width, texture.Depth );
		Assert.AreEqual( NextPowerOf2( texture.Width ), texture.Width );

		ConcurrentBag<Texture> bucket;

		lock ( Buckets )
		{
			if ( !Buckets.TryGetValue( texture.Width, out bucket ) )
			{
				bucket = new ConcurrentBag<Texture>();
				Buckets.Add( texture.Width, bucket );
			}
		}

		if ( bucket.Count < Capacity )
		{
			bucket.Add( texture );
		}
		else
		{
			texture.Dispose();
		}
	}
}

public static class SyncHelper
{
	private static TaskCompletionSource Tcs { get; set; } = new();

	public static Task NextFrame => Tcs.Task;

	[GameEvent.Client.Frame]
	public static void ClientFrame()
	{
		var oldTcs = Tcs;
		Tcs = new TaskCompletionSource();

		oldTcs.SetResult();
	}
}

public abstract class Sdf3DShader<TSdf, TShader>
	where TSdf : IComputeSdf3D<TSdf, TShader>
	where TShader : Sdf3DShader<TSdf, TShader>, new()
{
	private const int PoolCapacity = 64;
	private static ConcurrentBag<TShader> ShaderPool { get; } = new();
	private static Material Material { get; set; }

	public static TShader Rent()
	{
		return ShaderPool.TryTake( out var shader ) ? shader : new TShader();
	}

	public static void Return( TShader shader )
	{
		if ( ShaderPool.Count < PoolCapacity )
		{
			ShaderPool.Add( shader );
		}
	}

	private readonly ComputeShader _computeShader;
	public Texture OutputTexture { get; private set; }

	protected RenderAttributes Attributes => _computeShader.Attributes;

	protected Sdf3DShader( string shaderName )
	{
		Material ??= Material.Create( $"_computeShader_{shaderName}", $"shaders/sdf3d/compute/{shaderName}.shader" );

		Log.Info( $"Shader: {shaderName}, Material: {Material}" );

		_computeShader = new ComputeShader( Material );
	}

	public async Task InitializeAsync( TSdf sdf, Transform transform, (int X, int Y, int Z) outputSize )
	{
		OutputTexture = await VolumeTexturePool.RentAsync( outputSize );

		var matrix = Matrix.CreateTranslation( transform.Position )
			 * Matrix.CreateRotation( transform.Rotation )
			 * Matrix.CreateScale( transform.Scale );

		Attributes.Set( nameof(OutputTexture), OutputTexture );
		Attributes.Set( "g_mTransform", matrix );

		await OnInitializeAsync( sdf, transform, outputSize );
	}

	protected virtual Task OnInitializeAsync( TSdf sdf, Transform transform, (int X, int Y, int Z) outputSize )
	{
		return Task.CompletedTask;
	}

	public void Dispatch()
	{
		_computeShader.Dispatch();
	}

	public static async Task RunAsync( TSdf sdf, Transform transform, float[] output, (int X, int Y, int Z) outputSize )
	{
		var shader = Rent();

		try
		{
			await shader.InitializeAsync( sdf, transform, outputSize );

			shader.Dispatch();

			await SyncHelper.NextFrame;

			var sliceSize = outputSize.X * outputSize.Y;

			for ( var z = 0; z < outputSize.Z; ++z )
			{
				shader.OutputTexture.GetPixels( (0, 0, outputSize.X, outputSize.Y), z, 0,
					output.AsSpan( sliceSize * z, sliceSize ), ImageFormat.R32F, 0 );
			}
		}
		finally
		{
			try
			{
				shader.CleanUp();
			}
			finally
			{
				Return( shader );
			}
		}
	}

	public void CleanUp()
	{
		VolumeTexturePool.Return( OutputTexture );
		OutputTexture = null;
		OnCleanUp();
	}

	protected virtual void OnCleanUp()
	{

	}
}
