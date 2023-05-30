namespace Sandbox.Sdf;

partial class Sdf3DMeshWriter
{
	partial void AddTriangles( in Sdf3DArrayData data, int x, int y, int z )
	{
		var aRaw = data[x + 0, y + 0, z + 0];
		var bRaw = data[x + 1, y + 0, z + 0];
		var cRaw = data[x + 0, y + 1, z + 0];
		var dRaw = data[x + 1, y + 1, z + 0];

		var eRaw = data[x + 0, y + 0, z + 1];
		var fRaw = data[x + 1, y + 0, z + 1];
		var gRaw = data[x + 0, y + 1, z + 1];
		var hRaw = data[x + 1, y + 1, z + 1];

		var a = aRaw < 128 ? CubeConfiguration.A : 0;
		var b = bRaw < 128 ? CubeConfiguration.B : 0;
		var c = cRaw < 128 ? CubeConfiguration.C : 0;
		var d = dRaw < 128 ? CubeConfiguration.D : 0;

		var e = eRaw < 128 ? CubeConfiguration.A : 0;
		var f = fRaw < 128 ? CubeConfiguration.B : 0;
		var g = gRaw < 128 ? CubeConfiguration.C : 0;
		var h = hRaw < 128 ? CubeConfiguration.D : 0;

		var config = a | b | c | d | e | f | g | h;

		switch ( config )
		{
			case CubeConfiguration.None:
			case CubeConfiguration.A | CubeConfiguration.B
				| CubeConfiguration.C | CubeConfiguration.D 
				| CubeConfiguration.E | CubeConfiguration.F
				| CubeConfiguration.G | CubeConfiguration.H:
				return;

			case CubeConfiguration.A | CubeConfiguration.B
				| CubeConfiguration.C | CubeConfiguration.D:
				AddTriangle( x, y, z, CubeVertex.A, CubeVertex.C, CubeVertex.B );
				AddTriangle( x, y, z, CubeVertex.C, CubeVertex.D, CubeVertex.B );
				return;
		}
	}
}