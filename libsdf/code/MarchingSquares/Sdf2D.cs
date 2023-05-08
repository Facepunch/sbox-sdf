using System;

namespace Sandbox.MarchingSquares
{
    public interface ISdf2D
    {
        /// <summary>
        /// Axis aligned bounds that fully encloses the surface of this shape.
        /// </summary>
        Rect Bounds { get; }

        /// <summary>
        /// Find the signed distance of a point from the surface of this shape.
        /// Positive values are outside, negative are inside, and 0 is exactly on the surface.
        /// </summary>
        /// <param name="pos">Position to sample at</param>
        /// <returns>A signed distance from the surface of this shape</returns>
        float this[ Vector2 pos ] { get; }
    }

    public static class Sdf2DExtensions
    {
        public static TransformedSdf<T> Transform<T>( this T sdf, Transform2D transform )
            where T : ISdf2D
        {
            return new TransformedSdf<T>( sdf, transform );
        }

        public static TransformedSdf<T> Transform<T>( this T sdf, Vector2? translation = null, Rotation2D? rotation = null, float scale = 1f)
            where T : ISdf2D
        {
            return new TransformedSdf<T>( sdf, new Transform2D( translation, rotation, scale ) );
        }
    }

    public record struct BoxSdf( Vector2 Min, Vector2 Max ) : ISdf2D
    {
        public BoxSdf( Rect rect )
            : this( rect.TopLeft, rect.BottomRight )
        {

        }

        public Rect Bounds => new ( Min.x, Min.y, Max.x - Min.x, Max.y - Min.y );

        public float this[ Vector2 pos ]
        {
            get
            {
                var dist2 = Vector2.Max( Min - pos, pos - Max );

                return dist2.x <= 0f || dist2.y <= 0f
                    ? Math.Max( dist2.x, dist2.y )
                    : dist2.Length;
            }
        }
    }

    public record struct CircleSdf( Vector2 Center, float Radius ) : ISdf2D
    {
        public Rect Bounds => new ( Center - Radius, Radius * 2f );

        public float this[ Vector2 pos ] => (pos - Center).Length - Radius;
    }

    public record struct Rotation2D( float Cos, float Sin )
    {
        public static Rotation2D Identity { get; } = new Rotation2D( 1f, 0f );

        public static Vector2 operator *( Rotation2D rotation, Vector2 vector )
        {
            return rotation.UnitX * vector.x + rotation.UnitY * vector.y;
        }

        public static Rotation2D operator *( Rotation2D lhs, Rotation2D rhs )
        {
            return new Rotation2D( lhs.Cos * rhs.Cos - lhs.Sin * rhs.Sin, lhs.Sin * rhs.Cos + lhs.Cos * rhs.Sin );
        }

        public Vector2 UnitX => new( Cos, -Sin );
        public Vector2 UnitY => new( Sin, Cos );

        public Rotation2D Inverse => this with { Sin = -Sin };

        public Rotation2D Normalized
        {
            get
            {
                var length = MathF.Sqrt( Cos * Cos + Sin * Sin );
                var scale = 1f / length;

                return new Rotation2D( Cos * scale, Sin * scale );
            }
        }

        public Rotation2D( float radians )
            : this( MathF.Cos( radians ), MathF.Sin( radians ) )
        {

        }
    }

    public record struct Transform2D( Vector2 Translation, Rotation2D Rotation, float Scale, float InverseScale )
    {
        public static Transform2D Identity { get; } = new( Vector2.Zero, Rotation2D.Identity );

        public Transform2D( Vector2? translation = null, Rotation2D? rotation = null, float scale = 1f )
            : this( translation ?? Vector2.Zero, rotation ?? Rotation2D.Identity, scale, 1f / scale )
        {

        }

        public Vector2 TransformPoint( Vector2 pos )
        {
            return Translation + Rotation * (pos * Scale);
        }

        public Vector2 InverseTransformPoint( Vector2 pos )
        {
            return InverseScale * (Rotation.Inverse * (pos - Translation));
        }
    }

    public record struct TransformedSdf<T>( T Sdf, Transform2D Transform, Rect Bounds ) : ISdf2D
        where T : ISdf2D
    {
        private static Rect CalculateBounds( T sdf, Transform2D transform )
        {
            var inner = sdf.Bounds;

            var a = transform.TransformPoint( inner.TopLeft );
            var b = transform.TransformPoint( inner.TopRight );
            var c = transform.TransformPoint( inner.BottomLeft );
            var d = transform.TransformPoint( inner.BottomRight );

            var min = Vector2.Min( Vector2.Min( a, b ), Vector2.Min( c, d ) );
            var max = Vector2.Max( Vector2.Max( a, b ), Vector2.Max( c, d ) );

            return new Rect( min.x, min.y, max.x - min.x, max.y - min.y );
        }

        public TransformedSdf( T sdf, Transform2D transform )
            : this( sdf, transform, CalculateBounds( sdf, transform ) )
        {

        }

        public float this[ Vector2 pos ] => Sdf[Transform.InverseTransformPoint( pos )] * Transform.InverseScale;
    }
}
