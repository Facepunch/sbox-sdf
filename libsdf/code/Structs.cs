using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Sandbox.Sdf
{
	public enum NormalStyle
	{
		Flat,
		Smooth
	}

    public struct Vector3i : IEquatable<Vector3i>
	{
		public static Vector3i Zero => new Vector3i( 0, 0, 0 );
		public static Vector3i One => new Vector3i( 1, 1, 1 );

		public static implicit operator Vector3i( int value )
		{
			return new Vector3i( value, value, value );
		}

		public static implicit operator Vector3( Vector3i vector )
		{
			return new Vector3( vector.x, vector.y, vector.z );
		}

		public static Vector3i operator +( Vector3i lhs, Vector3i rhs )
		{
			return new Vector3i( lhs.x + rhs.x, lhs.y + rhs.y, lhs.z + rhs.z );
		}

		public static Vector3i operator -( Vector3i lhs, Vector3i rhs )
		{
			return new Vector3i( lhs.x - rhs.x, lhs.y - rhs.y, lhs.z - rhs.z );
		}

		public static Vector3i operator -( Vector3i vector )
		{
			return new Vector3i( -vector.x, -vector.y, -vector.z );
		}

		public static Vector3i operator /( Vector3i lhs, Vector3i rhs )
		{
			return new Vector3i( lhs.x / rhs.x, lhs.y / rhs.y, lhs.z / rhs.z );
		}

		public static bool operator ==( Vector3i lhs, Vector3i rhs )
		{
			return lhs.Equals( rhs );
		}

		public static bool operator !=( Vector3i lhs, Vector3i rhs )
		{
			return !lhs.Equals( rhs );
		}

		public static Vector3i Min( Vector3i lhs, Vector3i rhs )
		{
			return new Vector3i( Math.Min( lhs.x, rhs.x ), Math.Min( lhs.y, rhs.y ), Math.Min( lhs.z, rhs.z ) );
		}

		public static Vector3i Max( Vector3i lhs, Vector3i rhs )
		{
			return new Vector3i( Math.Max( lhs.x, rhs.x ), Math.Max( lhs.y, rhs.y ), Math.Max( lhs.z, rhs.z ) );
		}

		public static Vector3i Clamp( Vector3i vector, Vector3i min, Vector3i max )
		{
			return new Vector3i( Math.Clamp( vector.x, min.x, max.x ), Math.Clamp( vector.y, min.y, max.y ), Math.Clamp( vector.z, min.z, max.z ) );
		}

		public static Vector3i Floor( Vector3 vector )
		{
			return new Vector3i( (int)Math.Floor( vector.x ), (int)Math.Floor( vector.y ), (int)Math.Floor( vector.z ) );
		}

		public static Vector3i Ceiling( Vector3 vector )
		{
			return new Vector3i( (int)Math.Ceiling( vector.x ), (int)Math.Ceiling( vector.y ), (int)Math.Ceiling( vector.z ) );
		}

		public static Vector3i Round( Vector3 vector )
		{
			return new Vector3i( (int)Math.Round( vector.x ), (int)Math.Round( vector.y ), (int)Math.Round( vector.z ) );
		}

		public static int Dot( Vector3i lhs, Vector3i rhs )
		{
			return lhs.x * rhs.x + lhs.y * rhs.y + lhs.z * rhs.z;
		}

		public int x;
		public int y;
		public int z;

		public Vector3i( int x, int y, int z )
			=> (this.x, this.y, this.z) = (x, y, z);

		public bool Equals( Vector3i other )
		{
			return x == other.x && y == other.y && z == other.z;
		}

		public override bool Equals( [NotNullWhen( true )] object obj )
		{
			return obj is Vector3i vector && Equals( vector );
		}

		public override int GetHashCode()
		{
			return HashCode.Combine( x, y, z );
		}

		public override string ToString()
		{
			return $"({x} {y} {z})";
		}
	}
}
