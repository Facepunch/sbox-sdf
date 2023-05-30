using System;

namespace Sandbox.Sdf;

internal partial class Sdf3DMeshWriter
{
	public void Write( Sdf3DArrayData data, Sdf3DVolume volume, bool renderMesh, bool collisionMesh )
	{
		var quality = volume.Quality;
		var size = quality.ChunkResolution;

		for ( var z = 0; z < size; ++z )
			for ( var y = 0; y < size; ++y )
				for ( int x = 0; x < size; ++x )
					AddTriangles( in data, x, y, z );
	}

	partial void AddTriangles( in Sdf3DArrayData data, int x, int y, int z );

	private void AddTriangle( int x, int y, int z, CubeVertex v0, CubeVertex v1, CubeVertex v2 )
	{
		throw new NotImplementedException();
	}
}
