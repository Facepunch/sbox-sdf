using System;
using System.Collections.Generic;

namespace Sandbox.Sdf;

internal partial class Sdf2DChunk : Entity
{
	[Net] public Sdf2DWorld World { get; set; }

	[Net] public int ChunkX { get; set; }

	[Net] public int ChunkY { get; set; }

	public Mesh Front { get; set; }
	public Mesh Back { get; set; }
	public Mesh Cut { get; set; }

	public PhysicsShape Shape { get; set; }

	[Net] public Sdf2DLayer Layer { get; set; }

	public bool FrontBackUsed { get; set; }
	public bool CutUsed { get; set; }

	[Net] private Sdf2DArray Data { get; set; }

	public bool OwnedByServer { get; }
	public SceneObject SceneObject { get; private set; }

	private int _lastModificationCount;

	public Sdf2DChunk()
	{
		OwnedByServer = true;
	}

	public Sdf2DChunk( Sdf2DWorld world, Sdf2DLayer layer, int chunkX, int chunkY )
	{
		OwnedByServer = Game.IsServer;

		World = world;
		Data = new Sdf2DArray( layer.Quality );
		Layer = layer;

		ChunkX = chunkX;
		ChunkY = chunkY;

		Name = $"Chunk {layer.ResourceName} {chunkX} {chunkY}";
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

		if ( Layer == null )
		{
			Log.Warning( "Layer is null!" );
			return;
		}

		World.AddClientChunk( this );
	}

	protected override void OnDestroy()
	{
		base.OnDestroy();

		if ( Game.IsClient && !World.IsDestroying ) World.RemoveClientChunk( this );

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
		where T : ISdf2D
	{
		if ( !Data.Add( in sdf ) ) return false;

		if ( Game.IsServer ) Data.WriteNetworkData();

		return true;
	}

	public bool Subtract<T>( in T sdf )
		where T : ISdf2D
	{
		if ( !Data.Subtract( in sdf ) ) return false;

		if ( Game.IsServer ) Data.WriteNetworkData();

		return true;
	}

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

	[ThreadStatic] private static List<Vector3> TransformedVertices;

	public void UpdateLayerTexture( Sdf2DLayer layer, Sdf2DChunk sourceChunk )
	{
		if ( Layer.LayerTextures == null || SceneObject == null ) return;

		foreach ( var reference in Layer.LayerTextures )
		{
			if ( reference.SourceLayer != layer ) continue;

			UpdateLayerTexture( reference.TargetAttribute, reference.SourceLayer, sourceChunk );
		}
	}

	public void UpdateLayerTexture( string targetAttribute, Sdf2DLayer layer, Sdf2DChunk sourceChunk )
	{
		if ( sourceChunk != null )
		{
			// ReSharper disable once CompareOfFloatsByEqualityOperator
			if ( sourceChunk.Layer.Quality.ChunkSize != Layer.Quality.ChunkSize )
			{
				Log.Warning( $"Layer {Layer.ResourceName} references {layer.ResourceName} " +
							 $"as a texture source, but their chunk sizes don't match" );
				return;
			}

			SceneObject.Attributes.Set( targetAttribute, sourceChunk.Data.Texture );
		}
		else
		{
			SceneObject.Attributes.Set( targetAttribute, Texture.White );
		}

		SceneObject.Attributes.Set( $"{targetAttribute}_Params", layer.Quality.TextureParams );
	}

	public void UpdateMesh()
	{
		if ( Data == null || World == null ) return;

		if ( Data.ModificationCount == _lastModificationCount ) return;

		_lastModificationCount = Data.ModificationCount;

		World.ChunkMeshUpdated( this, false );

		if ( Layer.IsTextureSourceOnly ) return;

		var writer = Sdf2DMeshWriter.Rent();
		var subMeshesChanged = false;
		var anyMeshes = false;

		var tags = Layer.SplitCollisionTags;

		var enableRenderMesh = !Game.IsServer;
		var enableCollisionMesh = tags.Length > 0;

		try
		{
			Data.WriteTo( writer, Layer, enableRenderMesh, enableCollisionMesh );

			if ( enableRenderMesh )
			{
				subMeshesChanged |= Front == null;

				Front ??= Layer.FrontFaceMaterial != null ? new Mesh( Layer.FrontFaceMaterial ) : null;
				Back ??= Layer.FrontFaceMaterial != null ? new Mesh( Layer.BackFaceMaterial ) : null;
				Cut ??= Layer.FrontFaceMaterial != null ? new Mesh( Layer.CutFaceMaterial ) : null;

				var (wasFrontBackUsed, wasCutUsed) = (FrontBackUsed, CutUsed);

				(FrontBackUsed, CutUsed) =
					writer.ApplyTo( Front, Back, Cut );

				subMeshesChanged |= wasFrontBackUsed != FrontBackUsed;
				subMeshesChanged |= wasCutUsed != CutUsed;

				anyMeshes |= FrontBackUsed || CutUsed;
			}

			if ( enableCollisionMesh )
			{
				if ( writer.CollisionMesh.Indices.Count == 0 )
				{
					Shape?.Remove();
					Shape = null;
				}
				else
				{
					TransformedVertices ??= new List<Vector3>();
					TransformedVertices.Clear();

					var offset = new Vector3( ChunkX, ChunkY ) * Data.Quality.ChunkSize;

					foreach ( var vertex in writer.CollisionMesh.Vertices ) TransformedVertices.Add( vertex + offset );

					if ( !Shape.IsValid() )
					{
						Shape = World.AddMeshShape(
							TransformedVertices,
							writer.CollisionMesh.Indices );

						foreach ( var tag in tags ) Shape.AddTag( tag );
					}
					else
					{
						Shape.UpdateMesh(
							TransformedVertices,
							writer.CollisionMesh.Indices );
					}
				}
			}

			if ( !enableRenderMesh ) return;

			if ( SceneObject == null != anyMeshes || subMeshesChanged )
			{
				if ( !anyMeshes )
				{
					SceneObject?.Delete();
					SceneObject = null;
				}
				else
				{
					var builder = new ModelBuilder();

					if ( FrontBackUsed )
					{
						if ( Front != null ) builder.AddMesh( Front );

						if ( Back != null ) builder.AddMesh( Back );
					}

					if ( CutUsed && Cut != null ) builder.AddMesh( Cut );

					if ( SceneObject == null )
						SceneObject = new SceneObject( Scene, builder.Create() )
						{
							Transform = Transform,
							Batchable = Layer.LayerTextures is not { Count: > 0 }
						};
					else
						SceneObject.Model = builder.Create();
				}
			}

			if ( anyMeshes && SceneObject != null && Layer.LayerTextures != null )
				foreach ( var reference in Layer.LayerTextures )
				{
					var matching = World.GetChunk( reference.SourceLayer, ChunkX, ChunkY );
					UpdateLayerTexture( reference.TargetAttribute, reference.SourceLayer, matching );
				}
		}
		finally
		{
			writer.Return();
		}
	}
}
