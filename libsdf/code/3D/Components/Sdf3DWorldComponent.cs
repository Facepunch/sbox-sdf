using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sandbox;
using Sandbox.Sdf;

[Title( "SDF 3D World" ), Icon( "public" )]
public sealed class Sdf3DWorldComponent : Component, Component.ExecuteInEditor
{
	public Sdf3DWorld World
	{
		get
		{
			return Components.Get<Sdf3DWorld>();
		}
	}

	private bool _brushesInvalid = true;
	private HashSet<Sdf3DBrushComponent> _changes = new();
	private Task _rebuildTask;
	private int _rebuildCount;

	internal void InvalidateBrush( Sdf3DBrushComponent brush )
	{
		_brushesInvalid = true;
		_changes.Add( brush );
	}

	protected override void OnEnabled()
	{
		if ( World is null && Scene.SceneWorld.IsValid() )
		{
			World?.Destroy();
			GameObject.Components.GetOrCreate<Sdf3DWorld>();

			_brushesInvalid = true;
		}
	}

	protected override void OnDisabled()
	{
		World?.Destroy();
	}

	protected override void OnUpdate()
	{
		if ( World is null || !Game.IsPlaying )
		{
			return;
		}

		if ( _brushesInvalid )
		{
			_brushesInvalid = false;
			_rebuildTask = RebuildFromBrushesAsync( ++_rebuildCount );
		}
	}

	private async Task RebuildFromBrushesAsync( int rebuildCount )
	{
		var lastTask = _rebuildTask ?? Task.CompletedTask;

		if ( !lastTask.IsCompleted )
		{
			await lastTask;
		}

		if ( _rebuildCount != rebuildCount )
		{
			return;
		}

		var brushes = Components.GetAll<Sdf3DBrushComponent>()
			.ToArray();

		var modifications = Components.GetAll<Sdf3DBrushComponent>()
			.Select( x => x.NextModification )
			.Where( x => x.Resource != null )
			.ToArray();

		var changes = new List<Modification<Sdf3DVolume, ISdf3D>>( _changes.Count * 2 );

		foreach ( var changedBrush in _changes )
		{
			if ( changedBrush.PrevModification.Resource != null )
			{
				changes.Add( changedBrush.PrevModification );
			}

			if ( changedBrush.NextModification.Resource != null )
			{
				changes.Add( changedBrush.NextModification );
			}
		}

		_changes.Clear();

		foreach ( var brush in brushes )
		{
			brush.CommitModification();
		}

		await World.SetModificationsAsync( modifications, changes.Count == 0 ? null : changes );
	}
}
