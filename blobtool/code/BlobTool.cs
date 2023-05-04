using System;
using System.Linq;
using Sandbox.Physics;
using Sandbox.Tools;

namespace Sandbox.Sdf
{
	[Library( "tool_blob", Title = "Blobs", Description = "Create Blobs!", Group = "construction" )]
	public partial class BlobTool : BaseTool
	{
		public const float MinDistanceBetweenEdits = 4f;
        public const float MaxEditDistance = 512f;

		public VoxelVolume VoxelVolume { get; private set; }

		public Vector3? LastEditPos { get; set; }

		[Net]
		public float EditDistance { get; set; }

		public ModelEntity Preview { get; set; }

		public override void Activate()
		{
			base.Activate();

			if ( Game.IsServer )
			{
				VoxelVolume ??= Entity.All.OfType<VoxelVolume>().FirstOrDefault()
					?? new VoxelVolume( 256f );
			}
			else
			{
				Preview = new ModelEntity( "models/blob_preview.vmdl" );
			}
		}

		public override void Deactivate()
		{
			base.Deactivate();

			Preview?.Delete();
		}

		public override void Simulate()
		{
			if ( Preview != null && EditDistance > 64f )
			{
				Preview.Position = Owner.EyePosition + Owner.EyeRotation.Forward * EditDistance;
			}

			if ( !Game.IsServer || VoxelVolume == null )
			{
				return;
			}

			using ( Prediction.Off() )
			{
				if ( LastEditPos == null )
				{
					var tr = DoTrace();

					if ( tr.Hit && tr.Entity.IsValid() )
					{
						EditDistance = Math.Min( tr.Distance, MaxEditDistance );
					}
                    else
                    {
                        EditDistance = MaxEditDistance;
                    }
				}

				var add = Input.Down( "attack1" );
				var subtract = Input.Down( "attack2" );

				if ( !add && !subtract )
				{
					LastEditPos = null;
					return;
				}

				var editPos = Owner.EyePosition + Owner.EyeRotation.Forward * EditDistance;

				if ( LastEditPos != null && ( editPos - LastEditPos.Value).Length < MinDistanceBetweenEdits )
				{
					return;
				}

				var capsule = new CapsuleSdf( LastEditPos ?? editPos, editPos, 48f, 64f );

				if ( add )
				{
					VoxelVolume.Add( capsule, Matrix.Identity, Color.White );
				}
				else
				{
					VoxelVolume.Subtract( capsule, Matrix.Identity );
				}

				LastEditPos = editPos;
			}
		}
	}
}
