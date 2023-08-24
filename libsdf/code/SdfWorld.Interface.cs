using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.Internal;

namespace Sandbox.Sdf;

internal interface ISdfWorldImpl : IValid
{
	SceneWorld Scene { get; }
	bool HasPhysics { get; }

	Transform Transform { get; set; }

	Vector3 Position { get; set; }
	Rotation Rotation { get; set; }
	float Scale { get; set; }

	Vector3 LocalPosition { get; set; }
	Rotation LocalRotation { get; set; }
	float LocalScale { get; set; }

	EntityTags Tags { get; }

	PhysicsShape AddMeshShape( List<Vector3> vertices, List<int> indices );
}
