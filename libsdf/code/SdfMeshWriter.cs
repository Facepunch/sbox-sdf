using System;
using System.Collections.Generic;

namespace Sandbox.Sdf;

internal abstract class SdfMeshWriter<T>
	where T : SdfMeshWriter<T>, new()
{
#pragma warning disable SB3000
	private const int MaxPoolCount = 16;
	private static List<T> Pool { get; } = new();
#pragma warning restore SB3000

	public static T Rent()
	{
		if ( Pool.Count > 0 )
		{
			var writer = Pool[^1];
			Pool.RemoveAt( Pool.Count - 1 );

			writer._isInPool = false;
			writer.Clear();

			return writer;
		}

		return new T();
	}

	public void Return()
	{
		if ( _isInPool ) throw new InvalidOperationException( "Already returned." );

		Clear();

		_isInPool = true;

		if ( Pool.Count < MaxPoolCount ) Pool.Add( (T) this );
	}

	private bool _isInPool;

	public abstract void Clear();
}
