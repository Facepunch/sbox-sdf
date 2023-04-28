using System.Collections;
using System.Collections.Generic;

namespace Sandbox.Sdf
{
	public static class Helpers
	{
		public static BBox Transform( this Matrix matrix, BBox box )
		{
			var v000 = matrix.Transform( new Vector3( box.Mins.x, box.Mins.y, box.Mins.z ) );
			var v100 = matrix.Transform( new Vector3( box.Maxs.x, box.Mins.y, box.Mins.z ) );
			var v010 = matrix.Transform( new Vector3( box.Mins.x, box.Maxs.y, box.Mins.z ) );
			var v110 = matrix.Transform( new Vector3( box.Maxs.x, box.Maxs.y, box.Mins.z ) );
			var v001 = matrix.Transform( new Vector3( box.Mins.x, box.Mins.y, box.Maxs.z ) );
			var v101 = matrix.Transform( new Vector3( box.Maxs.x, box.Mins.y, box.Maxs.z ) );
			var v011 = matrix.Transform( new Vector3( box.Mins.x, box.Maxs.y, box.Maxs.z ) );
			var v111 = matrix.Transform( new Vector3( box.Maxs.x, box.Maxs.y, box.Maxs.z ) );

			return new BBox(
				Vector3.Min(
					Vector3.Min( Vector3.Min( v000, v100 ), Vector3.Min( v010, v110 ) ),
					Vector3.Min( Vector3.Min( v001, v101 ), Vector3.Min( v011, v111 ) ) ),
				Vector3.Max(
					Vector3.Max( Vector3.Max( v000, v100 ), Vector3.Max( v010, v110 ) ),
					Vector3.Max( Vector3.Max( v001, v101 ), Vector3.Max( v011, v111 ) ) ) );
		}

		public static float Lerp( float a, float b, float t )
		{
			return a + (b - a) * t;
		}

		public static ArrayEnumerable3D Enumerate( this Vector3i arraySize )
		{
			return new ArrayEnumerable3D( Vector3i.Zero, arraySize, arraySize );
		}

		public static ArrayEnumerable3D EnumerateArray3D( this Vector3i arraySize, Vector3i min, Vector3i max )
		{
			return new ArrayEnumerable3D( min, max, arraySize );
		}

		public readonly struct ArrayEnumerable3D : IEnumerable<(Vector3i index3, int index1)>
		{
			public struct Enumerator : IEnumerator<(Vector3i index3, int index1)>
			{
				public readonly Vector3i Min;
				public readonly Vector3i Max;

				public readonly Vector3i ArraySize;

				private readonly Vector3i IndexOffset;

				private int _index1;
				private Vector3i _index3;
				private bool _ended;

				public Enumerator( Vector3i min, Vector3i max, Vector3i arraySize )
				{
					Min = min;
					Max = max;

					ArraySize = arraySize;

					IndexOffset.x = 1;
					IndexOffset.y = (arraySize.x - (max.x - min.x)) * 1 + IndexOffset.x;
					IndexOffset.z = (arraySize.y - (max.y - min.y)) * arraySize.x + IndexOffset.y;

					_index1 = default;
					_index3 = default;
					_ended = default;

					Reset();
				}

				public bool MoveNext()
				{
					if ( _ended ) return false;

					_index3.x += 1;

					if ( _index3.x < Max.x )
					{
						_index1 += IndexOffset.x;
						return true;
					}

					_index3.x = Min.x;
					_index3.y += 1;

					if ( _index3.y < Max.y )
					{
						_index1 += IndexOffset.y;
						return true;
					}

					_index3.y = Min.y;
					_index3.z += 1;

					if ( _index3.z < Max.z )
					{
						_index1 += IndexOffset.z;
						return true;
					}

					_ended = true;

					return false;
				}

				public void Reset()
				{
					_index1 = Min.x + ArraySize.x * (Min.y + ArraySize.y * Min.z) - 1;
					_index3 = Min - new Vector3i( 1, 0, 0 );

					_ended = Min.x >= Max.x || Min.y >= Max.y || Min.z >= Max.z;
				}

				public (Vector3i index3, int index1) Current => (_index3, _index1);

				object IEnumerator.Current => Current;

				public void Dispose() { }
			}

			public readonly Vector3i Min;
			public readonly Vector3i Max;

			public readonly Vector3i ArraySize;

			public ArrayEnumerable3D( Vector3i min, Vector3i max, Vector3i arraySize )
			{
				Min = Vector3i.Max( min, Vector3i.Zero );
				Max = Vector3i.Min( max, arraySize );
				ArraySize = arraySize;
			}

			public Enumerator GetEnumerator()
			{
				return new Enumerator( Min, Max, ArraySize );
			}

			IEnumerator<(Vector3i index3, int index1)> IEnumerable<(Vector3i index3, int index1)>.GetEnumerator()
			{
				return GetEnumerator();
			}

			IEnumerator IEnumerable.GetEnumerator()
			{
				return GetEnumerator();
			}
		}
	}
}
