using System;
using System.Collections.Generic;
using System.Threading;

namespace Sandbox.Sdf;

partial class Sdf2DMeshWriter : SdfMeshWriter<Sdf2DMeshWriter>
{
	private List<SourceEdge> SourceEdges { get; } = new();

	private class EmptyMeshWriter : IMeshWriter
	{
		public static EmptyMeshWriter Instance { get; } = new EmptyMeshWriter();

		public bool IsEmpty => true;

		public void ApplyTo( Mesh mesh )
		{

		}
	}

	public IMeshWriter FrontWriter => EmptyMeshWriter.Instance;
	public IMeshWriter BackWriter => EmptyMeshWriter.Instance;
	public IMeshWriter CutWriter => EmptyMeshWriter.Instance;

	public byte[] Samples { get; set; }

	public override void Clear()
	{
		SourceEdges.Clear();
	}

	public Vector2 DebugOffset { get; set; }
	public float DebugScale { get; set; } = 1f;

	public void WriteRenderMesh( Sdf2DArrayData data, Sdf2DLayer layer )
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
	}

	public void WriteCollisionMesh( Sdf2DArrayData data, Sdf2DLayer layer )
	{

	}

	public (List<Vector3> Vertices, List<int> Indices) CollisionMesh { get; } = (new List<Vector3>(), new List<int>());
}
