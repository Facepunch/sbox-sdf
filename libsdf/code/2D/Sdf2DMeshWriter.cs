using System;
using System.Collections.Generic;

namespace Sandbox.Sdf;

partial class Sdf2DMeshWriter : Pooled<Sdf2DMeshWriter>
{
	private List<SourceEdge> SourceEdges { get; } = new();

	private class FrontBackMeshWriter : IMeshWriter
	{
		public bool IsEmpty { get; set; }

		public void Clear()
		{
			throw new NotImplementedException();
		}

		public void AddFaces( PolygonMeshBuilder builder, Vector3 offset, Vector3 scale )
		{
			throw new NotImplementedException();
		}

		public void ApplyTo( Mesh mesh )
		{
			throw new NotImplementedException();
		}
	}

	private class CutMeshWriter : IMeshWriter
	{
		public bool IsEmpty { get; set; }

		public void Clear()
		{
			throw new NotImplementedException();
		}

		public void AddFaces( IReadOnlyList<Vector2> vertices, IReadOnlyList<EdgeLoop> edgeLoops, Vector3 offset, Vector3 scale )
		{
			throw new NotImplementedException();
		}

		public void ApplyTo( Mesh mesh )
		{
			throw new NotImplementedException();
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

		_cutMeshWriter.AddFaces( SourceVertices, EdgeLoops,
			new Vector3( 0f, 0f, layer.Offset ),
			new Vector3( scale, scale, layer.Depth ) );

		if ( layer.FrontFaceMaterial != null || layer.BackFaceMaterial != null )
		{
			using var polyMeshBuilder = PolygonMeshBuilder.Rent();

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
					new Vector3( scale, scale, 1f ) );
			}

			if ( layer.BackFaceMaterial != null )
			{
				_backMeshWriter.AddFaces( polyMeshBuilder,
					new Vector3( 0f, 0f, layer.Depth - 0.5f + layer.Offset ),
					new Vector3( scale, scale, -1f ) );
			}
		}
	}

	private void WriteCollisionMesh( Sdf2DLayer layer )
	{
		var quality = layer.Quality;
		var scale = quality.UnitSize;

		using var polyMeshBuilder = PolygonMeshBuilder.Rent();

		InitPolyMeshBuilder( polyMeshBuilder );

		polyMeshBuilder.Close( false );

		ClipPolyMeshBuilder( polyMeshBuilder, layer );
	}
}
