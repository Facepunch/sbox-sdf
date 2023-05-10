using Sandbox.Sdf;
using System;
using System.Collections.Generic;

namespace Sandbox.MarchingSquares
{
    public record struct SdfArray2DLayer( byte[] Samples, int BaseIndex, int RowStride )
    {
        public byte this[ int x, int y ] => Samples[BaseIndex + x + y * RowStride];
    }

    public partial class SdfArray2D : BaseNetworkable, INetworkSerializer
    {
        private const byte MaxEncoded = 255;

        public int Resolution { get; private set; }
        public float Size { get; private set; }
        public float MaxDistance { get; private set; }
        public int Margin { get; private set; }

        private Dictionary<MarchingSquaresMaterial, byte[]> Layers { get; } = new();

        public IEnumerable<MarchingSquaresMaterial> Materials => Layers.Keys;

        private int _arraySize;
        private float _unitSize;
        private float _invUnitSize;
        private float _invMaxDistance;

        public SdfArray2D()
        {

        }

        public SdfArray2D( int resolution, float size, float maxDistance )
        {
            Resolution = resolution;
            Size = size;
            MaxDistance = maxDistance;

            Margin = 1;

            _arraySize = Resolution + Margin * 2 + 1;
            _unitSize = Size / Resolution;
            _invUnitSize = Resolution / Size;
            _invMaxDistance = 1f / MaxDistance;
        }

        private byte Encode( float distance )
        {
            return (byte)((int)((distance * _invMaxDistance * 0.5f + 0.5f) * MaxEncoded)).Clamp( 0, 255 );
        }

        private float Decode( byte encoded )
        {
            return (encoded * (1f / MaxEncoded) - 0.5f) * MaxDistance * 2f;
        }

        private byte[] GetOrCreateLayer( MarchingSquaresMaterial material, float fill )
        {
            if ( Layers.TryGetValue( material, out var layer ) )
            {
                return layer;
            }

            var encoded = Encode( fill );

            layer = new byte[_arraySize * _arraySize];
            Array.Fill( layer, encoded );
            Layers.Add( material, layer );

            return layer;
        }

        public void Clear( MarchingSquaresMaterial material = null )
        {
            foreach ( var layer in Layers )
            {
                var encoded = Encode( layer.Key == material ? -MaxDistance : MaxDistance );
                Array.Fill( layer.Value, encoded );
            }

            if ( material != null )
            {
                GetOrCreateLayer( material, -MaxDistance );
            }
        }

        public bool Add<T>( in T sdf, MarchingSquaresMaterial material )
            where T : ISdf2D
        {
            var bounds = sdf.Bounds;

            var min = (bounds.TopLeft - MaxDistance) * _invUnitSize;
            var max = (bounds.BottomRight + MaxDistance) * _invUnitSize;

            var minX = Math.Max( 0, (int) MathF.Ceiling( min.x ) + Margin );
            var minY = Math.Max( 0, (int) MathF.Ceiling( min.y ) + Margin );

            var maxX = Math.Min( _arraySize, (int) MathF.Ceiling( max.x ) + Margin );
            var maxY = Math.Min( _arraySize, (int) MathF.Ceiling( max.y ) + Margin );

            var changed = false;

            foreach ( var (mat, layer) in Layers )
            {
                if ( mat == material )
                {
                    continue;
                }

                for ( var y = minY; y < maxY; ++y )
                {
                    var worldY = (y - Margin) * _unitSize;

                    for ( int x = minX, index = minX + y * _arraySize; x < maxX; ++x, ++index )
                    {
                        var worldX = (x - Margin) * _unitSize;
                        var sampled = sdf[new Vector2( worldX, worldY )];

                        if ( sampled >= MaxDistance ) continue;

                        var encoded = Encode( sampled );

                        var oldValue = layer[index];
                        var newValue = Math.Max( (byte) (MaxEncoded - encoded), oldValue );

                        layer[index] = newValue;

                        changed |= oldValue != newValue;
                    }
                }
            }

            if ( material != null )
            {
                var layer = GetOrCreateLayer( material, MaxDistance );

                for ( var y = minY; y < maxY; ++y )
                {
                    var worldY = (y - Margin) * _unitSize;

                    for ( int x = minX, index = minX + y * _arraySize; x < maxX; ++x, ++index )
                    {
                        var worldX = (x - Margin) * _unitSize;
                        var sampled = sdf[new Vector2( worldX, worldY )];

                        if ( sampled >= MaxDistance ) continue;

                        var encoded = Encode( sampled );

                        var oldValue = layer[index];
                        var newValue = Math.Min( encoded, oldValue );

                        layer[index] = newValue;

                        changed |= oldValue != newValue;
                    }
                }
            }

            return changed;
        }

        public bool Subtract<T>( in T sdf )
            where T : ISdf2D
        {
            return Add<T>( sdf, null );
        }

        public void WriteTo( MarchingSquaresMeshWriter writer, MarchingSquaresMaterial material )
        {
            if ( !Layers.TryGetValue( material, out var layer ) )
            {
                return;
            }

            writer.Write( new SdfArray2DLayer( layer, Margin * _arraySize + Margin, _arraySize ),
                Resolution, Resolution, _unitSize, material.Depth );
        }

        public void Read( ref NetRead read )
        {
            throw new NotImplementedException();
        }

        public void Write( NetWrite write )
        {
            throw new NotImplementedException();
        }
    }
}
