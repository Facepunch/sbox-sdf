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

	private record struct SourceEdge( VertexKey V0, VertexKey V1 )
	{
		public SourceEdge( int x, int y, SquareVertex V0, SquareVertex V1 )
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

		public static Vertex Lerp( in Vertex a, in Vertex b, float t )
		{
			// TODO: this won't be exact for normal / tangent

			return new Vertex(
				Vector3.Lerp( a.Position, b.Position, t ),
				Vector3.Lerp( a.Normal, b.Normal, t ).Normal,
				Vector3.Lerp( a.Tangent, b.Tangent, t ).Normal,
				Vector2.Lerp( a.TexCoord, b.TexCoord, t ) );
		}
	}
}
