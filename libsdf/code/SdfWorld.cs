using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
#pragma warning disable SB3000
	private static readonly List<(TypeDescription Type, SdfReader<T> Reader)> _sRegisteredTypes = new();
	private static bool _sTypesRegistered;
#pragma warning restore SB3000

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
public abstract partial class SdfWorld<TWorld, TChunk, TResource, TChunkKey, TArray, TSdf> : ModelEntity
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

	private List<Modification> Modifications { get; } = new();
	private Dictionary<IClient, int> ClientModificationCounts { get; } = new ();

	private bool _receivingModifications;

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
		base.OnDestroy();

		IsDestroying = true;
	}

	private record struct Layer( Dictionary<TChunkKey, TChunk> Chunks );

	private Dictionary<TResource, Layer> Layers { get; } = new();

	internal TimeSpan CurrentTickChunkTaskDuration { get; set; }

	[GameEvent.Tick]
	private void Tick()
	{
		CurrentTickChunkTaskDuration = TimeSpan.Zero;
	}

	[GameEvent.Tick.Server]
	private void ServerTick()
	{
		foreach ( var client in Game.Clients )
		{
			SendModifications( client );
		}
	}

	private void SendModifications( IClient client )
	{
		if ( !ClientModificationCounts.TryGetValue( client, out var modified ) )
		{
			modified = 0;
		}

		if ( modified >= Modifications.Count )
		{
			return;
		}

		var msg = NetWrite.StartRpc( SendModificationsRpcIdent, this );
		var count = Math.Min( 8, Modifications.Count - modified );

		msg.Write( modified );
		msg.Write( count );
		msg.Write( Modifications.Count );

		for ( var i = 0; i < count; ++i )
		{
			var modification = Modifications[modified + i];

			msg.Write( modification.Operator );
			msg.Write( modification.Resource );
			modification.Sdf.Write( msg );
		}

		ClientModificationCounts[client] = modified + count;

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
		var prevCount = msg.Read<int>();
		var msgCount = msg.Read<int>();
		var totalCount = msg.Read<int>();

		if ( prevCount != Modifications.Count )
		{
			// TODO: ask for them again
			Log.Error( $"{GetType()} has dropped some modifications! {prevCount} vs {Modifications.Count}" );
			return;
		}

		_receivingModifications = true;

		try
		{
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
		finally
		{
			_receivingModifications = false;
		}
	}

	/// <summary>
	/// Removes all layers / volumes, making this equivalent to a brand new empty world.
	/// </summary>
	public void Clear()
	{
		AssertCanModify();

		foreach ( var layer in Layers.Values )
			foreach ( var chunk in layer.Chunks.Values )
				chunk.Dispose();

		Layers.Clear();
	}

	/// <summary>
	/// Removes the given layer or volume.
	/// </summary>
	/// <param name="resource">Layer or volume to clear</param>
	public void Clear( TResource resource )
	{
		AssertCanModify();

		if ( Layers.Remove( resource, out var layerData ) )
			foreach ( var chunk in layerData.Chunks.Values )
				chunk.Dispose();
	}

	/// <summary>
	/// Add a shape to the given layer or volume.
	/// </summary>
	/// <typeparam name="T">SDF type</typeparam>
	/// <param name="sdf">Shape to add</param>
	/// <param name="resource">Layer or volume to add to</param>
	/// <returns>True if any geometry was modified</returns>
	public Task<bool> AddAsync<T>( in T sdf, TResource resource )
		where T : TSdf
	{
		Modifications.Add( new Modification( sdf, resource, Operator.Add ) );

		return ModifyChunksAsync( sdf, resource, true, ( chunk, sdf ) => chunk.AddAsync( sdf ) );
	}

	/// <summary>
	/// Subtract a shape from the given layer or volume.
	/// </summary>
	/// <typeparam name="T">SDF type</typeparam>
	/// <param name="sdf">Shape to subtract</param>
	/// <param name="resource">Layer or volume to subtract from</param>
	/// <returns>True if any geometry was modified</returns>
	public Task<bool> SubtractAsync<T>( in T sdf, TResource resource )
		where T : TSdf
	{
		Modifications.Add( new Modification( sdf, resource, Operator.Subtract ) );

		return ModifyChunksAsync( sdf, resource, false, ( chunk, sdf ) => chunk.SubtractAsync( sdf ) );
	}

	/// <summary>
	/// Subtract a shape from all layers.
	/// </summary>
	/// <typeparam name="T">SDF type</typeparam>
	/// <param name="sdf">Shape to subtract</param>
	/// <returns>True if any geometry was modified</returns>
	public async Task<bool> SubtractAsync<T>( T sdf )
		where T : TSdf
	{
		var tasks = new List<Task<bool>>();

		foreach ( var material in Layers.Keys )
			tasks.Add( SubtractAsync( sdf, material ) );

		var result = await GameTask.WhenAll( tasks );

		return result.Any( x => x );
	}

	internal void AddClientChunk( TChunk chunk )
	{
		Assert.True( Game.IsClient );

		if ( !Layers.TryGetValue( chunk.Resource, out var layer ) )
			Layers.Add( chunk.Resource, layer = new Layer( new Dictionary<TChunkKey, TChunk>() ) );

		layer.Chunks[chunk.Key] = chunk;
	}

	internal void RemoveClientChunk( TChunk chunk )
	{
		if ( !Layers.TryGetValue( chunk.Resource, out var layer ) ) return;

		if ( layer.Chunks.TryGetValue( chunk.Key, out var existing ) && existing == chunk )
		{
			layer.Chunks.Remove( chunk.Key );

			ChunkMeshUpdated( chunk, true );
		}
	}

	internal TChunk GetChunk( TResource resource, TChunkKey key )
	{
		return Layers.TryGetValue( resource, out var layerData )
			&& layerData.Chunks.TryGetValue( key, out var chunk )
				? chunk
				: null;
	}

	private TChunk GetOrCreateChunk( TResource resource, TChunkKey key )
	{
		if ( !Layers.TryGetValue( resource, out var layerData ) )
		{
			layerData = new Layer( new Dictionary<TChunkKey, TChunk>() );
			Layers.Add( resource, layerData );
		}

		if ( layerData.Chunks.TryGetValue( key, out var chunk ) )
		{
			return chunk;
		}

		layerData.Chunks[key] = chunk = new TChunk();

		chunk.Init( (TWorld) this, resource, key );

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
	
	private async Task<bool> ModifyChunksAsync<T>( T sdf, TResource resource, bool createChunks,
		Func<TChunk, T, Task<bool>> func )
		where T : TSdf
	{
		ThreadSafe.AssertIsMainThread();
		AssertCanModify();

		if ( resource == null ) throw new ArgumentNullException( nameof( resource ) );

		var tasks = new List<Task<bool>>();

		foreach ( var key in GetAffectedChunks( sdf, resource.Quality ) )
		{
			var chunk = !createChunks
				? GetChunk( resource, key )
				: GetOrCreateChunk( resource, key );

			if ( chunk == null ) continue;

			tasks.Add( func( chunk, sdf ) );
		}

		var result = await GameTask.WhenAll( tasks );

		return result.Any( x => x );
	}
}
