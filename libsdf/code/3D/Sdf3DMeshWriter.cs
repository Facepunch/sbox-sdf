using System;
using System.Collections.Generic;

namespace Sandbox.Sdf;

internal partial class Sdf3DMeshWriter : SdfMeshWriter<Sdf3DMeshWriter>
{
	private List<Triangle> Triangles { get; } = new List<Triangle>();
	private Dictionary<VertexKey, int> VertexMap { get; } = new Dictionary<VertexKey, int>();

	public List<Vertex> Vertices { get; } = new List<Vertex>();
	public List<Vector3> VertexPositions { get; } = new List<Vector3>();
	public List<int> Indices { get; } = new List<int>();

	public override void Clear()
	{
		Triangles.Clear();
		VertexMap.Clear();

		Vertices.Clear();
		VertexPositions.Clear();
		Indices.Clear();
	}

	public void Write( Sdf3DArrayData data, Sdf3DVolume volume )
	{
		Triangles.Clear();
		VertexMap.Clear();

		var baseIndex = Vertices.Count;

		var quality = volume.Quality;
		var size = quality.ChunkResolution;

		for ( var z = 0; z < size; ++z )
			for ( var y = 0; y < size; ++y )
				for ( int x = 0; x < size; ++x )
					AddTriangles( in data, x, y, z );

		var unitSize = quality.UnitSize;

		foreach ( var triangle in Triangles )
		{
			var p0 = GetVertexPos( in data, triangle.V0 ) * unitSize;
			var p1 = GetVertexPos( in data, triangle.V0 ) * unitSize;
			var p2 = GetVertexPos( in data, triangle.V0 ) * unitSize;

			var normal = Vector3.Cross( p1 - p0, p2 - p0 ).Normal;

			Indices.Add( AddVertex( triangle.V0, p0, normal ) );
			Indices.Add( AddVertex( triangle.V1, p1, normal ) );
			Indices.Add( AddVertex( triangle.V2, p2, normal ) );
		}

		for ( var i = baseIndex; i < Vertices.Count; ++i )
		{
			var vertex = Vertices[i];

			Vertices[i] = vertex with { Normal = vertex.Normal.Normal };
		}
	}

	public void ApplyTo( Mesh mesh )
	{
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
			mesh.CreateVertexBuffer( Vertices.Count, Vertex.Layout, Vertices );
			mesh.CreateIndexBuffer( Indices.Count, Indices );
		}
	}

	private static Vector3 GetVertexPos( in Sdf3DArrayData data, VertexKey key )
	{
		switch ( key.Vertex )
		{
			case NormalizedVertex.A:
				return new Vector3( key.X, key.Y, key.Z );

			case NormalizedVertex.AB:
			{
				var a = data[key.X, key.Y, key.Z] - 127.5f;
				var b = data[key.X + 1, key.Y, key.Z] - 127.5f;
				var t = a / (a - b);
				return new Vector3( key.X + t, key.Y, key.Z );
			}

			case NormalizedVertex.AC:
			{
				var a = data[key.X, key.Y, key.Z] - 127.5f;
				var c = data[key.X, key.Y + 1, key.Z] - 127.5f;
				var t = a / (a - c);
				return new Vector3( key.X, key.Y + t, key.Z );
			}

			case NormalizedVertex.AE:
			{
				var a = data[key.X, key.Y, key.Z] - 127.5f;
				var e = data[key.X, key.Y, key.Z + 1] - 127.5f;
				var t = a / (a - e);
				return new Vector3( key.X, key.Y, key.Z + t );
			}

			default:
				throw new NotImplementedException();
		}
	}

	partial void AddTriangles( in Sdf3DArrayData data, int x, int y, int z );

	private void AddTriangle( int x, int y, int z, CubeVertex v0, CubeVertex v1, CubeVertex v2 )
	{
		Triangles.Add( new Triangle( x, y, z, v0, v1, v2 ) );
	}

	private int AddVertex( VertexKey key, Vector3 pos, Vector3 normal )
	{
		if ( !VertexMap.TryGetValue( key, out var index ) )
		{
			index = Vertices.Count;

			Vertices.Add( new Vertex( pos, normal ) );
			VertexPositions.Add( pos );
		}
		else
		{
			var vertex = Vertices[index];

			Vertices[index] = vertex with { Normal = vertex.Normal + normal };
		}

		return index;
	}
}
