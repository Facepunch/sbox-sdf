using Sandbox.Tools;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Sdf
{
	[Library( "tool_blob", Title = "Blobs", Description = "Create Blobs!", Group = "construction" )]
	public partial class BlobTool : BaseTool
	{
		public static Sdf3DWorld SdfWorld { get; set; }

		public const float MinDistanceBetweenEdits = 4f;
		public const float MaxEditDistance = 512f;

		private Task _lastEditTask;

		public Vector3? LastEditPos { get; set; }
		public Vector2? LastEditPos2D { get; set; }

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
				Sdf3DWorld ??= Entity.All.OfType<Sdf3DWorld>().FirstOrDefault() ?? new Sdf3DWorld( 256f );
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
			if ( Preview != null )
			{
				Preview.Position = Owner.EyePosition + Owner.EyeRotation.Forward * EditDistance;
				Preview.SceneObject.Attributes.Set( "ColorAdd", BrushColor );
				Preview.EnableDrawing = EditDistance > 64f && !IsDrawing;
			}

			if ( !Game.IsServer || MarchingCubes == null || !(_lastEditTask?.IsCompleted ?? true) )
			{
				return;
			}


			var add = Input.Down( "attack1" );
			var subtract = Input.Down( "attack2" );

			if ( SdfWorld != null )
			{
				if ( add || subtract )
				{
					var ray = new Ray( Owner.EyePosition, Owner.EyeRotation.Forward );
					var plane = new Plane( SdfWorld.Position, SdfWorld.Rotation.Up );
					var hit = plane.Trace( ray, true );

					if ( hit is { } hitPos )
					{
						var radius = 64f;
						var localPos = (Vector2)SdfWorld.Transform.PointToLocal( hitPos );

						var sdf = new LineSdf( localPos, LastEditPos2D ?? localPos, radius );

						if ( add )
						{
							SdfWorld.Add( sdf, DefaultLayer );
							SdfWorld.Subtract( sdf.Expand( 32f ), ScorchLayer );
						}
						else
						{
							SdfWorld.Subtract( sdf, DefaultLayer );
							SdfWorld.Add( sdf, ScorchLayer );
						}

						LastEditPos2D = localPos;
					}
				}
				else
				{
					LastEditPos2D = null;
				}
			}

			return;

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

			var editPos = Owner.EyePosition + Owner.EyeRotation.Forward * EditDistance;

			if ( LastEditPos != null && (editPos - LastEditPos.Value).Length < MinDistanceBetweenEdits )
			{
				return;
			}

			var capsule = new CapsuleSdf( LastEditPos ?? editPos, editPos, 48f, 64f );

			if ( add )
			{
				IsDrawing = true;

				_lastEditTask = MarchingCubes.Add( capsule, Matrix.Identity, BrushColor );

				if ( LastEditPos.HasValue )
				{
					Hue += (LastEditPos.Value - editPos).Length * 360f / 1024f;
				}
			}
			else
			{
				_lastEditTask = MarchingCubes.Subtract( capsule, Matrix.Identity );
			}

			LastEditPos = editPos;
		}
	}
}
