using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Sdf;

/// <summary>
/// Represents chunks in a <see cref="Sdf2DWorld"/>.
/// Each chunk contains an SDF for a sub-region of one specific layer.
/// </summary>
public partial class Sdf2DChunk : SdfChunk<Sdf2DWorld, Sdf2DChunk, Sdf2DLayer, (int X, int Y), Sdf2DArray, ISdf2D>
{
	public override Vector3 LocalPosition
	{
		get
		{
			var quality = Resource.Quality;
			return new Vector3( Key.X * quality.ChunkSize, Key.Y * quality.ChunkSize );
		}
	}

	private ExpandedSdf2D<TranslatedSdf2D<T>> ToLocal<T>( in T sdf, bool add )
		where T : ISdf2D
	{
		var edgeRadius = Resource.EdgeStyle == EdgeStyle.Sharp ? 0f : Resource.EdgeRadius;

		if ( add )
		{
			edgeRadius *= -1f;
		}

		return sdf.Translate( new Vector2( Key.X, Key.Y ) * -Resource.Quality.ChunkSize ).Expand( edgeRadius );
	}

	/// <inheritdoc />
	protected override Task<bool> OnAddAsync<T>( T sdf )
	{
		return Data.AddAsync( ToLocal( sdf, true ) );
	}

	/// <inheritdoc />
	protected override Task<bool> OnSubtractAsync<T>( T sdf )
	{
		return Data.SubtractAsync( ToLocal( sdf, false ) );
	}

	/// <inheritdoc />
	protected override async Task OnUpdateMeshAsync()
	{
		var enableRenderMesh = !Game.IsServer;
		var enableCollisionMesh = Resource.HasCollision;

		if ( !enableRenderMesh && !enableCollisionMesh )
		{
			return;
		}

		var writer = Sdf2DMeshWriter.Rent();

		try
		{
			await GameTask.WorkerThread();

			Data.WriteTo( writer, Resource, enableRenderMesh, enableCollisionMesh );

			var renderTask = Task.CompletedTask;
			var collisionTask = Task.CompletedTask;

			if ( enableRenderMesh )
			{
				renderTask = UpdateRenderMeshesAsync(
					new MeshDescription( writer.FrontWriter, Resource.FrontFaceMaterial ),
					new MeshDescription( writer.BackWriter, Resource.BackFaceMaterial ),
					new MeshDescription( writer.CutWriter, Resource.CutFaceMaterial ) );
			}

			if ( enableCollisionMesh )
			{
				var offset = new Vector3( Key.X, Key.Y ) * Resource.Quality.ChunkSize;

				collisionTask = GameTask.RunInThreadAsync( async () =>
				{
					var vertices = writer.CollisionMesh.Vertices;

					for ( var i = 0; i < vertices.Count; ++i )
					{
						vertices[i] += offset;
					}

					await UpdateCollisionMeshAsync( writer.CollisionMesh.Vertices, writer.CollisionMesh.Indices );
				} );
			}

			await GameTask.WhenAll( renderTask, collisionTask );
		}
		finally
		{
			writer.Return();
		}
	}
}
