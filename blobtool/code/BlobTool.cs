using Sandbox.Tools;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Sdf
{
	[Library( "tool_blob", Title = "Blobs", Description = "Create Blobs!", Group = "construction" )]
	public partial class BlobTool : BaseTool
	{
		private static Sdf3DVolume _sDefaultVolume;
		private static Sdf3DVolume _sCollisionVolume;

		public static Sdf3DVolume DefaultVolume => _sDefaultVolume ??= ResourceLibrary.Get<Sdf3DVolume>( "sdf/default.sdfvol" );
		public static Sdf3DVolume CollisionVolume => _sCollisionVolume ??= ResourceLibrary.Get<Sdf3DVolume>( "sdf/collision.sdfvol" );

		public static Sdf3DWorld SdfWorld { get; set; }

		public const float MinDistanceBetweenEdits = 4f;
		public const float MaxEditDistance = 1024f;

		private Task _lastEditTask;

		public Vector3? LastEditPos { get; set; }

		[Net]
		public float EditDistance { get; set; }

		[Net]
		public float Hue { get; set; }

		[Net]
		public bool IsDrawing { get; set; }

		public Color BrushColor => new ColorHsv( Hue, 0.875f, 1f );

		public ModelEntity Preview { get; set; }

		public override void Activate()
		{
			base.Activate();

			if ( Game.IsServer )
			{
				SdfWorld ??= Entity.All.OfType<Sdf3DWorld>().FirstOrDefault() ?? new Sdf3DWorld();
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
			var editPos = Owner.EyePosition + Owner.EyeRotation.Forward * EditDistance;
			var radius = MathF.Sin( Time.Now ) * 64f + 128f;

			if ( Preview != null )
			{
				Preview.Scale = radius / 48f;
				Preview.Position = Owner.EyePosition + Owner.EyeRotation.Forward * EditDistance;
				Preview.SceneObject.Attributes.Set( "ColorAdd", BrushColor );
				Preview.EnableDrawing = EditDistance > 64f && !IsDrawing;
			}

			if ( !Game.IsServer || SdfWorld == null || !(_lastEditTask?.IsCompleted ?? true) )
			{
				return;
			}

			var add = Input.Down( "attack1" );
			var subtract = Input.Down( "attack2" );

			IsDrawing &= add;

			if ( LastEditPos == null || subtract )
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

			if ( !add && !subtract )
			{
				LastEditPos = null;
				return;
			}

			if ( LastEditPos != null && (editPos - LastEditPos.Value).Length < MinDistanceBetweenEdits )
			{
				return;
			}

			var capsule = new CapsuleSdf( LastEditPos ?? editPos, editPos, radius );

			if ( add )
			{
				IsDrawing = true;

				_ = SdfWorld.AddAsync( capsule, DefaultVolume );
				_ = SdfWorld.AddAsync( capsule, CollisionVolume );

				if ( LastEditPos.HasValue )
				{
					Hue += (LastEditPos.Value - editPos).Length * 360f / 1024f;
				}
			}
			else
			{
				_ = SdfWorld.SubtractAsync( capsule, DefaultVolume );
				_ = SdfWorld.SubtractAsync( capsule, CollisionVolume );
			}

			LastEditPos = editPos;
		}
	}
}
