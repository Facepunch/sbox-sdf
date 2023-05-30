using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sandbox.Sdf;

public partial class Sdf3DChunk : SdfChunk<Sdf3DWorld, Sdf3DChunk, Sdf3DVolume, (int X, int Y, int Z), Sdf3DArray, ISdf3D>
{
	[Net] private Sdf3DWorld NetWorld { get; set; }
	[Net] private Sdf3DVolume NetResource { get; set; }
	[Net] private Sdf3DArray NetData { get; set; }
	[Net] private int NetKeyX { get; set; }
	[Net] private int NetKeyY { get; set; }
	[Net] private int NetKeyZ { get; set; }

	public override Sdf3DWorld World
	{
		get => NetWorld;
		set => NetWorld = value;
	}

	public override Sdf3DVolume Resource
	{
		get => NetResource;
		set => NetResource = value;
	}
	protected override Sdf3DArray Data
	{
		get => NetData;
		set => NetData = value;
	}

	public override (int X, int Y, int Z) Key
	{
		get => (NetKeyX, NetKeyY, NetKeyZ);
		set => (NetKeyX, NetKeyY, NetKeyZ) = value;
	}

	public Mesh Front { get; set; }
	public Mesh Back { get; set; }
	public Mesh Cut { get; set; }

	protected override void OnInit()
	{
		base.OnInit();

		var quality = Resource.Quality;

		LocalPosition = new Vector3( Key.X * quality.ChunkSize, Key.Y * quality.ChunkSize, Key.Z * quality.ChunkSize );
	}

	private TranslatedSdf3D<T> ToLocal<T>( in T sdf )
		where T : ISdf3D
	{
		return sdf.Translate( new Vector3( Key.X, Key.Y, Key.Z ) * -Resource.Quality.ChunkSize );
	}

	protected override bool OnAdd<T>( in T sdf )
	{
		return Data.Add( ToLocal( sdf ) );
	}

	protected override bool OnSubtract<T>( in T sdf )
	{
		return Data.Subtract( ToLocal( sdf ) );
	}

	protected override void OnUpdateMesh()
	{
		throw new NotImplementedException();
	}
}
