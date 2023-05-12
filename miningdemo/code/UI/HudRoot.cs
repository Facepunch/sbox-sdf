using Sandbox.UI;

namespace MiningDemo.UI
{
	public partial class HudRoot : RootPanel
	{
		public HudRoot()
		{
			AddChild( new Panel
			{
				Style =
				{
					PointerEvents = PointerEvents.All, Width = Length.Pixels( 0f ), Height = Length.Pixels( 0f )
				}
			} );
		}
	}
}
