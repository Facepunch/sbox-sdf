using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sandbox.Sdf
{
	partial class Sdf2DMeshWriter
	{
		private static float GetAdSubBc( float a, float b, float c, float d )
		{
			return (a - 127.5f) * (d - 127.5f) - (b - 127.5f) * (c - 127.5f);
		}

		private void AddSourceEdges( int x, int y, int aRaw, int bRaw, int cRaw, int dRaw )
		{
			var a = aRaw < 128 ? SquareConfiguration.A : 0;
			var b = bRaw < 128 ? SquareConfiguration.B : 0;
			var c = cRaw < 128 ? SquareConfiguration.C : 0;
			var d = dRaw < 128 ? SquareConfiguration.D : 0;

			var config = a | b | c | d;

			switch ( config )
			{
				case SquareConfiguration.None:
					break;

				case SquareConfiguration.A:
					SourceEdges.Add( new SourceEdge( x, y, SquareVertex.AC, SquareVertex.AB ) );
					break;

				case SquareConfiguration.B:
					SourceEdges.Add( new SourceEdge( x, y, SquareVertex.AB, SquareVertex.BD ) );
					break;

				case SquareConfiguration.C:
					SourceEdges.Add( new SourceEdge( x, y, SquareVertex.CD, SquareVertex.AC ) );
					break;

				case SquareConfiguration.D:
					SourceEdges.Add( new SourceEdge( x, y, SquareVertex.BD, SquareVertex.CD ) );
					break;


				case SquareConfiguration.AB:
					SourceEdges.Add( new SourceEdge( x, y, SquareVertex.AC, SquareVertex.BD ) );
					break;

				case SquareConfiguration.AC:
					SourceEdges.Add( new SourceEdge( x, y, SquareVertex.CD, SquareVertex.AB ) );
					break;

				case SquareConfiguration.CD:
					SourceEdges.Add( new SourceEdge( x, y, SquareVertex.BD, SquareVertex.AC ) );
					break;

				case SquareConfiguration.BD:
					SourceEdges.Add( new SourceEdge( x, y, SquareVertex.AB, SquareVertex.CD ) );
					break;


				case SquareConfiguration.AD:
					if ( GetAdSubBc( aRaw, bRaw, cRaw, dRaw ) > 0f )
					{
						SourceEdges.Add( new SourceEdge( x, y, SquareVertex.AC, SquareVertex.CD ) );
						SourceEdges.Add( new SourceEdge( x, y, SquareVertex.BD, SquareVertex.AB ) );
					}
					else
					{
						SourceEdges.Add( new SourceEdge( x, y, SquareVertex.AC, SquareVertex.AB ) );
						SourceEdges.Add( new SourceEdge( x, y, SquareVertex.BD, SquareVertex.CD ) );
					}

					break;

				case SquareConfiguration.BC:
					if ( GetAdSubBc( aRaw, bRaw, cRaw, dRaw ) < 0f )
					{
						SourceEdges.Add( new SourceEdge( x, y, SquareVertex.AB, SquareVertex.AC ) );
						SourceEdges.Add( new SourceEdge( x, y, SquareVertex.CD, SquareVertex.BD ) );
					}
					else
					{
						SourceEdges.Add( new SourceEdge( x, y, SquareVertex.AB, SquareVertex.BD ) );
						SourceEdges.Add( new SourceEdge( x, y, SquareVertex.CD, SquareVertex.AC ) );
					}

					break;

				case SquareConfiguration.ABC:
					SourceEdges.Add( new SourceEdge( x, y, SquareVertex.CD, SquareVertex.BD ) );
					break;

				case SquareConfiguration.ABD:
					SourceEdges.Add( new SourceEdge( x, y, SquareVertex.AC, SquareVertex.CD ) );
					break;

				case SquareConfiguration.ACD:
					SourceEdges.Add( new SourceEdge( x, y, SquareVertex.BD, SquareVertex.AB ) );
					break;

				case SquareConfiguration.BCD:
					SourceEdges.Add( new SourceEdge( x, y, SquareVertex.AB, SquareVertex.AC ) );
					break;

				case SquareConfiguration.ABCD:
					break;

				default:
					throw new NotImplementedException();
			}
		}

		private Dictionary<VertexKey, (SourceEdge NextEdge, Vector2 Position)> VertexMap { get; } = new();
		private HashSet<SourceEdge> RemainingSourceEdges { get; } = new();

		private record struct EdgeLoop( int FirstIndex, int Count, float Area, Vector2 Min, Vector2 Max );

		private List<Vector2> SourceVertices { get; } = new();
		private List<EdgeLoop> EdgeLoops { get; } = new();

		private static Vector3 GetVertexPos( in Sdf2DArrayData data, VertexKey key )
		{
			switch ( key.Vertex )
			{
				case NormalizedVertex.A:
					return new Vector3( key.X, key.Y );

				case NormalizedVertex.AB:
				{
					var a = data[key.X, key.Y] - 127.5f;
					var b = data[key.X + 1, key.Y] - 127.5f;
					var t = a / (a - b);
					return new Vector3( key.X + t, key.Y );
				}

				case NormalizedVertex.AC:
				{
					var a = data[key.X, key.Y] - 127.5f;
					var c = data[key.X, key.Y + 1] - 127.5f;
					var t = a / (a - c);
					return new Vector3( key.X, key.Y + t );
				}

				default:
					throw new NotImplementedException();
			}
		}

		private bool Contains( EdgeLoop loop, Vector2 pos )
		{
			if ( pos.x < loop.Min.x || pos.x > loop.Max.x )
			{
				return loop.Area < 0f;
			}

			if ( pos.y < loop.Min.y || pos.y > loop.Max.y )
			{
				return loop.Area < 0f;
			}

			var v0 = SourceVertices[loop.FirstIndex + loop.Count - 1] - pos;
			Vector2 v1;

			var intersections = 0;

			for ( var i = 0; i < loop.Count; ++i, v0 = v1 )
			{
				v1 = SourceVertices[loop.FirstIndex + i] - pos;

				if ( v0.y >= 0f == v1.y >= 0f )
				{
					continue;
				}

				if ( v0.x >= 0f && v1.x >= 0f )
				{
					continue;
				}

				var t = -v0.y / (v1.y - v0.y);

				if ( t >= 0f && t < 1f )
				{
					++intersections;
				}
			}

			return (intersections & 1) == 1 == loop.Area > 0f;
		}

		private bool Contains( EdgeLoop posLoop, EdgeLoop negLoop )
		{
			return posLoop.Area >= -negLoop.Area && Contains( posLoop, SourceVertices[negLoop.FirstIndex] );
		}

		private void FindEdgeLoops( in Sdf2DArrayData data )
		{
			const float collinearThreshold = 0.999877929688f;

			VertexMap.Clear();
			RemainingSourceEdges.Clear();

			foreach ( var sourceEdge in SourceEdges )
			{
				VertexMap[sourceEdge.V0] = (sourceEdge, GetVertexPos( in data, sourceEdge.V0 ));
				RemainingSourceEdges.Add( sourceEdge );
			}

			EdgeLoops.Clear();
			SourceVertices.Clear();

			while ( RemainingSourceEdges.Count > 0 )
			{
				var firstIndex = SourceVertices.Count;
				var first = RemainingSourceEdges.First();

				RemainingSourceEdges.Remove( first );
				SourceVertices.Add( VertexMap[first.V0].Position );

				// Build edge loop

				var count = 1;
				var next = first;

				while ( next.V1 != first.V0 )
				{
					(next, Vector2 pos) = VertexMap[next.V1];

					RemainingSourceEdges.Remove( next );
					SourceVertices.Add( pos );

					++count;
				}

				if ( count < 3 )
				{
					// Degenerate edge loop
					SourceVertices.RemoveRange( firstIndex, count );
					continue;
				}

				// Remove collinear vertices

				var v0 = SourceVertices[firstIndex + count - 1];
				var v1 = SourceVertices[firstIndex];
				var v01 = (v1 - v0).Normal;

				for ( var i = 0; i < count; ++i )
				{
					var v2 = SourceVertices[firstIndex + (i + 1) % count];
					var v12 = (v2 - v1).Normal;

					if ( Vector3.Dot( v01, v12 ) >= collinearThreshold )
					{
						count -= 1;
						SourceVertices.RemoveAt( firstIndex + i );
						--i;

						v1 = v2;
						v01 = (v1 - v0).Normal;
						continue;
					}

					v0 = v1;
					v1 = v2;
					v01 = v12;
				}

				if ( count < 3 )
				{
					// Degenerate edge loop
					SourceVertices.RemoveRange( firstIndex, count );
					continue;
				}

				// Find winding

				var area = 0f;

				v0 = SourceVertices[firstIndex];
				v1 = SourceVertices[firstIndex + 1];
				v01 = v1 - v0;

				var min = Vector2.Min( v0, v1 );
				var max = Vector2.Max( v0, v1 );

				for ( var i = 2; i < count; ++i )
				{
					var v2 = SourceVertices[firstIndex + i];
					var v12 = v2 - v1;

					area += v01.y * v12.x - v01.x * v12.y;

					min = Vector2.Min( min, v2 );
					max = Vector2.Max( max, v2 );

					v1 = v2;
					v01 = v1 - v0;
				}

				if ( area == 0f )
				{
					// Degenerate edge loop
					SourceVertices.RemoveRange( firstIndex, count );
					continue;
				}

				EdgeLoops.Add( new EdgeLoop( firstIndex, count, area * 0.5f, min, max ) );

				for ( var i = 0; i < count; ++i )
				{
					var a = DebugOffset + SourceVertices[firstIndex + i] * DebugScale;
					var b = DebugOffset + SourceVertices[firstIndex + (i + 1) % count] * DebugScale;

					DebugOverlay.Line( a, b, area > 0f ? Color.Green : Color.Red, 10f, false );
				}
			}

			if ( EdgeLoops.Count == 0 )
			{
				return;
			}

			DebugOverlay.Box( DebugOffset - new Vector2( 0.5f, 0.5f ) * DebugScale, DebugOffset + new Vector2( data.Size - 0.5f, data.Size - 0.5f ) * DebugScale, Color.White, 10f, false );

			// Sort by area: largest negative first, largest positive last

			EdgeLoops.Sort( ( a, b ) => a.Area.CompareTo( b.Area ) );

			// Put negative loops after the positive loops that contain them

			while ( EdgeLoops[0].Area < 0 )
			{
				var negLoop = EdgeLoops[0];
				EdgeLoops.RemoveAt( 0 );

				// Find containing positive loop

				for ( var i = 0; i < EdgeLoops.Count; ++i )
				{
					var posLoop = EdgeLoops[i];

					if ( !Contains( posLoop, negLoop ) )
					{
						continue;
					}

					EdgeLoops.Insert( i + 1, negLoop );
					break;
				}
			}
		}
	}
}
