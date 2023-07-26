using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sandbox.Sdf;

partial class PolygonMeshBuilder
{
	private StringBuilder DebugWriter { get; } = new StringBuilder();

	public void ClearDebug()
	{
		DebugWriter.Clear();
	}

	public void WriteDebug( string value )
	{
		DebugWriter.AppendLine( value );
	}

	public void PrintDebug()
	{
		Log.Info( DebugWriter );
	}
}
