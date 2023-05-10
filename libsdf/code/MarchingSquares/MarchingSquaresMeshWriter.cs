using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using static Sandbox.Package;
using static Sandbox.Prefab;

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

        private abstract class SubMesh<T>
            where T : unmanaged
        {
            public List<T> Vertices { get; } = new();
            public List<int> Indices { get; } = new();

            public abstract VertexAttribute[] VertexLayout { get; }

            public abstract void ClearMap();
            
            protected Vector3 GetVertexPos( SdfArray2DLayer layer, VertexKey key )
            {
                switch ( key.Vertex )
                {
                    case NormalizedVertex.A:
                        return new Vector3( key.X, key.Y );

                    case NormalizedVertex.AB:
                    {
                        var a = layer[key.X, key.Y] - 127.5f;
                        var b = layer[key.X + 1, key.Y] - 127.5f;
                        var t = a / (a - b);
                        return new Vector3( key.X + t, key.Y );
                    }

                    case NormalizedVertex.AC:
                    {
                        var a = layer[key.X, key.Y] - 127.5f;
                        var c = layer[key.X, key.Y + 1] - 127.5f;
                        var t = a / (a - c);
                        return new Vector3( key.X, key.Y + t );
                    }

                    default:
                        throw new NotImplementedException();
                }
            }

            public void Clear()
            {
                ClearMap();

                Vertices.Clear();
                Indices.Clear();
            }

            public void ApplyTo( Mesh mesh )
            {
                if ( mesh.HasVertexBuffer )
                {
                    if ( Indices.Count > 0 )
                    {
                        mesh.SetIndexBufferSize( Indices.Count );
                        mesh.SetVertexBufferSize( Vertices.Count );

                        mesh.SetIndexBufferData( Indices );
                        mesh.SetVertexBufferData( Vertices );
                    }

                    mesh.SetIndexRange( 0, Indices.Count );
                }
                else if ( Indices.Count > 0 )
                {
                    mesh.CreateVertexBuffer( Vertices.Count, VertexLayout, Vertices );
                    mesh.CreateIndexBuffer( Indices.Count, Indices );
                }
            }
        }

        private class FrontBackSubMesh : SubMesh<FrontBackVertex>
        {
            public Dictionary<VertexKey, int> Map { get; } = new();

            public override VertexAttribute[] VertexLayout => FrontBackVertex.Layout;

            public Vector3 Normal { get; set; }
            public Vector3 Offset { get; set; }

            public override void ClearMap()
            {
                Map.Clear();
            }

            private int AddVertex( SdfArray2DLayer layer, float unitSize, VertexKey key )
            {
                if ( Map.TryGetValue( key, out var index ) )
                {
                    return index;
                }

                var pos = GetVertexPos( layer, key );

                index = Vertices.Count;

                Vertices.Add( new FrontBackVertex(
                    pos * unitSize + Offset,
                    Normal,
                    new Vector3( 1f, 0f, 0f ),
                    new Vector2( pos.x * unitSize / 16f, pos.y * unitSize / 16f ) ) );

                Map.Add( key, index );

                return index;
            }

            public void AddTriangle( SdfArray2DLayer layer, float unitSize, FrontBackTriangle triangle )
            {
                Indices.Add( AddVertex( layer, unitSize, triangle.V0 ) );
                Indices.Add( AddVertex( layer, unitSize, triangle.V1 ) );
                Indices.Add( AddVertex( layer, unitSize, triangle.V2 ) );
            }
        }

        private class CutSubMesh : SubMesh<CutVertex>
        {
            private const float SmoothNormalThreshold = 33f;
            private static readonly float SmoothNormalDotTheshold = MathF.Cos( SmoothNormalThreshold * MathF.PI / 180f );

            private record struct VertexInfo( int FrontIndex, int BackIndex, Vector3 Normal );

            private Dictionary<VertexKey, VertexInfo> Map { get; } = new Dictionary<VertexKey, VertexInfo>();

            public override VertexAttribute[] VertexLayout => CutVertex.Layout;

            public Vector3 FrontOffset { get; set; }
            public Vector3 BackOffset { get; set; }

            private (int FrontIndex, int BackIndex) AddVertices( Vector3 pos, Vector3 normal, float unitSize, VertexKey key )
            {
                var wasInMap = false;

                if ( Map.TryGetValue( key, out var info ) )
                {
                    if ( Vector3.Dot( info.Normal, normal ) >= SmoothNormalDotTheshold )
                    {
                        normal = (info.Normal + normal).Normal;

                        Vertices[info.FrontIndex] = Vertices[info.FrontIndex] with { Normal = normal };
                        Vertices[info.BackIndex] = Vertices[info.BackIndex] with { Normal = normal };

                        return (info.FrontIndex, info.BackIndex);
                    }

                    wasInMap = true;
                }

                var frontIndex = Vertices.Count;
                var backIndex = frontIndex + 1;

                Vertices.Add( new CutVertex(
                    pos * unitSize + FrontOffset,
                    normal,
                    new Vector3( 0f, 0f, -1f ) ) );

                Vertices.Add( new CutVertex(
                    pos * unitSize + BackOffset,
                    normal,
                    new Vector3( 0f, 0f, -1f ) ) );

                if ( !wasInMap )
                {
                    Map[key] = new VertexInfo( frontIndex, backIndex, normal );
                }

                return (frontIndex, backIndex);
            }

            public void AddQuad( SdfArray2DLayer layer, float unitSize, CutFace face )
            {
                var aPos = GetVertexPos( layer, face.V0 );
                var bPos = GetVertexPos( layer, face.V1 );

                var normal = Vector3.Cross( aPos - bPos, new Vector3( 0f, 0f, 1f ) ).Normal;

                var (aFrontIndex, aBackIndex) = AddVertices( aPos, normal, unitSize, face.V0 );
                var (bFrontIndex, bBackIndex) = AddVertices( bPos, normal, unitSize, face.V1 );

                Indices.Add( aFrontIndex );
                Indices.Add( bFrontIndex );
                Indices.Add( aBackIndex );

                Indices.Add( bFrontIndex );
                Indices.Add( bBackIndex );
                Indices.Add( aBackIndex );
            }

            public override void ClearMap()
            {
                Map.Clear();
            }
        }

        private List<FrontBackTriangle> FrontBackTriangles { get; } = new();
        private List<CutFace> CutFaces { get; } = new();

        private FrontBackSubMesh Front { get; } = new();
        private FrontBackSubMesh Back { get; } = new();
        private CutSubMesh Cut { get; } = new();

        public void Clear()
        {
            FrontBackTriangles.Clear();
            CutFaces.Clear();
            Front.Clear();
            Back.Clear();
            Cut.Clear();
        }

        private static float GetAdSubBc( SdfArray2DLayer layer, int x, int y )
        {
            var a = layer[x, y] - 127.5f;
            var b = layer[x + 1, y] - 127.5f;
            var c = layer[x, y + 1] - 127.5f;
            var d = layer[x + 1, y + 1] - 127.5f;

            return a * d - b * c;
        }

        public void Write( SdfArray2DLayer layer, int width, int height, float unitSize, float depth )
        {
            FrontBackTriangles.Clear();
            CutFaces.Clear();

            for ( var y = 0; y < height; ++y )
            {
                for ( int x = 0, index = layer.BaseIndex + y * layer.RowStride; x < width; ++x, ++index )
                {
                    var a = layer.Samples[index] < 128 ? SquareConfiguration.A : 0;
                    var b = layer.Samples[index + 1] < 128 ? SquareConfiguration.B : 0;
                    var c = layer.Samples[index + layer.RowStride] < 128 ? SquareConfiguration.C : 0;
                    var d = layer.Samples[index + layer.RowStride + 1] < 128 ? SquareConfiguration.D : 0;

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
                            CutFaces.Add( new CutFace( x, y, SquareVertex.AB, SquareVertex.CD ) );
                            break;


                        case SquareConfiguration.AD:
                            if ( GetAdSubBc( layer, x, y ) > 0f )
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
                            if ( GetAdSubBc( layer, x, y ) < 0f )
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
            Cut.ClearMap();

            Front.Normal = new Vector3( 0f, 0f, 1f );
            Front.Offset = Cut.FrontOffset = Front.Normal * depth * 0.5f;
            Back.Normal = new Vector3( 0f, 0f, -1f );
            Back.Offset = Cut.BackOffset = Back.Normal * depth * 0.5f;

            foreach ( var triangle in FrontBackTriangles )
            {
                Front.AddTriangle( layer, unitSize, triangle.Flipped );
                Back.AddTriangle( layer, unitSize, triangle );
            }

            foreach ( var cutFace in CutFaces )
            {
                Cut.AddQuad( layer, unitSize, cutFace );
            }
        }

        public (bool HasFrontBackFaces, bool HasCutFaces) ApplyTo( Mesh front, Mesh back, Mesh cut )
        {
            if ( Front.Indices.Count > 0 )
            {
                Front.ApplyTo( front );
                Back.ApplyTo( back );
            }

            if ( Cut.Indices.Count > 0 )
            {
                Cut.ApplyTo( cut );
            }

            return (Front.Indices.Count > 0, Cut.Indices.Count > 0);
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

    [StructLayout( LayoutKind.Sequential )]
    public record struct CutVertex( Vector3 Position, Vector3 Normal, Vector3 Tangent )
    {
        public static VertexAttribute[] Layout { get; } =
        {
            new (VertexAttributeType.Position, VertexAttributeFormat.Float32),
            new (VertexAttributeType.Normal, VertexAttributeFormat.Float32),
            new (VertexAttributeType.Tangent, VertexAttributeFormat.Float32)
        };
    }
}
