using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.Internal;

namespace Sandbox.Sdf;

internal class SdfWorldSceneObject : SceneCustomObject, ISdfWorldImpl
{
	public SdfWorldSceneObject( SceneWorld sceneWorld ) : base( sceneWorld )
	{

	}

	public SceneWorld Scene => World;
	public bool HasPhysics => false;

	public float Scale
	{
		get => Transform.Scale;
		set => Transform = Transform.WithScale( value );
	}

	public Vector3 LocalPosition
	{
		get => Position;
		set => Position = value;
	}

	public Rotation LocalRotation
	{
		get => Rotation;
		set => Rotation = value;
	}

	public float LocalScale
	{
		get => Scale;
		set => Scale = value;
	}

	public EntityTags Tags => throw new NotImplementedException();

	public PhysicsShape AddMeshShape( List<Vector3> vertices, List<int> indices )
	{
		throw new NotImplementedException();
	}
}
