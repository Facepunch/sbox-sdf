using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.Diagnostics;
using Sandbox.Sdf;

namespace Sandbox.MarchingSquares
{
    public partial class MarchingSquaresChunk : ModelEntity
    {
        private class SubMesh
        {
            public Mesh Front { get; }
            public Mesh Back { get; }
            public Mesh Cut { get; }

            public bool FrontBackUsed { get; set; }
            public bool CutUsed { get; set; }

            public SubMesh( MarchingSquaresMaterial material )
            {
                Front = new Mesh( material.FrontFaceMaterial );
                Back = new Mesh( material.BackFaceMaterial );
                Cut = new Mesh( material.CutFaceMaterial );
            }
        }

        private Dictionary<MarchingSquaresMaterial, SubMesh> SubMeshes { get; } = new ();

        [Net]
        private SdfArray2D Data { get; set; }

        public bool OwnedByServer { get; }

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

        [GameEvent.PreRender]
        public void ClientPreRender()
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
        }

        public void UpdateMesh()
        {
            var writer = MarchingSquaresMeshWriter.Rent();
            var subMeshesChanged = false;

            try
            {
                foreach ( var mat in Data.Materials )
                {
                    if ( !SubMeshes.TryGetValue( mat, out var subMesh ) )
                    {
                        subMesh = new SubMesh( mat );

                        SubMeshes.Add( mat, subMesh );

                        subMeshesChanged = true;
                    }

                    writer.Clear();

                    Data.WriteTo( writer, mat );

                    var (wasFrontBackUsed, wasCutUsed) = (subMesh.FrontBackUsed, subMesh.CutUsed);

                    (subMesh.FrontBackUsed, subMesh.CutUsed) =
                        writer.ApplyTo( subMesh.Front, subMesh.Back, subMesh.Cut );

                    subMeshesChanged |= wasFrontBackUsed != subMesh.FrontBackUsed;
                    subMeshesChanged |= wasCutUsed != subMesh.CutUsed;
                }

                if ( Model == null || subMeshesChanged )
                {
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

                    Model = builder.Create();
                }
            }
            finally
            {
                writer.Return();
            }
        }
    }
}
