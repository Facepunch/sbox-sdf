using System;
using System.Collections.Generic;
using Sandbox.Diagnostics;
using Sandbox.MarchingSquares;

namespace Sandbox.Sdf
{
    /// <summary>
    /// Quality settings for <see cref="Sdf2DWorld"/> instances.
    /// </summary>
    /// <param name="ChunkResolution">How many rows / columns of samples are stored per chunk.</param>
    /// <param name="ChunkSize">Size of a chunk in world space.</param>
    /// <param name="MaxDistance">
    /// Maximum world space distance stored in the SDF. Larger values mean more samples are taken when adding / subtracting.
    /// </param>
    public record struct Sdf2DWorldQuality( int ChunkResolution, float ChunkSize, float MaxDistance )
    {
        /// <summary>
        /// Cheap and cheerful, suitable for frequent (per-frame) edits.
        /// </summary>
        public static Sdf2DWorldQuality Low { get; } = new Sdf2DWorldQuality( 8, 256f, 32f );

        /// <summary>
        /// Recommended quality for most cases.
        /// </summary>
        public static Sdf2DWorldQuality Medium { get; } = new Sdf2DWorldQuality( 16, 256f, 64f );

        /// <summary>
        /// More expensive to update and network, but a much smoother result.
        /// </summary>
        public static Sdf2DWorldQuality High { get; } = new Sdf2DWorldQuality( 32, 256f, 96f );
    }

    /// <summary>
    /// Main entity for creating a 2D surface that can be added to and subtracted from.
    /// Multiple layers can be added to this entity with different materials.
    /// </summary>
    public partial class Sdf2DWorld : ModelEntity
    {
        private record struct Layer( Dictionary<(int ChunkX, int ChunkY), MarchingSquaresChunk> Chunks );

        public Sdf2DWorldQuality Quality { get; }

        private readonly float _unitSize;

        private static Dictionary<Sdf2DMaterial, Layer> Layers { get; } = new ();

        /// <summary>
        /// Constructor used internally.
        /// </summary>
        public Sdf2DWorld()
            : this( Sdf2DWorldQuality.Medium )
        {

        }

        /// <summary>
        /// Main entity for creating a 2D surface that can be added to and subtracted from.
        /// Multiple layers can be added to this entity with different materials.
        /// </summary>
        /// <param name="quality">Controls the </param>
        public Sdf2DWorld( Sdf2DWorldQuality quality )
        {
            Quality = quality;

            _unitSize = quality.ChunkSize / quality.ChunkResolution;
        }

        public override void Spawn()
        {
            base.Spawn();

            Transmit = TransmitType.Always;
        }

        /// <summary>
        /// Add a shape with the given material layer.
        /// </summary>
        /// <typeparam name="T">SDF type</typeparam>
        /// <param name="sdf">Shape to add</param>
        /// <param name="material">Material to use when adding</param>
        /// <returns>True if any geometry was modified</returns>
        public bool Add<T>( in T sdf, Sdf2DMaterial material )
            where T : ISdf2D
        {
            return ModifyChunks( sdf, material, true, ( chunk, sdf ) => chunk.Add( sdf ) );
        }

        /// <summary>
        /// Subtract a shape from the given material layer.
        /// </summary>
        /// <typeparam name="T">SDF type</typeparam>
        /// <param name="sdf">Shape to subtract</param>
        /// <param name="material">Material to subtract from</param>
        /// <returns>True if any geometry was modified</returns>
        public bool Subtract<T>( in T sdf, Sdf2DMaterial material )
            where T : ISdf2D
        {
            return ModifyChunks( sdf, material, false, ( chunk, sdf ) => chunk.Subtract( sdf ) );
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
                ? chunk : layer.Chunks[(chunkX, chunkY)] = new MarchingSquaresChunk( this, material, chunkX, chunkY )
                {
                    Parent = this,
                    LocalPosition = new Vector3( chunkX * Quality.ChunkSize, chunkY * Quality.ChunkSize ),
                    LocalRotation = Rotation.Identity,
                    LocalScale = 1f
                };
        }

        private void AssertCanModify()
        {
            Assert.True( IsClientOnly || Game.IsServer, "Can only modify server-created SDF Worlds on the server." );
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

        private bool ModifyChunks<T>( in T sdf, Sdf2DMaterial material, bool createChunks,
            Func<MarchingSquaresChunk, TranslatedSdf<T>, bool> func )
            where T : ISdf2D
        {
            AssertCanModify();

            if ( material == null )
            {
                throw new ArgumentNullException( nameof( material ) );
            }

            var bounds = sdf.Bounds;

            var min = (bounds.TopLeft - Quality.MaxDistance - _unitSize) / Quality.ChunkSize;
            var max = (bounds.BottomRight + Quality.MaxDistance + _unitSize) / Quality.ChunkSize;

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

                    changed |= func( chunk, sdf.Translate( new Vector2( x, y ) * -Quality.ChunkSize ) );
                }
            }

            return changed;
        }
    }
}
