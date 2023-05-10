using System;
using System.Collections.Generic;
using System.Drawing;
using Sandbox.Diagnostics;
using Sandbox.MarchingSquares;
using Sandbox.UI;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Sandbox.Sdf
{
    public partial class Sdf2DWorld : Entity
    {
        private readonly int _chunkResolution;
        private readonly float _chunkSize;
        private readonly float _maxDistance;
        private readonly float _unitSize;

        public bool OwnedByServer { get; }

        private static Dictionary<(int ChunkX, int ChunkY), MarchingSquaresChunk> Chunks { get; } = new ();

        public Sdf2DWorld()
        {
            OwnedByServer = true;
        }

        public Sdf2DWorld( int chunkResolution, float chunkSize, float? maxDistance = null )
        {
            OwnedByServer = Game.IsServer;

            _chunkResolution = chunkResolution;
            _chunkSize = chunkSize;
            _unitSize = _chunkSize / _chunkResolution;
            _maxDistance = maxDistance ?? (chunkSize * 4f / chunkResolution);
        }

        private MarchingSquaresChunk GetChunk( int chunkX, int chunkY )
        {
            return Chunks.TryGetValue( (chunkX, chunkY), out var chunk ) ? chunk : null;
        }

        private MarchingSquaresChunk GetOrCreateChunk( int chunkX, int chunkY )
        {
            return Chunks.TryGetValue( (chunkX, chunkY), out var chunk )
                ? chunk : Chunks[(chunkX, chunkY)] = new MarchingSquaresChunk( _chunkResolution, _chunkSize, _maxDistance )
                {
                    Parent = this,
                    LocalPosition = new Vector3( chunkX * _chunkSize, chunkY * _chunkSize ),
                    LocalRotation = Rotation.Identity,
                    LocalScale = 1f
                };
        }

        private void AssertCanModify()
        {
            Assert.True( OwnedByServer == Game.IsServer, "Can only modify server-created SDF Worlds on the server." );
        }

        private bool ModifyChunks<T>( in T sdf, MarchingSquaresMaterial material, bool createChunks,
            Func<MarchingSquaresChunk, TransformedSdf<T>, MarchingSquaresMaterial, bool> func )
            where T : ISdf2D
        {
            AssertCanModify();

            var bounds = sdf.Bounds;

            var min = (bounds.TopLeft - _maxDistance - _unitSize) / _chunkSize;
            var max = (bounds.BottomRight + _maxDistance + _unitSize) / _chunkSize;

            var minX = (int) MathF.Floor( min.x );
            var minY = (int) MathF.Floor( min.y );

            var maxX = (int) MathF.Ceiling( max.x );
            var maxY = (int) MathF.Ceiling( max.y );

            var changed = false;

            for ( var y = minY; y < maxY; ++y )
            {
                for ( var x = minX; x < maxX; ++x )
                {
                    var chunk = !createChunks
                        ? GetChunk( x, y )
                        : GetOrCreateChunk( x, y );

                    if ( chunk == null )
                    {
                        continue;
                    }

                    changed |= func( chunk, sdf.Transform( translation: new Vector2( x * -_chunkSize, y * -_chunkSize ) ), material );
                }
            }

            return changed;
        }

        public bool Add<T>( in T sdf, MarchingSquaresMaterial material )
            where T : ISdf2D
        {
            return ModifyChunks( sdf, material, true, ( chunk, sdf, mat ) => chunk.Add( sdf, mat ) );
        }

        public bool Replace<T>( in T sdf, MarchingSquaresMaterial material )
            where T : ISdf2D
        {
            return ModifyChunks( sdf, material, false, ( chunk, sdf, mat ) => chunk.Replace( sdf, mat ) );
        }

        public bool Subtract<T>( in T sdf )
            where T : ISdf2D
        {
            return ModifyChunks( sdf, null, false, ( chunk, sdf, _ ) => chunk.Subtract( sdf ) );
        }
    }
}
