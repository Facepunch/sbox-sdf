using System;
using System.Collections.Generic;
using Sandbox.Sdf;

namespace Sandbox.MarchingSquares
{
    internal partial class MarchingSquaresChunk : Entity
    {
        [Net]
        public Sdf2DWorld World { get; set; }

        [Net]
        public int ChunkX { get; set; }

        [Net]
        public int ChunkY { get; set; }

        public Mesh Front { get; set; }
        public Mesh Back { get; set; }
        public Mesh Cut { get; set; }
        public PhysicsShape Shape { get; set; }

        [Net]
        public Sdf2DMaterial Material { get; set; }

        public bool FrontBackUsed { get; set; }
        public bool CutUsed { get; set; }

        [Net]
        private SdfArray2D Data { get; set; }

        public bool OwnedByServer { get; }
        public SceneObject SceneObject { get; private set; }

        private int _lastModificationCount;

        public MarchingSquaresChunk()
        {
            OwnedByServer = true;
        }

        public MarchingSquaresChunk( Sdf2DWorld world, Sdf2DMaterial material, int chunkX, int chunkY )
        {
            OwnedByServer = Game.IsServer;

            World = world;
            Data = new SdfArray2D( material.Quality );
            Material = material;

            ChunkX = chunkX;
            ChunkY = chunkY;

            Name = $"Chunk {material.ResourceName} {chunkX} {chunkY}";
        }

        public override void Spawn()
        {
            base.Spawn();

            Transmit = TransmitType.Always;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if ( World.IsValid() && !World.IsDestroying && Shape.IsValid() )
            {
                Shape.Remove();
            }

            if ( SceneObject.IsValid() )
            {
                SceneObject.Delete();
            }

            Shape = null;
            SceneObject = null;
        }

        public void Clear( bool solid )
        {
            Data.Clear( solid );

            if ( Game.IsServer )
            {
                Data.WriteNetworkData();
            }
        }

        public bool Add<T>( in T sdf )
            where T : ISdf2D
        {
            if ( !Data.Add( in sdf ) )
            {
                return false;
            }

            if ( Game.IsServer )
            {
                Data.WriteNetworkData();
            }

            return true;
        }
        
        public bool Subtract<T>( in T sdf )
            where T : ISdf2D
        {
            if ( !Data.Subtract( in sdf ) )
            {
                return false;
            }

            if ( Game.IsServer )
            {
                Data.WriteNetworkData();
            }

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

            if ( SceneObject != null )
            {
                SceneObject.Transform = Transform;
            }
        }

        [ThreadStatic]
        private static List<Vector3> TransformedVertices;

        public void UpdateMesh()
        {
            if ( Data == null || World == null )
            {
                return;
            }

            if ( Data.ModificationCount == _lastModificationCount )
            {
                return;
            }

            _lastModificationCount = Data.ModificationCount;

            UpdateMesh();

            var writer = MarchingSquaresMeshWriter.Rent();
            var subMeshesChanged = false;
            var anyMeshes = false;

            var tags = Material.SplitCollisionTags;

            var enableRenderMesh = !Game.IsServer;
            var enableCollisionMesh = tags.Length > 0;

            try
            {
                Data.WriteTo( writer, Material, enableRenderMesh, enableCollisionMesh );

                if ( enableRenderMesh )
                {
                    subMeshesChanged |= Front == null;

                    Front ??= Material.FrontFaceMaterial != null ? new Mesh( Material.FrontFaceMaterial ) : null;
                    Back ??= Material.FrontFaceMaterial != null ? new Mesh( Material.BackFaceMaterial ) : null;
                    Cut ??= Material.FrontFaceMaterial != null ? new Mesh( Material.CutFaceMaterial ) : null;

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

                        foreach ( var vertex in writer.CollisionMesh.Vertices )
                        {
                            TransformedVertices.Add( vertex + offset );
                        }

                        if ( !Shape.IsValid() )
                        {
                            Shape = World.AddMeshShape(
                                TransformedVertices,
                                writer.CollisionMesh.Indices );

                            foreach ( var tag in tags )
                            {
                                Shape.AddTag( tag );
                            }
                        }
                        else
                        {
                            Shape.UpdateMesh(
                                TransformedVertices,
                                writer.CollisionMesh.Indices );
                        }
                    }
                }

                if ( !enableRenderMesh || SceneObject == null == anyMeshes && !subMeshesChanged )
                {
                    return;
                }

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
                        if ( Front != null )
                        {
                            builder.AddMesh( Front );
                        }

                        if ( Back != null )
                        {
                            builder.AddMesh( Back );
                        }
                    }

                    if ( CutUsed && Cut != null )
                    {
                        builder.AddMesh( Cut );
                    }

                    if ( SceneObject == null )
                    {
                        SceneObject = new SceneObject( Scene, builder.Create() );
                        SceneObject.Transform = Transform;
                    }
                    else
                    {
                        SceneObject.Model = builder.Create();
                    }
                }
            }
            finally
            {
                writer.Return();
            }
        }
    }
}
