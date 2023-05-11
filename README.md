# facepunch.libsdf
Allows you to define a 2D / 3D field of signed distances to a virtual surface, perform operations with primitive shapes, then generate a 3D mesh of the surface in real time.

## 2D
https://github.com/Facepunch/sbox-sdf/assets/1110904/b85d065d-14b0-46af-97d3-2fe2d799aa97

### Create a world
On the server, create a SDF 2D world like this:

```csharp
using Sandbox.Sdf;

// ...

var sdfWorld = new Sdf2DWorld( Sdf2DWorldQuality.Medium )
{
    // Rotate so that Y is up
    LocalRotation = Rotation.FromRoll( 90f )
};
```

In this example we've rotated it by 90 degrees, so that the Y axis when doing 2D operations becomes the Z (up) axis in world space.

### Draw some shapes
Still in server-side code, you can modify the SDF world like this:

```csharp
// Shape that we want to add
var circle = new CircleSdf( new Vector2( localPos.x, localPos.y ), radius );

// Load the material to use
var material = ResourceLibrary.Get<Sdf2DMaterial>( "materials/sdf2d_default.sdflayer" );

// Draw the circle!
sdfWorld.Add( circle, material );

// Move the circle to the right, then subtract it!
sdfWorld.Subtract( circle.Translate( new Vector2( 32f, 0f ) ), baseMat );
```

### Materials
You can create your own `.sdflayer` resource by selecting "New SDF 2D Material" when creating a new asset.

![sbox-dev_rfmug4wJSE](https://github.com/Facepunch/sbox-sdf/assets/1110904/41551730-6161-4ba7-bcd6-ea617a66e9f2)

In the editor you can choose how deep the layer is, which collision tags it has, and which materials to use for the front, back, and side faces.
If you remove all collision tags, the layer will have no physics shapes.

### Load an SDF
Let's say you have an SDF image like this:

![facepunch_sdf](https://github.com/Facepunch/sbox-sdf/assets/1110904/4474d35d-7899-45df-b195-b7d65009bd1b)

You can convert it into a shape that you can add to / subtract from the world like this:

```csharp
var fpSdfTexture = await Texture.LoadAsync( FileSystem.Mounted, "textures/facepunch_sdf.png" );
var fpSdf = new TextureSdf( fpSdfTexture, 64, 1024f );

var baseMat = ResourceLibrary.Get<Sdf2DMaterial>( "materials/sdf2d_default.sdflayer" );
var greyMat = ResourceLibrary.Get<Sdf2DMaterial>( "materials/sdf2d_darker.sdflayer" );

sdfWorld.Add( fpSdf, baseMat );
sdfWorld.Add( fpSdf.Expand( 16f ), greyMat );
```

![sbox-dev_qrBj4yCkWN](https://github.com/Facepunch/sbox-sdf/assets/1110904/1007398b-9c96-42d1-8139-746b9b6d37d8)

Be careful to give the correct width of the gradient in pixels (64 in this example), so that any operations you perform on the SDF will be scaled correctly.

### SDF manipulation
You can move SDFs around like this:
```csharp
var moved = someSdf.Translate( new Vector2( x, y ) );
```

Or do arbitrary transformations:
```csharp
var transformed = someSdf.Transform( new Vector2( x, y ), angle, scale );
```

Expand the surface of the shape like this:
```csharp
var expanded = someSdf.Expand( distance );
```
Negative values will shrink the surface. This may not work perfectly for SDFs loaded from an image, and definitely won't work for large distances.

## 3D
WIP
