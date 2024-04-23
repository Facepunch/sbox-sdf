using System;

namespace Sandbox.Sdf;

/// <summary>
/// Codegen for Broadcasts on classes with generic types seems to fail,
/// so this is added as a temporary measure.
/// </summary>
public sealed class SdfNetwork : Component
{
	[Property] public Sdf2DWorld SdfWorld { get; set; }
	public static SdfNetwork Instance { get; set; }

	public SdfNetwork()
	{
		Instance = this;
	}
	
	[Broadcast]
	public void SendMeMissing( Guid to, int clearCount, int modificationCount )
	{
		if ( Connection.Local != Connection.Host )
			return;

		var conn = Connection.Find( to );
		Log.Info( $"Want to send missing to {conn.DisplayName} : {conn.Name} with {clearCount}-{modificationCount}" );
		SdfWorld.RequestMissing( conn, clearCount, modificationCount );
	}

	private float _notifiedMissingModifications = float.PositiveInfinity;

	[Broadcast]
	public void WriteRpc( Guid guid, byte[] bytes )
	{
		if ( Connection.Local.Id != guid )
			return;

		var byteStream = ByteStream.CreateReader( bytes );
		if ( SdfWorld.Read( ref byteStream ) )
		{
			_notifiedMissingModifications = float.PositiveInfinity;
			return;
		}

		if ( _notifiedMissingModifications >= 0.5f )
		{
			_notifiedMissingModifications = 0f;

			SendMeMissing( Connection.Local.Id, SdfWorld.ClearCount, SdfWorld.ModificationCount );
		}
	}
}