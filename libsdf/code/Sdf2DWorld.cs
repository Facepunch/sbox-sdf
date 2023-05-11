using System;
using System.Collections.Generic;
using Sandbox.Diagnostics;
using Sandbox.MarchingSquares;

namespace Sandbox.Sdf
{
    public partial class Sdf2DWorld : Entity
    {
        private record struct Layer( Dictionary<(int ChunkX, int ChunkY), MarchingSquaresChunk> Chunks );

        private readonly int _chunkResolution;
        private readonly float _chunkSize;
        private readonly float _maxDistance;
        private readonly float _unitSize;

        public bool OwnedByServer { get; }

        private static Dictionary<Sdf2DMaterial, Layer> Layers { get; } = new ();

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

        private MarchingSquaresChunk GetChunk( Sdf2DMaterial material, int chunkX, int chunkY )
        {
            return Layers.TryGetValue( material, out var layer )
                && layer.Chunks.TryGetValue( (chunkX, chunkY), out var chunk ) ? chunk : null;
        }

        private MarchingSquaresChunk GetOrCreateChunk( Sdf2DMaterial material, int chunkX, int chunkY )
        {
            if ( !Layers.TryGetValue( material, out var layer ) )
            {
                layer = new Layer( new Dictionary<(int ChunkX, int ChunkY), MarchingSquaresChunk>() );
                Layers.Add( material, layer );
            }

            return layer.Chunks.TryGetValue( (chunkX, chunkY), out var chunk )
                ? chunk : layer.Chunks[(chunkX, chunkY)] = new MarchingSquaresChunk( _chunkResolution, _chunkSize, _maxDistance, material )
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

        private bool ModifyChunks<T>( in T sdf, Sdf2DMaterial material, bool createChunks,
            Func<MarchingSquaresChunk, TranslatedSdf<T>, bool> func )
            where T : ISdf2D
        {
            AssertCanModify();

            if ( material == null )
            {
                throw new ArgumentNullException( nameof(material) );
            }

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
                        ? GetChunk( material, x, y )
                        : GetOrCreateChunk( material, x, y );

                    if ( chunk == null )
                    {
                        continue;
                    }

                    changed |= func( chunk, sdf.Translate( new Vector2( x * -_chunkSize, y * -_chunkSize ) ) );
                }
            }

            return changed;
        }

        public bool Add<T>( in T sdf, Sdf2DMaterial material )
            where T : ISdf2D
        {
            return ModifyChunks( sdf, material, true, ( chunk, sdf ) => chunk.Add( sdf ) );
        }

        public bool Subtract<T>( in T sdf, Sdf2DMaterial material )
            where T : ISdf2D
        {
            return ModifyChunks( sdf, material, false, ( chunk, sdf ) => chunk.Subtract( sdf ) );
        }

        public bool Subtract<T>( in T sdf )
            where T : ISdf2D
        {
            var changed = false;

            foreach ( var material in Layers.Keys )
            {
                changed |= ModifyChunks( sdf, material, false, ( chunk, sdf ) => chunk.Subtract( sdf ) );
            }

            return changed;
        }
    }
}
