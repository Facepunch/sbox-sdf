using System;

namespace Sandbox.Sdf
{
	/// <summary>
	/// Base interface for shapes that can be added to or subtracted from a <see cref="Sdf3DWorld"/>.
	/// </summary>
	public interface ISdf3D
	{
		/// <summary>
		/// Axis aligned bounds that fully encloses the surface of this shape.
		/// </summary>
		BBox Bounds { get; }

		/// <summary>
		/// Find the signed distance of a point from the surface of this shape.
		/// Positive values are outside, negative are inside, and 0 is exactly on the surface.
		/// </summary>
		/// <param name="pos">Position to sample at</param>
		/// <returns>A signed distance from the surface of this shape</returns>
		float this[Vector3 pos] { get; }
	}

	/// <summary>
	/// Some extension methods for <see cref="ISdf3D"/>.
	/// </summary>
	public static class Sdf3DExtensions
	{
		/// <summary>
		/// Moves the given SDF by the specified offset.
		/// </summary>
		/// <typeparam name="T">SDF type</typeparam>
		/// <param name="sdf">SDF to translate</param>
		/// <param name="offset">Offset to translate by</param>
		/// <returns>A translated version of <paramref name="sdf"/></returns>
		public static TranslatedSdf3D<T> Translate<T>( this T sdf, Vector3 offset )
			where T : ISdf3D
		{
			return new TranslatedSdf3D<T>( sdf, offset );
		}

		/// <summary>
		/// Scales, rotates, and translates the given SDF.
		/// </summary>
		/// <typeparam name="T">SDF type</typeparam>
		/// <param name="sdf">SDF to transform</param>
		/// <param name="transform">Transformation to apply</param>
		/// <returns>A transformed version of <paramref name="sdf"/></returns>
		public static TransformedSdf3D<T> Transform<T>( this T sdf, Transform transform )
			where T : ISdf3D
		{
			return new TransformedSdf3D<T>( sdf, transform );
		}

		/// <summary>
		/// Scales, rotates, and translates the given SDF.
		/// </summary>
		/// <typeparam name="T">SDF type</typeparam>
		/// <param name="sdf">SDF to transform</param>
		/// <param name="translation">Offset to translate by</param>
		/// <param name="rotation">Rotation to apply</param>
		/// <param name="scale">Scale multiplier to apply</param>
		/// <returns>A transformed version of <paramref name="sdf"/></returns>
		public static TransformedSdf3D<T> Transform<T>( this T sdf, Vector3? translation = null, Rotation? rotation = null, float scale = 1f )
			where T : ISdf3D
		{
			return new TransformedSdf3D<T>( sdf, new Transform( translation ?? Vector3.Zero, rotation ?? Rotation.Identity, scale ) );
		}

		/// <summary>
		/// Expands the surface of the given SDF by the specified margin.
		/// </summary>
		/// <typeparam name="T">SDF type</typeparam>
		/// <param name="sdf">SDF to expand</param>
		/// <param name="margin">Distance to expand by</param>
		/// <returns>An expanded version of <paramref name="sdf"/></returns>
		public static ExpandedSdf3D<T> Expand<T>( this T sdf, float margin )
			where T : ISdf3D
		{
			return new ExpandedSdf3D<T>( sdf, margin );
		}
	}

	/// <summary>
	/// Describes an axis-aligned box with rounded corners.
	/// </summary>
	/// <param name="Min">Position of the corner with smallest X, Y and Z values</param>
	/// <param name="Max">Position of the corner with largest X, Y and Z values</param>
	/// <param name="CornerRadius">Controls the roundness of corners, or 0 for (approximately) sharp corners</param>
	public record struct BoxSdf( Vector3 Min, Vector3 Max, float CornerRadius = 0f ) : ISdf3D
	{
		/// <summary>
		/// Describes an axis-aligned box with rounded corners.
		/// </summary>
		/// <param name="box">Size and position of the box</param>
		/// <param name="cornerRadius">Controls the roundness of corners, or 0 for (approximately) sharp corners</param>
		public BoxSdf( BBox box, float cornerRadius = 0f )
			: this( box.Mins, box.Maxs, cornerRadius )
		{

		}

		public BBox Bounds => new( Min, Max );

		public float this[Vector3 pos]
		{
			get
			{
				var dist3 = Vector3.Max( Min + CornerRadius - pos, pos - Max + CornerRadius );

				return (dist3.x <= 0f || dist3.y <= 0f || dist3.z <= 0f
					? Math.Max( dist3.x, Math.Max( dist3.y, dist3.z ) )
					: dist3.Length) - CornerRadius;
			}
		}
	}

	/// <summary>
	/// Describes a sphere with a position and radius.
	/// </summary>
	/// <param name="Center">Position of the center of the sphere</param>
	/// <param name="Radius">Distance from the center to the surface of the sphere</param>
	public record struct SphereSdf( Vector3 Center, float Radius ) : ISdf3D
	{
		public BBox Bounds => new( Center - Radius, Radius * 2f );

		public float this[Vector3 pos] => (pos - Center).Length - Radius;
	}

	/// <summary>
	/// Describes two spheres connected by a cylinder, all with a common radius.
	/// </summary>
	/// <param name="PointA">Center of the first sphere</param>
	/// <param name="PointB">Center of the second sphere</param>
	/// <param name="Radius">Radius of the spheres and connecting cylinder</param>
	/// <param name="Along">
	/// Internal helper vector for optimization.
	/// Please use the other constructor instead of specifying this yourself.
	/// </param>
	public record struct CapsuleSdf( Vector3 PointA, Vector3 PointB, float Radius, Vector3 Along ) : ISdf3D
	{
		/// <summary>
		/// Describes two spheres connected by a cylinder, all with a common radius.
		/// </summary>
		/// <param name="pointA">Center of the first sphere</param>
		/// <param name="pointB">Center of the second sphere</param>
		/// <param name="radius">Radius of the spheres and connecting cylinder</param>
		public CapsuleSdf( Vector3 pointA, Vector3 pointB, float radius )
			: this( pointA, pointB, radius, pointA.AlmostEqual( pointB )
				? Vector3.Zero
				: (pointB - pointA) / (pointB - pointA).LengthSquared )
		{

		}

		public BBox Bounds
		{
			get
			{
				var min = Vector3.Min( PointA, PointB );
				var max = Vector3.Max( PointA, PointB );

				return new BBox( min - Radius, max + Radius );
			}
		}

		public float this[Vector3 pos]
		{
			get
			{
				var t = Vector2.Dot( pos - PointA, Along );
				var closest = Vector3.Lerp( PointA, PointB, t );

				return (pos - closest).Length - Radius;
			}
		}
	}

	/// <summary>
	/// Helper struct returned by <see cref="Sdf3DExtensions.Transform{T}(T,Transform)"/>
	/// </summary>
	public record struct TransformedSdf3D<T>( T Sdf, Transform Transform, BBox Bounds, float InverseScale ) : ISdf3D
		where T : ISdf3D
	{
		public TransformedSdf3D( T sdf, Transform transform )
			: this( sdf, transform, sdf.Bounds.Transform( transform ), 1f / transform.Scale )
		{

		}

		public float this[Vector3 pos] => Sdf[Transform.PointToLocal( pos )] * InverseScale;
	}

	/// <summary>
	/// Helper struct returned by <see cref="Sdf3DExtensions.Translate{T}"/>
	/// </summary>
	public record struct TranslatedSdf3D<T>( T Sdf, Vector3 Offset ) : ISdf3D
		where T : ISdf3D
	{
		public BBox Bounds => Sdf.Bounds.Translate( Offset );

		public float this[Vector3 pos] => Sdf[pos - Offset];
	}

	/// <summary>
	/// Helper struct returned by <see cref="Sdf3DExtensions.Expand{T}"/>
	/// </summary>
	public record struct ExpandedSdf3D<T>( T Sdf, float Margin ) : ISdf3D
		where T : ISdf3D
	{
		public BBox Bounds => new( Sdf.Bounds.Mins - Margin, Sdf.Bounds.Maxs + Margin );

		public float this[Vector3 pos] => Sdf[pos] - Margin;
	}
}
