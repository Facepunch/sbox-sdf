using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Sdf;

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
{
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

	/// <summary>
	/// Removes all layers / volumes, making this equivalent to a brand new empty world.
	/// </summary>
	public void Clear()
	{
		AssertCanModify();

		foreach ( var layer in Layers.Values )
			foreach ( var chunk in layer.Chunks.Values )
				chunk.Delete();

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
				chunk.Delete();
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

		layerData.Chunks[key] = chunk = new TChunk
		{
			Parent = this,
			LocalRotation = Rotation.Identity,
			LocalScale = 1f
		};

		chunk.Init( (TWorld) this, resource, key );

		return chunk;
	}

	private void AssertCanModify()
	{
		Assert.True( IsClientOnly || Game.IsServer, "Can only modify server-created SDF Worlds on the server." );
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
