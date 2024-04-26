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

	[Broadcast]
	private void SendMeMissing( int clearCount, int modificationCount )
	{
		Log.Info( $"Want to send missing to {Rpc.Caller.DisplayName} : {Rpc.Caller.Name} with {clearCount}-{modificationCount}" );

		SdfWorld.RequestMissing( Rpc.Caller, clearCount, modificationCount );
	}

	private TimeSince _notifiedMissingModifications = float.PositiveInfinity;

	[Broadcast]
	public void WriteRpc( byte[] bytes )
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

			using ( Rpc.FilterOnly( Rpc.Caller ) )
			{
				SendMeMissing( SdfWorld.ClearCount, SdfWorld.ModificationCount );
			}
		}
	}
}
