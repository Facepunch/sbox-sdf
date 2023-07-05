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
		public const float MinRadius = 32f;
		public const float MaxRadius = 256f;

		public const float MinDistance = 32f;
		public const float MaxDistance = 1024f;

		[ConCmd.Admin( "blobs_clear" )]
		public static void ClearWorld()
		{
			SdfWorld?.Delete();
			SdfWorld = null;
		}

		private static Sdf3DVolume _sDefaultVolume;
		private static Sdf3DVolume _sCollisionVolume;
		private static Sdf3DVolume _sScorchVolume;

		public static Sdf3DVolume DefaultVolume => _sDefaultVolume ??= ResourceLibrary.Get<Sdf3DVolume>( "sdf/default.sdfvol" );
		public static Sdf3DVolume ScorchVolume => _sScorchVolume ??= ResourceLibrary.Get<Sdf3DVolume>( "sdf/scorch.sdfvol" );

		public static Sdf3DWorld SdfWorld { get; set; }

		public const float MinDistanceBetweenEdits = 4f;

		private int _editSeed;

		public Vector3? LastEditPos { get; set; }
		private Task _lastEditTask = Task.CompletedTask;

		[ConVar.ClientData("blobs_brush_radius")]
		public static float BrushRadius { get; set; } = 48f;

		[ConVar.ClientData( "blobs_brush_distance" )]
		public static float BrushDistance { get; set; } = 256f;

		[ConVar.ClientData( "blobs_brush_roughness" )]
		public static float BrushRoughness { get; set; } = 0.5f;

		[Net]
		public bool IsDrawing { get; set; }

		public ModelEntity Preview { get; set; }

		public override void Activate()
		{
			base.Activate();

			if ( Game.IsClient )
			{
				SettingsPage.AddToSpawnMenu( this );

				Preview = new ModelEntity( "models/blob_preview.vmdl" )
				{
					Owner = Owner,
					Predictable = true
				};
			}
		}

		public override void Deactivate()
		{
			base.Deactivate();

			if ( Game.IsClient )
			{
				SettingsPage.RemoveFromSpawnMenu();

				Preview?.Delete();
				Preview = null;
			}
		}

		public override void Simulate()
		{
			var radius = Game.IsServer
				? Owner.Client.GetClientData( "blobs_brush_radius", 48f )
				: BrushRadius;
			var distance = Game.IsServer
				? Owner.Client.GetClientData( "blobs_brush_distance", 256f )
				: BrushDistance;
			var roughness = Game.IsServer
				? Owner.Client.GetClientData( "blobs_brush_roughness", 0.5f )
				: BrushRoughness;

			var editPos = Owner.EyePosition + Owner.EyeRotation.Forward * (distance + radius);

			var add = Input.Down( "attack1" );
			var subtract = Input.Down( "attack2" );

			if ( Preview != null )
			{
				Preview.Scale = radius / 48f;
				Preview.Position = editPos;
			}

			if ( Game.IsServer )
			{
				SdfWorld ??= new Sdf3DWorld();
			}

			if ( !Game.IsServer || SdfWorld == null || !_lastEditTask.IsCompleted )
			{
				return;
			}

			IsDrawing &= add || subtract;

			if ( LastEditPos == null )
			{
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

			var capsule = new CapsuleSdf3D( SdfWorld.Transform.PointToLocal( LastEditPos ?? editPos ),  SdfWorld.Transform.PointToLocal( editPos ), radius );
			var noise = new CellularNoiseSdf3D( _editSeed, new Vector3( 128f, 128f, 128f ), 96f );

			var sdf = capsule.Bias( noise, roughness * -0.5f );

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
