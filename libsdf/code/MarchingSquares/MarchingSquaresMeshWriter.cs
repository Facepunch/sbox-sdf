using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Sandbox.MarchingSquares
{
    public class MarchingSquaresMeshWriter
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

        private bool[] _solidity;

        private abstract class SubMesh<T>
        {
            public Dictionary<VertexKey, int> Map { get; } = new();
            public List<T> Vertices { get; } = new List<T>();
            public List<int> Indices { get; } = new List<int>();

            public void ClearMap()
            {
                Map.Clear();
            }

            public void Clear()
            {
                Map.Clear();
                Vertices.Clear();
                Indices.Clear();
            }
        }

        private class FrontBackSubMesh : SubMesh<FrontBackVertex>
        {
            public int AddVertex( float[] data, int baseIndex, int rowStride, float unitSize, VertexKey key )
            {
                if ( Map.TryGetValue( key, out var index ) )
                {
                    return index;
                }

                Vector3 pos;

                switch ( key.Vertex )
                {
                    case NormalizedVertex.A:
                        pos = new Vector3( key.X, key.Y );
                        break;

                    case NormalizedVertex.AB:
                    {
                        var a = data[baseIndex + key.X + key.Y * rowStride];
                        var b = data[baseIndex + key.X + key.Y * rowStride + 1];
                        var t = a / (a - b);
                        pos = new Vector3( key.X + t, key.Y );
                        break;
                    }

                    case NormalizedVertex.AC:
                    {
                        var a = data[baseIndex + key.X + key.Y * rowStride];
                        var b = data[baseIndex + key.X + key.Y * rowStride + rowStride];
                        var t = a / (a - b);
                        pos = new Vector3( key.X, key.Y + t );
                        break;
                    }

                    default:
                        throw new NotImplementedException();
                }

                index = Vertices.Count;

                Vertices.Add( new FrontBackVertex(
                    pos * unitSize,
                    new Vector3( 0f, 0f, 1f ),
                    new Vector3( 1f, 0f, 0f ),
                    new Vector2( pos.x * unitSize / 16f, pos.y * unitSize / 16f ) ) );

                Map.Add( key, index );

                return index;
            }
        }

        private List<FrontBackTriangle> FrontBackTriangles { get; } = new();
        private List<CutFace> CutFaces { get; } = new();

        private FrontBackSubMesh Front { get; } = new();
        private FrontBackSubMesh Back { get; } = new();

        public void Clear()
        {
            FrontBackTriangles.Clear();
            CutFaces.Clear();
            Front.Clear();
            Back.Clear();
        }

        private float GetAdSubBc( float[] data, int baseIndex, int rowStride, int x, int y )
        {
            var index = baseIndex + x + y * rowStride;

            var a = data[index];
            var b = data[index + 1];
            var c = data[index + rowStride];
            var d = data[index + rowStride + 1];

            return a * d - b * c;
        }

        public void Write( float[] data, int baseIndex, int width, int height, int rowStride, float unitSize )
        {
            if ( _solidity == null || _solidity.Length < width * height )
            {
                var pow2 = 256;
                while ( pow2 < width * height ) pow2 <<= 1;

                Array.Resize( ref _solidity, pow2 );
            }

            for ( var y = 0; y < height; ++y )
            {
                for ( int x = 0, srcIndex = baseIndex + y * rowStride, dstIndex = y * width; x < width; ++x, ++srcIndex, ++dstIndex )
                {
                    _solidity[dstIndex] = data[srcIndex] < 0f;
                }
            }

            FrontBackTriangles.Clear();
            CutFaces.Clear();

            for ( var y = 0; y < height - 1; ++y )
            {
                for ( int x = 0, index = y * width; x < width - 1; ++x, ++index )
                {
                    var a = _solidity[index] ? SquareConfiguration.A : 0;
                    var b = _solidity[index + 1] ? SquareConfiguration.B : 0;
                    var c = _solidity[index + width] ? SquareConfiguration.C : 0;
                    var d = _solidity[index + width + 1] ? SquareConfiguration.D : 0;

                    var config = a | b | c | d;

                    switch ( config )
                    {
                        case SquareConfiguration.None:
                            break;

                        case SquareConfiguration.A:
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.AC, SquareVertex.AB ) );
                            CutFaces.Add( new CutFace( x, y, SquareVertex.AC, SquareVertex.AB ) );
                            break;

                        case SquareConfiguration.B:
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.B, SquareVertex.AB, SquareVertex.BD ) );
                            CutFaces.Add( new CutFace( x, y, SquareVertex.AB, SquareVertex.BD ) );
                            break;

                        case SquareConfiguration.C:
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.C, SquareVertex.CD, SquareVertex.AC ) );
                            CutFaces.Add( new CutFace( x, y, SquareVertex.CD, SquareVertex.AC ) );
                            break;

                        case SquareConfiguration.D:
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.D, SquareVertex.BD, SquareVertex.CD ) );
                            CutFaces.Add( new CutFace( x, y, SquareVertex.BD, SquareVertex.CD ) );
                            break;


                        case SquareConfiguration.AB:
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.AC, SquareVertex.B ) );
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.B, SquareVertex.AC, SquareVertex.BD ) );
                            CutFaces.Add( new CutFace( x, y, SquareVertex.AC, SquareVertex.BD ) );
                            break;

                        case SquareConfiguration.AC:
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.C, SquareVertex.AB ) );
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.C, SquareVertex.CD, SquareVertex.AB ) );
                            CutFaces.Add( new CutFace( x, y, SquareVertex.CD, SquareVertex.AB ) );
                            break;

                        case SquareConfiguration.CD:
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.C, SquareVertex.D, SquareVertex.AC ) );
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.D, SquareVertex.BD, SquareVertex.AC ) );
                            CutFaces.Add( new CutFace( x, y, SquareVertex.BD, SquareVertex.AC ) );
                            break;

                        case SquareConfiguration.BD:
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.B, SquareVertex.AB, SquareVertex.D ) );
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.D, SquareVertex.AB, SquareVertex.CD ) );
                            CutFaces.Add( new CutFace( x, y, SquareVertex.BD, SquareVertex.AC ) );
                            break;


                        case SquareConfiguration.AD:
                            if ( GetAdSubBc( data, baseIndex, rowStride, x, y ) > 0f )
                            {
                                FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.AC, SquareVertex.D ) );
                                FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.D, SquareVertex.AC, SquareVertex.CD ) );
                                FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.D, SquareVertex.AB ) );
                                FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.D, SquareVertex.BD, SquareVertex.AB ) );
                                CutFaces.Add( new CutFace( x, y, SquareVertex.AC, SquareVertex.CD ) );
                                CutFaces.Add( new CutFace( x, y, SquareVertex.BD, SquareVertex.AB ) );
                            }
                            else
                            {
                                FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.AC, SquareVertex.AB ) );
                                FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.D, SquareVertex.BD, SquareVertex.CD ) );
                                CutFaces.Add( new CutFace( x, y, SquareVertex.AC, SquareVertex.AB ) );
                                CutFaces.Add( new CutFace( x, y, SquareVertex.BD, SquareVertex.CD ) );
                            }

                            break;

                        case SquareConfiguration.BC:
                            if ( GetAdSubBc( data, baseIndex, rowStride, x, y ) < 0f )
                            {
                                FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.B, SquareVertex.AB, SquareVertex.C ) );
                                FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.C, SquareVertex.AB, SquareVertex.AC ) );
                                FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.B, SquareVertex.C, SquareVertex.BD ) );
                                FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.C, SquareVertex.CD, SquareVertex.BD ) );
                                CutFaces.Add( new CutFace( x, y, SquareVertex.AB, SquareVertex.AC ) );
                                CutFaces.Add( new CutFace( x, y, SquareVertex.CD, SquareVertex.BD ) );
                            }
                            else
                            {
                                FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.B, SquareVertex.AB, SquareVertex.BD ) );
                                FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.C, SquareVertex.CD, SquareVertex.AC ) );
                                CutFaces.Add( new CutFace( x, y, SquareVertex.AB, SquareVertex.BD ) );
                                CutFaces.Add( new CutFace( x, y, SquareVertex.CD, SquareVertex.AC ) );
                            }

                            break;


                        case SquareConfiguration.ABC:
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.C, SquareVertex.B ) );
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.B, SquareVertex.C, SquareVertex.BD ) );
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.C, SquareVertex.CD, SquareVertex.BD ) );
                            CutFaces.Add( new CutFace( x, y, SquareVertex.CD, SquareVertex.BD ) );
                            break;

                        case SquareConfiguration.ABD:
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.D, SquareVertex.B ) );
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.AC, SquareVertex.D ) );
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.D, SquareVertex.AC, SquareVertex.CD ) );
                            CutFaces.Add( new CutFace( x, y, SquareVertex.AC, SquareVertex.CD ) );
                            break;

                        case SquareConfiguration.ACD:
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.C, SquareVertex.D ) );
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.D, SquareVertex.AB ) );
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.D, SquareVertex.BD, SquareVertex.AB ) );
                            CutFaces.Add( new CutFace( x, y, SquareVertex.BD, SquareVertex.AB ) );
                            break;

                        case SquareConfiguration.BCD:
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.B, SquareVertex.C, SquareVertex.D ) );
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.B, SquareVertex.AB, SquareVertex.C ) );
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.C, SquareVertex.AB, SquareVertex.AC ) );
                            CutFaces.Add( new CutFace( x, y, SquareVertex.AB, SquareVertex.AC ) );
                            break;


                        case SquareConfiguration.ABCD:
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.A, SquareVertex.C, SquareVertex.B ) );
                            FrontBackTriangles.Add( new FrontBackTriangle( x, y, SquareVertex.D, SquareVertex.B, SquareVertex.C ) );
                            break;

                        default:
                            throw new NotImplementedException();
                    }
                }
            }

            Front.ClearMap();
            Back.ClearMap();

            foreach ( var triangle in FrontBackTriangles )
            {
                var a = Front.AddVertex( data, baseIndex, rowStride, unitSize, triangle.V0 );
                var b = Front.AddVertex( data, baseIndex, rowStride, unitSize, triangle.V1 );
                var c = Front.AddVertex( data, baseIndex, rowStride, unitSize, triangle.V2 );

                Front.Indices.Add( a );
                Front.Indices.Add( c );
                Front.Indices.Add( b );
            }
        }

        public void ApplyTo( Mesh mesh )
        {
            if ( mesh.HasVertexBuffer )
            {
                mesh.SetIndexBufferSize( Front.Indices.Count );
                mesh.SetVertexBufferSize( Front.Vertices.Count );

                mesh.SetIndexBufferData( Front.Indices );
                mesh.SetVertexBufferData( Front.Vertices );
            }
            else
            {
                mesh.CreateVertexBuffer( Front.Vertices.Count, FrontBackVertex.Layout, Front.Vertices );
                mesh.CreateIndexBuffer( Front.Indices.Count, Front.Indices );
            }

            mesh.SetIndexRange( 0, Front.Indices.Count );
        }
    }

    [StructLayout( LayoutKind.Sequential )]
    public record struct FrontBackVertex( Vector3 Position, Vector3 Normal, Vector3 Tangent, Vector2 TexCoord )
    {
        public static VertexAttribute[] Layout { get; } =
        {
            new (VertexAttributeType.Position, VertexAttributeFormat.Float32),
            new (VertexAttributeType.Normal, VertexAttributeFormat.Float32),
            new (VertexAttributeType.Tangent, VertexAttributeFormat.Float32),
            new (VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2)
        };
    }
}
