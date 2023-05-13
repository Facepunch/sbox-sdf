using System;
using System.Text.Json.Serialization;

namespace Sandbox.Sdf
{
    /// <summary>
    /// Controls the appearance and physical properties of a layer in a <see cref="Sdf2DWorld"/>.
    /// </summary>
    [GameResource("SDF 2D Material", "sdflayer", $"Material used by {nameof(Sdf2DWorld)}", Icon = "brush")]
    public class Sdf2DMaterial : GameResource
    {
        private static char[] SplitChars { get; } = new[] { ' ' };

        /// <summary>
        /// Tags that physics shapes with this material should have, separated by spaces.
        /// If empty, no physics shapes will be created.
        /// </summary>
        [Editor( "tags" )]
        public string CollisionTags { get; set; } = "solid";

        [HideInEditor, JsonIgnore]
        public string[] SplitCollisionTags =>
            CollisionTags?.Split( SplitChars, StringSplitOptions.RemoveEmptyEntries ) ?? Array.Empty<string>();

        /// <summary>
        /// How wide this layer is in the z-axis. This can help prevent
        /// z-fighting for overlapping layers.
        /// </summary>
        public float Depth { get; set; } = 64f;

        /// <summary>
        /// How far to offset this layer in the z-axis.
        /// Useful for things like background / foreground layers.
        /// </summary>
        public float Offset { get; set; } = 0f;

        /// <summary>
        /// How wide a single tile of the texture should be.
        /// </summary>
        public float TexCoordSize { get; set; } = 256f;

        /// <summary>
        /// Material used by the front face of this layer.
        /// </summary>
        public Material FrontFaceMaterial { get; set; }

        /// <summary>
        /// Material used by the back face of this layer.
        /// </summary>
        public Material BackFaceMaterial { get; set; }

        /// <summary>
        /// Material used by the cut face connecting the front and
        /// back of this layer.
        /// </summary>
        public Material CutFaceMaterial { get; set; }

        /// <summary>
        /// Controls mesh visual quality, affecting performance and networking costs.
        /// </summary>
        public WorldQuality QualityLevel { get; set; } = WorldQuality.Medium;

        /// <summary>
        /// How many rows / columns of samples are stored per chunk.
        /// Higher means more needs to be sent over the network, and more work for the mesh generator.
        /// Medium quality is 16.
        /// </summary>
        [ShowIf(nameof(QualityLevel), WorldQuality.Custom)]
        public int ChunkResolution { get; set; } = 16;

        /// <summary>
        /// How wide / tall a chunk is in world space. If you'll always make small
        /// edits to this layer, you can reduce this to add detail.
        /// Medium quality is 256.
        /// </summary>
        [ShowIf( nameof( QualityLevel ), WorldQuality.Custom )]
        public float ChunkSize { get; set; } = 256f;

        /// <summary>
        /// Largest absolute value stored in a chunk's SDF.
        /// Higher means more samples are written to when doing modifications.
        /// I'd arbitrarily recommend ChunkSize / ChunkResolution * 4.
        /// </summary>
        [ShowIf( nameof( QualityLevel ), WorldQuality.Custom )]
        public float MaxDistance { get; set; } = 64f;

        [HideInEditor, JsonIgnore]
        internal Sdf2DWorldQuality Quality => QualityLevel switch
        {
            WorldQuality.Low => Sdf2DWorldQuality.Low,
            WorldQuality.Medium => Sdf2DWorldQuality.Medium,
            WorldQuality.High => Sdf2DWorldQuality.High,
            WorldQuality.Extreme => Sdf2DWorldQuality.Extreme,
            WorldQuality.Custom => new Sdf2DWorldQuality( ChunkResolution, ChunkSize, MaxDistance ),
            _ => throw new NotImplementedException()
        };
    }
}
