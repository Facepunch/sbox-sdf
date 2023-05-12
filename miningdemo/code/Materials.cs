using Sandbox.Sdf;

namespace MiningDemo;

public static class Materials
{
	private static Sdf2DMaterial _black;
	private static Sdf2DMaterial _rock;
	private static Sdf2DMaterial _background;

	public static Sdf2DMaterial Black => _black ??= ResourceLibrary.Get<Sdf2DMaterial>( "materials/black.sdflayer" );
	public static Sdf2DMaterial Rock => _rock ??= ResourceLibrary.Get<Sdf2DMaterial>( "materials/rock.sdflayer" );
	public static Sdf2DMaterial Background => _background ??= ResourceLibrary.Get<Sdf2DMaterial>( "materials/background.sdflayer" );
}
