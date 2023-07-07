using System;
using System.Collections.Generic;
using System.Threading;

namespace Sandbox.Sdf;

partial class Sdf2DMeshWriter : SdfMeshWriter<Sdf2DMeshWriter>
{
	private abstract class SubMesh<T> : IMeshWriter
		where T : unmanaged
	{
		public List<T> Vertices { get; } = new();
		public List<int> Indices { get; } = new();

		public abstract VertexAttribute[] VertexLayout { get; }

		public abstract void ClearMap();

		internal static Vector3 GetVertexPos( in Sdf2DArrayData data, VertexKey key )
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

		public void Clear()
		{
			ClearMap();

			Vertices.Clear();
			Indices.Clear();
		}

		public bool IsEmpty => Indices.Count == 0;

		public void ApplyTo( Mesh mesh )
		{
			if ( mesh == null )
			{
				return;
			}

			if ( mesh.HasVertexBuffer )
			{
				if ( Indices.Count > 0 )
				{
					mesh.SetIndexBufferSize( Indices.Count );
					mesh.SetVertexBufferSize( Vertices.Count );

					mesh.SetIndexBufferData( Indices );
					mesh.SetVertexBufferData( Vertices );
				}

				mesh.SetIndexRange( 0, Indices.Count );
			}
			else if ( Indices.Count > 0 )
			{
				mesh.CreateVertexBuffer( Vertices.Count, VertexLayout, Vertices );
				mesh.CreateIndexBuffer( Indices.Count, Indices );
			}
		}
	}

	private class CollisionSubMesh : SubMesh<Vector3>
	{
		public Vector3 FrontOffset { get; set; }
		public Vector3 BackOffset { get; set; }

		private Dictionary<VertexKey, (int FrontIndex, int BackIndex)> Map { get; } = new();

		public override VertexAttribute[] VertexLayout => throw new NotImplementedException();

		public override void ClearMap()
		{
			Map.Clear();
		}

		private (int FrontIndex, int BackIndex) AddVertex( Sdf2DArrayData data, float biasSampleSpace, float unitSize, VertexKey key )
		{
			if ( Map.TryGetValue( key, out var indices ) ) return indices;

			var pos = GetVertexPos( data, key ) * unitSize;

			var frontIndex = Vertices.Count;
			var backIndex = frontIndex + 1;

			Vertices.Add( pos + FrontOffset );
			Vertices.Add( pos + BackOffset );

			Map.Add( key, (frontIndex, backIndex) );

			return (frontIndex, backIndex);
		}

		public void AddFrontBackTriangle( Sdf2DArrayData data, float unitSize, FrontBackTriangle triangle )
		{
			var (front0, back0) = AddVertex( data, 0f, unitSize, triangle.V0 );
			var (front1, back1) = AddVertex( data, 0f, unitSize, triangle.V1 );
			var (front2, back2) = AddVertex( data, 0f, unitSize, triangle.V2 );

			Indices.Add( front0 );
			Indices.Add( front2 );
			Indices.Add( front1 );

			Indices.Add( back0 );
			Indices.Add( back1 );
			Indices.Add( back2 );
		}

		public void AddCutFace( Sdf2DArrayData data, float unitSize, CutFace face )
		{
			var (front0, back0) = AddVertex( data, 0f, unitSize, face.V0 );
			var (front1, back1) = AddVertex( data, 0f, unitSize, face.V1 );

			Indices.Add( front0 );
			Indices.Add( front1 );
			Indices.Add( back0 );

			Indices.Add( front1 );
			Indices.Add( back1 );
			Indices.Add( back0 );
		}
	}

	private class FrontBackSubMesh : SubMesh<Vertex>
	{
		private Dictionary<VertexKey, int> Map { get; } = new();

		public override VertexAttribute[] VertexLayout => Vertex.Layout;

		public Vector3 Normal { get; set; }
		public Vector3 Offset { get; set; }

		public override void ClearMap()
		{
			Map.Clear();
		}

		private int AddVertex( Sdf2DArrayData data, float unitSize, float uvScale, VertexKey key )
		{
			if ( Map.TryGetValue( key, out var index ) ) return index;

			var pos = GetVertexPos( data, key );

			index = Vertices.Count;

			Vertices.Add( new Vertex(
				pos * unitSize + Offset,
				Normal,
				new Vector3( 1f, 0f, 0f ),
				new Vector2( pos.x * unitSize * uvScale, pos.y * unitSize * uvScale ) ) );

			Map.Add( key, index );

			return index;
		}

		public void AddTriangle( Sdf2DArrayData data, float unitSize, float uvScale, FrontBackTriangle triangle )
		{
			Indices.Add( AddVertex( data, unitSize, uvScale, triangle.V0 ) );
			Indices.Add( AddVertex( data, unitSize, uvScale, triangle.V1 ) );
			Indices.Add( AddVertex( data, unitSize, uvScale, triangle.V2 ) );
		}
	}

	private class CutSubMesh : SubMesh<Vertex>
	{
		private const float RootHalf = 1.41421f * 0.5f;

		private const float SmoothNormalThreshold = 33f;
		private static readonly float SmoothNormalDotTheshold = MathF.Cos( SmoothNormalThreshold * MathF.PI / 180f );

		private record struct VertexInfo( int IndexOffset, Vector3 Normal, float V );

		private Dictionary<VertexKey, VertexInfo> Map { get; } = new();

		public override VertexAttribute[] VertexLayout => Vertex.Layout;

		public Vector3 FrontOffset { get; set; }
		public Vector3 BackOffset { get; set; }
		public EdgeStyle EdgeStyle { get; set; }
		public float EdgeRadius { get; set; }
		public int CutVertexCount { get; set; }

		private void UpdateVertices( VertexInfo info, Vector3 otherNormal, Vector3 pos, float unitSize, float uvScale, bool smooth )
		{
			var tangent = new Vector3( 0f, 0f, 1f );
			var width = FrontOffset.z - BackOffset.z;
			var centerPos = pos * unitSize;

			var normal = (info.Normal + otherNormal).Normal;

			switch ( EdgeStyle )
			{
				case EdgeStyle.Sharp:
				{
					Vertices[info.IndexOffset + 0] = new Vertex(
						centerPos + FrontOffset,
						smooth ? normal : info.Normal, tangent,
						new Vector2( 0f, info.V ) * uvScale );

					Vertices[info.IndexOffset + 1] = new Vertex(
						centerPos + BackOffset,
						smooth ? normal : info.Normal, tangent,
						new Vector2( width, info.V ) * uvScale );
					break;
				}

				case EdgeStyle.Bevel:
				{
					var innerLength = EdgeRadius * 2f / (info.Normal + otherNormal).Length;
					var innerCenter = centerPos + normal * innerLength;
					var frontNormal = ((smooth ? normal : info.Normal) + new Vector3( 0f, 0f, 1f )).Normal;
					var backNormal = frontNormal.WithZ( -frontNormal.z );
					var innerFrontOffset = FrontOffset.WithZ( FrontOffset.z - EdgeRadius );
					var innerBackOffset = BackOffset.WithZ( BackOffset.z + EdgeRadius );
					var frontPos = centerPos + FrontOffset;
					var backPos = centerPos + BackOffset;
					var innerFrontPos = innerCenter + innerFrontOffset;
					var innerBackPos = innerCenter + innerBackOffset;
					var frontTangent = (innerFrontPos - frontPos).Normal;
					var backTangent = (backPos - innerBackPos).Normal;

					Vertices[info.IndexOffset + 0] = new Vertex(
						frontPos, frontNormal, frontTangent,
						new Vector2( 0f, info.V ) * uvScale );

					Vertices[info.IndexOffset + 1] = new Vertex(
						innerFrontPos, frontNormal, frontTangent,
						new Vector2( EdgeRadius, info.V ) * uvScale );

					Vertices[info.IndexOffset + 2] = new Vertex(
						innerFrontPos, smooth ? normal : info.Normal, tangent,
						new Vector2( EdgeRadius, info.V ) * uvScale );

					Vertices[info.IndexOffset + 3] = new Vertex(
						innerBackPos, smooth ? normal : info.Normal, tangent,
						new Vector2( width - EdgeRadius, info.V ) * uvScale );

					Vertices[info.IndexOffset + 4] = new Vertex(
						innerBackPos, backNormal, backTangent,
						new Vector2( width - EdgeRadius, info.V ) * uvScale );

					Vertices[info.IndexOffset + 5] = new Vertex(
						backPos, backNormal, backTangent,
						new Vector2( width, info.V ) * uvScale );
					break;
				}

				case EdgeStyle.Round:
				{
					var innerLength = EdgeRadius * 2f / (info.Normal + otherNormal).Length;
					var innerFrontOffset = FrontOffset.WithZ( FrontOffset.z - EdgeRadius );
					var innerBackOffset = BackOffset.WithZ( BackOffset.z + EdgeRadius );
					var frontCenter = centerPos + innerFrontOffset;
					var backCenter = centerPos + innerBackOffset;

					var vertexCount = CutVertexCount;
					var halfVertexCount = CutVertexCount / 2;
					var frontAdd = new Vector3( 0f, 0f, EdgeRadius );
					var backAdd = new Vector3( 0f, 0f, -EdgeRadius );
					var innerAdd = normal * innerLength;

					for ( var i = 0; i < halfVertexCount; ++i )
					{
						var angle = 0.5f * MathF.PI * i / (halfVertexCount - 1f);
						var cos = MathF.Cos( angle );
						var sin = MathF.Sin( angle );

						Vertices[info.IndexOffset + i] = new Vertex(
							frontCenter + cos * frontAdd + sin * innerAdd,
							(cos * frontAdd + sin * innerAdd).Normal,
							(cos * innerAdd + sin * backAdd).Normal,
							new Vector2( angle * EdgeRadius, info.V ) * uvScale );

						Vertices[info.IndexOffset + vertexCount - i - 1] = new Vertex(
							backCenter + cos * backAdd + sin * innerAdd,
							(cos * backAdd + sin * innerAdd).Normal,
							(cos * -innerAdd + sin * backAdd).Normal,
							new Vector2( width + (MathF.PI - angle - 2f) * EdgeRadius, info.V ) * uvScale );
					}

					break;
				}

				default:
					throw new NotImplementedException();
			}
		}

		private int AddVertices( Vector3 pos, Vector3 normal, float unitSize, float uvScale,
			VertexKey key )
		{
			var wasInMap = false;

			var binormal = MathF.Abs( normal.y ) > RootHalf
				? new Vector3( -MathF.Sign( normal.y ), 0f, 0f )
				: new Vector3( 0f, MathF.Sign( normal.x ), 0f );

			// Tex coord, we'll only merge vertices with similar values
			var v = Vector3.Dot( pos, binormal ) * unitSize;

			var smooth = false;
			var otherNormal = normal;

			if ( Map.TryGetValue( key, out var info ) )
			{
				smooth = Vector3.Dot( info.Normal, normal ) >= SmoothNormalDotTheshold;
				otherNormal = info.Normal;

				UpdateVertices( info, normal, pos, unitSize, uvScale, smooth );

				if ( smooth && MathF.Abs( v - info.V ) <= 1f ) return info.IndexOffset;

				wasInMap = true;
			}

			var indexOffset = Vertices.Count;

			for ( var i = 0; i < CutVertexCount; ++i )
			{
				Vertices.Add( default );
			}

			info = new VertexInfo( indexOffset, normal, v );

			if ( wasInMap )
			{
				UpdateVertices( info, otherNormal, pos, unitSize, uvScale, smooth );
			}
			else
			{
				Map[key] = info;
			}

			return indexOffset;
		}

		public void AddQuad( Sdf2DArrayData data, float unitSize, float uvScale, CutFace face )
		{
			var aPos = GetVertexPos( data, face.V0 );
			var bPos = GetVertexPos( data, face.V1 );

			var normal = Vector3.Cross( aPos - bPos, new Vector3( 0f, 0f, 1f ) ).Normal;

			var aIndexOffset = AddVertices( aPos, normal, unitSize, uvScale, face.V0 );
			var bIndexOffset = AddVertices( bPos, normal, unitSize, uvScale, face.V1 );

			switch ( EdgeStyle )
			{
				case EdgeStyle.Sharp:
				{
					Indices.Add( aIndexOffset + 0 );
					Indices.Add( bIndexOffset + 0 );
					Indices.Add( aIndexOffset + 1 );

					Indices.Add( aIndexOffset + 1 );
					Indices.Add( bIndexOffset + 0 );
					Indices.Add( bIndexOffset + 1 );

					break;
				}

				case EdgeStyle.Bevel:
				{
					for ( var i = 0; i < 6; i += 2 )
					{
						Indices.Add( aIndexOffset + i + 0 );
						Indices.Add( bIndexOffset + i + 0 );
						Indices.Add( aIndexOffset + i + 1 );

						Indices.Add( aIndexOffset + i + 1 );
						Indices.Add( bIndexOffset + i + 0 );
						Indices.Add( bIndexOffset + i + 1 );
					}

					break;
				}

				case EdgeStyle.Round:
				{
					var vertexCount = CutVertexCount;

					for ( var i = 0; i < vertexCount - 1; ++i )
					{
						Indices.Add( aIndexOffset + i + 0 );
						Indices.Add( bIndexOffset + i + 0 );
						Indices.Add( aIndexOffset + i + 1 );

						Indices.Add( aIndexOffset + i + 1 );
						Indices.Add( bIndexOffset + i + 0 );
						Indices.Add( bIndexOffset + i + 1 );
					}

					break;
				}
			}
		}

		public void AddNeighborQuad( Sdf2DArrayData data, float unitSize, float uvScale, CutFace face )
		{
			var aPos = GetVertexPos( data, face.V0 );
			var bPos = GetVertexPos( data, face.V1 );

			var normal = Vector3.Cross( aPos - bPos, new Vector3( 0f, 0f, 1f ) ).Normal;

			AddVertices( aPos, normal, unitSize, uvScale, face.V0 );
			AddVertices( bPos, normal, unitSize, uvScale, face.V1 );
		}

		public override void ClearMap()
		{
			Map.Clear();
		}
	}

	private List<FrontBackTriangle> FrontBackTriangles { get; } = new();
	private List<CutFace> CutFaces { get; } = new();
	private List<CutFace> NeighborCutFaces { get; } = new();

	private CollisionSubMesh Collision { get; } = new();
	private FrontBackSubMesh Front { get; } = new();
	private FrontBackSubMesh Back { get; } = new();
	private CutSubMesh Cut { get; } = new();

	public IMeshWriter FrontWriter => Front;
	public IMeshWriter BackWriter => Back;
	public IMeshWriter CutWriter => Cut;

	private List<SolidBlock> SolidBlocks { get; } = new();

	public byte[] Samples { get; set; }

	public override void Clear()
	{
		SolidBlocks.Clear();
		FrontBackTriangles.Clear();
		CutFaces.Clear();
		NeighborCutFaces.Clear();

		Collision.Clear();
		Front.Clear();
		Back.Clear();
		Cut.Clear();
	}

	private static float GetAdSubBc( float a, float b, float c, float d )
	{
		return (a - 127.5f) * (d - 127.5f) - (b - 127.5f) * (c - 127.5f);
	}

	private void AddFrontBack( SquareConfiguration config, int x, int y, int aRaw, int bRaw, int cRaw, int dRaw )
	{
		switch ( config )
		{
			case SquareConfiguration.None:
				break;

			case SquareConfiguration.A:
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.AC,
					SquareVertex.AB ) );
				break;

			case SquareConfiguration.B:
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.B, SquareVertex.AB,
					SquareVertex.BD ) );
				break;

			case SquareConfiguration.C:
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.C, SquareVertex.CD,
					SquareVertex.AC ) );
				break;

			case SquareConfiguration.D:
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.D, SquareVertex.BD,
					SquareVertex.CD ) );
				break;


			case SquareConfiguration.AB:
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.AC,
					SquareVertex.B ) );
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.B, SquareVertex.AC,
					SquareVertex.BD ) );
				break;

			case SquareConfiguration.AC:
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.C,
					SquareVertex.AB ) );
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.C, SquareVertex.CD,
					SquareVertex.AB ) );
				break;

			case SquareConfiguration.CD:
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.C, SquareVertex.D,
					SquareVertex.AC ) );
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.D, SquareVertex.BD,
					SquareVertex.AC ) );
				break;

			case SquareConfiguration.BD:
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.B, SquareVertex.AB,
					SquareVertex.D ) );
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.D, SquareVertex.AB,
					SquareVertex.CD ) );
				break;


			case SquareConfiguration.AD:
				if ( GetAdSubBc( aRaw, bRaw, cRaw, dRaw ) > 0f )
				{
					FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.AC,
						SquareVertex.D ) );
					FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.D, SquareVertex.AC,
						SquareVertex.CD ) );
					FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.D,
						SquareVertex.AB ) );
					FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.D, SquareVertex.BD,
						SquareVertex.AB ) );
				}
				else
				{
					FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.AC,
						SquareVertex.AB ) );
					FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.D, SquareVertex.BD,
						SquareVertex.CD ) );
				}

				break;

			case SquareConfiguration.BC:
				if ( GetAdSubBc( aRaw, bRaw, cRaw, dRaw ) < 0f )
				{
					FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.B, SquareVertex.AB,
						SquareVertex.C ) );
					FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.C, SquareVertex.AB,
						SquareVertex.AC ) );
					FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.B, SquareVertex.C,
						SquareVertex.BD ) );
					FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.C, SquareVertex.CD,
						SquareVertex.BD ) );
				}
				else
				{
					FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.B, SquareVertex.AB,
						SquareVertex.BD ) );
					FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.C, SquareVertex.CD,
						SquareVertex.AC ) );
				}

				break;


			case SquareConfiguration.ABC:
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.C,
					SquareVertex.B ) );
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.B, SquareVertex.C,
					SquareVertex.BD ) );
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.C, SquareVertex.CD,
					SquareVertex.BD ) );
				break;

			case SquareConfiguration.ABD:
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.D,
					SquareVertex.B ) );
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.AC,
					SquareVertex.D ) );
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.D, SquareVertex.AC,
					SquareVertex.CD ) );
				break;

			case SquareConfiguration.ACD:
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.C,
					SquareVertex.D ) );
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.D,
					SquareVertex.AB ) );
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.D, SquareVertex.BD,
					SquareVertex.AB ) );
				break;

			case SquareConfiguration.BCD:
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.B, SquareVertex.C,
					SquareVertex.D ) );
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.B, SquareVertex.AB,
					SquareVertex.C ) );
				FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.C, SquareVertex.AB,
					SquareVertex.AC ) );
				break;


			case SquareConfiguration.ABCD:
				SolidBlocks.Add( new SolidBlock( x, y, x + 1, y + 1 ) );
				break;

			default:
				throw new NotImplementedException();
		}
	}

	private static void AddCut( List<CutFace> target, SquareConfiguration config, int x, int y, int aRaw, int bRaw, int cRaw, int dRaw )
	{
		switch ( config )
		{
			case SquareConfiguration.None:
				break;

			case SquareConfiguration.A:
				target.Add( new CutFace( x, y, SquareVertex.AC, SquareVertex.AB ) );
				break;

			case SquareConfiguration.B:
				target.Add( new CutFace( x, y, SquareVertex.AB, SquareVertex.BD ) );
				break;

			case SquareConfiguration.C:
				target.Add( new CutFace( x, y, SquareVertex.CD, SquareVertex.AC ) );
				break;

			case SquareConfiguration.D:
				target.Add( new CutFace( x, y, SquareVertex.BD, SquareVertex.CD ) );
				break;


			case SquareConfiguration.AB:
				target.Add( new CutFace( x, y, SquareVertex.AC, SquareVertex.BD ) );
				break;

			case SquareConfiguration.AC:
				target.Add( new CutFace( x, y, SquareVertex.CD, SquareVertex.AB ) );
				break;

			case SquareConfiguration.CD:
				target.Add( new CutFace( x, y, SquareVertex.BD, SquareVertex.AC ) );
				break;

			case SquareConfiguration.BD:
				target.Add( new CutFace( x, y, SquareVertex.AB, SquareVertex.CD ) );
				break;


			case SquareConfiguration.AD:
				if ( GetAdSubBc( aRaw, bRaw, cRaw, dRaw ) > 0f )
				{
					target.Add( new CutFace( x, y, SquareVertex.AC, SquareVertex.CD ) );
					target.Add( new CutFace( x, y, SquareVertex.BD, SquareVertex.AB ) );
				}
				else
				{
					target.Add( new CutFace( x, y, SquareVertex.AC, SquareVertex.AB ) );
					target.Add( new CutFace( x, y, SquareVertex.BD, SquareVertex.CD ) );
				}

				break;

			case SquareConfiguration.BC:
				if ( GetAdSubBc( aRaw, bRaw, cRaw, dRaw ) < 0f )
				{
					target.Add( new CutFace( x, y, SquareVertex.AB, SquareVertex.AC ) );
					target.Add( new CutFace( x, y, SquareVertex.CD, SquareVertex.BD ) );
				}
				else
				{
					target.Add( new CutFace( x, y, SquareVertex.AB, SquareVertex.BD ) );
					target.Add( new CutFace( x, y, SquareVertex.CD, SquareVertex.AC ) );
				}

				break;

			case SquareConfiguration.ABC:
				target.Add( new CutFace( x, y, SquareVertex.CD, SquareVertex.BD ) );
				break;

			case SquareConfiguration.ABD:
				target.Add( new CutFace( x, y, SquareVertex.AC, SquareVertex.CD ) );
				break;

			case SquareConfiguration.ACD:
				target.Add( new CutFace( x, y, SquareVertex.BD, SquareVertex.AB ) );
				break;

			case SquareConfiguration.BCD:
				target.Add( new CutFace( x, y, SquareVertex.AB, SquareVertex.AC ) );
				break;

			case SquareConfiguration.ABCD:
				break;

			default:
				throw new NotImplementedException();
		}
	}

	public void WriteRenderMesh( Sdf2DArrayData data, Sdf2DLayer layer )
	{
		SolidBlocks.Clear();
		FrontBackTriangles.Clear();
		CutFaces.Clear();
		NeighborCutFaces.Clear();

		var quality = layer.Quality;
		var size = quality.ChunkResolution;

		for ( var y = -1; y <= size; ++y )
		{
			var yInBounds = y >= 0 && y < size; 

			for ( int x = -1, index = data.BaseIndex + y * data.RowStride - 1; x <= size; ++x, ++index )
			{
				var xInBounds = x >= 0 && x < size;

				var aRaw = data.Samples[index];
				var bRaw = data.Samples[index + 1];
				var cRaw = data.Samples[index + data.RowStride];
				var dRaw = data.Samples[index + data.RowStride + 1];

				var a = aRaw < 128 ? SquareConfiguration.A : 0;
				var b = bRaw < 128 ? SquareConfiguration.B : 0;
				var c = cRaw < 128 ? SquareConfiguration.C : 0;
				var d = dRaw < 128 ? SquareConfiguration.D : 0;

				var config = a | b | c | d;

				if ( yInBounds && xInBounds )
				{
					AddFrontBack( config, x, y, aRaw, bRaw, cRaw, dRaw );
					AddCut( CutFaces, config, x, y, aRaw, bRaw, cRaw, dRaw );
				}
				else
				{
					AddCut( NeighborCutFaces, config, x, y, aRaw, bRaw, cRaw, dRaw );
				}
			}
		}

		ReduceSolidBlocks( SolidBlocks );

		Front.ClearMap();
		Back.ClearMap();
		Cut.ClearMap();

		var depth = layer.Depth;
		var offset = layer.Offset;
		var unitSize = quality.UnitSize;
		var uvScale = 1f / layer.TexCoordSize;

		Front.Normal = new Vector3( 0f, 0f, 1f );
		Front.Offset = Cut.FrontOffset = Front.Normal * (depth * 0.5f + offset);
		Back.Normal = new Vector3( 0f, 0f, -1f );
		Back.Offset = Cut.BackOffset = Back.Normal * (depth * 0.5f - offset);

		Cut.EdgeStyle = layer.EdgeStyle;
		Cut.EdgeRadius = layer.EdgeRadius;
		Cut.CutVertexCount = layer.EdgeStyle switch
		{
			EdgeStyle.Sharp => 2,
			EdgeStyle.Bevel => 6,
			EdgeStyle.Round => 2 + layer.EdgeFaces * 2,
			_ => throw new NotImplementedException()
		};

		foreach ( var triangle in FrontBackTriangles )
		{
			Front.AddTriangle( data, unitSize, uvScale, triangle.Flipped );
			Back.AddTriangle( data, unitSize, uvScale, triangle );
		}

		foreach ( var block in SolidBlocks )
		{
			var (tri0, tri1) = block.Triangles;

			Front.AddTriangle( data, unitSize, uvScale, tri0.Flipped );
			Front.AddTriangle( data, unitSize, uvScale, tri1.Flipped );

			Back.AddTriangle( data, unitSize, uvScale, tri0 );
			Back.AddTriangle( data, unitSize, uvScale, tri1 );
		}

		foreach ( var cutFace in CutFaces )
		{
			Cut.AddQuad( data, unitSize, uvScale, cutFace );
		}

		foreach ( var cutFace in NeighborCutFaces )
		{
			Cut.AddNeighborQuad( data, unitSize, uvScale, cutFace );
		}
	}

	public void WriteCollisionMesh( Sdf2DArrayData data, Sdf2DLayer layer )
	{
		SolidBlocks.Clear();
		FrontBackTriangles.Clear();
		CutFaces.Clear();
		NeighborCutFaces.Clear();

		var quality = layer.Quality;
		var size = quality.ChunkResolution;

		for ( var y = 0; y < size; ++y )
		{
			for ( int x = 0, index = data.BaseIndex + y * data.RowStride; x < size; ++x, ++index )
			{
				var aRaw = data.Samples[index];
				var bRaw = data.Samples[index + 1];
				var cRaw = data.Samples[index + data.RowStride];
				var dRaw = data.Samples[index + data.RowStride + 1];

				var a = aRaw < 128 ? SquareConfiguration.A : 0;
				var b = bRaw < 128 ? SquareConfiguration.B : 0;
				var c = cRaw < 128 ? SquareConfiguration.C : 0;
				var d = dRaw < 128 ? SquareConfiguration.D : 0;

				var config = a | b | c | d;

				AddFrontBack( config, x, y, aRaw, bRaw, cRaw, dRaw );
				AddCut( CutFaces, config, x, y, aRaw, bRaw, cRaw, dRaw );
			}
		}

		ReduceSolidBlocks( SolidBlocks );

		Collision.ClearMap();

		var depth = layer.Depth;
		var offset = layer.Offset;
		var unitSize = quality.UnitSize;

		Collision.FrontOffset = new Vector3( 0f, 0f, depth * 0.5f + offset );
		Collision.BackOffset = new Vector3( 0f, 0f, depth * -0.5f + offset );

		foreach ( var triangle in FrontBackTriangles ) Collision.AddFrontBackTriangle( data, unitSize, triangle );

		foreach ( var block in SolidBlocks )
		{
			var (tri0, tri1) = block.Triangles;

			Collision.AddFrontBackTriangle( data, unitSize, tri0 );
			Collision.AddFrontBackTriangle( data, unitSize, tri1 );
		}

		foreach ( var cutFace in CutFaces ) Collision.AddCutFace( data, unitSize, cutFace );
	}

	private static void ReduceSolidBlocks( List<SolidBlock> blocks )
	{
		if ( blocks.Count < 2 ) return;

		// Merge adjacent blocks on the same row

		{
			var next = blocks[^1];

			for ( var i = blocks.Count - 2; i >= 0; --i )
			{
				var prev = blocks[i];

				if ( prev.MinY == next.MinY && prev.MaxY == next.MaxY && prev.MaxX == next.MinX )
				{
					prev = new SolidBlock( prev.MinX, prev.MinY, next.MaxX, prev.MaxY );
					blocks[i] = prev;
					blocks.RemoveAt( i + 1 );
				}

				next = prev;
			}
		}

		// Dumb and slow merging of vertically adjacent blocks
		// Only merge if at least one of the left or right sides are flush

		var changed = true;
		while ( changed )
		{
			changed = false;

			for ( var i = blocks.Count - 2; i >= 0; --i )
			{
				var prev = blocks[i];

				for ( var j = i + 1; j < blocks.Count; ++j )
				{
					var next = blocks[j];

					if ( next.MinY != prev.MaxY ) continue;

					if ( next.MinX >= prev.MaxX || next.MaxX <= prev.MinX ) continue;

					if ( next.MinX == prev.MinX && next.MaxX == prev.MaxX )
					{
						blocks[i] = prev = prev with { MaxY = next.MaxY };
						blocks.RemoveAt( j );
						j -= 1;
						changed = true;
						continue;
					}

					if ( next.MinX != prev.MinX && next.MaxX != prev.MaxX ) continue;

					if ( next.MinX < prev.MinX )
					{
						blocks[j] = next with { MaxX = prev.MinX };
						blocks[i] = prev = prev with { MaxY = next.MaxY };
						changed = true;
						continue;
					}

					if ( next.MinX > prev.MinX )
					{
						blocks[j] = prev with { MaxX = next.MinX };
						blocks[i] = prev = next with { MinY = prev.MinY };
						changed = true;
						continue;
					}

					if ( next.MaxX > prev.MaxX )
					{
						blocks[j] = next with { MinX = prev.MaxX };
						blocks[i] = prev = prev with { MaxY = next.MaxY };
						changed = true;
						continue;
					}

					if ( next.MaxX < prev.MaxX )
					{
						blocks[j] = prev with { MinX = next.MaxX };
						blocks[i] = prev = next with { MinY = prev.MinY };
						changed = true;
						continue;
					}
				}
			}
		}
	}

	public (List<Vector3> Vertices, List<int> Indices) CollisionMesh => (Collision.Vertices, Collision.Indices);
}
