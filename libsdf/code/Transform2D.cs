using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sandbox.Sdf
{
    public record struct Rotation2D( float Cos, float Sin )
    {
        public static implicit operator Rotation2D( float degrees )
        {
            return new Rotation2D( degrees );
        }

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

        public Rotation2D( float degrees )
            : this( MathF.Cos( degrees * MathF.PI / 180f ), MathF.Sin( degrees * MathF.PI / 180f ) )
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
}
