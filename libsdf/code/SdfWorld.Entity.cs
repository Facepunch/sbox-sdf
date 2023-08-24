using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using Sandbox.Internal;

namespace Sandbox.Sdf;

internal partial class SdfWorldEntity : ModelEntity, ISdfWorldImpl
{
	public ISdfWorld World { get; private set; }

	[Net]
	public int Dimensions { get; set; }

	public SdfWorldEntity( ISdfWorld world )
	{
		World = world;
		Dimensions = world.Dimensions;
	}

	public SdfWorldEntity()
	{

	}

	EntityTags ISdfWorldImpl.Tags => Tags;

	public override void Spawn()
	{
		Transmit = TransmitType.Always;
	}

	protected override void OnDestroy()
	{
		World?.Dispose();
	}

	public override void ClientSpawn()
	{
		switch ( Dimensions )
		{
			case 2:
				World = new Sdf2DWorld( this );
				break;

			case 3:
				World = new Sdf3DWorld( this );
				break;
		}
	}

	[GameEvent.Tick]
	private void Tick()
	{
		World?.Tick();
	}

	[GameEvent.Tick.Server]
	private void ServerTick()
	{
		foreach ( var client in Game.Clients )
		{
			SendModifications( client );
		}
	}

	[GameEvent.Tick.Client]
	private void ClientTick()
	{
		if ( !_lastTransform.Equals( Transform ) )
		{
			_lastTransform = Transform;
			World.UpdateChunkTransforms();
		}
	}

	private const int SendModificationsRpcIdent = 269924031;

	private Dictionary<IClient, ClientState> ClientStates { get; } = new();
	private record struct ClientState( int ClearCount, int ModificationCount, TimeSince LastMessage );

	private Transform _lastTransform;

	private TimeSince _notifiedMissingModifications;

	private const float HeartbeatPeriod = 2f;

	private void SendModifications( IClient client )
	{
		if ( !ClientStates.TryGetValue( client, out var state ) )
		{
			state = new ClientState( 0, 0, 0f );
		}

		if ( state.ClearCount != World.ClearCount )
		{
			state = state with { ClearCount = World.ClearCount, ModificationCount = 0 };
		}
		else if ( state.ModificationCount >= World.ModificationCount && state.LastMessage < HeartbeatPeriod )
		{
			return;
		}

		state = state with { LastMessage = 0f };

		var msg = NetWrite.StartRpc( SendModificationsRpcIdent, this );
		var count = World.Write( msg, state.ModificationCount );

		ClientStates[client] = state with { ModificationCount = state.ModificationCount + count };

		msg.SendRpc( To.Single( client ), this );
	}

	protected override void OnCallRemoteProcedure( int id, NetRead read )
	{
		switch ( id )
		{
			case SendModificationsRpcIdent:
				ReceiveModifications( ref read );
				break;

			default:
				base.OnCallRemoteProcedure( id, read );
				break;
		}
	}

	private void ReceiveModifications( ref NetRead msg )
	{
		if ( World.Read( ref msg ) )
		{
			_notifiedMissingModifications = float.PositiveInfinity;
			return;
		}

		if ( _notifiedMissingModifications >= 0.5f )
		{
			_notifiedMissingModifications = 0f;

			ConsoleCommands.RequestMissingModifications( NetworkIdent, World.ClearCount, World.ModificationCount );
		}
	}

	public bool HasPhysics => true;

	public PhysicsShape AddMeshShape( List<Vector3> vertices, List<int> indices )
	{
		if ( PhysicsBody == null )
		{
			SetupPhysicsFromSphere( PhysicsMotionType.Static, 0f, 1f );
			PhysicsBody!.ClearShapes();
		}

		return PhysicsBody.AddMeshShape( vertices, indices );
	}
	
	public void RequestMissing( IClient client, int clearCount, int modificationCount )
	{
		if ( !ClientStates.TryGetValue( client, out var state ) )
		{
			return;
		}

		if ( state.ClearCount != clearCount || state.ModificationCount <= modificationCount )
		{
			return;
		}

		ClientStates[client] = state with { ModificationCount = modificationCount };
	}
}

internal static class ConsoleCommands
{
	[ConCmd.Server( "sdf_request_missing" )]
	public static void RequestMissingModifications( int worldIdent, int clearCount, int modificationCount )
	{
		var worldEnt = Entity.FindByIndex( worldIdent );
		var client = ConsoleSystem.Caller;

		if ( worldEnt is not SdfWorldEntity world )
		{
			return;
		}

		if ( !client.IsValid )
		{
			return;
		}

		world.RequestMissing( client, clearCount, modificationCount );
	}
}
