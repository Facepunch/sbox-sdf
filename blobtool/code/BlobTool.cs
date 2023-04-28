using System.Linq;
using Sandbox.Physics;
using Sandbox.Tools;

namespace Sandbox.Sdf
{
    [Library( "tool_blob", Title = "Blobs", Description = "Create Blobs!", Group = "construction" )]
    public class BlobTool : BaseTool
    {
        public const float MinDistanceBetweenEdits = 16f;

        public VoxelVolume VoxelVolume { get; private set; }

        public Vector3? LastEditPos { get; set; }
        public float EditDistance { get; set; }

        public override void Activate()
        {
            base.Activate();

            if ( Game.IsServer )
            {
                VoxelVolume ??= Entity.All.OfType<VoxelVolume>().FirstOrDefault()
                    ?? new VoxelVolume( new Vector3( 65536f, 65536f, 65536f ), 256f );
            }
            else
            {
                VoxelVolume ??= Entity.All.OfType<VoxelVolume>().FirstOrDefault();
            }
        }

        public override void Simulate()
        {
            if ( !Game.IsServer || VoxelVolume == null )
            {
                return;
            }

            using ( Prediction.Off() )
            {
                var add = Input.Down( "attack1" );
                var subtract = Input.Down( "attack2" );

                if ( !add && !subtract )
                {
                    LastEditPos = null;
                    return;
                }

                if ( LastEditPos == null )
                {
                    var tr = DoTrace();

                    if ( !tr.Hit )
                        return;

                    if ( !tr.Entity.IsValid() )
                        return;

                    EditDistance = tr.Distance;
                }

                var editPos = Owner.EyePosition + Owner.EyeRotation.Forward * EditDistance;

                if ( LastEditPos != null && ( editPos - LastEditPos.Value).Length < MinDistanceBetweenEdits )
                {
                    return;
                }

                LastEditPos = editPos;
                VoxelVolume.Add( new SphereSdf( editPos, 64f, 64f), Matrix.Identity, Color.White );
            }
        }
    }
}
