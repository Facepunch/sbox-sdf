using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Sdf;

/// <summary>
/// Represents chunks in a <see cref="Sdf2DWorld"/>.
/// Each chunk contains an SDF for a sub-region of one specific layer.
/// </summary>
public partial class Sdf2DChunk : SdfChunk<Sdf2DWorld, Sdf2DChunk, Sdf2DLayer, (int X, int Y), Sdf2DArray, ISdf2D>
{
	[Net] private Sdf2DWorld NetWorld { get; set; }
	[Net] private Sdf2DLayer NetResource { get; set; }
	[Net] private Sdf2DArray NetData { get; set; }
	[Net] private int NetKeyX { get; set; }
	[Net] private int NetKeyY { get; set; }

	/// <inheritdoc />
	public override Sdf2DWorld World
	{
		get => NetWorld;
		set => NetWorld = value;
	}

	/// <inheritdoc />
	public override Sdf2DLayer Resource
	{
		get => NetResource;
		set => NetResource = value;
	}

	/// <inheritdoc />
	protected override Sdf2DArray Data
	{
		get => NetData;
		set => NetData = value;
	}

	/// <inheritdoc />
	public override (int X, int Y) Key
	{
		get => (NetKeyX, NetKeyY);
		set => (NetKeyX, NetKeyY) = value;
	}

	/// <summary>
	/// Render mesh for the front face of this chunk.
	/// </summary>
	public Mesh Front { get; set; }

	/// <summary>
	/// Render mesh for the back face of this chunk.
	/// </summary>
	public Mesh Back { get; set; }

	/// <summary>
	/// Render mesh for the cut faces of this chunk.
	/// </summary>
	public Mesh Cut { get; set; }

	/// <inheritdoc />
	protected override void OnInit()
	{
		base.OnInit();

		var quality = Resource.Quality;

		LocalPosition = new Vector3( Key.X * quality.ChunkSize, Key.Y * quality.ChunkSize );
	}

	private TranslatedSdf2D<T> ToLocal<T>( in T sdf )
		where T : ISdf2D
	{
		return sdf.Translate( new Vector2( Key.X, Key.Y ) * -Resource.Quality.ChunkSize );
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

		var enableRenderMesh = !Game.IsServer;
		var enableCollisionMesh = tags.Length > 0;

		if ( !enableRenderMesh && !enableCollisionMesh )
		{
			return;
		}

		var writer = Sdf2DMeshWriter.Rent();

		try
		{
			await GameTask.RunInThreadAsync( () => Data.WriteTo( writer, Resource, enableRenderMesh, enableCollisionMesh ) );

			token.ThrowIfCancellationRequested();

			var renderTask = Task.CompletedTask;
			var collisionTask = Task.CompletedTask;

			if ( enableRenderMesh )
			{
				renderTask = RunInMainThread( MainThreadTask.UpdateRenderMeshes, () =>
				{
					Front = Resource.FrontFaceMaterial != null && writer.HasFrontFaces ? Front ?? new Mesh( Resource.FrontFaceMaterial ) : null;
					Back = Resource.FrontFaceMaterial != null && writer.HasBackFaces ? Back ?? new Mesh( Resource.BackFaceMaterial ) : null;
					Cut = Resource.FrontFaceMaterial != null && writer.HasCutFaces ? Cut ?? new Mesh( Resource.CutFaceMaterial ) : null;

					writer.ApplyTo( Front, Back, Cut );
					
					UpdateRenderMeshes( Front, Back, Cut );
				} );
			}

			token.ThrowIfCancellationRequested();

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

			await Task.WhenAll( renderTask, collisionTask );
		}
		finally
		{
			writer.Return();
		}
	}
}
