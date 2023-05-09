using Sandbox.Sdf;
using System;
using System.Collections.Generic;

namespace Sandbox.MarchingSquares
{
    public class SdfArray2D
    {
        public int Resolution { get; }
        public float Size { get; }
        public NormalStyle NormalStyle { get; }

        private Dictionary<MarchingSquaresMaterial, float[]> Layers { get; }

        public IEnumerable<MarchingSquaresMaterial> Materials => Layers.Keys;

        private int ArraySize { get; }
        private float UnitSize { get; }
        private float InvUnitSize { get; }

        private int Margin { get; }

        public SdfArray2D( int resolution, float size, NormalStyle normalStyle )
        {
            Resolution = resolution;
            Size = size;
            NormalStyle = normalStyle;

            Margin = normalStyle == NormalStyle.Flat ? 0 : 1;

            ArraySize = Resolution + Margin * 2 + 1;
            UnitSize = Size / Resolution;
            InvUnitSize = Resolution / Size;
            Layers = new Dictionary<MarchingSquaresMaterial, float[]>();
        }

        public void Clear()
        {
            foreach ( var layer in Layers )
            {
                Array.Fill( layer.Value, 1f );
            }
        }

        public bool Add<T>( in T sdf, MarchingSquaresMaterial material )
            where T : ISdf2D
        {
            var bounds = sdf.Bounds;

            var min = (bounds.TopLeft - Vector2.One) * InvUnitSize;
            var max = (bounds.BottomRight + Vector2.One) * InvUnitSize;

            var minX = Math.Max( 0, (int) MathF.Ceiling( min.x ) + Margin );
            var minY = Math.Max( 0, (int) MathF.Ceiling( min.y ) + Margin );

            var maxX = Math.Min( ArraySize, (int) MathF.Ceiling( max.x ) + Margin );
            var maxY = Math.Min( ArraySize, (int) MathF.Ceiling( max.y ) + Margin );

            var changed = false;

            if ( material != null )
            {
                if ( !Layers.TryGetValue( material, out var layer ) )
                {
                    layer = new float[ArraySize * ArraySize];
                    Array.Fill( layer, 1f );
                    Layers.Add( material, layer );
                }

                for ( var y = minY; y < maxY; ++y )
                {
                    var worldY = (y - Margin) * UnitSize;

                    for ( int x = minX, index = minX + y * ArraySize; x < maxX; ++x, ++index )
                    {
                        var worldX = (x - Margin) * UnitSize;
                        var sampled = sdf[new Vector2( worldX, worldY )];

                        if ( sampled >= 1f ) continue;

                        var oldValue = layer[index];
                        var newValue = Math.Clamp( sampled, -1f, oldValue );

                        layer[index] = newValue;

                        // ReSharper disable once CompareOfFloatsByEqualityOperator
                        changed |= oldValue != newValue;
                    }
                }
            }

            foreach ( var (mat, layer) in Layers )
            {
                if ( mat == material )
                {
                    continue;
                }

                for ( var y = minY; y < maxY; ++y )
                {
                    var worldY = (y - Margin) * UnitSize;

                    for ( int x = minX, index = minX + y * ArraySize; x < maxX; ++x, ++index )
                    {
                        var worldX = (x - Margin) * UnitSize;
                        var sampled = sdf[new Vector2( worldX, worldY )];

                        if ( sampled >= 1f ) continue;

                        var oldValue = layer[index];
                        var newValue = Math.Clamp( -sampled, oldValue, 1f );

                        layer[index] = newValue;

                        // ReSharper disable once CompareOfFloatsByEqualityOperator
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

            writer.Write( layer, Margin * ArraySize + Margin, Resolution, Resolution, ArraySize );
        }
    }
}
