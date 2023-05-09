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

		[GameEvent.PreRender]
		private void Frame()
		{

        }

		public override void Simulate()
		{
			if ( Preview != null )
			{
				Preview.Position = Owner.EyePosition + Owner.EyeRotation.Forward * EditDistance;
				Preview.SceneObject.Attributes.Set( "ColorAdd", BrushColor );
				Preview.EnableDrawing = EditDistance > 64f && !IsDrawing;
			}

			if ( Chunk != null )
			{
				var mat = ResourceLibrary.Get<MarchingSquaresMaterial>( "materials/sdf2d_default.msmat" );

				Chunk.Clear( mat );
				Chunk.Subtract( new CircleSdf( new Vector2( 256f, 256f ), 128f ) );
				Chunk.Subtract( new CircleSdf( 0f, 64f )
					.Transform( new Vector2( 256f, 256f ) + (Vector2) Rotation.FromAxis( new Vector3( 0f, 0f, 1f ), Time.Now * 22.5f ).Forward * 160f ) );
                Chunk.UpdateMesh();
			}

            if ( !Game.IsServer || MarchingCubes == null || !(_lastEditTask?.IsCompleted ?? true) )
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
