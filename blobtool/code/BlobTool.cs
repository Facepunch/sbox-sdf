using System;
using System.Linq;
using System.Threading.Tasks;
using Sandbox.MarchingCubes;
using Sandbox.MarchingSquares;
using Sandbox.Physics;
using Sandbox.Tools;

namespace Sandbox.Sdf
{
	[Library( "tool_blob", Title = "Blobs", Description = "Create Blobs!", Group = "construction" )]
	public partial class BlobTool : BaseTool
	{
		public static MarchingSquaresChunk Chunk { get; set; }

		[ConCmd.Client("sdf_2d_test")]
		public static void Sdf2DTest( int resolution = 64 )
		{
			Chunk?.Delete();

			Chunk = new MarchingSquaresChunk( resolution, 512f )
			{
				LocalPosition = new Vector3( -256f, -1024f + 32f ),
				LocalRotation = Rotation.FromRoll( 90f )
			};
		}

		public const float MinDistanceBetweenEdits = 4f;
		public const float MaxEditDistance = 512f;

		private Task _lastEditTask;

		public MarchingCubesEntity MarchingCubes { get; private set; }

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
				MarchingCubes ??= Entity.All.OfType<MarchingCubesEntity>().FirstOrDefault() ?? new MarchingCubesEntity( 256f );
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

			var add = Input.Down( "attack1" );

            if ( Chunk != null )
			{
				if ( Input.Pressed( "attack1" ) || Input.Pressed( "attack2" ) )
				{
					var radius = 32f + MathF.Pow( Random.Shared.NextSingle(), 2f ) * 128f;

					if ( Input.Pressed( "attack2" ) )
					{
						radius *= 0.5f;
					}

					var min = radius + 8f;
					var max = 512f - 8f - radius;

					var sdf = new CircleSdf( new Vector2(
						Random.Shared.NextSingle() * (max - min) + min,
						Random.Shared.NextSingle() * (max - min) + min ), radius );

                    if ( Input.Pressed( "attack1" ) )
                    {
	                    var mat = ResourceLibrary.Get<MarchingSquaresMaterial>( "materials/sdf2d_default.msmat" );
                        Chunk.Add( sdf, mat );
                    }
					else
                    {
	                    Chunk.Subtract( sdf );
                    }

					Chunk.UpdateMesh();
                }
			}

            return;

            if ( !Game.IsServer || MarchingCubes == null || !(_lastEditTask?.IsCompleted ?? true) )
			{
				return;
			}

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

			var editPos = Owner.EyePosition + Owner.EyeRotation.Forward * EditDistance;

			if ( LastEditPos != null && ( editPos - LastEditPos.Value).Length < MinDistanceBetweenEdits )
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
