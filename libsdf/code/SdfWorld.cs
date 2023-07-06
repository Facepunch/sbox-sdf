using Sandbox.Diagnostics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Sandbox.Utility;

namespace Sandbox.Sdf;

[AttributeUsage( AttributeTargets.Method )]
public sealed class RegisterSdfTypesAttribute : Attribute
{

}

public delegate T SdfReader<T>( ref NetRead read ) where T : ISdf<T>;
public delegate T SdfReader<TBase, T>( ref NetRead read ) where TBase : ISdf<TBase> where T : TBase;

public interface ISdf<T>
	where T : ISdf<T>
{
	// ReSharper disable StaticMemberInGenericType
#pragma warning disable SB3000
	private static readonly List<(TypeDescription Type, SdfReader<T> Reader)> _sRegisteredTypes = new();
#pragma warning restore SB3000
	private static bool _sTypesRegistered;
	// ReSharper enable StaticMemberInGenericType

	private static void EnsureTypesRegistered()
	{
		if ( _sTypesRegistered ) return;

		_sTypesRegistered = true;

		foreach ( var (method, _) in TypeLibrary.GetMethodsWithAttribute<RegisterSdfTypesAttribute>() )
		{
			method.Invoke( null );
		}

		_sRegisteredTypes.Sort( ( a, b ) => string.CompareOrdinal( a.Type.FullName, b.Type.FullName ) );
	}

	void WriteRaw( NetWrite writer );

	public static void RegisterType<TSdf>( SdfReader<T, TSdf> readRaw )
		where TSdf : T
	{
		_sRegisteredTypes.Add( (TypeLibrary.GetType( typeof(TSdf) ), ( ref NetRead read ) => readRaw( ref read )) );
	}

	public void Write( NetWrite writer )
	{
		EnsureTypesRegistered();

		var type = TypeLibrary.GetType( GetType() );
		var typeIndex = _sRegisteredTypes.FindIndex( x => x.Type == type );

		if ( typeIndex == -1 )
		{
			throw new NotImplementedException( $"Unable to serialize SDF type {type.FullName}" );
		}

		writer.Write( typeIndex );
		WriteRaw( writer );
	}

	public static T Read( ref NetRead reader )
	{
		EnsureTypesRegistered();

		var typeIndex = reader.Read<int>();
		var sdfReader = _sRegisteredTypes[typeIndex].Reader;

		return sdfReader( ref reader );
	}
}

internal static class ConsoleCommands
{
	[ConCmd.Server( "sdf_request_missing" )]
	public static void RequestMissingModifications( int worldIdent, int clearCount, int modificationCount )
	{
		var worldEnt = Entity.FindByIndex( worldIdent );
		var client = ConsoleSystem.Caller;

		if ( worldEnt is not ISdfWorld world )
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

internal interface ISdfWorld
{
	void RequestMissing( IClient client, int clearCount, int modificationCount );
}


/// <summary>
/// Base type for entities representing a set of volumes / layers containing geometry generated from
/// signed distance fields.
/// </summary>
/// <typeparam name="TWorld">Non-abstract world type</typeparam>
/// <typeparam name="TChunk">Non-abstract chunk type</typeparam>
/// <typeparam name="TResource">Volume / layer resource</typeparam>
/// <typeparam name="TChunkKey">Integer coordinates used to index a chunk</typeparam>
/// <typeparam name="TArray">Type of <see cref="SdfArray{TSdf}"/> used to contain samples</typeparam>
/// <typeparam name="TSdf">Interface for SDF shapes used to make modifications</typeparam>
public abstract partial class SdfWorld<TWorld, TChunk, TResource, TChunkKey, TArray, TSdf> : ModelEntity, ISdfWorld
	where TWorld : SdfWorld<TWorld, TChunk, TResource, TChunkKey, TArray, TSdf>
	where TChunk : SdfChunk<TWorld, TChunk, TResource, TChunkKey, TArray, TSdf>, new()
	where TResource : SdfResource<TResource>
	where TChunkKey : struct
	where TArray : SdfArray<TSdf>, new()
	where TSdf : ISdf<TSdf>
{
	private const int SendModificationsRpcIdent = 269924031;

	private enum Operator
	{
		Add,
		Subtract
	}

	private record struct Modification( TSdf Sdf, TResource Resource, Operator Operator );
	private record struct ClientState( int ClearCount, int ModificationCount, TimeSince LastMessage );

	private List<Modification> Modifications { get; } = new();
	private Dictionary<IClient, ClientState> ClientStates { get; } = new();

	private ConcurrentQueue<TChunk> UpdatedChunkQueue { get; } = new();

	private bool _receivingModifications;
	private int _clearCount;
	private Transform _lastTransform;

	private TimeSince _notifiedMissingModifications;

	/// <inheritdoc />
	public override void Spawn()
	{
		base.Spawn();

		Transmit = TransmitType.Always;
	}

	internal bool IsDestroying { get; private set; }

	/// <inheritdoc />
	protected override void OnDestroy()
	{
		IsDestroying = true;

		_ = ClearAsync();
	}

	private class Layer
	{
		public Dictionary<TChunkKey, TChunk> Chunks { get; } = new();
		public HashSet<TChunk> NeedsMeshUpdate { get; } = new();
		public Task UpdateMeshTask { get; set; } = System.Threading.Tasks.Task.CompletedTask;
	}

	private Dictionary<TResource, Layer> Layers { get; } = new();
	private List<TChunk> AllChunks { get; } = new();

	private Task _lastModificationTask = System.Threading.Tasks.Task.CompletedTask;

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

			for ( var i = AllChunks.Count - 1; i >= 0; --i )
			{
				AllChunks[i].UpdateTransform();
			}
		}
	}

	[GameEvent.Tick]
	private void Tick()
	{
		ProcessUpdatedChunkQueue();

		foreach ( var layer in Layers.Values )
		{
			if ( layer.NeedsMeshUpdate.Count > 0 && layer.UpdateMeshTask.IsCompleted )
			{
				DispatchUpdateMesh( layer );
			}
		}
	}

	private const int MaxModificationsPerMessage = 256;
	private const float HeartbeatPeriod = 2f;

	private void SendModifications( IClient client )
	{
		if ( !ClientStates.TryGetValue( client, out var state ) )
		{
			state = new ClientState( 0, 0, 0f );
		}

		if ( state.ClearCount != _clearCount )
		{
			state = state with { ClearCount = _clearCount, ModificationCount = 0 };
		}
		else if ( state.ModificationCount >= Modifications.Count && state.LastMessage < HeartbeatPeriod )
		{
			return;
		}

		state = state with { LastMessage = 0f };

		var msg = NetWrite.StartRpc( SendModificationsRpcIdent, this );
		var count = Math.Min( MaxModificationsPerMessage, Modifications.Count - state.ModificationCount );

		msg.Write( _clearCount );
		msg.Write( state.ModificationCount );
		msg.Write( count );
		msg.Write( Modifications.Count );

		for ( var i = 0; i < count; ++i )
		{
			var modification = Modifications[state.ModificationCount + i];

			msg.Write( modification.Operator );
			msg.Write( modification.Resource );
			modification.Sdf.Write( msg );
		}

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

	private IDisposable AllowClientModifications()
	{
		_receivingModifications = true;

		return new DisposeAction( () => _receivingModifications = false );
	}

	private void ReceiveModifications( ref NetRead msg )
	{
		var clearCount = msg.Read<int>();
		var prevCount = msg.Read<int>();
		var msgCount = msg.Read<int>();
		var totalCount = msg.Read<int>();

		using var clientMods = AllowClientModifications();

		if ( clearCount < _clearCount )
		{
			// Outdated
			return;
		}

		if ( clearCount > _clearCount )
		{
			_clearCount = clearCount;
			_ = ClearAsync();

			Assert.AreEqual( 0, Modifications.Count );
		}

		if ( prevCount != Modifications.Count )
		{
			if ( _notifiedMissingModifications >= 0.5f )
			{
				_notifiedMissingModifications = 0f;
				Log.Warning( $"{this} has dropped some modifications! {prevCount} vs {Modifications.Count}" );

				ConsoleCommands.RequestMissingModifications( NetworkIdent, _clearCount, Modifications.Count );
			}

			return;
		}

		_notifiedMissingModifications = float.PositiveInfinity;

		for ( var i = 0; i < msgCount; ++i )
		{
			var op = msg.Read<Operator>();
			var res = msg.ReadClass<TResource>();
			var sdf = ISdf<TSdf>.Read( ref msg );

			var modification = new Modification( sdf, res, op );

			switch ( modification.Operator )
			{
				case Operator.Add:
					_ = AddAsync( modification.Sdf, modification.Resource );
					break;

				case Operator.Subtract:
					_ = SubtractAsync( modification.Sdf, modification.Resource );
					break;
			}
		}
	}

	[Obsolete($"Please use {nameof(ClearAsync)}")]
	public void Clear()
	{
		_ = ClearAsync();
	}

	/// <summary>
	/// Removes all layers / volumes, making this equivalent to a brand new empty world.
	/// </summary>
	public async Task ClearAsync()
	{
		if ( !IsDestroying )
		{
			AssertCanModify();
		}

		await Task.MainThread();

		Modifications.Clear();

		if ( Game.IsServer )
		{
			++_clearCount;
		}

		lock ( this )
		{
			_lastModificationTask = ClearImpl();
		}

		await _lastModificationTask;
	}

	private async Task ClearImpl()
	{
		ThreadSafe.AssertIsMainThread();

		var lastTask = _lastModificationTask;

		if ( !lastTask.IsCompleted )
		{
			await lastTask;
		}

		await Task.MainThread();

		foreach ( var layer in Layers.Values )
		{
			await layer.UpdateMeshTask;

			foreach ( var chunk in layer.Chunks.Values )
			{
				chunk.Dispose();
			}

			layer.Chunks.Clear();
		}

		AllChunks.Clear();
	}

	[Obsolete( $"Please use {nameof( ClearAsync )}" )]
	public void Clear( TResource resource )
	{
		_ = ClearAsync( resource );
	}

	/// <summary>
	/// Removes the given layer or volume.
	/// </summary>
	/// <param name="resource">Layer or volume to clear</param>
	public Task ClearAsync( TResource resource )
	{
		throw new NotImplementedException();
	}

	private void EnsureLayerExists( TResource resource )
	{
		ThreadSafe.AssertIsMainThread();

		if ( !Layers.ContainsKey( resource ) )
		{
			Layers.Add( resource, new Layer() );
		}
	}

	[Obsolete( $"Please use {nameof( AddAsync )}" )]
	public bool Add<T>( in T sdf, TResource resource )
		where T : TSdf
	{
		_ = AddAsync( in sdf, resource );
		return false;
	}

	/// <summary>
	/// Add a shape to the given layer or volume.
	/// </summary>
	/// <typeparam name="T">SDF type</typeparam>
	/// <param name="sdf">Shape to add</param>
	/// <param name="resource">Layer or volume to add to</param>
	/// <returns>True if any geometry was modified</returns>
	public Task AddAsync<T>( in T sdf, TResource resource )
		where T : TSdf
	{
		EnsureLayerExists( resource );

		Modifications.Add( new Modification( sdf, resource, Operator.Add ) );

		return ModifyChunksAsync( sdf, resource, true, ( chunk, sdf ) => chunk.AddAsync( sdf ) );
	}

	[Obsolete( $"Please use {nameof( SubtractAsync )}" )]
	public bool Subtract<T>( in T sdf, TResource resource )
		where T : TSdf
	{
		_ = SubtractAsync( in sdf, resource );
		return false;
	}

	/// <summary>
	/// Subtract a shape from the given layer or volume.
	/// </summary>
	/// <typeparam name="T">SDF type</typeparam>
	/// <param name="sdf">Shape to subtract</param>
	/// <param name="resource">Layer or volume to subtract from</param>
	/// <returns>True if any geometry was modified</returns>
	public Task SubtractAsync<T>( in T sdf, TResource resource )
		where T : TSdf
	{
		EnsureLayerExists( resource );

		Modifications.Add( new Modification( sdf, resource, Operator.Subtract ) );

		return ModifyChunksAsync( sdf, resource, false, ( chunk, sdf ) => chunk.SubtractAsync( sdf ) );
	}

	[Obsolete( $"Please use {nameof( SubtractAsync )}" )]
	public bool Subtract<T>( in T sdf )
		where T : TSdf
	{
		_ = SubtractAsync( sdf );
		return false;
	}

	/// <summary>
	/// Subtract a shape from all layers.
	/// </summary>
	/// <typeparam name="T">SDF type</typeparam>
	/// <param name="sdf">Shape to subtract</param>
	/// <returns>True if any geometry was modified</returns>
	public async Task SubtractAsync<T>( T sdf )
		where T : TSdf
	{
		var tasks = new List<Task>();

		foreach ( var material in Layers.Keys )
			tasks.Add( SubtractAsync( sdf, material ) );

		await GameTask.WhenAll( tasks );
	}

	internal TChunk GetChunk( TResource resource, TChunkKey key )
	{
		if ( IsDestroying ) return null;

		return Layers.TryGetValue( resource, out var layerData )
			&& layerData.Chunks.TryGetValue( key, out var chunk )
				? chunk
				: null;
	}

	private TChunk GetOrCreateChunk( TResource resource, TChunkKey key )
	{
		if ( IsDestroying ) return null;

		if ( !Layers.TryGetValue( resource, out var layerData ) )
		{
			throw new Exception( "Layer doesn't exist!" );
		}

		if ( layerData.Chunks.TryGetValue( key, out var chunk ) )
		{
			return chunk;
		}

		layerData.Chunks[key] = chunk = new TChunk();
		chunk.Init( (TWorld) this, resource, key );

		AllChunks.Add( chunk );

		return chunk;
	}

	private void AssertCanModify()
	{
		Assert.True( IsClientOnly || Game.IsServer || _receivingModifications, 
			"Can only modify server-created SDF Worlds on the server." );
	}

	internal void ChunkMeshUpdated( TChunk chunk, bool removed )
	{
		if ( !Game.IsClient ) return;

		foreach ( var (key, value) in Layers )
		{
			if ( key.ReferencedTextures == null ) continue;

			if ( key == chunk.Resource ) continue;

			foreach ( var layerTexture in key.ReferencedTextures )
			{
				if ( layerTexture.Source != chunk.Resource ) continue;

				if ( value.Chunks.TryGetValue( chunk.Key, out var matching ) )
					matching.UpdateLayerTexture( chunk.Resource, removed ? null : chunk );
			}
		}
	}

	internal PhysicsShape AddMeshShape( List<Vector3> vertices, List<int> indices )
	{
		if ( PhysicsBody == null )
		{
			SetupPhysicsFromSphere( PhysicsMotionType.Static, 0f, 1f );
			PhysicsBody!.ClearShapes();
		}

		return PhysicsBody.AddMeshShape( vertices, indices );
	}

	/// <summary>
	/// Implements getting the indices of chunks within the bounds of the given SDF.
	/// </summary>
	/// <typeparam name="T">SDF type.</typeparam>
	/// <param name="sdf">SDF to check the bounds of.</param>
	/// <param name="quality">Quality setting that affects chunk size.</param>
	/// <returns>Indices of possible affected chunks.</returns>
	protected abstract IEnumerable<TChunkKey> GetAffectedChunks<T>( T sdf, WorldQuality quality)
		where T : TSdf;
	
	private async Task ModifyChunksAsync<T>( T sdf, TResource resource, bool createChunks,
		Func<TChunk, T, Task<bool>> func )
		where T : TSdf
	{
		AssertCanModify();

		if ( resource == null ) throw new ArgumentNullException( nameof( resource ) );

		if ( !Game.IsClient && !resource.HasCollision )
		{
			// Only care about collision on the server
			return;
		}

		Task task;

		lock ( this )
		{
			_lastModificationTask = task = ModifyChunksAsyncImpl( sdf, resource, createChunks, func );
		}

		await task;
	}

	private async Task ModifyChunksAsyncImpl<T>( T sdf, TResource resource, bool createChunks,
		Func<TChunk, T, Task<bool>> func )
		where T : TSdf
	{
		var prevTask = _lastModificationTask;

		await GameTask.WorkerThread();

		if ( !prevTask.IsCompleted )
		{
			await prevTask;
		}

		try
		{
			var tasks = new List<(TChunk Chunk, Task<bool> Task)>();

			foreach ( var key in GetAffectedChunks( sdf, resource.Quality ) )
			{
				var chunk = !createChunks
					? GetChunk( resource, key )
					: GetOrCreateChunk( resource, key );

				if ( chunk == null ) continue;

				tasks.Add( (chunk, func( chunk, sdf )) );
			}

			var result = await GameTask.WhenAll( tasks.Select( x => x.Task ) );

			for ( var i = 0; i < tasks.Count; ++i )
			{
				if ( result[i] )
				{
					UpdatedChunkQueue.Enqueue( tasks[i].Chunk );
				}
			}
		}
		catch ( Exception e )
		{
			Log.Error( e );
		}
	}

	private void ProcessUpdatedChunkQueue()
	{
		ThreadSafe.AssertIsMainThread();

		while ( UpdatedChunkQueue.TryDequeue( out var chunk ) )
		{
			if ( !chunk.IsValid )
			{
				continue;
			}

			if ( !Layers.TryGetValue( chunk.Resource, out var layer ) )
			{
				continue;
			}

			layer.NeedsMeshUpdate.Add( chunk );
		}
	}

	private void DispatchUpdateMesh( Layer layer )
	{
		ThreadSafe.AssertIsMainThread();

		if ( layer.NeedsMeshUpdate.Count == 0 )
		{
			return;
		}

		if ( layer.UpdateMeshTask.IsCompleted )
		{
			layer.UpdateMeshTask = UpdateMesh( layer );
		}
	}

	private async Task UpdateMesh( Layer layer )
	{
		ThreadSafe.AssertIsMainThread();

		var tasks = layer.NeedsMeshUpdate.Select( x => x.UpdateMesh() ).ToArray();

		layer.NeedsMeshUpdate.Clear();

		await Task.WhenAll( tasks );
	}

	void ISdfWorld.RequestMissing( IClient client, int clearCount, int modificationCount )
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
