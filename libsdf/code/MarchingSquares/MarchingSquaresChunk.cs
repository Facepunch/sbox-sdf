using System.Collections.Generic;
using System.Linq;
using Sandbox.Diagnostics;
using Sandbox.Sdf;

namespace Sandbox.MarchingSquares
{
    public partial class MarchingSquaresChunk : Entity
    {
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
        public PhysicsBody PhysicsBody { get; private set; }

        private int _lastModificationCount;

        public MarchingSquaresChunk()
        {
            OwnedByServer = true;
        }

        public MarchingSquaresChunk( int resolution, float size, float maxDistance, Sdf2DMaterial material )
        {
            OwnedByServer = Game.IsServer;

            Data = new SdfArray2D( resolution, size, maxDistance );
            Material = material;
        }

        public override void Spawn()
        {
            base.Spawn();

            Transmit = TransmitType.Always;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            PhysicsBody?.Remove();
            PhysicsBody = null;

            SceneObject?.Delete();
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

                    Front ??= new Mesh( Material.FrontFaceMaterial );
                    Back ??= new Mesh( Material.BackFaceMaterial );
                    Cut ??= new Mesh( Material.CutFaceMaterial );

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
                        if ( !Shape.IsValid() )
                        {
                            if ( !PhysicsBody.IsValid() )
                            {
                                PhysicsBody = new PhysicsBody( Game.PhysicsWorld );
                            }

                            Shape = PhysicsBody.AddMeshShape(
                                writer.CollisionMesh.Vertices,
                                writer.CollisionMesh.Indices );

                            foreach ( var tag in tags )
                            {
                                Shape.AddTag( tag );
                            }
                        }
                        else
                        {
                            Shape.UpdateMesh(
                                writer.CollisionMesh.Vertices,
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
                        builder.AddMesh( Front );
                        builder.AddMesh( Back );
                    }

                    if ( CutUsed )
                    {
                        builder.AddMesh( Cut );
                    }

                    if ( SceneObject == null )
                    {
                        SceneObject = new SceneObject( Scene, builder.Create() );
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
