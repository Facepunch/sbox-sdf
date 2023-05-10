using System.Collections.Generic;
using System.Linq;
using Sandbox.Diagnostics;

namespace Sandbox.MarchingSquares
{
    public partial class MarchingSquaresChunk : Entity
    {
        private class SubMesh
        {
            public Mesh Front { get; set; }
            public Mesh Back { get; set; }
            public Mesh Cut { get; set; }
            public PhysicsShape Shape { get; set; }

            public bool FrontBackUsed { get; set; }
            public bool CutUsed { get; set; }
        }

        private Dictionary<MarchingSquaresMaterial, SubMesh> SubMeshes { get; } = new ();

        [Net]
        private SdfArray2D Data { get; set; }

        public bool OwnedByServer { get; }
        public SceneObject SceneObject { get; private set; }
        public PhysicsBody PhysicsBody { get; private set; }

        private int _lastModificationCount;

        public MarchingSquaresChunk()
        {
            OwnedByServer = true;
        }

        public MarchingSquaresChunk( int resolution, float size, float? maxDistance = null )
        {
            OwnedByServer = Game.IsServer;
            Data = new SdfArray2D( resolution, size, maxDistance ?? (size * 4f / resolution) );
        }

        private void AssertCanModify()
        {
            Assert.True( OwnedByServer == Game.IsServer, "Can only modify server-created chunks on the server." );
        }

        public void Clear( MarchingSquaresMaterial material = null )
        {
            AssertCanModify();

            Data.Clear( material );

            if ( Game.IsServer )
            {
                Data.WriteNetworkData();
            }
        }

        public bool Add<T>( in T sdf, MarchingSquaresMaterial material )
            where T : ISdf2D
        {
            AssertCanModify();

            if ( !Data.Add( in sdf, material ) )
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
            AssertCanModify();

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

            if ( PhysicsBody.IsValid() )
            {
                PhysicsBody.Transform = Transform;
            }
        }

        [GameEvent.PreRender]
        public void ClientPreRender()
        {
            UpdateMesh();

            if ( SceneObject.IsValid() )
            {
                SceneObject.Transform = Transform;
            }
        }

        public void UpdateMesh()
        {
            if ( Data == null )
            {
                return;
            }

            if ( Data.ModificationCount == _lastModificationCount )
            {
                return;
            }

            _lastModificationCount = Data.ModificationCount;

            UpdateMesh();

            var collisionOnly = Game.IsServer;

            var writer = MarchingSquaresMeshWriter.Rent( collisionOnly );
            var subMeshesChanged = false;

            try
            {
                foreach ( var mat in Data.Materials )
                {
                    if ( !SubMeshes.TryGetValue( mat, out var subMesh ) )
                    {
                        subMesh = new SubMesh();

                        if ( !collisionOnly )
                        {
                            subMesh.Front = new Mesh( mat.FrontFaceMaterial );
                            subMesh.Back = new Mesh( mat.BackFaceMaterial );
                            subMesh.Cut = new Mesh( mat.CutFaceMaterial );
                        }

                        SubMeshes.Add( mat, subMesh );

                        subMeshesChanged = true;
                    }

                    writer.Clear();

                    Data.WriteTo( writer, mat );

                    if ( !collisionOnly )
                    {
                        var (wasFrontBackUsed, wasCutUsed) = (subMesh.FrontBackUsed, subMesh.CutUsed);

                        (subMesh.FrontBackUsed, subMesh.CutUsed) =
                            writer.ApplyTo( subMesh.Front, subMesh.Back, subMesh.Cut );

                        subMeshesChanged |= wasFrontBackUsed != subMesh.FrontBackUsed;
                        subMeshesChanged |= wasCutUsed != subMesh.CutUsed;
                    }

                    if ( writer.CollisionMesh.Indices.Count == 0 )
                    {
                        subMesh.Shape?.Remove();
                        subMesh.Shape = null;
                    }
                    else
                    {
                        if ( !subMesh.Shape.IsValid() )
                        {
                            if ( !PhysicsBody.IsValid() )
                            {
                                PhysicsBody = new PhysicsBody( Game.PhysicsWorld );
                            }

                            subMesh.Shape = PhysicsBody.AddMeshShape(
                                writer.CollisionMesh.Vertices,
                                writer.CollisionMesh.Indices );

                            subMesh.Shape.AddTag( "solid" );
                        }
                        else
                        {
                            subMesh.Shape.UpdateMesh(
                                writer.CollisionMesh.Vertices,
                                writer.CollisionMesh.Indices );
                        }
                    }
                }

                if ( collisionOnly || SceneObject != null && !subMeshesChanged )
                {
                    return;
                }

                var builder = new ModelBuilder();

                foreach ( var subMesh in SubMeshes.Values )
                {
                    if ( subMesh.FrontBackUsed )
                    {
                        builder.AddMesh( subMesh.Front );
                        builder.AddMesh( subMesh.Back );
                    }

                    if ( subMesh.CutUsed )
                    {
                        builder.AddMesh( subMesh.Cut );
                    }
                }

                if ( SceneObject == null )
                {
                    SceneObject ??= new SceneObject( Scene, builder.Create() );
                }
                else
                {
                    SceneObject.Model = builder.Create();
                }

                var maxDepth = Data.Materials.Max( x => x.Depth );

                SceneObject.Bounds = new BBox( new Vector3( 0f, 0f, -maxDepth * 0.5f ),
                    new Vector3( Data.Size, Data.Size, maxDepth * 0.5f ) );
            }
            finally
            {
                writer.Return();
            }
        }
    }
}
