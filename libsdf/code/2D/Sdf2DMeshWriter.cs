using System;
using System.Collections.Generic;
using Sandbox.Diagnostics;

namespace Sandbox.Sdf;

partial class Sdf2DMeshWriter : Pooled<Sdf2DMeshWriter>
{
	private List<SourceEdge> SourceEdges { get; } = new();

	private abstract class MeshWriter : IMeshWriter
	{
		protected List<Vertex> Vertices { get; } = new();
		protected List<int> Indices { get; } = new();

		public bool IsEmpty => Indices.Count == 0;

		public virtual void Clear()
		{
			Vertices.Clear();
			Indices.Clear();
		}

		public void ApplyTo( Mesh mesh )
		{
			ThreadSafe.AssertIsMainThread();

			if ( mesh == null )
			{
				return;
			}

			if ( mesh.HasVertexBuffer )
			{
				if ( Indices.Count > 0 )
				{
					if ( mesh.IndexCount < Indices.Count )
					{
						mesh.SetIndexBufferSize( Indices.Count );
					}

					if ( mesh.VertexCount < Vertices.Count )
					{
						mesh.SetVertexBufferSize( Vertices.Count );
					}

					mesh.SetIndexBufferData( Indices );
					mesh.SetVertexBufferData( Vertices );
				}

				mesh.SetIndexRange( 0, Indices.Count );
			}
			else if ( Indices.Count > 0 )
			{
				mesh.CreateVertexBuffer( Vertices.Count, Vertex.Layout, Vertices );
				mesh.CreateIndexBuffer( Indices.Count, Indices );
			}
		}

		public void Clip( float xMin, float yMin, float xMax, float yMax )
		{
			Clip( new Vector3( 1f, 0f, 0f ), xMin );
			Clip( new Vector3( -1f, 0f, 0f ), -xMax );

			Clip( new Vector3( 0f, 1f, 0f ), yMin );
			Clip( new Vector3( 0f, -1f, 0f ), -yMax );
		}

		private Dictionary<(int Pos, int Neg), int> ClippedEdges { get; } = new();

		private int ClipEdge( Vector3 normal, float distance, int posIndex, int negIndex )
		{
			if ( ClippedEdges.TryGetValue( (posIndex, negIndex), out var index ) )
			{
				return index;
			}

			var a = Vertices[posIndex];
			var b = Vertices[negIndex];

			var x = Vector3.Dot( a.Position, normal ) - distance;
			var y = Vector3.Dot( b.Position, normal ) - distance;

			var t = x - y <= 0.0001f ? 0.5f : x / (x - y);

			index = Vertices.Count;
			Vertices.Add( Vertex.Lerp( a, b, t ) );

			ClippedEdges.Add( (posIndex, negIndex), index );

			return index;
		}

		private void ClipOne( Vector3 normal, float distance, int negIndex, int posAIndex, int posBIndex )
		{
			var clipAIndex = ClipEdge( normal, distance, posAIndex, negIndex );
			var clipBIndex = ClipEdge( normal, distance, posBIndex, negIndex );

			Indices.Add( clipAIndex );
			Indices.Add( posAIndex );
			Indices.Add( posBIndex );

			Indices.Add( clipAIndex );
			Indices.Add( posBIndex );
			Indices.Add( clipBIndex );
		}

		private void ClipTwo( Vector3 normal, float distance, int posIndex, int negAIndex, int negBIndex )
		{
			var clipAIndex = ClipEdge( normal, distance, posIndex, negAIndex );
			var clipBIndex = ClipEdge( normal, distance, posIndex, negBIndex );

			Indices.Add( posIndex );
			Indices.Add( clipAIndex );
			Indices.Add( clipBIndex );
		}

		public void Clip( Vector3 normal, float distance )
		{
			const float epsilon = 0.001f;

			ClippedEdges.Clear();

			var indexCount = Indices.Count;

			for ( var i = 0; i < indexCount; i += 3 )
			{
				var ai = Indices[i + 0];
				var bi = Indices[i + 1];
				var ci = Indices[i + 2];

				var a = Vertices[ai];
				var b = Vertices[bi];
				var c = Vertices[ci];

				var aNeg = Vector3.Dot( normal, a.Position ) - distance < -epsilon;
				var bNeg = Vector3.Dot( normal, b.Position ) - distance < -epsilon;
				var cNeg = Vector3.Dot( normal, c.Position ) - distance < -epsilon;

				switch (aNeg, bNeg, cNeg)
				{
					case (false, false, false):
						Indices.Add( ai );
						Indices.Add( bi );
						Indices.Add( ci );
						break;

					case (true, false, false):
						ClipOne( normal, distance, ai, bi, ci );
						break;
					case (false, true, false):
						ClipOne( normal, distance, bi, ci, ai );
						break;
					case (false, false, true):
						ClipOne( normal, distance, ci, ai, bi );
						break;

					case (false, true, true):
						ClipTwo( normal, distance, ai, bi, ci );
						break;
					case (true, false, true):
						ClipTwo( normal, distance, bi, ci, ai );
						break;
					case (true, true, false):
						ClipTwo( normal, distance, ci, ai, bi );
						break;
				}
			}

			Indices.RemoveRange( 0, indexCount );
		}
	}

	private class FrontBackMeshWriter : MeshWriter
	{
		public void AddFaces( PolygonMeshBuilder builder, Vector3 offset, Vector3 scale, float texCoordSize )
		{

		}
	}

	private class CutMeshWriter : MeshWriter
	{
		private Dictionary<int, (int Prev, int Next)> IndexMap { get; } = new();

		public void AddFaces( IReadOnlyList<Vector2> vertices, IReadOnlyList<EdgeLoop> edgeLoops, Vector3 offset, Vector3 scale, float texCoordSize, float maxSmoothAngle )
		{
			var minSmoothNormalDot = MathF.Cos( maxSmoothAngle * MathF.PI / 180f );

			foreach ( var edgeLoop in edgeLoops )
			{
				IndexMap.Clear();

				if ( edgeLoop.Count < 3 )
				{
					continue;
				}

				var prev = vertices[edgeLoop.FirstIndex + edgeLoop.Count - 2];
				var curr = vertices[edgeLoop.FirstIndex + edgeLoop.Count - 1];

				var prevNormal = PolygonMeshBuilder.Rotate90( prev - curr ).Normal;
				var currIndex = edgeLoop.Count - 1;

				for ( var i = 0; i < edgeLoop.Count; i++ )
				{
					var next = vertices[edgeLoop.FirstIndex + i];
					var nextNormal = PolygonMeshBuilder.Rotate90( curr - next ).Normal;

					var index = Vertices.Count;
					var frontPos = offset + new Vector3( curr.x, curr.y, 0.5f ) * scale;
					var backPos = offset + new Vector3( curr.x, curr.y, -0.5f ) * scale;

					if ( Vector2.Dot( prevNormal, nextNormal ) >= minSmoothNormalDot )
					{
						IndexMap.Add( currIndex, (index, index) );

						var normal = (prevNormal + nextNormal).Normal;

						Vertices.Add( new Vertex( frontPos, normal, new Vector3( 0f, 0f, -1f ), Vector2.Zero ) );
						Vertices.Add( new Vertex( backPos, normal, new Vector3( 0f, 0f, -1f ), Vector2.Zero ) );
					}
					else
					{
						IndexMap.Add( currIndex, (index, index + 2) );

						Vertices.Add( new Vertex( frontPos, prevNormal, new Vector3( 0f, 0f, -1f ), Vector2.Zero ) );
						Vertices.Add( new Vertex( backPos, prevNormal, new Vector3( 0f, 0f, -1f ), Vector2.Zero ) );

						Vertices.Add( new Vertex( frontPos, nextNormal, new Vector3( 0f, 0f, -1f ), Vector2.Zero ) );
						Vertices.Add( new Vertex( backPos, nextNormal, new Vector3( 0f, 0f, -1f ), Vector2.Zero ) );
					}

					currIndex = i;
					prevNormal = nextNormal;
					prev = curr;
					curr = next;
				}

				var a = IndexMap[edgeLoop.Count - 1];
				for ( var i = 0; i < edgeLoop.Count; i++ )
				{
					var b = IndexMap[i];

					Indices.Add( a.Next + 0 );
					Indices.Add( b.Prev + 0 );
					Indices.Add( a.Next + 1 );

					Indices.Add( a.Next + 1 );
					Indices.Add( b.Prev + 0 );
					Indices.Add( b.Prev + 1 );

					a = b;
				}
			}
		}
	}

	private readonly FrontBackMeshWriter _frontMeshWriter = new();
	private readonly FrontBackMeshWriter _backMeshWriter = new();
	private readonly CutMeshWriter _cutMeshWriter = new();

	public IMeshWriter FrontWriter => _frontMeshWriter;
	public IMeshWriter BackWriter => _backMeshWriter;
	public IMeshWriter CutWriter => _cutMeshWriter;

	public (List<Vector3> Vertices, List<int> Indices) CollisionMesh { get; } = (new List<Vector3>(), new List<int>());

	public byte[] Samples { get; set; }

	public override void Reset()
	{
		SourceEdges.Clear();

		_frontMeshWriter.Clear();
		_backMeshWriter.Clear();
		_cutMeshWriter.Clear();

		CollisionMesh.Vertices.Clear();
		CollisionMesh.Indices.Clear();
	}

	public Vector2 DebugOffset { get; set; }
	public float DebugScale { get; set; } = 1f;

	public void Write( Sdf2DArrayData data, Sdf2DLayer layer, bool renderMesh, bool collisionMesh )
	{
		SourceEdges.Clear();

		var quality = layer.Quality;
		var size = quality.ChunkResolution;

		// Find edges between solid and empty

		for ( var y = -2; y <= size + 1; ++y )
		{
			for ( int x = -2; x <= size + 1; ++x )
			{
				var aRaw = data[x + 0, y + 0];
				var bRaw = data[x + 1, y + 0];
				var cRaw = data[x + 0, y + 1];
				var dRaw = data[x + 1, y + 1];

				AddSourceEdges( x, y, aRaw, bRaw, cRaw, dRaw );
			}
		}

		FindEdgeLoops( data );

		if ( renderMesh )
		{
			WriteRenderMesh( layer );
		}

		if ( collisionMesh )
		{
			WriteCollisionMesh( layer );
		}
	}

	private bool NextPolygon( ref int index, out int offset, out int count )
	{
		if ( index >= EdgeLoops.Count )
		{
			offset = count = default;
			return false;
		}

		offset = index;
		count = 1;

		Assert.True( EdgeLoops[offset].Area > 0f );

		while ( offset + count < EdgeLoops.Count && EdgeLoops[offset + count].Area < 0f )
		{
			++count;
		}

		return true;
	}

	private void InitPolyMeshBuilder( PolygonMeshBuilder builder, int offset, int count )
	{
		builder.Clear();

		for ( var i = 0; i < count; ++i )
		{
			var edgeLoop = EdgeLoops[offset + i];
			builder.AddEdgeLoop( SourceVertices, edgeLoop.FirstIndex, edgeLoop.Count );
		}
	}

	private void WriteRenderMesh( Sdf2DLayer layer )
	{
		var quality = layer.Quality;
		var scale = quality.UnitSize;

		const float maxSmoothAngle = 33f;

		if ( layer.CutFaceMaterial != null )
		{
			_cutMeshWriter.AddFaces( SourceVertices, EdgeLoops,
				new Vector3( 0f, 0f, layer.Offset ),
				new Vector3( scale, scale, layer.Depth ),
				layer.TexCoordSize, maxSmoothAngle );

			_cutMeshWriter.Clip( 0f, 0f, quality.ChunkSize, quality.ChunkSize );
		}

		if ( layer.FrontFaceMaterial == null && layer.BackFaceMaterial == null ) return;

		return;

		using var polyMeshBuilder = PolygonMeshBuilder.Rent();

		polyMeshBuilder.MaxSmoothAngle = maxSmoothAngle;

		var index = 0;
		while ( NextPolygon( ref index, out var offset, out var count ) )
		{
			InitPolyMeshBuilder( polyMeshBuilder, offset, count );

			switch ( layer.EdgeStyle )
			{
				case EdgeStyle.Sharp:
					// polyMeshBuilder.Close( false );
					break;

				case EdgeStyle.Bevel:
					polyMeshBuilder.Bevel( layer.EdgeRadius, layer.EdgeRadius, false );
					// polyMeshBuilder.Close( false );
					break;

				case EdgeStyle.Round:
					for ( var i = 0; i < layer.EdgeFaces; ++i )
					{
						var theta = MathF.PI * (i + 1f) / layer.EdgeFaces;
						var cos = MathF.Cos( theta );
						var sin = MathF.Sin( theta );

						polyMeshBuilder.Bevel( 1f - cos, sin, true );
					}

					// polyMeshBuilder.Close( true );
					break;
			}

			if ( layer.FrontFaceMaterial != null )
			{
				_frontMeshWriter.AddFaces( polyMeshBuilder,
					new Vector3( 0f, 0f, layer.Depth * 0.5f + layer.Offset ),
					new Vector3( scale, scale, 1f ),
					layer.TexCoordSize );
			}

			if ( layer.BackFaceMaterial != null )
			{
				_backMeshWriter.AddFaces( polyMeshBuilder,
					new Vector3( 0f, 0f, layer.Depth - 0.5f + layer.Offset ),
					new Vector3( scale, scale, -1f ),
					layer.TexCoordSize );
			}
		}

		_frontMeshWriter.Clip( 0f, 0f, quality.ChunkSize, quality.ChunkSize );
		_backMeshWriter.Clip( 0f, 0f, quality.ChunkSize, quality.ChunkSize );
	}

	private void WriteCollisionMesh( Sdf2DLayer layer )
	{
		return;

		var quality = layer.Quality;
		var scale = quality.UnitSize;

		using var polyMeshBuilder = PolygonMeshBuilder.Rent();

		polyMeshBuilder.MaxSmoothAngle = 180f;

		var index = 0;
		while ( NextPolygon( ref index, out var offset, out var count ) )
		{
			InitPolyMeshBuilder( polyMeshBuilder, offset, count );

			// polyMeshBuilder.Close( true );
		}
	}
}
