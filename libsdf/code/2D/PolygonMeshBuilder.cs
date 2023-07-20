using System;
using System.Collections.Generic;

namespace Sandbox.Sdf;

public partial class PolygonMeshBuilder : Pooled<PolygonMeshBuilder>
{
	private int _nextEdgeIndex;
	private Edge[] _allEdges = new Edge[64];
	private readonly HashSet<int> _activeEdges = new HashSet<int>();

	private readonly List<Vector3> _vertices = new List<Vector3>();
	private readonly List<Vector3> _normals = new List<Vector3>();
	private readonly List<int> _indices = new List<int>();

	private float _prevDistance;
	private float _nextDistance;

	private float _invDistance;

	private float _prevPrevHeight;
	private float _prevHeight;
	private float _nextHeight;

	private float _prevPrevAngle;
	private float _prevAngle;
	private float _nextAngle;

	private float _minSmoothNormalDot;

	public int ActiveEdgeCount => _activeEdges.Count;
	public bool IsClosed => _activeEdges.Count == 0;

	public float MaxSmoothAngle = 0f;

	public IReadOnlyList<Vector3> Vertices => _vertices;
	public IReadOnlyList<Vector3> Normals => _normals;
	public IReadOnlyList<int> Indices => _indices;

	public void Clear()
	{
		_nextEdgeIndex = 0;
		_activeEdges.Clear();

		_vertices.Clear();
		_normals.Clear();
		_indices.Clear();

		_prevDistance = 0f;
		_nextDistance = 0f;

		_invDistance = 0f;

		_prevPrevHeight = 0f;
		_prevHeight = 0f;
		_nextHeight = 0f;

		_prevPrevAngle = 0f;
		_prevAngle = 0f;
		_nextAngle = 0f;

		_minSmoothNormalDot = 0f;
	}

	public override void Reset()
	{
		Clear();

		MaxSmoothAngle = 0f;
		Debug = false;
	}

	private static int NextPowerOfTwo( int value )
	{
		var po2 = 1;
		while ( po2 < value )
		{
			po2 <<= 1;
		}

		return po2;
	}

	private void EnsureCapacity( int toAdd )
	{
		if ( _nextEdgeIndex + toAdd > _allEdges.Length )
		{
			Array.Resize( ref _allEdges, NextPowerOfTwo( _nextEdgeIndex + toAdd ) );
		}
	}

	private int AddEdge( Vector2 origin, Vector2 tangent, float distance )
	{
		var edge = new Edge( _nextEdgeIndex++, origin, tangent, distance );
		_allEdges[edge.Index] = edge;
		return edge.Index;
	}

	public void AddEdgeLoop( IReadOnlyList<Vector2> vertices, int offset, int count )
	{
		var firstIndex = _nextEdgeIndex;

		EnsureCapacity( count );

		var prevVertex = vertices[offset + count - 1];
		for ( var i = 0; i < count; ++i )
		{
			var nextVertex = vertices[offset + i];

			_activeEdges.Add( AddEdge( prevVertex, Helpers.NormalizeSafe( nextVertex - prevVertex), _prevDistance ) );

			prevVertex = nextVertex;
		}

		var prevIndex = count - 1;
		for ( var i = 0; i < count; ++i )
		{
			ref var prevEdge = ref _allEdges[firstIndex + prevIndex];
			ref var nextEdge = ref _allEdges[firstIndex + i];
			ConnectEdges( ref prevEdge, ref nextEdge );
			prevIndex = i;
		}
	}

	[ThreadStatic]
	private static Dictionary<int, int> AddEdges_VertexMap;

	public void AddEdges( IReadOnlyList<Vector2> vertices, IReadOnlyList<(int Prev, int Next)> edges )
	{
		AddEdges_VertexMap ??= new Dictionary<int, int>();
		AddEdges_VertexMap.Clear();

		EnsureCapacity( edges.Count );

		foreach ( var (i, j) in edges )
		{
			var prev = vertices[i];
			var next = vertices[j];

			var index = AddEdge( prev, Helpers.NormalizeSafe( next - prev ), _prevDistance );

			_activeEdges.Add( index );
			AddEdges_VertexMap.Add( i, index );
		}

		for ( var i = 0; i < edges.Count; ++i )
		{
			var edge = edges[i];

			ref var prev = ref _allEdges[AddEdges_VertexMap[edge.Prev]];
			ref var next = ref _allEdges[AddEdges_VertexMap[edge.Next]];

			ConnectEdges( ref prev, ref next );
		}
	}

	private static float LerpRadians( float a, float b, float t )
	{
		var delta = b - a;
		delta -= MathF.Floor( delta * (0.5f / MathF.PI) ) * MathF.PI * 2f;

		if ( delta > MathF.PI )
		{
			delta -= MathF.PI * 2f;
		}

		return a + delta * Math.Clamp( t, 0f, 1f );
	}

	private (int Prev, int Next) AddVertices( ref Edge edge, bool forceMaxDistance = false )
	{
		if ( edge.Vertices.Prev > -1 )
		{
			return edge.Vertices;
		}

		var index = _vertices.Count;
		var prevNormal = -_allEdges[edge.PrevEdge].Normal;
		var nextNormal = -edge.Normal;

		var t = forceMaxDistance ? 1f : (edge.Distance - _prevDistance) * _invDistance;
		var height = _prevHeight + t * (_nextHeight - _prevHeight);

		var pos = new Vector3( edge.Origin.x, edge.Origin.y, height );

		if ( MathF.Abs( _nextDistance - _prevDistance ) <= 0.001f )
		{
			_vertices.Add( pos );
			_normals.Add( new Vector3( 0f, 0f, 1f ) );

			edge.Vertices = (index, index);
		}
		else
		{
			var angle = LerpRadians( _prevAngle, _nextAngle, t );
			var cos = MathF.Cos( angle );
			var sin = MathF.Sin( angle );

			if ( Vector2.Dot( prevNormal, nextNormal ) >= _minSmoothNormalDot )
			{
				var normal = new Vector3( (prevNormal.x + nextNormal.x) * cos, (prevNormal.y + nextNormal.y) * cos, sin * 2f ).Normal;

				_vertices.Add( pos );
				_normals.Add( normal );

				edge.Vertices = (index, index);
			}
			else
			{
				var normal0 = new Vector3( prevNormal.x * cos, prevNormal.y * cos, sin ).Normal;
				var normal1 = new Vector3( nextNormal.x * cos, nextNormal.y * cos, sin ).Normal;

				_vertices.Add( pos );
				_normals.Add( normal0 );

				_vertices.Add( pos );
				_normals.Add( normal1 );

				edge.Vertices = (index, index + 1);
			}
		}

		return edge.Vertices;
	}

	private void BlendNormals( float minHeight, float maxHeight, float minAngle, float maxAngle )
	{
		var invRange = minHeight < maxHeight ? 1f / (maxHeight - minHeight) : float.PositiveInfinity;

		var sinMax = MathF.Sin( maxAngle );
		var cosMax = MathF.Cos( maxAngle );

		for ( var i = 0; i < _vertices.Count; ++i )
		{
			var pos = _vertices[i];

			if ( pos.z < minHeight )
			{
				continue;
			}

			if ( pos.z >= maxHeight )
			{
				_normals[i] = Helpers.RotateNormal( _normals[i], sinMax, cosMax );
				continue;
			}

			var t = Math.Clamp( (pos.z - minHeight) * invRange, 0f, 1f );
			var angle = LerpRadians( minAngle, maxAngle, t );

			var sin = MathF.Sin( angle );
			var cos = MathF.Cos( angle );

			_normals[i] = Helpers.RotateNormal( _normals[i], sin, cos );
		}
	}

	private void AddTriangle( int a, int b, int c )
	{
		_indices.Add( a );
		_indices.Add( b );
		_indices.Add( c );
	}

	public void Close( bool smooth )
	{
		Bevel( float.PositiveInfinity, 0f, smooth );
	}

	public void Round( int faces, float width, float height, bool smooth )
	{
		var prevWidth = 0f;
		var prevHeight = 0f;

		for ( var i = 0; i < faces; ++i )
		{
			var theta = MathF.PI * 0.5f * (i + 1f) / faces;
			var cos = MathF.Cos( theta );
			var sin = MathF.Sin( theta );

			var nextWidth = 1f - cos;
			var nextHeight = sin;

			Bevel( (nextWidth - prevWidth) * width,
				(nextHeight - prevHeight) * height, true );

			prevWidth = nextWidth;
			prevHeight = nextHeight;
		}
	}
}