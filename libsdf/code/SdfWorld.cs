using Sandbox.Diagnostics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sandbox.Internal;
using Sandbox.Utility;

namespace Sandbox.Sdf;

[AttributeUsage( AttributeTargets.Method )]
public sealed class RegisterSdfTypesAttribute : Attribute
{

}

public delegate T SdfReader<T>( ref NetRead read, IReadOnlyDictionary<int, SdfReader<T>> sdfTypes ) where T : ISdf<T>;
public delegate T SdfReader<TBase, T>( ref NetRead read, IReadOnlyDictionary<int, SdfReader<TBase>> sdfTypes ) where TBase : ISdf<TBase> where T : TBase;

public interface ISdf<T>
	where T : ISdf<T>
{
	// ReSharper disable StaticMemberInGenericType
#pragma warning disable SB3000
	internal static List<(TypeDescription Type, SdfReader<T> Reader)> RegisteredTypes { get; } = new();
	private static bool _sTypesRegistered;
	// ReSharper enable StaticMemberInGenericType
#pragma warning restore SB3000

	internal static void EnsureTypesRegistered()
	{
		if ( _sTypesRegistered ) return;

		_sTypesRegistered = true;

		foreach ( var (method, _) in GlobalGameNamespace.TypeLibrary.GetMethodsWithAttribute<RegisterSdfTypesAttribute>() )
		{
			method.Invoke( null );
		}

		RegisteredTypes.Sort( ( a, b ) => string.CompareOrdinal( a.Type.FullName, b.Type.FullName ) );
	}

	void WriteRaw( NetWrite writer, Dictionary<TypeDescription, int> sdfTypes );

	public static void RegisterType<TSdf>( SdfReader<T, TSdf> readRaw )
		where TSdf : T
	{
		RegisteredTypes.Add( (GlobalGameNamespace.TypeLibrary.GetType( typeof(TSdf) ),
			( ref NetRead read, IReadOnlyDictionary<int, SdfReader<T>> sdfTypes ) => readRaw( ref read, sdfTypes )) );
	}

	public void Write( NetWrite writer, Dictionary<TypeDescription, int> sdfTypes )
	{
		EnsureTypesRegistered();

		var type = GlobalGameNamespace.TypeLibrary.GetType( GetType() );

		if ( !sdfTypes.TryGetValue( type, out var typeIndex ) )
		{
			typeIndex = RegisteredTypes.FindIndex( x => x.Type == type );

			if ( typeIndex == -1 )
			{
				throw new NotImplementedException( $"Unable to serialize SDF type {type.FullName}" );
			}

			sdfTypes[type] = typeIndex;
		}

		writer.Write( typeIndex );
		WriteRaw( writer, sdfTypes );
	}

	public static T Read( ref NetRead reader, IReadOnlyDictionary<int, SdfReader<T>> sdfTypes )
	{
		var typeIndex = reader.Read<int>();
		var sdfReader = sdfTypes[typeIndex];

		return sdfReader( ref reader, sdfTypes );
	}
}

internal interface ISdfWorld : IDisposable, IValid
{
	int ClearCount { get; }
	int ModificationCount { get; }
	int Dimensions { get; }

	void UpdateChunkTransforms();
	void Update();

	int Write( NetWrite msg, int prevModifications );
	bool Read( ref NetRead msg );
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
public abstract partial class SdfWorld<TWorld, TChunk, TResource, TChunkKey, TArray, TSdf> : ISdfWorld
	where TWorld : SdfWorld<TWorld, TChunk, TResource, TChunkKey, TArray, TSdf>
	where TChunk : SdfChunk<TWorld, TChunk, TResource, TChunkKey, TArray, TSdf>, new()
	where TResource : SdfResource<TResource>
	where TChunkKey : struct
	where TArray : SdfArray<TSdf>, new()
	where TSdf : ISdf<TSdf>
{
	public static implicit operator Entity( SdfWorld<TWorld, TChunk, TResource, TChunkKey, TArray, TSdf> world )
	{
		return world._impl as Entity;
	}

	private enum Operator
	{
		Add,
		Subtract
	}

	private record struct Modification( TSdf Sdf, TResource Resource, Operator Operator );

	private List<Modification> Modifications { get; } = new();

	private ConcurrentQueue<TChunk> UpdatedChunkQueue { get; } = new();

	private readonly ISdfWorldImpl _impl;

	private bool _receivingModifications;

	/// <summary>
	/// Spacial dimensions. 2 for <see cref="Sdf2DWorld"/>, 3 for <see cref="Sdf3DWorld"/>.
	/// </summary>
	public abstract int Dimensions { get; }

	/// <summary>
	/// How many times this world has been cleared.
	/// </summary>
	public int ClearCount { get; private set; }

	/// <summary>
	/// How many modifications have been applied since the last time this world was cleared.
	/// </summary>
	public int ModificationCount => Modifications.Count;

	public bool IsValid => _impl.IsValid();
	public SceneWorld Scene => _impl.Scene;

	public Transform Transform
	{
		get => _impl.Transform;
		set => _impl.Transform = value;
	}

	public Vector3 Position
	{
		get => _impl.Position;
		set => _impl.Position = value;
	}

	public Rotation Rotation
	{
		get => _impl.Rotation;
		set => _impl.Rotation = value;
	}

	public float Scale
	{
		get => _impl.Scale;
		set => _impl.Scale = value;
	}

	public Vector3 LocalPosition
	{
		get => _impl.LocalPosition;
		set => _impl.LocalPosition = value;
	}

	public Rotation LocalRotation
	{
		get => _impl.LocalRotation;
		set => _impl.LocalRotation = value;
	}

	public float LocalScale
	{
		get => _impl.LocalScale;
		set => _impl.LocalScale = value;
	}

	public EntityTags Tags => _impl.Tags;

	internal SdfWorld( ISdfWorldImpl impl )
	{
		_impl = impl;
	}

	protected SdfWorld( SceneWorld sceneWorld )
	{
		_impl = new SdfWorldSceneObject( sceneWorld );
	}

	protected SdfWorld()
	{
		_impl = new SdfWorldEntity( this );
	}

	internal bool IsDestroying { get; private set; }

	public void Dispose()
	{
		IsDestroying = true;

		_ = ClearAsync();
	}

	private class Layer
	{
		public int LastChangeCount { get; set; }
		public Dictionary<TChunkKey, TChunk> Chunks { get; } = new();
		public HashSet<TChunk> NeedsMeshUpdate { get; } = new();
		public Task UpdateMeshTask { get; set; } = System.Threading.Tasks.Task.CompletedTask;
	}

	private Dictionary<TResource, Layer> Layers { get; } = new();
	private List<TChunk> AllChunks { get; } = new();

	private Task _lastModificationTask = System.Threading.Tasks.Task.CompletedTask;

	private Transform _prevTransform;

	public void UpdateChunkTransforms()
	{
		for ( var i = AllChunks.Count - 1; i >= 0; --i )
		{
			AllChunks[i].UpdateTransform();
		}
	}

	public void Update()
	{
		var transform = Transform;

		if ( !transform.Equals( _prevTransform ) )
		{
			_prevTransform = transform;
			UpdateChunkTransforms();
		}

		ProcessUpdatedChunkQueue();

		foreach ( var (resource, layer) in Layers )
		{
			if ( resource.ChangeCount != layer.LastChangeCount )
			{
				layer.LastChangeCount = resource.ChangeCount;

				foreach ( var chunk in layer.Chunks.Values.ToArray() )
				{
					layer.NeedsMeshUpdate.Add( chunk );
				}
			}

			if ( layer.NeedsMeshUpdate.Count > 0 && layer.UpdateMeshTask.IsCompleted )
			{
				DispatchUpdateMesh( layer );
			}
		}
	}

	public void Write( Stream stream )
	{
		throw new NotImplementedException();
	}

	public void Read( Stream stream )
	{
		throw new NotImplementedException();
	}

	private const int MaxModificationsPerMessage = 256;

	// ReSharper disable StaticMemberInGenericType
#pragma warning disable SB3000
	private static Dictionary<TypeDescription, int> NetWrite_TypeIndices { get; } = new ();
	private static Dictionary<int, SdfReader<TSdf>> NetRead_TypeReaders { get; } = new();
	// ReSharper enable StaticMemberInGenericType
#pragma warning restore SB3000

	public int Write( NetWrite msg, int prevModifications )
	{
		var count = Math.Min( MaxModificationsPerMessage, ModificationCount - prevModifications );

		msg.Write( ClearCount );
		msg.Write( prevModifications );
		msg.Write( count );
		msg.Write( ModificationCount );

		WriteRange( msg, prevModifications, count, NetWrite_TypeIndices );

		return count;
	}

	private void WriteRange( NetWrite msg, int from, int count, Dictionary<TypeDescription, int> sdfTypes )
	{
		for ( var i = 0; i < count; ++i )
		{
			var modification = Modifications[from + i];

			msg.Write( modification.Operator );
			msg.Write( modification.Resource );
			modification.Sdf.Write( msg, sdfTypes );
		}
	}

	public bool Read( ref NetRead msg )
	{
		var clearCount = msg.Read<int>();
		var prevCount = msg.Read<int>();
		var msgCount = msg.Read<int>();
		var totalCount = msg.Read<int>();

		using var clientMods = AllowClientModifications();

		if ( clearCount < ClearCount )
		{
			// Outdated
			return true;
		}

		if ( clearCount > ClearCount )
		{
			ClearCount = clearCount;
			_ = ClearAsync();

			Assert.AreEqual( 0, Modifications.Count );
		}

		if ( prevCount != Modifications.Count )
		{
			return false;
		}

		ISdf<TSdf>.EnsureTypesRegistered();

		NetRead_TypeReaders.Clear();

		var index = 0;
		foreach ( var (_, reader) in ISdf<TSdf>.RegisteredTypes )
		{
			NetRead_TypeReaders[index++] = reader;
		}

		ReadRange( ref msg, msgCount, NetRead_TypeReaders );

		return true;
	}

	private void ReadRange( ref NetRead msg, int count, IReadOnlyDictionary<int, SdfReader<TSdf>> sdfTypes )
	{
		for ( var i = 0; i < count; ++i )
		{
			var op = msg.Read<Operator>();
			var res = msg.ReadClass<TResource>();
			var sdf = ISdf<TSdf>.Read( ref msg, sdfTypes );

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

	private IDisposable AllowClientModifications()
	{
		_receivingModifications = true;

		return new DisposeAction( () => _receivingModifications = false );
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

		await GameTask.MainThread();

		Modifications.Clear();

		if ( Game.IsServer )
		{
			++ClearCount;
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

		await GameTask.MainThread();

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
		Assert.True( _impl is not Entity { IsClientOnly: false } || Game.IsServer || _receivingModifications, 
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

	public bool HasPhysics => _impl.HasPhysics;

	internal PhysicsShape AddMeshShape( List<Vector3> vertices, List<int> indices )
	{
		return _impl.AddMeshShape( vertices, indices );
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

		await GameTask.WhenAll( tasks );
	}
}
