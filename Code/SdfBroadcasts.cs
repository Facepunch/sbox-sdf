using System;
using System.Linq;
using Sandbox.Diagnostics;

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

	[Unicast]
	public void SendMeMissing( Connection connection, int clearCount, int modificationCount )
	{
		Log.Info( $"Want to send missing to {connection.DisplayName} : {connection.Name} with {clearCount}-{modificationCount}" );
		SdfWorld.RequestMissing( connection, clearCount, modificationCount );
	}

	private TimeSince _notifiedMissingModifications = float.PositiveInfinity;

	[Unicast]
	public void WriteRpc( Connection connection, byte[] bytes )
	{
		var byteStream = ByteStream.CreateReader( bytes );
		if ( SdfWorld.Read( ref byteStream ) )
		{
			_notifiedMissingModifications = float.PositiveInfinity;
			return;
		}

		if ( _notifiedMissingModifications >= 0.5f )
		{
			_notifiedMissingModifications = 0f;

			SendMeMissing( connection, SdfWorld.ClearCount, SdfWorld.ModificationCount );
		}
	}
}
