using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sandbox.MarchingSquares
{
    [GameResource("Marching Squares Material", "msmat", "Material used by Sandbox.MarchingSquares", Icon = "brush")]
    public class MarchingSquaresMaterial : GameResource
    {
        public float Depth { get; set; } = 64f;

        public Material FrontFaceMaterial { get; set; }

        public Material BackFaceMaterial { get; set; }

        public Material CutFaceMaterial { get; set; }
    }
}
