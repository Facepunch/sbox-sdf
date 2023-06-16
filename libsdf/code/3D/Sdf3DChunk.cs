using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Sdf;

/// <summary>
/// Represents chunks in a <see cref="Sdf3DWorld"/>.
/// Each chunk contains an SDF for a sub-region of one specific volume.
/// </summary>
public partial class Sdf3DChunk : SdfChunk<Sdf3DWorld, Sdf3DChunk, Sdf3DVolume, (int X, int Y, int Z), Sdf3DArray, ISdf3D>
{
	protected override float MaxNetworkWriteRate => 4f;

	public override Vector3 LocalPosition
	{
		get
		{
			var quality = Resource.Quality;
			return new Vector3( Key.X * quality.ChunkSize, Key.Y * quality.ChunkSize, Key.Z * quality.ChunkSize );
		}
	}

	/// <summary>
	/// Render mesh used by this chunk.
	/// </summary>
	public Mesh Mesh { get; set; }

	private TranslatedSdf3D<T> ToLocal<T>( in T sdf )
		where T : ISdf3D
	{
		return sdf.Translate( new Vector3( Key.X, Key.Y, Key.Z ) * -Resource.Quality.ChunkSize );
	}

	/// <inheritdoc />
	protected override bool OnAdd<T>( in T sdf )
	{
		return Data.Add( ToLocal( sdf ) );
	}

	/// <inheritdoc />
	protected override bool OnSubtract<T>( in T sdf )
	{
		return Data.Subtract( ToLocal( sdf ) );
	}

	/// <inheritdoc />
	protected override async Task OnUpdateMeshAsync( CancellationToken token )
	{
		var tags = Resource.SplitCollisionTags;

		var enableRenderMesh = !Game.IsServer && Resource.Material != null;
		var enableCollisionMesh = tags.Length > 0;

		if ( !enableRenderMesh && !enableCollisionMesh )
		{
			return;
		}

		var writer = Sdf3DMeshWriter.Rent();

		try
		{
			await Data.WriteToAsync( writer, Resource, token );

			token.ThrowIfCancellationRequested();

			var renderTask = Task.CompletedTask;
			var collisionTask = Task.CompletedTask;

			if ( enableRenderMesh )
			{
				renderTask = RunInMainThread( MainThreadTask.UpdateRenderMeshes, () =>
				{
					Mesh = Resource.Material != null && writer.Indices.Count > 0 ? Mesh ?? new Mesh( Resource.Material ) : null;

					writer.ApplyTo( Mesh );

					UpdateRenderMeshes( Mesh );
				} );
			}

			token.ThrowIfCancellationRequested();

			if ( enableCollisionMesh )
			{
				var offset = new Vector3( Key.X, Key.Y, Key.Z ) * Resource.Quality.ChunkSize;

				collisionTask = GameTask.RunInThreadAsync( async () =>
				{
					var vertices = writer.VertexPositions;

					for ( var i = 0; i < vertices.Count; ++i )
					{
						vertices[i] += offset;
					}

					token.ThrowIfCancellationRequested();

					await UpdateCollisionMeshAsync( writer.VertexPositions, writer.Indices );
				} );
			}

			await World.Task.WhenAll( renderTask, collisionTask );
		}
		finally
		{
			writer.Return();
		}
	}
}
