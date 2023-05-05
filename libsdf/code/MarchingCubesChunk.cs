using System.Collections.Generic;
using Sandbox;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Sdf
{
	public partial class MarchingCubesChunk : ModelEntity
	{
		[Net] public ArrayVoxelData Data { get; private set; }
		[Net] public float Size { get; private set; }

		private Mesh _mesh;
		private PhysicsBody _body;
		private PhysicsShape _shape;

		private Task _updateMeshTask;

		private bool _meshInvalid;
		private int _lastNetReadCount;

		public MarchingCubesChunk()
		{

		}

		public MarchingCubesChunk( ArrayVoxelData data, float size )
		{
			Data = data;
			Size = size;

			CollisionBounds = new BBox( 0f, size );
		}

		public void InvalidateMesh()
		{
			_meshInvalid = true;
		}

		[Event.Tick.Client]
		public void ClientTick()
		{
			if ( _lastNetReadCount != Data.NetReadCount )
			{
				_lastNetReadCount = Data.NetReadCount;

				InvalidateMesh();
			}

			if ( _meshInvalid )
			{
				_meshInvalid = false;

				_updateMeshTask = UpdateMeshAsync( true, true );
			}
		}

		[Event.Tick.Server]
		public void ServerTick()
		{
			if ( _meshInvalid )
			{
				_meshInvalid = false;

				_updateMeshTask = UpdateMeshAsync( false, true );

				Data.WriteNetworkData();
			}
		}

		public bool Add<T>( T sdf, BBox bounds, Matrix transform, Color color )
			where T : ISignedDistanceField
		{
			return Data.Add( sdf, bounds, transform, color );
		}

		public bool Subtract<T>( T sdf, BBox bounds, Matrix transform )
			where T : ISignedDistanceField
		{
			return Data.Subtract( sdf, bounds, transform );
		}

		public async Task UpdateMeshAsync( bool render, bool collision )
		{
			var writer = MarchingCubesMeshWriter.Rent();

			writer.Scale = Size;

			try
			{
				if ( render )
				{
					await Task.RunInThreadAsync( () => Data.UpdateMesh( writer, 0, true, false ) );

					if ( writer.Vertices.Count == 0 )
					{
						EnableDrawing = false;
						EnableShadowCasting = false;
						
						SetModel( "" );
					}
					else
					{
						if ( _mesh == null )
						{
							var material = Material.Load( "materials/sdf_default.vmat" );

							_mesh = new Mesh( material )
							{
								Bounds = new BBox( Size * 0.5f, Size )
							};
						}

						if ( _mesh.HasVertexBuffer )
						{
							_mesh.SetVertexBufferSize( writer.Vertices.Count );
							_mesh.SetVertexBufferData( writer.Vertices );
						}
						else
						{
							_mesh.CreateVertexBuffer( writer.Vertices.Count, VoxelVertex.Layout, writer.Vertices );
						}

						_mesh.SetVertexRange( 0, writer.Vertices.Count );

						if ( Model == null )
						{
							var modelBuilder = new ModelBuilder();

							modelBuilder.AddMesh( _mesh );

							Model = modelBuilder.Create();
						}

						EnableDrawing = true;
						EnableShadowCasting = true;
					}
				}

				if ( collision )
				{
					Data.UpdateMesh( writer, 1, false, true );

					if ( writer.CollisionVertices.Count == 0 )
					{
						if (_body.IsValid())
						{
							_body.ClearShapes();
						}

						PhysicsClear();

						_body = null;
						_shape = null;
					}
					else
					{
						if ( !_body.IsValid() )
						{
							SetupPhysicsFromAABB( PhysicsMotionType.Static, 0f, Size );

							_body = PhysicsBody;
							_shape = null;

							if ( _body.IsValid() )
							{
								_body.ClearShapes();
							}
						}

						if ( _body.IsValid() )
						{
							if ( !_shape.IsValid() )
							{
								_shape = _body.AddMeshShape( writer.CollisionVertices, writer.CollisionIndices );
								_shape.AddTag( "solid" );
							}

							_shape.UpdateMesh(writer.CollisionVertices, writer.CollisionIndices);
						}
					}
				}
			}
			finally
			{
				writer.Return();
			}
		}

		protected override void OnDestroy()
		{

		}
	}
}
