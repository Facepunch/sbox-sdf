using Sandbox.Sdf;
using System;
using System.Collections.Generic;

namespace Sandbox.MarchingSquares
{
    public class SdfArray2D
    {
        public int Resolution { get; }
        public float Size { get; }
        public float MaxDistance { get; }
        public NormalStyle NormalStyle { get; }

        private Dictionary<MarchingSquaresMaterial, float[]> Layers { get; }

        public IEnumerable<MarchingSquaresMaterial> Materials => Layers.Keys;

        private int ArraySize { get; }
        private float UnitSize { get; }
        private float InvUnitSize { get; }

        private int Margin { get; }

        public SdfArray2D( int resolution, float size, float maxDistance, NormalStyle normalStyle )
        {
            Resolution = resolution;
            Size = size;
            MaxDistance = maxDistance;
            NormalStyle = normalStyle;

            Margin = normalStyle == NormalStyle.Flat ? 0 : 1;

            ArraySize = Resolution + Margin * 2 + 1;
            UnitSize = Size / Resolution;
            InvUnitSize = Resolution / Size;
            Layers = new Dictionary<MarchingSquaresMaterial, float[]>();
        }

        private float[] GetOrCreateLayer( MarchingSquaresMaterial material, float fill )
        {
            if ( Layers.TryGetValue( material, out var layer ) )
            {
                return layer;
            }

            layer = new float[ArraySize * ArraySize];
            Array.Fill( layer, fill );
            Layers.Add( material, layer );

            return layer;
        }

        public void Clear( MarchingSquaresMaterial material = null )
        {
            foreach ( var layer in Layers )
            {
                if ( layer.Key == material )
                {
                    Array.Fill( layer.Value, -MaxDistance );
                }
                else
                {
                    Array.Fill( layer.Value, MaxDistance );
                }
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

            var min = (bounds.TopLeft - MaxDistance) * InvUnitSize;
            var max = (bounds.BottomRight + MaxDistance) * InvUnitSize;

            var minX = Math.Max( 0, (int) MathF.Ceiling( min.x ) + Margin );
            var minY = Math.Max( 0, (int) MathF.Ceiling( min.y ) + Margin );

            var maxX = Math.Min( ArraySize, (int) MathF.Ceiling( max.x ) + Margin );
            var maxY = Math.Min( ArraySize, (int) MathF.Ceiling( max.y ) + Margin );

            var changed = false;

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

                        if ( sampled >= MaxDistance ) continue;

                        var oldValue = layer[index];
                        var newValue = Math.Clamp( -sampled, oldValue, MaxDistance );

                        layer[index] = newValue;

                        // ReSharper disable once CompareOfFloatsByEqualityOperator
                        changed |= oldValue != newValue;
                    }
                }
            }

            if ( material != null )
            {
                var layer = GetOrCreateLayer( material, MaxDistance );

                for ( var y = minY; y < maxY; ++y )
                {
                    var worldY = (y - Margin) * UnitSize;

                    for ( int x = minX, index = minX + y * ArraySize; x < maxX; ++x, ++index )
                    {
                        var worldX = (x - Margin) * UnitSize;
                        var sampled = sdf[new Vector2( worldX, worldY )];

                        if ( sampled >= MaxDistance ) continue;

                        var oldValue = layer[index];
                        var newValue = Math.Clamp( sampled, -MaxDistance, oldValue );

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

            writer.Write( layer, Margin * ArraySize + Margin, Resolution, Resolution, ArraySize, UnitSize, material.Depth );
        }
    }
}
