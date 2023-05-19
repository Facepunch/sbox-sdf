using System;
using System.Collections.Generic;
using Sandbox.Diagnostics;
using Sandbox.MarchingSquares;

namespace Sandbox.Sdf
{
    /// <summary>
    /// Quality settings for <see cref="Sdf2DLayer"/>.
    /// </summary>
    public enum WorldQuality
    {
        /// <summary>
        /// Cheap and cheerful, suitable for frequent (per-frame) edits.
        /// </summary>
        Low,

        /// <summary>
        /// Recommended quality for most cases.
        /// </summary>
        Medium,

        /// <summary>
        /// More expensive to update and network, but a much smoother result.
        /// </summary>
        High,

        /// <summary>
        /// Only use this for small, detailed objects!
        /// </summary>
        Extreme,

        /// <summary>
        /// Manually tweak quality parameters.
        /// </summary>
        Custom = -1
    }
    
    internal record struct Sdf2DWorldQuality( int ChunkResolution, float ChunkSize, float MaxDistance )
    {
        public static Sdf2DWorldQuality Low { get; } = new Sdf2DWorldQuality( 8, 256f, 32f );

        public static Sdf2DWorldQuality Medium { get; } = new Sdf2DWorldQuality( 16, 256f, 64f );

        public static Sdf2DWorldQuality High { get; } = new Sdf2DWorldQuality( 32, 256f, 96f );

        public static Sdf2DWorldQuality Extreme { get; } = new Sdf2DWorldQuality( 16, 128f, 32f );

        public float UnitSize => ChunkSize / ChunkResolution;
        
        public Vector4 TextureParams
        {
            get
            {
                var arraySize = ChunkResolution + SdfArray2D.Margin * 2 + 1;

                var margin = (SdfArray2D.Margin + 0.5f) / arraySize;
                var scale = 1f / ChunkSize;
                var size = 1f - (SdfArray2D.Margin * 2 + 1f) / arraySize;

                return new Vector4( margin, margin, scale * size, MaxDistance * 2f );
            }
        }
    }

    /// <summary>
    /// Main entity for creating a 2D surface that can be added to and subtracted from.
    /// Multiple layers can be added to this entity with different materials.
    /// </summary>
    public partial class Sdf2DWorld : ModelEntity
    {
        private record struct Layer( Dictionary<(int ChunkX, int ChunkY), MarchingSquaresChunk> Chunks );
        
        private static Dictionary<Sdf2DLayer, Layer> Layers { get; } = new ();

        public override void Spawn()
        {
            base.Spawn();

            Transmit = TransmitType.Always;
        }

        internal bool IsDestroying { get; private set; }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            IsDestroying = true;
        }

        /// <summary>
        /// Removes all layers, making this equivalent to a brand new empty world.
        /// </summary>
        public void Clear()
        {
            foreach ( var layer in Layers.Values )
            {
                foreach ( var chunk in layer.Chunks.Values )
                {
                    chunk.Delete();
                }
            }

            Layers.Clear();
        }

        /// <summary>
        /// Removes the given layer.
        /// </summary>
        /// <param name="layer">Layer to clear</param>
        public void Clear( Sdf2DLayer layer )
        {
            if ( Layers.Remove( layer, out var layerData ) )
            {
                foreach ( var chunk in layerData.Chunks.Values )
                {
                    chunk.Delete();
                }
            }
        }

        /// <summary>
        /// Add a shape to the given layer.
        /// </summary>
        /// <typeparam name="T">SDF type</typeparam>
        /// <param name="sdf">Shape to add</param>
        /// <param name="layer">Layer to add to</param>
        /// <returns>True if any geometry was modified</returns>
        public bool Add<T>( in T sdf, Sdf2DLayer layer )
            where T : ISdf2D
        {
            return ModifyChunks( sdf, layer, true, ( chunk, sdf ) => chunk.Add( sdf ) );
        }

        /// <summary>
        /// Subtract a shape from the given layer.
        /// </summary>
        /// <typeparam name="T">SDF type</typeparam>
        /// <param name="sdf">Shape to subtract</param>
        /// <param name="layer">Layer to subtract from</param>
        /// <returns>True if any geometry was modified</returns>
        public bool Subtract<T>( in T sdf, Sdf2DLayer layer )
            where T : ISdf2D
        {
            return ModifyChunks( sdf, layer, false, ( chunk, sdf ) => chunk.Subtract( sdf ) );
        }

        /// <summary>
        /// Subtract a shape from all layers.
        /// </summary>
        /// <typeparam name="T">SDF type</typeparam>
        /// <param name="sdf">Shape to subtract</param>
        /// <returns>True if any geometry was modified</returns>
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

        internal void AddClientChunk( MarchingSquaresChunk chunk )
        {
            Assert.True( Game.IsClient );

            if ( !Layers.TryGetValue( chunk.Layer, out var layer ) )
            {
                Layers.Add( chunk.Layer, layer = new Layer( new Dictionary<(int ChunkX, int ChunkY), MarchingSquaresChunk>() ) );
            }

            layer.Chunks[(chunk.ChunkX, chunk.ChunkY)] = chunk;
        }

        internal void RemoveClientChunk( MarchingSquaresChunk chunk )
        {
            if ( !Layers.TryGetValue( chunk.Layer, out var layer ) )
            {
                return;
            }

            if ( layer.Chunks.TryGetValue( (chunk.ChunkX, chunk.ChunkY), out var existing ) && existing == chunk )
            {
                layer.Chunks.Remove( (chunk.ChunkX, chunk.ChunkY) );

                ChunkMeshUpdated( chunk, true );
            }
        }

        internal MarchingSquaresChunk GetChunk( Sdf2DLayer layer, int chunkX, int chunkY )
        {
            return Layers.TryGetValue( layer, out var layerData )
                && layerData.Chunks.TryGetValue( (chunkX, chunkY), out var chunk ) ? chunk : null;
        }

        private MarchingSquaresChunk GetOrCreateChunk( Sdf2DLayer layer, int chunkX, int chunkY )
        {
            var quality = layer.Quality;

            if ( !Layers.TryGetValue( layer, out var layerData ) )
            {
                layerData = new Layer( new Dictionary<(int ChunkX, int ChunkY), MarchingSquaresChunk>() );
                Layers.Add( layer, layerData );
            }

            return layerData.Chunks.TryGetValue( (chunkX, chunkY), out var chunk )
                ? chunk : layerData.Chunks[(chunkX, chunkY)] = new MarchingSquaresChunk( this, layer, chunkX, chunkY )
                {
                    Parent = this,
                    LocalPosition = new Vector3( chunkX * quality.ChunkSize, chunkY * quality.ChunkSize ),
                    LocalRotation = Rotation.Identity,
                    LocalScale = 1f
                };
        }

        private void AssertCanModify()
        {
            Assert.True( IsClientOnly || Game.IsServer, "Can only modify server-created SDF Worlds on the server." );
        }

        internal void ChunkMeshUpdated( MarchingSquaresChunk chunk, bool removed )
        {
            if ( !Game.IsClient )
            {
                return;
            }

            foreach ( var (key, value) in Layers )
            {
                if ( key.LayerTextures == null )
                {
                    continue;
                }

                if ( key == chunk.Layer )
                {
                    continue;
                }

                foreach ( var layerTexture in key.LayerTextures )
                {
                    if ( layerTexture.SourceLayer != chunk.Layer )
                    {
                        continue;
                    }

                    if ( value.Chunks.TryGetValue( (chunk.ChunkX, chunk.ChunkY), out var matching ) )
                    {
                        matching.UpdateLayerTexture( chunk.Layer, removed ? null : chunk );
                    }
                }
            }
        }

        internal PhysicsShape AddMeshShape( List<Vector3> vertices, List<int> indices )
        {
            if ( PhysicsBody == null )
            {
                SetupPhysicsFromSphere( PhysicsMotionType.Static, 0f, 1f );
                PhysicsBody!.ClearShapes();
            }

            return PhysicsBody.AddMeshShape( vertices, indices );
        }

        private bool ModifyChunks<T>( in T sdf, Sdf2DLayer layer, bool createChunks,
            Func<MarchingSquaresChunk, TranslatedSdf<T>, bool> func )
            where T : ISdf2D
        {
            AssertCanModify();

            if ( layer == null )
            {
                throw new ArgumentNullException( nameof( layer ) );
            }

            var bounds = sdf.Bounds;
            var quality = layer.Quality;
            var unitSize = quality.UnitSize;

            var min = (bounds.TopLeft - quality.MaxDistance - unitSize) / quality.ChunkSize;
            var max = (bounds.BottomRight + quality.MaxDistance + unitSize) / quality.ChunkSize;

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
                        ? GetChunk( layer, x, y )
                        : GetOrCreateChunk( layer, x, y );

                    if ( chunk == null )
                    {
                        continue;
                    }

                    changed |= func( chunk, sdf.Translate( new Vector2( x, y ) * -quality.ChunkSize ) );
                }
            }

            return changed;
        }
    }
}
