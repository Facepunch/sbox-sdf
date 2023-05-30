using System;
using System.Runtime.InteropServices;

namespace Sandbox.Sdf;

partial class Sdf2DMeshWriter
{
	/// <summary>
	/// <code>
	/// c - d
	/// |   |
	/// a - b
	/// </code>
	/// </summary>
	[Flags]
	private enum SquareConfiguration : byte
	{
		None = 0,

		A = 1,
		B = 2,
		C = 4,
		D = 8,

		AB = A | B,
		AC = A | C,
		BD = B | D,
		CD = C | D,

		AD = A | D,
		BC = B | C,

		ABC = A | B | C,
		ABD = A | B | D,
		ACD = A | C | D,
		BCD = B | C | D,

		ABCD = A | B | C | D
	}

	private enum NormalizedVertex : byte
	{
		A,
		AB,
		AC
	}

	private enum SquareVertex : byte
	{
		A,
		B,
		C,
		D,

		AB,
		AC,
		BD,
		CD
	}

	private record struct FrontBackTriangle( VertexKey V0, VertexKey V1, VertexKey V2 )
	{
		public FrontBackTriangle( int x, int y, SquareVertex V0, SquareVertex V1, SquareVertex V2 )
			: this( VertexKey.Normalize( x, y, V0 ), VertexKey.Normalize( x, y, V1 ), VertexKey.Normalize( x, y, V2 ) )
		{
		}

		public FrontBackTriangle Flipped => new( V0, V2, V1 );
	}

	private record struct CutFace( VertexKey V0, VertexKey V1 )
	{
		public CutFace( int x, int y, SquareVertex V0, SquareVertex V1 )
			: this( VertexKey.Normalize( x, y, V0 ), VertexKey.Normalize( x, y, V1 ) )
		{
		}
	}

	private record struct VertexKey( int X, int Y, NormalizedVertex Vertex )
	{
		public static VertexKey Normalize( int x, int y, SquareVertex vertex )
		{
			switch ( vertex )
			{
				case SquareVertex.A:
					return new VertexKey( x, y, NormalizedVertex.A );

				case SquareVertex.AB:
					return new VertexKey( x, y, NormalizedVertex.AB );

				case SquareVertex.AC:
					return new VertexKey( x, y, NormalizedVertex.AC );


				case SquareVertex.B:
					return new VertexKey( x + 1, y, NormalizedVertex.A );

				case SquareVertex.C:
					return new VertexKey( x, y + 1, NormalizedVertex.A );

				case SquareVertex.D:
					return new VertexKey( x + 1, y + 1, NormalizedVertex.A );


				case SquareVertex.BD:
					return new VertexKey( x + 1, y, NormalizedVertex.AC );

				case SquareVertex.CD:
					return new VertexKey( x, y + 1, NormalizedVertex.AB );


				default:
					throw new NotImplementedException();
			}
		}
	}
	
	private record struct SolidBlock( int MinX, int MinY, int MaxX, int MaxY )
	{
		public (FrontBackTriangle Tri0, FrontBackTriangle Tri1) Triangles
		{
			get
			{
				var a = new VertexKey( MinX, MinY, NormalizedVertex.A );
				var b = new VertexKey( MaxX, MinY, NormalizedVertex.A );
				var c = new VertexKey( MinX, MaxY, NormalizedVertex.A );
				var d = new VertexKey( MaxX, MaxY, NormalizedVertex.A );

				var tri0 = new FrontBackTriangle( a, c, b );
				var tri1 = new FrontBackTriangle( c, d, b );

				return (tri0, tri1);
			}
		}
	}

	[StructLayout( LayoutKind.Sequential )]
	public record struct Vertex( Vector3 Position, Vector3 Normal, Vector3 Tangent, Vector2 TexCoord )
	{
		public static VertexAttribute[] Layout { get; } =
		{
			new( VertexAttributeType.Position, VertexAttributeFormat.Float32 ),
			new( VertexAttributeType.Normal, VertexAttributeFormat.Float32 ),
			new( VertexAttributeType.Tangent, VertexAttributeFormat.Float32 ),
			new( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2 )
		};
	}
}
