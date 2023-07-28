﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Sandbox.Diagnostics;
using Sandbox.Polygons;

namespace Sandbox.Sdf;

partial class Sdf2DMeshWriter : Pooled<Sdf2DMeshWriter>
{
	private List<SourceEdge> SourceEdges { get; } = new();

	public interface IVertexHelper<TVertex>
		where TVertex : unmanaged
	{
		Vector3 GetPosition( in TVertex vertex );
		TVertex Lerp( in TVertex a, in TVertex b, float t );
	}

	private abstract class MeshWriter<TVertex, THelper> : IMeshWriter
		where TVertex : unmanaged
		where THelper : IVertexHelper<TVertex>, new()
	{
		private readonly THelper _helper = new();

		public List<TVertex> Vertices { get; } = new();
		public List<int> Indices { get; } = new();

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

		public void Clip( in WorldQuality quality )
		{
			Clip( 0f, 0f, quality.ChunkSize, quality.ChunkSize );
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

			var x = Vector3.Dot( _helper.GetPosition( a ), normal ) - distance;
			var y = Vector3.Dot( _helper.GetPosition( b ), normal ) - distance;

			var t = x - y <= 0.0001f ? 0.5f : x / (x - y);

			index = Vertices.Count;
			Vertices.Add( _helper.Lerp( a, b, t ) );

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

				var a = _helper.GetPosition( Vertices[ai] );
				var b = _helper.GetPosition( Vertices[bi] );
				var c = _helper.GetPosition( Vertices[ci] );

				var aNeg = Vector3.Dot( normal, a ) - distance < -epsilon;
				var bNeg = Vector3.Dot( normal, b ) - distance < -epsilon;
				var cNeg = Vector3.Dot( normal, c ) - distance < -epsilon;

				switch (aNeg, bNeg, cNeg)
				{
					case (false, false, false ):
						Indices.Add( ai );
						Indices.Add( bi );
						Indices.Add( ci );
						break;

					case (true, false, false ):
						ClipOne( normal, distance, ai, bi, ci );
						break;
					case (false, true, false ):
						ClipOne( normal, distance, bi, ci, ai );
						break;
					case (false, false, true ):
						ClipOne( normal, distance, ci, ai, bi );
						break;

					case (false, true, true ):
						ClipTwo( normal, distance, ai, bi, ci );
						break;
					case (true, false, true ):
						ClipTwo( normal, distance, bi, ci, ai );
						break;
					case (true, true, false ):
						ClipTwo( normal, distance, ci, ai, bi );
						break;
				}
			}

			Indices.RemoveRange( 0, indexCount );
		}
	}

	private class FrontBackMeshWriter : MeshWriter<Vertex, VertexHelper>
	{
		public void AddFaces( PolygonMeshBuilder builder, Vector3 offset, Vector3 scale, float texCoordSize )
		{
			var uvScale = 1f / texCoordSize;

			var indexOffset = Vertices.Count;
			var normalScale = new Vector3( 1f / scale.x, 1f / scale.y, 1f / scale.z );

			for ( var i = 0; i < builder.Vertices.Count; ++i )
			{
				var pos = builder.Vertices[i] * scale;
				var normal = builder.Normals[i] * normalScale;

				Vertices.Add( new Vertex( offset + pos, normal.Normal, Vector3.Cross( normal, new Vector3( 0f, 1f, 0f ) ).Normal, pos * uvScale ) );
			}

			if ( scale.z >= 0f )
			{
				foreach ( var index in builder.Indices )
				{
					Indices.Add( indexOffset + index );
				}
			}
			else
			{
				for ( var i = builder.Indices.Count - 1; i >= 0; --i )
				{
					Indices.Add( indexOffset + builder.Indices[i] );
				}
			}
		}
	}

	private class CutMeshWriter : MeshWriter<Vertex, VertexHelper>
	{
		private Dictionary<int, (int Prev, int Next)> IndexMap { get; } = new();

		public void AddFaces( IReadOnlyList<Vector2> vertices, IReadOnlyList<EdgeLoop> edgeLoops, Vector3 offset, Vector3 scale, float texCoordSize, float maxSmoothAngle )
		{
			var minSmoothNormalDot = MathF.Cos( maxSmoothAngle * MathF.PI / 180f );

			static float GetV( Vector2 pos, Vector2 normal )
			{
				return Math.Abs( normal.y ) > Math.Abs( normal.x ) ? pos.x : pos.y;
			}

			var texCoordScale = texCoordSize == 0f ? 0f : 1f / texCoordSize;

			foreach ( var edgeLoop in edgeLoops )
			{
				IndexMap.Clear();

				if ( edgeLoop.Count < 3 )
				{
					continue;
				}

				var prev = vertices[edgeLoop.FirstIndex + edgeLoop.Count - 2];
				var curr = vertices[edgeLoop.FirstIndex + edgeLoop.Count - 1];

				var prevNormal = Helpers.NormalizeSafe( Helpers.Rotate90( prev - curr ) );
				var currIndex = edgeLoop.Count - 1;

				for ( var i = 0; i < edgeLoop.Count; i++ )
				{
					var next = vertices[edgeLoop.FirstIndex + i];
					var nextNormal = Helpers.NormalizeSafe( Helpers.Rotate90( curr - next ) );

					var prevV = GetV( curr * (Vector2) scale, prevNormal ) * texCoordScale;
					var nextV = GetV( curr * (Vector2) scale, nextNormal ) * texCoordScale;

					var index = Vertices.Count;
					var frontPos = offset + new Vector3( curr.x, curr.y, 0.5f ) * scale;
					var backPos = offset + new Vector3( curr.x, curr.y, -0.5f ) * scale;

					var frontU = 0f;
					var backU = scale.z * texCoordScale;

					if ( Vector2.Dot( prevNormal, nextNormal ) >= minSmoothNormalDot && Math.Abs( prevV - nextV ) <= 0.001f )
					{
						IndexMap.Add( currIndex, (index, index) );

						var normal = Helpers.NormalizeSafe( prevNormal + nextNormal );

						Vertices.Add( new Vertex( frontPos, normal, new Vector3( 0f, 0f, 1f ), new Vector2( frontU, prevV ) ) );
						Vertices.Add( new Vertex( backPos, normal, new Vector3( 0f, 0f, 1f ), new Vector2( backU, prevV ) ) );
					}
					else
					{
						IndexMap.Add( currIndex, (index, index + 2) );

						Vertices.Add( new Vertex( frontPos, prevNormal, new Vector3( 0f, 0f, 1f ), new Vector2( frontU, prevV ) ) );
						Vertices.Add( new Vertex( backPos, prevNormal, new Vector3( 0f, 0f, 1f ), new Vector2( backU, prevV ) ) );

						Vertices.Add( new Vertex( frontPos, nextNormal, new Vector3( 0f, 0f, 1f ), new Vector2( frontU, nextV ) ) );
						Vertices.Add( new Vertex( backPos, nextNormal, new Vector3( 0f, 0f, 1f ), new Vector2( backU, nextV ) ) );
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

	private readonly struct CollisionVertexHelper : IVertexHelper<Vector3>
	{
		public Vector3 GetPosition( in Vector3 vertex )
		{
			return vertex;
		}

		public Vector3 Lerp( in Vector3 a, in Vector3 b, float t )
		{
			return Vector3.Lerp( a, b, t );
		}
	}

	private class CollisionMeshWriter : MeshWriter<Vector3, CollisionVertexHelper>
	{
		public void AddFaces( PolygonMeshBuilder builder, Vector3 offset, Vector3 scale )
		{
			var indexOffset = Vertices.Count;

			foreach ( var v in builder.Vertices )
			{
				Vertices.Add( offset + v * scale );
			}

			if ( scale.z >= 0f )
			{
				foreach ( var index in builder.Indices )
				{
					Indices.Add( indexOffset + index );
				}
			}
			else
			{
				for ( var i = builder.Indices.Count - 1; i >= 0; --i )
				{
					Indices.Add( indexOffset + builder.Indices[i] );
				}
			}
		}

		public void AddFaces( IReadOnlyList<Vector2> vertices, IReadOnlyList<EdgeLoop> edgeLoops, Vector3 offset, Vector3 scale )
		{
			foreach ( var edgeLoop in edgeLoops )
			{
				if ( edgeLoop.Count < 2 )
				{
					continue;
				}

				var prevIndex = Vertices.Count + (edgeLoop.Count - 1) * 2;

				for ( var i = 0; i < edgeLoop.Count; i++ )
				{
					var next = vertices[edgeLoop.FirstIndex + i];
					var frontPos = offset + new Vector3( next.x, next.y, 0.5f ) * scale;
					var backPos = offset + new Vector3( next.x, next.y, -0.5f ) * scale;

					var nextIndex = Vertices.Count;

					Vertices.Add( frontPos );
					Vertices.Add( backPos );

					Indices.Add( prevIndex + 0 );
					Indices.Add( nextIndex + 0 );
					Indices.Add( prevIndex + 1 );

					Indices.Add( prevIndex + 1 );
					Indices.Add( nextIndex + 0 );
					Indices.Add( nextIndex + 1 );

					prevIndex = nextIndex;
				}
			}
		}
	}

	private readonly FrontBackMeshWriter _frontMeshWriter = new();
	private readonly FrontBackMeshWriter _backMeshWriter = new();
	private readonly CutMeshWriter _cutMeshWriter = new();
	private readonly CollisionMeshWriter _collisionMeshWriter = new();

	public IMeshWriter FrontWriter => _frontMeshWriter;
	public IMeshWriter BackWriter => _backMeshWriter;
	public IMeshWriter CutWriter => _cutMeshWriter;

	public (List<Vector3> Vertices, List<int> Indices) CollisionMesh =>
		(_collisionMeshWriter.Vertices, _collisionMeshWriter.Indices);

	public byte[] Samples { get; set; }

	public override void Reset()
	{
		SourceEdges.Clear();

		_frontMeshWriter.Clear();
		_backMeshWriter.Clear();
		_cutMeshWriter.Clear();
		_collisionMeshWriter.Clear();
	}

	public Vector2 DebugOffset { get; set; }
	public float DebugScale { get; set; } = 1f;

	public void Write( Sdf2DArrayData data, Sdf2DLayer layer, bool renderMesh, bool collisionMesh )
	{
		SourceEdges.Clear();

		var quality = layer.Quality;
		var size = quality.ChunkResolution;

		// Find edges between solid and empty

		for ( var y = -2; y <= size + 2; ++y )
		{
			for ( int x = -2; x <= size + 2; ++x )
			{
				var aRaw = data[x + 0, y + 0];
				var bRaw = data[x + 1, y + 0];
				var cRaw = data[x + 0, y + 1];
				var dRaw = data[x + 1, y + 1];

				AddSourceEdges( x, y, aRaw, bRaw, cRaw, dRaw );
			}
		}

		FindEdgeLoops( data, layer.MaxSmoothAngle, 0.25f );

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

		// TODO: this seemed to leave some negative polys on their own, so just doing them all now

		offset = index;
		count = EdgeLoops.Count - index;

		index += count;

		return count > 0;

		offset = index;
		count = 1;

		Assert.True( EdgeLoops[offset].Area > 0f );

		while ( offset + count < EdgeLoops.Count && EdgeLoops[offset + count].Area < 0f )
		{
			++count;
		}

		index += count;

		return count > 0;
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

	private string PrintEdgeLoops( int offset, int count, out Vector2 pos )
	{
		var writer = new StringWriter();

		pos = 0f;
		var vertexCount = 0;

		for ( var i = 0; i < count; ++i )
		{
			var edgeLoop = EdgeLoops[offset + i];

			for ( var j = 0; j < edgeLoop.Count; ++j )
			{
				var vertex = SourceVertices[edgeLoop.FirstIndex + j];
				writer.Write( $"({vertex.x:R}, {vertex.y:R})," );

				pos += vertex;
			}

			vertexCount += edgeLoop.Count;

			writer.WriteLine();
		}

		if ( vertexCount > 0 )
		{
			pos /= vertexCount;
		}

		return writer.ToString();
	}

	private void WriteRenderMesh( Sdf2DLayer layer )
	{
		var quality = layer.Quality;
		var scale = quality.UnitSize;
		var edgeRadius = layer.EdgeStyle == EdgeStyle.Sharp ? 0f : layer.EdgeRadius;

		if ( layer.CutFaceMaterial != null )
		{
			_cutMeshWriter.AddFaces( SourceVertices, EdgeLoops,
				new Vector3( 0f, 0f, layer.Offset ),
				new Vector3( scale, scale, layer.Depth - edgeRadius * 2f ),
				layer.TexCoordSize, layer.MaxSmoothAngle );

			_cutMeshWriter.Clip( quality );
		}

		if ( layer.FrontFaceMaterial == null && layer.BackFaceMaterial == null ) return;

		using var polyMeshBuilder = PolygonMeshBuilder.Rent();

		polyMeshBuilder.MaxSmoothAngle = layer.MaxSmoothAngle;

		var bevelScale = layer.EdgeRadius / scale;

		var index = 0;
		while ( NextPolygon( ref index, out var offset, out var count ) )
		{
			InitPolyMeshBuilder( polyMeshBuilder, offset, count );

			switch ( layer.EdgeStyle )
			{
				case EdgeStyle.Sharp:
					polyMeshBuilder.Close( false );
					break;

				case EdgeStyle.Bevel:
					polyMeshBuilder.Bevel( bevelScale, layer.EdgeRadius, false );
					polyMeshBuilder.Close( false );
					break;

				case EdgeStyle.Round:
					polyMeshBuilder.Round( layer.EdgeFaces, bevelScale, layer.EdgeRadius, true );
					polyMeshBuilder.Close( true );
					break;
			}

			if ( layer.FrontFaceMaterial != null )
			{
				_frontMeshWriter.AddFaces( polyMeshBuilder,
					new Vector3( 0f, 0f, layer.Depth * 0.5f + layer.Offset - edgeRadius ),
					new Vector3( scale, scale, 1f ),
					layer.TexCoordSize );
			}

			if ( layer.BackFaceMaterial != null )
			{
				_backMeshWriter.AddFaces( polyMeshBuilder,
					new Vector3( 0f, 0f, layer.Depth * -0.5f + layer.Offset + edgeRadius ),
					new Vector3( scale, scale, -1f ),
					layer.TexCoordSize );
			}
		}

		_frontMeshWriter.Clip( quality );
		_backMeshWriter.Clip( quality );
	}

	private void WriteCollisionMesh( Sdf2DLayer layer )
	{
		var quality = layer.Quality;
		var scale = quality.UnitSize;

		_collisionMeshWriter.AddFaces( SourceVertices, EdgeLoops,
			new Vector3( 0f, 0f, layer.Offset ),
			new Vector3( scale, scale, layer.Depth ) );

		using var polyMeshBuilder = PolygonMeshBuilder.Rent();

		polyMeshBuilder.MaxSmoothAngle = 180f;

		var index = 0;
		while ( NextPolygon( ref index, out var offset, out var count ) )
		{
			InitPolyMeshBuilder( polyMeshBuilder, offset, count );

			polyMeshBuilder.Close( true );

			_collisionMeshWriter.AddFaces( polyMeshBuilder,
				new Vector3( 0f, 0f, layer.Depth * 0.5f + layer.Offset ),
				new Vector3( scale, scale, 1f ) );

			_collisionMeshWriter.AddFaces( polyMeshBuilder,
				new Vector3( 0f, 0f, layer.Depth * -0.5f + layer.Offset ),
				new Vector3( scale, scale, -1f ) );
		}

		_collisionMeshWriter.Clip( quality );
	}
}
