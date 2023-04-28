using System.Linq;
using Sandbox.Physics;
using Sandbox.Tools;

namespace Sandbox.Sdf
{
    [Library( "tool_blob", Title = "Blobs", Description = "Create Blobs!", Group = "construction" )]
    public class BlobTool : BaseTool
    {
        public VoxelVolume VoxelVolume { get; private set; }

        public TimeSince LastEdit { get; set; }

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
                    return;
                }

                if ( LastEdit < 0.125f )
                {
                    return;
                }

                var tr = DoTrace();

                if ( !tr.Hit )
                    return;

                if ( !tr.Entity.IsValid() )
                    return;

                VoxelVolume.Add( new SphereSdf( tr.HitPosition, 64f, 64f), Matrix.Identity, Color.White );

                LastEdit = 0f;
            }
        }
    }
}
