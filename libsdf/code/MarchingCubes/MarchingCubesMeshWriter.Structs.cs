using System;

namespace Sandbox.MarchingCubes
{
	partial class MarchingCubesMeshWriter
	{
		public readonly struct EdgeIntersection
		{
			public static EdgeIntersection operator -( EdgeIntersection intersection )
			{
				return intersection.Compliment;
			}

			public static EdgeIntersection operator +( EdgeIntersection intersection )
			{
				return intersection;
			}

			public readonly bool Exists;
			public readonly int Index;

			public readonly Voxel Value0;
			public readonly Voxel Value1;

			public readonly Vector3 Pos;

			public EdgeIntersection Compliment => new EdgeIntersection( Index, Value1, Value0, Pos );

			public EdgeIntersection( int index, Voxel value0, Voxel value1, Vector3 pos )
			{
				Exists = (value0.RawValue & 0x80) != (value1.RawValue & 0x80);
				Index = index;

				Value0 = value0;
				Value1 = value1;

				Pos = pos;
			}
		}

		public readonly struct DualEdge
		{
			public readonly bool Exists;

			public readonly int Index0;
			public readonly int Index1;

			public readonly Vector3 Pos0;
			public readonly Vector3 Pos1;

			public DualEdge( EdgeIntersection a, EdgeIntersection b )
			{
				Exists = a.Exists && b.Exists;

				if ( a.Value1.RawValue >= 128 )
				{
					// Make sure winding of edge loops is consistent
					// with positive SDF values on the inside

					(a, b) = (b, a);
				}

				Index0 = a.Index;
				Index1 = b.Index;

				Pos0 = a.Pos;
				Pos1 = b.Pos;
			}
		}
	}
}
