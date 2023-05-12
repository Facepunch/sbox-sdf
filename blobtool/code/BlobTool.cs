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
		public static Sdf2DWorld SdfWorld { get; set; }

		[ConCmd.Admin("sdf_2d_test")]
		public static async Task Sdf2DTest()
		{
			SdfWorld?.Delete();

			SdfWorld = new Sdf2DWorld
			{
				LocalPosition = new Vector3( -1024f, 1536f ),
				LocalRotation = Rotation.FromRoll( 90f )
			};

			var mapSdfTexture = await Texture.LoadAsync( FileSystem.Mounted, "textures/facepunch_sdf.png" );
			var mapSdf = new TextureSdf( mapSdfTexture, 64, 1024f );

			var baseMat = ResourceLibrary.Get<Sdf2DMaterial>( "materials/sdf2d_default.sdflayer" );
			var greyMat = ResourceLibrary.Get<Sdf2DMaterial>( "materials/sdf2d_darker.sdflayer" );

			SdfWorld.Add( mapSdf, baseMat );
			SdfWorld.Add( mapSdf.Expand( 16f ), greyMat );
        }

		public const float MinDistanceBetweenEdits = 4f;
		public const float MaxEditDistance = 512f;

		private Task _lastEditTask;

		public MarchingCubesEntity MarchingCubes { get; private set; }

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
			            var localPos = (Vector2) SdfWorld.Transform.PointToLocal( hitPos );

						var baseMat = ResourceLibrary.Get<Sdf2DMaterial>( "materials/sdf2d_default.sdflayer" );
						var greyMat = ResourceLibrary.Get<Sdf2DMaterial>( "materials/sdf2d_darker.sdflayer" );

						var sdf = new LineSdf( localPos, LastEditPos2D ?? localPos, radius );

						if ( add )
						{
						    SdfWorld.Add( sdf, greyMat );
						}
						else
						{
						    SdfWorld.Subtract( sdf.Expand( 8f ), baseMat );
						    SdfWorld.Subtract( sdf, greyMat );
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
