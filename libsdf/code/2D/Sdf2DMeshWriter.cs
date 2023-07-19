using System;
using System.Collections.Generic;

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

				var prevNormal = PolygonMeshBuilder.Rotate90( curr - prev ).Normal;
				var currIndex = edgeLoop.Count - 1;

				for ( var i = 0; i < edgeLoop.Count; i++ )
				{
					var next = vertices[edgeLoop.FirstIndex + i];
					var nextNormal = PolygonMeshBuilder.Rotate90( next - curr ).Normal;

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

	private void InitPolyMeshBuilder( PolygonMeshBuilder builder )
	{
		foreach ( var edgeLoop in EdgeLoops )
		{
			builder.AddEdgeLoop( SourceVertices, edgeLoop.FirstIndex, edgeLoop.Count );
		}
	}

	private void ClipPolyMeshBuilder( PolygonMeshBuilder builder, Sdf2DLayer layer )
	{
		builder.Clip( new Vector3( 1f, 0f, 0f ), 0f );
		builder.Clip( new Vector3( 0f, 1f, 0f ), 0f );
		builder.Clip( new Vector3( -1f, 0f, 0f ), -layer.ChunkResolution );
		builder.Clip( new Vector3( 0f, -1f, 0f ), -layer.ChunkResolution );
	}

	private void WriteRenderMesh( Sdf2DLayer layer )
	{
		var quality = layer.Quality;
		var scale = quality.UnitSize;

		const float maxSmoothAngle = 33f;

		_cutMeshWriter.AddFaces( SourceVertices, EdgeLoops,
			new Vector3( 0f, 0f, layer.Offset ),
			new Vector3( scale, scale, layer.Depth ),
			layer.TexCoordSize, maxSmoothAngle );

		if ( layer.FrontFaceMaterial == null && layer.BackFaceMaterial == null ) return;

		return;

		using var polyMeshBuilder = PolygonMeshBuilder.Rent();

		polyMeshBuilder.MaxSmoothAngle = maxSmoothAngle;

		InitPolyMeshBuilder( polyMeshBuilder );

		switch ( layer.EdgeStyle )
		{
			case EdgeStyle.Sharp:
				polyMeshBuilder.Close( false );
				break;

			case EdgeStyle.Bevel:
				polyMeshBuilder.Bevel( layer.EdgeRadius, layer.EdgeRadius, false );
				polyMeshBuilder.Close( false );
				break;

			case EdgeStyle.Round:
				for ( var i = 0; i < layer.EdgeFaces; ++i )
				{
					var theta = MathF.PI * (i + 1f) / layer.EdgeFaces;
					var cos = MathF.Cos( theta );
					var sin = MathF.Sin( theta );

					polyMeshBuilder.Bevel( 1f - cos, sin, true );
				}

				polyMeshBuilder.Close( true );
				break;
		}

		ClipPolyMeshBuilder( polyMeshBuilder, layer );

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
				layer.TexCoordSize);
		}
	}

	private void WriteCollisionMesh( Sdf2DLayer layer )
	{
		return;

		var quality = layer.Quality;
		var scale = quality.UnitSize;

		using var polyMeshBuilder = PolygonMeshBuilder.Rent();

		InitPolyMeshBuilder( polyMeshBuilder );

		polyMeshBuilder.Close( false );

		ClipPolyMeshBuilder( polyMeshBuilder, layer );
	}
}
