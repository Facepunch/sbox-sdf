using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Sdf
{
	public enum NormalStyle
	{
		Flat,
		Smooth
	}

	public interface IVoxelMeshWriter
	{
		void Write( Voxel[] data, Vector3i size, Vector3i min, Vector3i max, int lod, NormalStyle normalStyle, bool render, bool collision );
	}

	public partial class MarchingCubesMeshWriter : IVoxelMeshWriter
	{
		private static readonly List<MarchingCubesMeshWriter> _sPool = new List<MarchingCubesMeshWriter>();

		public static MarchingCubesMeshWriter Rent()
		{
			if ( _sPool.Count > 0 )
			{
				var writer = _sPool[_sPool.Count - 1];
				_sPool.RemoveAt( _sPool.Count - 1 );

				writer._isInPool = false;
				writer.Clear();

				return writer;
			}

			return new MarchingCubesMeshWriter();
		}

		public void Return()
		{
			if ( _isInPool )
			{
				throw new InvalidOperationException( "Already returned." );
			}

			Clear();

			_isInPool = true;
			_sPool.Add( this );
		}

		private bool _isInPool;

		private Vector3[] _chunkNormals;

        private readonly Voxel[] _cubeVoxels = new Voxel[8];
        private readonly Vector3[] _cubeColors = new Vector3[8];
        private readonly Vector3[] _cubeNormals = new Vector3[8];
        private readonly DualEdge[] _cubeDualEdges = new DualEdge[12];

		public List<VoxelVertex> Vertices { get; } = new List<VoxelVertex>();
		public List<Vector3> CollisionVertices { get; } = new List<Vector3>();
		public List<int> CollisionIndices { get; } = new List<int>();

		public Vector3 Offset { get; set; } = 0f;
		public Vector3 Scale { get; set; } = 1f;

		public void Clear()
		{
			Vertices.Clear();
			CollisionVertices.Clear();
			CollisionIndices.Clear();

			Offset = 0f;
			Scale = 1f;
		}

		public void Write( Voxel[] data, Vector3i size, Vector3i min, Vector3i max, int lod, NormalStyle normalStyle, bool render, bool collision )
		{
			var updateNormals = render && normalStyle != NormalStyle.Flat;
			var step = 1 << lod;

			if ( updateNormals )
			{
				if ( min.x < step || min.y < step || min.z < step || max.x > size.x - step || max.y > size.y - step || max.z > size.z - step )
				{
					throw new ArgumentException( $"When using non-flat normals, {nameof(min)} and {nameof(max)} " +
						$"must have a margin of 1 from the full size of the array." );
				}

				if ( _chunkNormals == null || _chunkNormals.Length < data.Length )
				{
					_chunkNormals = new Vector3[data.Length];
				}

				CalculateNormals( data, size, min, max );
			}

			var xStride = step;
			var yStride = size.x * step;
			var zStride = size.x * size.y * step;

			max -= 1;

			min /= step;
			max /= step;

			var scale = new Vector3( 1f / (max.x - min.x), 1f / (max.y - min.y), 1f / (max.z - min.z) );

			var xIndexOffset = min.x * xStride;

			for ( var z = min.z; z < max.z; ++z )
			{
				for ( var y = min.y; y < max.y; ++y )
				{
					var i0 = xIndexOffset + (y + 0) * yStride + (z + 0) * zStride;
					var i1 = xIndexOffset + (y + 1) * yStride + (z + 0) * zStride;
					var i2 = xIndexOffset + (y + 0) * yStride + (z + 1) * zStride;
					var i3 = xIndexOffset + (y + 1) * yStride + (z + 1) * zStride;

                    _cubeVoxels[0] = data[i0];
                    _cubeVoxels[2] = data[i1];
                    _cubeVoxels[4] = data[i2];
                    _cubeVoxels[6] = data[i3];

                    if ( render )
                    {
                        _cubeColors[0] = _cubeVoxels[0].Color;
                        _cubeColors[2] = _cubeVoxels[2].Color;
                        _cubeColors[4] = _cubeVoxels[4].Color;
                        _cubeColors[6] = _cubeVoxels[6].Color;
                    }

					if ( updateNormals )
					{
                        _cubeNormals[0] = _chunkNormals[i0];
                        _cubeNormals[2] = _chunkNormals[i1];
                        _cubeNormals[4] = _chunkNormals[i2];
                        _cubeNormals[6] = _chunkNormals[i3];
					}

					var x0hash
						= ((_cubeVoxels[0].RawValue & 0x80) >> 7)
						| ((_cubeVoxels[2].RawValue & 0x80) >> 6)
						| ((_cubeVoxels[4].RawValue & 0x80) >> 5)
						| ((_cubeVoxels[6].RawValue & 0x80) >> 4);

					for ( var x = min.x; x < max.x; ++x )
					{
						i0 += xStride;
						i1 += xStride;
						i2 += xStride;
						i3 += xStride;

						_cubeVoxels[1] = data[i0];
						_cubeVoxels[3] = data[i1];
						_cubeVoxels[5] = data[i2];
						_cubeVoxels[7] = data[i3];

                        if (render)
                        {
                            _cubeColors[1] = _cubeVoxels[1].Color;
                            _cubeColors[3] = _cubeVoxels[3].Color;
                            _cubeColors[5] = _cubeVoxels[5].Color;
                            _cubeColors[7] = _cubeVoxels[7].Color;
                        }

						if ( updateNormals )
						{
                            _cubeNormals[1] = _chunkNormals[i0];
                            _cubeNormals[3] = _chunkNormals[i1];
                            _cubeNormals[5] = _chunkNormals[i2];
                            _cubeNormals[7] = _chunkNormals[i3];
						}

						var x1hash
							= ((_cubeVoxels[1].RawValue & 0x80) >> 7)
							| ((_cubeVoxels[3].RawValue & 0x80) >> 6)
							| ((_cubeVoxels[5].RawValue & 0x80) >> 5)
							| ((_cubeVoxels[7].RawValue & 0x80) >> 4);

						var hash = (x1hash << 4) | x0hash;

						if ( hash != 0b0000_0000 && hash != 0b1111_1111 )
						{
							WriteCube( new Vector3i( x, y, z ), scale,
								normalStyle, render, collision );
						}

						_cubeVoxels[0] = _cubeVoxels[1];
						_cubeVoxels[2] = _cubeVoxels[3];
						_cubeVoxels[4] = _cubeVoxels[5];
						_cubeVoxels[6] = _cubeVoxels[7];

						x0hash = x1hash;

                        if ( render )
                        {
                            _cubeColors[0] = _cubeColors[1];
                            _cubeColors[2] = _cubeColors[3];
                            _cubeColors[4] = _cubeColors[5];
                            _cubeColors[6] = _cubeColors[7];
                        }

						if ( updateNormals )
						{
                            _cubeNormals[0] = _cubeNormals[1];
                            _cubeNormals[2] = _cubeNormals[3];
                            _cubeNormals[4] = _cubeNormals[5];
                            _cubeNormals[6] = _cubeNormals[7];
						}
					}
				}
			}
		}

		private void CalculateNormals( Voxel[] data, Vector3i size, Vector3i min, Vector3i max )
		{
			const int xStride = 1;
			var yStride = xStride * size.x;
			var zStride = yStride * size.y;

			for ( var z = min.z; z < max.z; ++z )
			{
				for ( var y = min.y; y < max.y; ++y )
				{
					var index = min.x * xStride + y * yStride + z * zStride;

					for ( var x = min.x; x < max.x; ++x, ++index )
					{
						var xNeg = data[index - xStride].RawValue;
						var xPos = data[index + xStride].RawValue;
						var yNeg = data[index - yStride].RawValue;
						var yPos = data[index + yStride].RawValue;
						var zNeg = data[index - zStride].RawValue;
						var zPos = data[index + zStride].RawValue;

						var hash = xNeg + xPos + yNeg + yPos + zNeg + zPos;

						if ( hash == 0 || hash == 255 * 6 ) continue;

						var diff = new Vector3( xNeg - xPos, yNeg - yPos, zNeg - zPos );

						_chunkNormals[index] = diff.Normal;
					}
				}
			}
		}

		private EdgeIntersection GetIntersection( int index, Vector3 pos0, Vector3 pos1, Voxel val0, Voxel val1 )
		{
			if ( (val0.RawValue & 0x80) == (val1.RawValue & 0x80) )
			{
				return default;
			}

			var t = (val0.RawValue - 127.5f) / (val0.RawValue - val1.RawValue);

			return new EdgeIntersection( index, val0, val1, pos0 + (pos1 - pos0) * t );
		}

		private int ProcessFace( EdgeIntersection edge0, EdgeIntersection edge1,
			EdgeIntersection edge2, EdgeIntersection edge3 )
		{
			var hash
				= (edge0.Exists ? 0b0001 : 0)
				| (edge1.Exists ? 0b0010 : 0)
				| (edge2.Exists ? 0b0100 : 0)
				| (edge3.Exists ? 0b1000 : 0);

			switch ( hash )
			{
				case 0b0000:
					return 0;

				case 0b0011:
				{
					var dualEdge = new DualEdge( edge0, edge1 );
                    _cubeDualEdges[dualEdge.Index0] = dualEdge;
					return 1;
				}

				case 0b0101:
				{
					var dualEdge = new DualEdge( edge0, edge2 );
                    _cubeDualEdges[dualEdge.Index0] = dualEdge;
					return 1;
				}

				case 0b1001:
				{
					var dualEdge = new DualEdge( edge0, edge3 );
                    _cubeDualEdges[dualEdge.Index0] = dualEdge;
					return 1;
				}

				case 0b0110:
				{
					var dualEdge = new DualEdge( edge1, edge2 );
                    _cubeDualEdges[dualEdge.Index0] = dualEdge;
					return 1;
				}

				case 0b1010:
				{
					var dualEdge = new DualEdge( edge1, edge3 );
                    _cubeDualEdges[dualEdge.Index0] = dualEdge;
					return 1;
				}

				case 0b1100:
				{
					var dualEdge = new DualEdge( edge2, edge3 );
                    _cubeDualEdges[dualEdge.Index0] = dualEdge;
					return 1;
				}

				case 0b1111:
				{
					// Special case: two possible pairs of edges,
					// so find the shortest total length

					var len0 = (edge0.Pos - edge1.Pos).Length + (edge2.Pos - edge3.Pos).Length;
					var len1 = (edge0.Pos - edge3.Pos).Length + (edge1.Pos - edge2.Pos).Length;

					DualEdge dualEdge0;
					DualEdge dualEdge1;

					if ( len0 <= len1 )
					{
						dualEdge0 = new DualEdge( edge0, edge1 );
						dualEdge1 = new DualEdge( edge2, edge3 );
					}
					else
					{
						dualEdge0 = new DualEdge( edge0, edge3 );
						dualEdge1 = new DualEdge( edge1, edge2 );
					}

                    _cubeDualEdges[dualEdge0.Index0] = dualEdge0;
                    _cubeDualEdges[dualEdge1.Index0] = dualEdge1;
					return 2;
				}

				default:
					throw new Exception( "Invalid mesh generated" );
			}
		}

		private static readonly Vector3 X0Y0Z0 = new Vector3( 0f, 0f, 0f );
		private static readonly Vector3 X1Y0Z0 = new Vector3( 1f, 0f, 0f );
		private static readonly Vector3 X0Y1Z0 = new Vector3( 0f, 1f, 0f );
		private static readonly Vector3 X1Y1Z0 = new Vector3( 1f, 1f, 0f );
		private static readonly Vector3 X0Y0Z1 = new Vector3( 0f, 0f, 1f );
		private static readonly Vector3 X1Y0Z1 = new Vector3( 1f, 0f, 1f );
		private static readonly Vector3 X0Y1Z1 = new Vector3( 0f, 1f, 1f );
		private static readonly Vector3 X1Y1Z1 = new Vector3( 1f, 1f, 1f );

		private Vector3 Interpolate( Vector3 pos, Vector3[] cubeArray )
        {
            var x00 = Vector3.Lerp( cubeArray[0], cubeArray[1], pos.x );
            var x10 = Vector3.Lerp( cubeArray[2], cubeArray[3], pos.x );
            var x01 = Vector3.Lerp( cubeArray[4], cubeArray[5], pos.x );
            var x11 = Vector3.Lerp( cubeArray[6], cubeArray[7], pos.x );

			var xy0 = Vector3.Lerp( x00, x10, pos.y );
			var xy1 = Vector3.Lerp( x01, x11, pos.y );

			return Vector3.Lerp( xy0, xy1, pos.z );
		}

        private Vector3 InterpolateNormal( Vector3 pos )
        {
            return Interpolate( pos, _cubeNormals ).Normal;
		}

		private void WriteCube( Vector3i index3, Vector3 scale,
			NormalStyle normalStyle,
			bool render, bool collision)
		{
			// Find out if / where the surface intersects each edge of the cube.

			var edgeXMinYMin = GetIntersection(  0, X0Y0Z0, X0Y0Z1, _cubeVoxels[0], _cubeVoxels[4] );
			var edgeXMinZMin = GetIntersection(  1, X0Y0Z0, X0Y1Z0, _cubeVoxels[0], _cubeVoxels[2] );
			var edgeXMaxYMin = GetIntersection(  2, X1Y0Z0, X1Y0Z1, _cubeVoxels[1], _cubeVoxels[5] );
			var edgeXMaxZMin = GetIntersection(  3, X1Y0Z0, X1Y1Z0, _cubeVoxels[1], _cubeVoxels[3] );
			var edgeXMinYMax = GetIntersection(  4, X0Y1Z0, X0Y1Z1, _cubeVoxels[2], _cubeVoxels[6] );
			var edgeXMinZMax = GetIntersection(  5, X0Y0Z1, X0Y1Z1, _cubeVoxels[4], _cubeVoxels[6] );
			var edgeXMaxYMax = GetIntersection(  6, X1Y1Z0, X1Y1Z1, _cubeVoxels[3], _cubeVoxels[7] );
			var edgeXMaxZMax = GetIntersection(  7, X1Y0Z1, X1Y1Z1, _cubeVoxels[5], _cubeVoxels[7] );
			var edgeYMinZMin = GetIntersection(  8, X0Y0Z0, X1Y0Z0, _cubeVoxels[0], _cubeVoxels[1] );
			var edgeYMaxZMin = GetIntersection(  9, X0Y1Z0, X1Y1Z0, _cubeVoxels[2], _cubeVoxels[3] );
			var edgeYMinZMax = GetIntersection( 10, X0Y0Z1, X1Y0Z1, _cubeVoxels[4], _cubeVoxels[5] );
			var edgeYMaxZMax = GetIntersection( 11, X0Y1Z1, X1Y1Z1, _cubeVoxels[6], _cubeVoxels[7] );

			// Each face of the cube will have either 0, 2 or 4 edges with intersections.
			// We will turn each pair of edge intersections into an edge in the final mesh.
			// Each of these "dual edges" will be stored in a table, indexed by the first
			// intersection edge.

            Array.Clear( _cubeDualEdges );

			var dualEdgeCount = 0;

			dualEdgeCount += ProcessFace( +edgeXMinZMin, +edgeXMinYMax, -edgeXMinZMax, -edgeXMinYMin );
			dualEdgeCount += ProcessFace( +edgeXMaxZMax, -edgeXMaxYMax, -edgeXMaxZMin, +edgeXMaxYMin );
			dualEdgeCount += ProcessFace( +edgeXMinYMin, +edgeYMinZMax, -edgeXMaxYMin, -edgeYMinZMin );
			dualEdgeCount += ProcessFace( +edgeXMaxYMax, -edgeYMaxZMax, -edgeXMinYMax, +edgeYMaxZMin );
			dualEdgeCount += ProcessFace( +edgeXMaxZMin, -edgeYMaxZMin, -edgeXMinZMin, +edgeYMinZMin );
			dualEdgeCount += ProcessFace( +edgeXMinZMax, +edgeYMaxZMax, -edgeXMaxZMax, -edgeYMinZMax );

			if ( dualEdgeCount == 0 )
			{
				return;
			}

			if ( dualEdgeCount < 3 )
			{
				throw new Exception( "Invalid mesh generated" );
			}

			scale *= Scale;

			var offset = Offset + index3 * scale;

			// Follow edges in a loop to triangulate.

			for (var i = 0; i < 12 && dualEdgeCount > 0; ++i)
            {
				var first = _cubeDualEdges[i];
				if (!first.Exists) continue;

				// Start of an edge loop.

				var prev = first;
				var next = first;

				// Remove first edge in edge loop.
                _cubeDualEdges[i] = default;
				--dualEdgeCount;

                var a0 = first.Pos0;
				var b0 = first.Pos1;

				var a = offset + a0 * scale;
				var b = offset + b0 * scale;

				var aIndex = CollisionVertices.Count;
				var bIndex = CollisionVertices.Count + 1;

				if ( collision )
				{
					CollisionVertices.Add( a );
					CollisionVertices.Add( b );
				}

				Vector3 aNorm = default, bNorm = default, aColor = default, bColor = default;

                if ( render )
                {
                    if ( normalStyle != NormalStyle.Flat )
                    {
                        aNorm = InterpolateNormal( a0 );
                        bNorm = InterpolateNormal( b0 );
                    }

                    aColor = Interpolate( a0, _cubeColors );
                    bColor = Interpolate( b0, _cubeColors );
                }

				// We skip the first edge, and break on the last edge, so
				// we output a triangle every N-2 edges in the loop.

				while ((next = _cubeDualEdges[prev.Index1]).Exists)
				{
					// Remove edge from map.
                    _cubeDualEdges[prev.Index1] = default;
					--dualEdgeCount;

					if ( next.Index1 == i ) break;

                    var c0 = next.Pos1;
					var c = offset + c0 * scale;

                    if ( render )
                    {
                        var cColor = Interpolate( c0, _cubeColors );

                        switch ( normalStyle )
						{
							case NormalStyle.Flat:
								{
									var cross = Vector3.Cross( b - a, c - a );
									var normal = cross.Normal;
									var tangent = (b - a).Normal;

									Vertices.Add( new VoxelVertex( a, normal, tangent, aColor ) );
									Vertices.Add( new VoxelVertex( b, normal, tangent, bColor ) );
									Vertices.Add( new VoxelVertex( c, normal, tangent, cColor ) );

									break;
								}

							case NormalStyle.Smooth:
								{
									var cNorm = InterpolateNormal( c0 );

									var tangent = (b - a).Normal;

									Vertices.Add( new VoxelVertex( a, aNorm, tangent, aColor ) );
									Vertices.Add( new VoxelVertex( b, bNorm, tangent, bColor ) );
									Vertices.Add( new VoxelVertex( c, cNorm, tangent, cColor ) );

									bNorm = cNorm;

									break;
								}
						}

                        bColor = cColor;
                    }

					if ( collision )
					{
						CollisionVertices.Add( c );

						var cIndex = bIndex + 1;

						CollisionIndices.Add( aIndex );
						CollisionIndices.Add( bIndex );
						CollisionIndices.Add( cIndex );

						bIndex = cIndex;
					}

					b = c;
					prev = next;
				}
			}

			if ( dualEdgeCount != 0 )
			{
				throw new Exception( "Invalid mesh generated" );
			}
		}
	}
}
