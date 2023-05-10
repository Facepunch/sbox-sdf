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
		public static async Task Sdf2DTest( int resolution = 16 )
		{
			SdfWorld?.Delete();

			SdfWorld = new Sdf2DWorld( resolution, 256f )
			{
				LocalPosition = new Vector3( -256f, -2470f ),
				LocalRotation = Rotation.FromRoll( 90f )
			};

			var mapSdfTexture = await Texture.LoadAsync( FileSystem.Mounted, "textures/example_sdf.png" );
			var mapSdf = new TextureSdf( mapSdfTexture, 32, 2048f );
			var mat = ResourceLibrary.Get<MarchingSquaresMaterial>( "materials/sdf2d_default.msmat" );

			SdfWorld.Add( mapSdf, mat );
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
			            var localPos = SdfWorld.Transform.PointToLocal( hitPos );

			            var sdf = new CircleSdf( new Vector2( localPos.x, localPos.y ), radius );

			            if ( add )
			            {
				            var mat = ResourceLibrary.Get<MarchingSquaresMaterial>( "materials/sdf2d_default.msmat" );
				            SdfWorld.Add( sdf, mat );
			            }
			            else
			            {
				            SdfWorld.Subtract( sdf );
			            }
                    }
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
