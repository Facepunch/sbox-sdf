using Sandbox;
using System;
using System.Linq;
using MiningDemo.UI;
using Sandbox.Sdf;

namespace MiningDemo;

/// <summary>
/// This is your game class. This is an entity that is created serverside when
/// the game starts, and is replicated to the client. 
/// 
/// You can use this to create things like HUDs and declare which player class
/// to use for spawned players.
/// </summary>
public partial class MiningDemoGame : GameManager
{
	[Net]
	public Sdf2DWorld SdfWorld { get; set; }

	public HudRoot HudRoot { get; set; }

	public new static MiningDemoGame Current => GameManager.Current as MiningDemoGame;

	public MiningDemoGame()
	{
		if ( Game.IsClient )
		{
			HudRoot = new HudRoot();
		}
	}

	public override void PostLevelLoaded()
	{
		base.PostLevelLoaded();

		var cavernTexture = Texture.Load( FileSystem.Mounted, "textures/cavern_sdf.png" );
		var cavernSdf = new TextureSdf( cavernTexture, 128, 4096f, pivot: 0f )
			.Translate( new Vector2( -2048f, -8192f ) );

		SdfWorld = new Sdf2DWorld { Rotation = Rotation.FromYaw( -90f ) * Rotation.FromRoll( 90f ) };
		SdfWorld.Add( cavernSdf.Expand( -32f ), Layers.Rock );
		SdfWorld.Add( new RectSdf(new Vector2( -2048f, -8192f ), new Vector2( 2048f, 0f ) ), Layers.Background );
	}

	/// <summary>
	/// A client has joined the server. Make them a pawn to play with
	/// </summary>
	public override void ClientJoined( IClient client )
	{
		base.ClientJoined( client );

		// Create a pawn for this client to play with
		var pawn = new Pawn( client );
		client.Pawn = pawn;

		pawn.Position = Vector3.Up * -512f;
		pawn.Respawn();
	}

	public void MineOut( Vector3 pos, float radius )
	{
		var localPos = (Vector2)SdfWorld.Transform.PointToLocal( pos );
		var sdf = new CircleSdf( localPos, radius );

		SdfWorld.Subtract( sdf, Layers.Rock );

		Particles.Create( "particles/mine_hit.vpcf", pos );
		Sound.FromWorld( "melee.hitstone", pos );
	}
}
