using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.Sdf;

namespace Sandbox.MarchingSquares
{
    public partial class MarchingSquaresChunk : ModelEntity
    {
        private Mesh _mesh;

        private SdfArray2D Data { get; set; }

        public MarchingSquaresChunk()
        {

        }

        public MarchingSquaresChunk( int resolution, float size )
        {
            Data = new SdfArray2D( resolution, size, NormalStyle.Flat );
        }

        public void Clear()
        {
            Data.Clear();
        }

        public bool Add<T>( in T sdf, MarchingSquaresMaterial material )
            where T : ISdf2D
        {
            return Data.Add( in sdf, material );
        }

        public void UpdateMesh()
        {
            var writer = new MarchingSquaresMeshWriter();

            Data.WriteTo( writer, Data.Materials.First() );

            _mesh ??= new Mesh( Data.Materials.First().FrontFaceMaterial );

            writer.ApplyTo( _mesh );

            if ( Model == null )
            {
                var builder = new ModelBuilder();

                builder.AddMesh( _mesh );

                Model = builder.Create();
            }
        }
    }
}
