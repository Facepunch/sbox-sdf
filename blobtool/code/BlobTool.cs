using Sandbox.Tools;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Sandbox.Sdf.Noise;

namespace Sandbox.Sdf
{
	[Library( "tool_blob", Title = "Blobs", Description = "Create Blobs!", Group = "construction" )]
	public partial class BlobTool : BaseTool
	{
		[ConCmd.Admin( "blobs_clear" )]
		public static void ClearWorld()
		{
			_ = SdfWorld?.ClearAsync();
		}

		private static Sdf3DVolume _sDefaultVolume;
		private static Sdf3DVolume _sCollisionVolume;
		private static Sdf3DVolume _sScorchVolume;

		public static Sdf3DVolume DefaultVolume => _sDefaultVolume ??= ResourceLibrary.Get<Sdf3DVolume>( "sdf/default.sdfvol" );
		public static Sdf3DVolume ScorchVolume => _sScorchVolume ??= ResourceLibrary.Get<Sdf3DVolume>( "sdf/scorch.sdfvol" );

		public static Sdf3DWorld SdfWorld { get; set; }

		public const float MinDistanceBetweenEdits = 4f;
		public const float MaxEditDistance = 2048f;

		private int _editSeed;

		public Vector3? LastEditPos { get; set; }
		private Task _lastEditTask = Task.CompletedTask;

		[Net]
		public float EditDistance { get; set; }

		[Net]
		public float EditRadius { get; set; } = 48f;

		[Net]
		public bool IsDrawing { get; set; }

		[Net]
		public ModelEntity Preview { get; set; }

		public override void Activate()
		{
			base.Activate();

			if ( Game.IsServer )
			{
				SdfWorld ??= Entity.All.OfType<Sdf3DWorld>().FirstOrDefault() ?? new Sdf3DWorld();
				Preview = new ModelEntity( "models/blob_preview.vmdl" )
				{
					Owner = Owner,
					Predictable = true
				};
			}
			else
			{
				SettingsPage.AddToSpawnMenu();
			}
		}

		public override void Deactivate()
		{
			base.Deactivate();

			if ( Game.IsServer )
			{
				Preview?.Delete();
				Preview = null;
			}
			else
			{
				SettingsPage.RemoveFromSpawnMenu();
			}
		}

		public override void Simulate()
		{
			var editPos = Owner.EyePosition + Owner.EyeRotation.Forward * EditDistance;

			var add = Input.Down( "attack1" );
			var subtract = Input.Down( "attack2" );

			if ( Preview != null )
			{
				Preview.Scale = EditRadius / 48f;
				Preview.Position = Owner.EyePosition + Owner.EyeRotation.Forward * EditDistance;
				Preview.EnableDrawing = EditDistance > EditRadius + 32f;
			}

			if ( !Game.IsServer || SdfWorld == null || !_lastEditTask.IsCompleted )
			{
				return;
			}

			IsDrawing &= add || subtract;

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

				EditRadius = (MathF.Sin( Time.Now * MathF.PI ) * 0.25f + 0.75f) * Math.Clamp( EditDistance / 2f, 64f, 256f );
				_editSeed = Random.Shared.Next();
			}

			if ( !add && !subtract )
			{
				LastEditPos = null;
				return;
			}

			IsDrawing = true;

			if ( LastEditPos != null && (editPos - LastEditPos.Value).Length < MinDistanceBetweenEdits )
			{
				return;
			}

			var capsule = new CapsuleSdf3D( SdfWorld.Transform.PointToLocal( LastEditPos ?? editPos ),  SdfWorld.Transform.PointToLocal( editPos ), EditRadius );
			var noise = new CellularNoiseSdf3D( _editSeed, new Vector3( 128f, 128f, 128f ), 96f );

			var sdf = capsule.Bias( noise, -0.25f );

			if ( add )
			{
				_lastEditTask = GameTask.WhenAll(
					SdfWorld.AddAsync( sdf, DefaultVolume ),
					SdfWorld.SubtractAsync( sdf.Expand( 16f ), ScorchVolume ) );
			}
			else
			{
				_lastEditTask = GameTask.WhenAll(
					SdfWorld.SubtractAsync( sdf, DefaultVolume ),
					SdfWorld.AddAsync( sdf.Expand( 16f ), ScorchVolume ) );
			}

			LastEditPos = editPos;
		}
	}
}
