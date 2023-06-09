using System;
using System.Buffers;
using Sandbox.Diagnostics;
using Sandbox.UI;

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
		BBox? Bounds { get; }

		/// <summary>
		/// Find the signed distance of a point from the surface of this shape.
		/// Positive values are outside, negative are inside, and 0 is exactly on the surface.
		/// </summary>
		/// <param name="pos">Position to sample at</param>
		/// <returns>A signed distance from the surface of this shape</returns>
		float this[Vector3 pos] { get; }

		/// <summary>
		/// Sample an axis-aligned box shaped region, writing to an <paramref name="output"/> array.
		/// </summary>
		/// <param name="bounds">Region to sample.</param>
		/// <param name="output">Array to write signed distance values to.</param>
		/// <param name="outputSize">Dimensions of the <paramref name="output"/> array.</param>
		public void SampleRange( BBox bounds, float[] output, (int X, int Y, int Z) outputSize )
		{
			var minX = bounds.Mins.x;
			var incX = bounds.Size.x / outputSize.X;

			var minY = bounds.Mins.y;
			var incY = bounds.Size.y / outputSize.Y;

			var minZ = bounds.Mins.z;
			var incZ = bounds.Size.z / outputSize.Z;

			var sampleZ = minZ;
			for ( var z = 0; z < outputSize.Z; ++z, sampleZ += incZ )
			{
				var sampleY = minY;
				for ( var y = 0; y < outputSize.Y; ++y, sampleY += incY )
				{
					var sampleX = minX;
					for ( int x = 0, index = (y + z * outputSize.Y) * outputSize.X; x < outputSize.X; ++x, ++index, sampleX += incX )
					{
						output[index] = this[new Vector3( sampleX, sampleY, sampleZ )];
					}
				}
			}
		}
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

		public static IntersectedSdf<T1, T2> Intersection<T1, T2>( this T1 sdf1, T2 sdf2 )
			where T1 : ISdf3D
			where T2 : ISdf3D
		{
			return new IntersectedSdf<T1, T2>( sdf1, sdf2,
				sdf1.Bounds is { } bounds1 && sdf2.Bounds is { } bounds2
					? new BBox( Vector3.Max( bounds1.Mins, bounds2.Mins ), Vector3.Min( bounds1.Maxs, bounds2.Maxs ) )
					: sdf1.Bounds ?? sdf2.Bounds );
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

		/// <inheritdoc />
		public BBox? Bounds => new( Min, Max );

		/// <inheritdoc />
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
		/// <inheritdoc />
		public BBox? Bounds => new( Center - Radius, Radius * 2f );

		/// <inheritdoc />
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

		/// <inheritdoc />
		public BBox? Bounds
		{
			get
			{
				var min = Vector3.Min( PointA, PointB );
				var max = Vector3.Max( PointA, PointB );

				return new BBox( min - Radius, max + Radius );
			}
		}

		/// <inheritdoc />
		public float this[Vector3 pos]
		{
			get
			{
				var t = Vector3.Dot( pos - PointA, Along );
				var closest = Vector3.Lerp( PointA, PointB, t );

				return (pos - closest).Length - Radius;
			}
		}
	}

	public readonly struct TextureSdf3D : ISdf3D
	{
		private readonly Vector3 _worldSize;
		private readonly Vector3 _worldOffset;
		private readonly Vector3 _invSampleSize;
		private readonly (int X, int Y, int Z) _imageSize;
		private readonly float[] _samples;

		public TextureSdf3D( float[] samples, (int X, int Y, int Z) textureSize, Vector3 worldSize )
		{
			_samples = samples;
			_imageSize = textureSize;
			_worldSize = worldSize;
			_worldOffset = worldSize * -0.5f;
			_invSampleSize = new Vector3( textureSize.X / _worldSize.x, textureSize.Y / _worldSize.y, textureSize.Z / _worldSize.z );
		}

		/// <inheritdoc />
		public BBox? Bounds => new( _worldOffset, _worldSize );

		/// <inheritdoc />
		public float this[ Vector3 pos]
		{
			get
			{
				var localPos = (pos - _worldOffset) * _invSampleSize;

				var x = (int) MathF.Round( localPos.x );
				var y = (int) MathF.Round( localPos.y );
				var z = (int) MathF.Round( localPos.z );

				if ( x < 0 || y < 0 || z < 0 || x >= _imageSize.X || y >= _imageSize.Y || z >= _imageSize.Z ) return float.PositiveInfinity;

				return _samples[x + (y + z * _imageSize.Y) * _imageSize.X];
			}
		}
	}

	/// <summary>
	/// Helper struct returned by <see cref="Sdf3DExtensions.Transform{T}(T,Transform)"/>
	/// </summary>
	public record struct TransformedSdf3D<T>( T Sdf, Transform Transform, BBox? Bounds, float InverseScale ) : ISdf3D
		where T : ISdf3D
	{
		/// <summary>
		/// Helper struct returned by <see cref="Sdf3DExtensions.Transform{T}(T,Transform)"/>
		/// </summary>
		public TransformedSdf3D( T sdf, Transform transform )
			: this( sdf, transform, sdf.Bounds?.Transform( transform ), 1f / transform.Scale )
		{

		}

		/// <inheritdoc />
		public float this[Vector3 pos] => Sdf[Transform.PointToLocal( pos )] * InverseScale;
	}

	/// <summary>
	/// Helper struct returned by <see cref="Sdf3DExtensions.Translate{T}"/>
	/// </summary>
	public record struct TranslatedSdf3D<T>( T Sdf, Vector3 Offset ) : ISdf3D
		where T : ISdf3D
	{
		/// <inheritdoc />
		public BBox? Bounds => Sdf.Bounds?.Translate( Offset );

		/// <inheritdoc />
		public float this[Vector3 pos] => Sdf[pos - Offset];

		void ISdf3D.SampleRange( BBox bounds, float[] output, (int X, int Y, int Z) outputSize )
		{
			Sdf.SampleRange( bounds + -Offset, output, outputSize );
		}
	}

	/// <summary>
	/// Helper struct returned by <see cref="Sdf3DExtensions.Expand{T}"/>
	/// </summary>
	public record struct ExpandedSdf3D<T>( T Sdf, float Margin ) : ISdf3D
		where T : ISdf3D
	{
		/// <inheritdoc />
		public BBox? Bounds => Sdf.Bounds is { } bounds ? new( bounds.Mins - Margin, bounds.Maxs + Margin ) : null;

		/// <inheritdoc />
		public float this[Vector3 pos] => Sdf[pos] - Margin;

		void ISdf3D.SampleRange( BBox bounds, float[] output, (int X, int Y, int Z) outputSize )
		{
			Sdf.SampleRange( bounds, output, outputSize );

			var sampleCount = outputSize.X * outputSize.Y * outputSize.Z;

			for ( var i = 0; i < sampleCount; ++i )
			{
				output[i] -= Margin;
			}
		}
	}

	public record struct IntersectedSdf<T1, T2>( T1 Sdf1, T2 Sdf2, BBox? Bounds ) : ISdf3D
		where T1 : ISdf3D
		where T2 : ISdf3D
	{
		/// <inheritdoc />
		public float this[ Vector3 pos ] => Math.Max( Sdf1[pos], Sdf2[pos] );

		void ISdf3D.SampleRange( BBox bounds, float[] output, (int X, int Y, int Z) outputSize )
		{
			Sdf1.SampleRange( bounds, output, outputSize );

			var sampleCount = outputSize.X * outputSize.Y * outputSize.Z;
			var temp = ArrayPool<float>.Shared.Rent( sampleCount );

			try
			{
				Sdf2.SampleRange( bounds, temp, outputSize );

				for ( var i = 0; i < sampleCount; ++i )
				{
					output[i] = Math.Max( output[i], temp[i] );
				}
			}
			finally
			{
				ArrayPool<float>.Shared.Return( temp );
			}
		}
	}
}
