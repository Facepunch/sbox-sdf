using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Sdf;

internal static partial class Temp
{
	[ThreadStatic] public static List<Vector3> TransformedVertices;
}

public abstract partial class SdfChunk<TWorld, TChunk, TResource, TChunkKey, TArray, TSdf> : Entity
	where TWorld : SdfWorld<TWorld, TChunk, TResource, TChunkKey, TArray, TSdf>
	where TChunk : SdfChunk<TWorld, TChunk, TResource, TChunkKey, TArray, TSdf>, new()
	where TResource : SdfResource<TResource>
	where TChunkKey : struct
	where TArray : SdfArray<TSdf>, new()
{
	public abstract TWorld World { get; set; }
	public abstract TResource Resource { get; set; }
	protected abstract TArray Data { get; set; }
	public abstract TChunkKey Key { get; set; }

	public PhysicsShape Shape { get; set; }

	public SceneObject SceneObject { get; private set; }

	private int _lastModificationCount;
	private readonly List<Mesh> _usedMeshes = new List<Mesh>();

	internal void Init( TWorld world, TResource resource, TChunkKey key )
	{
		World = world;
		Resource = resource;
		Key = key;

		Data = new TArray();
		Data.Init( resource.Quality );

		Name = $"Chunk {Resource.ResourceName} {Key}";

		OnInit();
	}

	protected virtual void OnInit()
	{

	}

	public override void Spawn()
	{
		base.Spawn();

		Transmit = TransmitType.Always;
	}

	public override void ClientSpawn()
	{
		base.ClientSpawn();

		if ( World == null )
		{
			Log.Warning( "World is null!" );
			return;
		}

		if ( Resource == null )
		{
			Log.Warning( "Resource is null!" );
			return;
		}

		World.AddClientChunk( (TChunk) this );
	}

	protected override void OnDestroy()
	{
		base.OnDestroy();

		if ( Game.IsClient && !World.IsDestroying ) World.RemoveClientChunk( (TChunk) this );

		if ( World.IsValid() && !World.IsDestroying && Shape.IsValid() ) Shape.Remove();

		if ( SceneObject.IsValid() ) SceneObject.Delete();

		Shape = null;
		SceneObject = null;
	}

	public void Clear( bool solid )
	{
		Data.Clear( solid );

		if ( Game.IsServer ) Data.WriteNetworkData();
	}

	public bool Add<T>( in T sdf )
		where T : TSdf
	{
		if ( !OnAdd( in sdf ) ) return false;

		if ( Game.IsServer ) Data.WriteNetworkData();

		return true;
	}

	protected abstract bool OnAdd<T>( in T sdf )
		where T : TSdf;

	public bool Subtract<T>( in T sdf )
		where T : TSdf
	{
		if ( !OnSubtract( in sdf ) ) return false;

		if ( Game.IsServer ) Data.WriteNetworkData();

		return true;
	}

	protected abstract bool OnSubtract<T>( in T sdf )
		where T : TSdf;

	[GameEvent.Tick]
	public void Tick()
	{
		UpdateMesh();
	}

	[GameEvent.PreRender]
	public void ClientPreRender()
	{
		UpdateMesh();

		if ( SceneObject != null ) SceneObject.Transform = Transform;
	}

	private void UpdateMesh()
	{
		if ( Data == null || World == null ) return;

		if ( Data.ModificationCount == _lastModificationCount ) return;

		_lastModificationCount = Data.ModificationCount;

		World.ChunkMeshUpdated( (TChunk) this, false );

		if ( Resource.IsTextureSourceOnly ) return;

		OnUpdateMesh();

		if ( SceneObject == null || Resource.ReferencedTextures is not { Count: > 0 } ) return;

		foreach ( var reference in Resource.ReferencedTextures )
		{
			var matching = World.GetChunk( reference.Source, Key );
			UpdateLayerTexture( reference.TargetAttribute, reference.Source, matching );
		}
	}

	public void UpdateLayerTexture( TResource resource, TChunk source )
	{
		if ( SceneObject == null || Resource.ReferencedTextures is not { Count: > 0 } ) return;

		foreach ( var reference in Resource.ReferencedTextures )
		{
			if ( reference.Source != resource ) continue;
			UpdateLayerTexture( reference.TargetAttribute, reference.Source, source );
		}
	}

	public void UpdateLayerTexture( string targetAttribute, TResource resource, TChunk source )
	{
		if ( source != null )
		{
			if ( resource != source.Resource )
			{
				Log.Warning( $"Source chunk is using the wrong layer or volume resource" );
				return;
			}

			// ReSharper disable once CompareOfFloatsByEqualityOperator
			if ( source.Resource.Quality.ChunkSize != Resource.Quality.ChunkSize )
			{
				Log.Warning( $"Layer {Resource.ResourceName} references {resource.ResourceName} " +
					$"as a texture source, but their chunk sizes don't match" );
				return;
			}

			SceneObject.Attributes.Set( targetAttribute, source.Data.Texture );
		}
		else
		{
			SceneObject.Attributes.Set( targetAttribute, Texture.White );
		}

		var quality = resource.Quality;
		var arraySize = quality.ChunkResolution + Sdf2DArray.Margin * 2 + 1;

		var margin = (Sdf2DArray.Margin + 0.5f) / arraySize;
		var scale = 1f / quality.ChunkSize;
		var size = 1f - (Sdf2DArray.Margin * 2 + 1f) / arraySize;

		var texParams = new Vector4( margin, margin, scale * size, quality.MaxDistance * 2f );

		SceneObject.Attributes.Set( $"{targetAttribute}_Params", texParams );
	}

	protected abstract void OnUpdateMesh();

	protected void UpdateCollisionMesh( List<Vector3> vertices, List<int> indices, Vector3 offset )
	{
		if ( indices.Count == 0 )
		{
			Shape?.Remove();
			Shape = null;
		}
		else
		{
			var transformed = Temp.TransformedVertices ??= new List<Vector3>();
			transformed.Clear();

			foreach ( var vertex in vertices )
			{
				transformed.Add( vertex + offset );
			}

			var tags = Resource.SplitCollisionTags;

			if ( !Shape.IsValid() )
			{
				Shape = World.AddMeshShape( transformed, indices );

				foreach ( var tag in tags ) Shape.AddTag( tag );
			}
			else
			{
				Shape.UpdateMesh( transformed, indices );
			}
		}
	}

	protected void UpdateRenderMeshes( params Mesh[] meshes )
	{
		var anyChanges = false;

		foreach ( var mesh in meshes )
		{
			if ( mesh == null || mesh.IndexCount == 0 || _usedMeshes.Contains( mesh ) )
			{
				continue;
			}

			anyChanges = true;
			break;
		}

		foreach ( var mesh in _usedMeshes )
		{
			if ( mesh.IndexCount > 0 && Array.IndexOf( meshes, mesh ) != -1 )
			{
				continue;
			}

			anyChanges = true;
			break;
		}

		if ( !anyChanges )
		{
			return;
		}

		_usedMeshes.Clear();
		_usedMeshes.AddRange( meshes.Where( x => x is { IndexCount: > 0 } ) );

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
			SceneObject = new SceneObject( Scene, model )
			{
				Transform = Transform,
				Batchable = Resource.ReferencedTextures is not { Count: > 0 }
			};
		else
			SceneObject.Model = model;
	}
}
