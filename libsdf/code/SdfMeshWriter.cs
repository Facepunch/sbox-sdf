using System;
using System.Collections.Generic;

namespace Sandbox.Sdf;

public interface IMeshWriter
{
	bool IsEmpty { get; }
	void ApplyTo( Mesh mesh );
}

public record struct MeshDescription( IMeshWriter Writer, Material Material );

internal abstract class SdfMeshWriter<T>
	where T : SdfMeshWriter<T>, new()
{
#pragma warning disable SB3000
	private const int MaxPoolCount = 64;
	private static List<T> Pool { get; } = new();
#pragma warning restore SB3000

	public static T Rent()
	{
		lock ( Pool )
		{
			if ( Pool.Count <= 0 ) return new T();

			var writer = Pool[^1];
			Pool.RemoveAt( Pool.Count - 1 );

			writer._isInPool = false;
			writer.Clear();

			return writer;
		}
	}

	public void Return()
	{
		lock ( Pool )
		{
			if ( _isInPool ) throw new InvalidOperationException( "Already returned." );

			Clear();

			_isInPool = true;

			if ( Pool.Count < MaxPoolCount ) Pool.Add( (T)this );
		}
	}

	private bool _isInPool;

	public abstract void Clear();
}
