using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Sdf;

internal static class Static
{
	private static Texture _sWhite3D;

	public static Texture White3D => _sWhite3D ??= new Texture3DBuilder()
		.WithName( "White 3D" )
		.WithSize( 1, 1, 1 )
		.WithFormat( ImageFormat.I8 )
		.WithData( new byte[] { 255 } )
		.Finish();

	private const int MaxPooledMeshes = 256;

	private static List<Mesh> _sMeshPool { get; } = new();

	public static Mesh RentMesh( Material mat )
	{
		if ( _sMeshPool.Count == 0 )
		{
			return new Mesh( mat );
		}

		var last = _sMeshPool[^1];
		_sMeshPool.RemoveAt( _sMeshPool.Count - 1 );

		last.Material = mat;

		return last;
	}

	public static void ReturnMesh( Mesh mesh )
	{
		if ( _sMeshPool.Count >= MaxPooledMeshes )
		{
			return;
		}

		_sMeshPool.Add( mesh );
	}
}

/// <summary>
/// Base class for chunks in a <see cref="SdfWorld{TWorld,TChunk,TResource,TChunkKey,TArray,TSdf}"/>.
/// Each chunk contains an SDF for a sub-region of one specific volume / layer resource.
/// </summary>
/// <typeparam name="TWorld">Non-abstract world type</typeparam>
/// <typeparam name="TChunk">Non-abstract chunk type</typeparam>
/// <typeparam name="TResource">Volume / layer resource</typeparam>
/// <typeparam name="TChunkKey">Integer coordinates used to index a chunk</typeparam>
/// <typeparam name="TArray">Type of <see cref="SdfArray{TSdf}"/> used to contain samples</typeparam>
/// <typeparam name="TSdf">Interface for SDF shapes used to make modifications</typeparam>
public abstract partial class SdfChunk<TWorld, TChunk, TResource, TChunkKey, TArray, TSdf> : IDisposable
	where TWorld : SdfWorld<TWorld, TChunk, TResource, TChunkKey, TArray, TSdf>
	where TChunk : SdfChunk<TWorld, TChunk, TResource, TChunkKey, TArray, TSdf>, new()
	where TResource : SdfResource<TResource>
	where TChunkKey : struct
	where TArray : SdfArray<TSdf>, new()
	where TSdf : ISdf<TSdf>
{
	/// <summary>
	/// Array storing SDF samples for this chunk.
	/// </summary>
	protected TArray Data { get; private set; }

	/// <summary>
	/// World that owns this chunk.
	/// </summary>
	public TWorld World { get; private set; }

	/// <summary>
	/// Volume or layer resource controlling the rendering and collision of this chunk.
	/// </summary>
	public TResource Resource { get; private set; }

	/// <summary>
	/// Position index of this chunk in the world.
	/// </summary>
	public TChunkKey Key { get; private set; }

	/// <summary>
	/// If this chunk has collision, the generated physics mesh for this chunk.
	/// </summary>
	public PhysicsShape Shape { get; set; }

	/// <summary>
	/// If this chunk is rendered, the scene object containing the generated mesh.
	/// </summary>
	public SceneObject SceneObject { get; private set; }

	public abstract Vector3 LocalPosition { get; }

	private int _lastModificationCount;
	private readonly List<Mesh> _usedMeshes = new();

	internal void Init( TWorld world, TResource resource, TChunkKey key )
	{
		World = world;
		Resource = resource;
		Key = key;

		Data = new TArray();
		Data.Init( resource.Quality );

		OnInit();
	}

	/// <summary>
	/// Called after the chunk is added to the <see cref="World"/>.
	/// </summary>
	protected virtual void OnInit()
	{

	}

	/// <inheritdoc />
	public void Dispose()
	{
		if ( Game.IsClient && !World.IsDestroying ) World.RemoveClientChunk( (TChunk)this );

		if ( World.IsValid() && !World.IsDestroying && Shape.IsValid() ) Shape.Remove();

		if ( SceneObject.IsValid() ) SceneObject.Delete();

		Shape = null;
		SceneObject = null;
	}

	private Task<bool> ModifyAsync( Func<bool> func )
	{
		return GameTask.RunInThreadAsync( func );
	}

	/// <summary>
	/// Sets every sample in this chunk's SDF to solid or empty.
	/// </summary>
	/// <param name="solid">Solidity to set each sample to.</param>
	public Task ClearAsync( bool solid )
	{
		return ModifyAsync( () =>
		{
			Data.Clear( solid );
			return true;
		} );
	}

	/// <summary>
	/// Add a world-space shape to this chunk.
	/// </summary>
	/// <typeparam name="T">SDF type</typeparam>
	/// <param name="sdf">Shape to add</param>
	/// <returns>True if any geometry was modified</returns>
	public Task<bool> AddAsync<T>( T sdf )
		where T : TSdf
	{
		return ModifyAsync( () => OnAdd( sdf ) );
	}

	/// <summary>
	/// Implements adding a world-space shape to this chunk.
	/// </summary>
	/// <typeparam name="T">SDF type</typeparam>
	/// <param name="sdf">Shape to add</param>
	/// <returns>True if any geometry was modified</returns>
	protected abstract bool OnAdd<T>( in T sdf )
		where T : TSdf;

	/// <summary>
	/// Subtract a world-space shape from this chunk.
	/// </summary>
	/// <typeparam name="T">SDF type</typeparam>
	/// <param name="sdf">Shape to subtract</param>
	/// <returns>True if any geometry was modified</returns>
	public Task<bool> SubtractAsync<T>( T sdf )
		where T : TSdf
	{
		return ModifyAsync( () => OnSubtract( sdf ) );
	}

	/// <summary>
	/// Implements subtracting a world-space shape from this chunk.
	/// </summary>
	/// <typeparam name="T">SDF type</typeparam>
	/// <param name="sdf">Shape to subtract</param>
	/// <returns>True if any geometry was modified</returns>
	protected abstract bool OnSubtract<T>( in T sdf )
		where T : TSdf;

	internal async Task UpdateMesh()
	{
		await OnUpdateMeshAsync();

		if ( SceneObject == null || Resource.ReferencedTextures is not { Count: > 0 } ) return;

		await GameTask.MainThread();

		foreach ( var reference in Resource.ReferencedTextures )
		{
			var matching = World.GetChunk( reference.Source, Key );
			UpdateLayerTexture( reference.TargetAttribute, reference.Source, matching );
		}
	}

	internal void UpdateLayerTexture( TResource resource, TChunk source )
	{
		if ( SceneObject == null || Resource.ReferencedTextures is not { Count: > 0 } ) return;

		foreach ( var reference in Resource.ReferencedTextures )
		{
			if ( reference.Source != resource ) continue;
			UpdateLayerTexture( reference.TargetAttribute, reference.Source, source );
		}
	}

	internal void UpdateLayerTexture( string targetAttribute, TResource resource, TChunk source )
	{
		ThreadSafe.AssertIsMainThread();

		if ( source != null )
		{
			if ( resource != source.Resource )
			{
				Log.Warning( $"Source chunk is using the wrong layer or volume resource" );
				return;
			}

			// ReSharper disable once CompareOfFloatsByEqualityOperator
			if ( resource.Quality.ChunkSize != Resource.Quality.ChunkSize )
			{
				Log.Warning( $"Layer {Resource.ResourceName} references {resource.ResourceName} " +
					$"as a texture source, but their chunk sizes don't match" );
				return;
			}

			SceneObject.Attributes.Set( targetAttribute, source.Data.Texture );
		}
		else
		{
			SceneObject.Attributes.Set( targetAttribute, Data.Dimensions == 3 ? Static.White3D : Texture.White );
		}

		var quality = resource.Quality;
		var arraySize = quality.ChunkResolution + SdfArray<TSdf>.Margin * 2 + 1;

		var margin = (SdfArray<TSdf>.Margin + 0.5f) / arraySize;
		var scale = 1f / quality.ChunkSize;
		var size = 1f - (SdfArray<TSdf>.Margin * 2 + 1f) / arraySize;

		var texParams = new Vector4( margin, margin, scale * size, quality.MaxDistance * 2f );

		SceneObject.Attributes.Set( $"{targetAttribute}_Params", texParams );
	}

	/// <summary>
	/// Implements updating the render / collision meshes of this chunk.
	/// </summary>
	/// <param name="token">Token to cancel outdated mesh updates</param>
	/// <returns>Task that completes when the meshes have finished updating.</returns>
	protected abstract Task OnUpdateMeshAsync();

	/// <summary>
	/// Asynchronously updates the collision shape to the defined mesh.
	/// </summary>
	/// <param name="vertices">Collision mesh vertices</param>
	/// <param name="indices">Collision mesh indices</param>
	protected async Task UpdateCollisionMeshAsync( List<Vector3> vertices, List<int> indices )
	{
		await GameTask.MainThread();
		UpdateCollisionMesh( vertices, indices );
	}

	protected async Task UpdateRenderMeshesAsync( params MeshDescription[] meshes )
	{
		await GameTask.MainThread();
		UpdateRenderMeshes( meshes );
	}

	/// <summary>
	/// Updates the collision shape to the defined mesh. Must be called on the main thread.
	/// </summary>
	/// <param name="vertices">Collision mesh vertices</param>
	/// <param name="indices">Collision mesh indices</param>
	protected void UpdateCollisionMesh( List<Vector3> vertices, List<int> indices )
	{
		ThreadSafe.AssertIsMainThread();

		if ( indices.Count == 0 )
		{
			Shape?.Remove();
			Shape = null;
		}
		else
		{
			var tags = Resource.SplitCollisionTags;

			if ( !Shape.IsValid() )
			{
				Shape = World.AddMeshShape( vertices, indices );

				foreach ( var tag in tags ) Shape.AddTag( tag );
			}
			else
			{
				Shape.UpdateMesh( vertices, indices );
			}
		}
	}

	/// <summary>
	/// Updates this chunk's model to use the given set of meshes. Must be called on the main thread.
	/// </summary>
	/// <param name="meshes">Set of meshes this model should use</param>
	private void UpdateRenderMeshes( params MeshDescription[] meshes )
	{
		meshes = meshes.Where( x => x.Material != null && !x.Writer.IsEmpty ).ToArray();

		ThreadSafe.AssertIsMainThread();

		var meshCountChanged = meshes.Length != _usedMeshes.Count;

		if ( meshCountChanged )
		{
			foreach ( var mesh in _usedMeshes )
			{
				Static.ReturnMesh( mesh );
			}

			_usedMeshes.Clear();

			foreach ( var mesh in meshes )
			{
				_usedMeshes.Add( Static.RentMesh( mesh.Material ) );
			}
		}
		else
		{
			for ( var i = 0; i < meshes.Length; ++i )
			{
				_usedMeshes[i].Material = meshes[i].Material;
			}
		}

		for ( var i = 0; i < meshes.Length; ++i )
		{
			meshes[i].Writer.ApplyTo( _usedMeshes[i] );
		}

		if ( !meshCountChanged )
		{
			return;
		}

		if ( _usedMeshes.Count == 0 )
		{
			SceneObject?.Delete();
			SceneObject = null;
			return;
		}

		var model = new ModelBuilder()
			.AddMeshes( _usedMeshes.ToArray() )
			.Create();

		if ( SceneObject == null )
		{
			SceneObject = new SceneObject( World.Scene, model )
			{
				Transform = new Transform( LocalPosition ),
				Batchable = Resource.ReferencedTextures is not { Count: > 0 }
			};
		}
		else
		{
			SceneObject.Model = model;
		}
	}
}
