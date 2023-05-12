using Sandbox;
using System;
using System.Linq;
using static System.Runtime.CompilerServices.RuntimeHelpers;

namespace MiningDemo;

partial class Pawn : AnimatedEntity
{
	public const float FollowLightDistance = 64f;

	public PointLightEntity FollowLight { get; set; }
	public SpotLightEntity SpotLight { get; set; }

	// An example BuildInput method within a player's Pawn class.
	[ClientInput] public Vector3 InputDirection { get; protected set; }
	[ClientInput] public Vector3 AimPos { get; set; }

	[Net, Predicted]
	public bool IsDucking { get; set; }

	[ClientInput]
	public ButtonState Attack { get; set; }

	[Net, Predicted]
	public TimeSince LastAttack { get; set; }

	public Vector3 EyePos => Position + Vector3.Up * (IsDucking ? 32f : 64f);

	public override Ray AimRay => new( EyePos, (AimPos - EyePos).Normal );

	public ClothingContainer Clothing = new();

	public Pawn()
	{

	}

	public Pawn( IClient client )
	{
		Clothing.LoadFromClient( client );
	}

	/// <summary>
	/// Called when the entity is first created 
	/// </summary>
	public override void Spawn()
	{
		base.Spawn();

		SetModel( "models/citizen/citizen.vmdl" );

		EnableDrawing = true;
		EnableHideInFirstPerson = true;
		EnableShadowInFirstPerson = true;
	}

	public override void ClientSpawn()
	{
		base.ClientSpawn();

		FollowLight = new PointLightEntity
		{
			Range = 192f,
			BrightnessMultiplier = 0.25f
		};

		SpotLight = new SpotLightEntity { };

		SpotLight.SetParent( this, "head", new Transform( new Vector3( 16f, 0f, 0f ), Rotation.FromYaw( 90f ) ) );
	}

	public void Respawn()
	{
		Clothing.DressEntity( this );
	}

	public override void BuildInput()
	{
		InputDirection = Input.AnalogMove;

		var aimRay = new Ray( Camera.Position, Screen.GetDirection( Mouse.Position ) );
		var plane = new Plane( 0f, new Vector3( 1f, 0f, 0f ) );

		if ( plane.TryTrace( aimRay, out var hitPoint, true ) )
		{
			AimPos = hitPoint;
		}

		Attack = Input.Down( "attack1" );
	}

	/// <summary>
	/// Called every tick, clientside and serverside.
	/// </summary>
	public override void Simulate( IClient cl )
	{
		base.Simulate( cl );

		var animHelper = new CitizenAnimationHelper( this );

		if ( GroundEntity != null && InputDirection.x > 0.5f )
		{
			GroundEntity = null;
			Velocity = Velocity.WithZ( 400f );
		}

		var move = InputDirection.y;

		Rotation = AimPos.y < EyePos.y ? Rotation.FromYaw( -90f ) : Rotation.FromYaw( 90f );
		Velocity = Velocity.WithY( move * (IsDucking ? 60f : 100f) );

		if ( GroundEntity == null )
		{
			Velocity -= Vector3.Up * 600f * Time.Delta;
			animHelper.TriggerJump();
		}

		var helper = new MoveHelper( Position, Velocity.WithX( 0f ) );

		helper.Trace = helper.Trace
			.Size( BBox.FromHeightAndRadius( IsDucking ? 32f : 72f, 12f ) )
			.Ignore( this );

		if ( InputDirection.x < -0.5f )
		{
			IsDucking = true;
		}
		else if ( IsDucking && !helper.TraceDirection( Vector3.Up * (76f - 32f) ).Hit )
		{
			IsDucking = false;
		}

		helper.TryMoveWithStep( Time.Delta, 18f );

		var tr = helper.TraceDirection( Vector3.Up * -18f );

		if ( tr.Hit && Velocity.z <= 200f )
		{
			GroundEntity = tr.Entity;
			helper.TraceMove( -Vector3.Up * tr.Distance );
		}
		else
		{
			GroundEntity = null;
		}

		if ( LastAttack > 0.5f && Attack.IsDown )
		{
			LastAttack = 0f;
			SetAnimParameter( "b_attack", true );

			if ( Game.IsServer )
			{
				var attackResult = Trace.Ray( AimRay, 64f )
					.WorldAndEntities()
					.Ignore( this )
					.WithAnyTags( "solid" )
					.Run();

				if ( attackResult.Hit )
				{
					Log.Info( "Hit!" );
					MiningDemoGame.Current.MineOut( attackResult.HitPosition, 32f );
				}
			}
		}

		Position = helper.Position.WithX( 0f );
		Velocity = helper.Velocity.WithX( 0f );
		
		animHelper.WithVelocity( Velocity );
		animHelper.WithWishVelocity( Velocity.WithZ( 0f ) );
		animHelper.WithLookAt( AimPos );

		animHelper.IsWeaponLowered = false;
		animHelper.HoldType = CitizenAnimationHelper.HoldTypes.Swing;
		animHelper.IsGrounded = GroundEntity != null;
		animHelper.DuckLevel = IsDucking ? 1f : 0f;
	}

	/// <summary>
	/// Called every frame on the client
	/// </summary>
	public override void FrameSimulate( IClient cl )
	{
		const float cameraDist = 2048f;

		Camera.Position = Position + Vector3.Up * 128f - new Vector3( cameraDist, 0f, 0f );
		Camera.Rotation = Rotation.LookAt( Position + Vector3.Up * 64f - Camera.Position, Vector3.Up );

		// Set field of view to whatever the user chose in options
		Camera.FieldOfView = 20f;
		Camera.ZNear = cameraDist - 128f;
		Camera.ZFar = cameraDist + 128f;

		Sound.Listener = new Transform( Position + Vector3.Up * 64f - new Vector3( 256f, 0f ), Camera.Rotation );
	}

	[GameEvent.Client.Frame]
	private void ClientFrame()
	{
		SpotLight.LocalPosition = new Vector3( 16f, 4f, 0f );
		SpotLight.Rotation = Rotation.LookAt( AimRay.Forward );
		SpotLight.InnerConeAngle = 5f;
		SpotLight.OuterConeAngle = 45f;
		SpotLight.Range = 512f;
		SpotLight.Color = new Color( 0xD5E6FA );
		SpotLight.BrightnessMultiplier = 0.25f * Math.Clamp( LastAttack, 0f, 1f );
		SpotLight.DynamicShadows = true;

		FollowLight.BrightnessMultiplier = 0.001f;
		FollowLight.Range = 192f;
		FollowLight.Position = Position + Vector3.Up * 64f - new Vector3( FollowLightDistance, 0f, 0f );
		FollowLight.Color = new Color( 0xFFF6DF );
	}
}
