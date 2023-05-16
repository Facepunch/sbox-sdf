using Sandbox.Sdf;

namespace MiningDemo;

public static class Materials
{
	private static Sdf2DLayer _black;
	private static Sdf2DLayer _rock;
	private static Sdf2DLayer _background;

	public static Sdf2DLayer Black => _black ??= ResourceLibrary.Get<Sdf2DLayer>( "materials/black.sdflayer" );
	public static Sdf2DLayer Rock => _rock ??= ResourceLibrary.Get<Sdf2DLayer>( "materials/rock.sdflayer" );
	public static Sdf2DLayer Background => _background ??= ResourceLibrary.Get<Sdf2DLayer>( "materials/background.sdflayer" );
}
