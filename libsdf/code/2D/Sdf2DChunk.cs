using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Sdf;

public partial class Sdf2DChunk : SdfChunk<Sdf2DWorld, Sdf2DChunk, Sdf2DLayer, (int X, int Y), Sdf2DArray, ISdf2D>
{
	[Net] private Sdf2DWorld NetWorld { get; set; }
	[Net] private Sdf2DLayer NetResource { get; set; }
	[Net] private Sdf2DArray NetData { get; set; }
	[Net] private int NetKeyX { get; set; }
	[Net] private int NetKeyY { get; set; }

	public override Sdf2DWorld World
	{
		get => NetWorld;
		set => NetWorld = value;
	}

	public override Sdf2DLayer Resource
	{
		get => NetResource;
		set => NetResource = value;
	}
	protected override Sdf2DArray Data
	{
		get => NetData;
		set => NetData = value;
	}

	public override (int X, int Y) Key
	{
		get => (NetKeyX, NetKeyY);
		set => (NetKeyX, NetKeyY) = value;
	}

	public Mesh Front { get; set; }
	public Mesh Back { get; set; }
	public Mesh Cut { get; set; }

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

	protected override bool OnAdd<T>( in T sdf )
	{
		return Data.Add( ToLocal( sdf ) );
	}

	protected override bool OnSubtract<T>( in T sdf )
	{
		return Data.Subtract( ToLocal( sdf ) );
	}

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

			if ( enableRenderMesh )
			{
				await RunInMainThread( () =>
				{
					Front ??= Resource.FrontFaceMaterial != null ? new Mesh( Resource.FrontFaceMaterial ) : null;
					Back ??= Resource.FrontFaceMaterial != null ? new Mesh( Resource.BackFaceMaterial ) : null;
					Cut ??= Resource.FrontFaceMaterial != null ? new Mesh( Resource.CutFaceMaterial ) : null;

					writer.ApplyTo( Front, Back, Cut );
					
					UpdateRenderMeshes( Front, Back, Cut );
				} );
			}

			token.ThrowIfCancellationRequested();

			if ( enableCollisionMesh )
			{
				var offset = new Vector3( Key.X, Key.Y ) * Resource.Quality.ChunkSize;

				await GameTask.RunInThreadAsync( () =>
				{
					var vertices = writer.CollisionMesh.Vertices;

					for ( var i = 0; i < vertices.Count; ++i )
					{
						vertices[i] += offset;
					}
				} );

				await RunInMainThread( () =>
				{
					UpdateCollisionMesh( writer.CollisionMesh.Vertices, writer.CollisionMesh.Indices );
				} );
			}
		}
		finally
		{
			writer.Return();
		}
	}
}
