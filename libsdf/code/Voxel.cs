using Sandbox;
using System;
using System.Runtime.InteropServices;

namespace Sandbox.Sdf
{
	public readonly struct Voxel : IEquatable<Voxel>
	{
		private static readonly float[] _sValueLookup = new float[256];

		private const float ColorScale = 1f / 255f;

		public static bool operator ==( Voxel a, Voxel b )
		{
			return a.RawValue == b.RawValue && a.R == b.R && a.G == b.G && a.B == b.B;
		}

		public static bool operator !=( Voxel a, Voxel b )
		{
			return a.RawValue != b.RawValue || a.R != b.R || a.G != b.G || a.B != b.B;
		}

		public static Voxel operator +( Voxel a, Voxel b )
		{
			var alpha = Math.Clamp( ColorScale * (b.RawValue + 127 - a.RawValue * 2), 0f, 1f );

			return new Voxel( Math.Max( a.RawValue, b.RawValue ),
				(byte)MathF.Round( a.R + (b.R - a.R) * alpha ),
				(byte)MathF.Round( a.G + (b.G - a.G) * alpha ),
				(byte)MathF.Round( a.B + (b.B - a.B) * alpha ) );
		}

		public static Voxel operator -( Voxel a, Voxel b )
		{
			return new Voxel( (byte) Math.Max( a.RawValue - b.RawValue, 0 ), a.R, a.G, a.B );
		}

		static Voxel()
		{
			for ( var i = 1; i < 255; ++i )
			{
				_sValueLookup[i] = (i - 127.5f) / 127.5f;
			}

			_sValueLookup[0] = -1f;
			_sValueLookup[255] = 1f;
		}

		public readonly byte RawValue;
		public readonly byte R;
		public readonly byte G;
		public readonly byte B;

		public float Value => _sValueLookup[RawValue];
		public Color Color => new Color( R * ColorScale, G * ColorScale, B * ColorScale );

		public Voxel( float value, byte r, byte g, byte b )
		{
			RawValue = (byte)Math.Clamp( (int)MathF.Round( value * 127.5f + 127.5f ), 0, 255 );
			R = r;
			G = g;
			B = b;
		}

		public Voxel( byte rawValue, byte r, byte g, byte b )
		{
			RawValue = rawValue;
			R = r;
			G = g;
			B = b;
		}

		public override string ToString()
		{
			return $"({Value:F2}, #{R:x2}{G:x2}{B:x2})";
		}

		public bool Equals( Voxel other )
		{
			return RawValue == other.RawValue && R == other.R && G == other.G && B == other.B;
		}

		public override bool Equals( object obj )
		{
			return obj is Voxel other && Equals( other );
		}

		public override int GetHashCode()
		{
			return HashCode.Combine( RawValue, R, G, B );
		}
	}

	[StructLayout( LayoutKind.Sequential )]
	public readonly struct VoxelVertex
	{
		public static VertexAttribute[] Layout { get; } =
		{
			new VertexAttribute(VertexAttributeType.Position, VertexAttributeFormat.Float32),
			new VertexAttribute(VertexAttributeType.Normal, VertexAttributeFormat.Float32),
			new VertexAttribute(VertexAttributeType.Tangent, VertexAttributeFormat.Float32),
			new VertexAttribute(VertexAttributeType.Color, VertexAttributeFormat.Float32)
		};

		public readonly Vector3 Position;
		public readonly Vector3 Normal;
		public readonly Vector3 Tangent;
		public readonly Vector3 Color;

		public VoxelVertex(Vector3 position, Vector3 normal, Vector3 tangent, Vector3 color)
		{
			Position = position;
			Normal = normal;
			Tangent = tangent;
			Color = color;
		}
	}
}
