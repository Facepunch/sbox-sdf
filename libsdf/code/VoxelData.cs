using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Sdf
{
	public interface IVoxelData
	{
		bool Clear();
		void UpdateMesh( IVoxelMeshWriter writer, int lod, bool render, bool collision );

		bool Add<T>( T sdf, BBox bounds, Matrix transform, Color color )
			where T : ISignedDistanceField;
		bool Subtract<T>( T sdf, BBox bounds, Matrix transform )
			where T : ISignedDistanceField;
	}

	public partial class ArrayVoxelData : BaseNetworkable, IVoxelData, INetworkSerializer
	{
		public const int MaxSubdivisions = 5;

		public int Subdivisions { get; private set; }
		public NormalStyle NormalStyle { get; private set; }

		public int NetReadCount { get; private set; }

		private Voxel[] _voxels;
		private Vector3i _size;
		private Vector3i _renderedSize;
		private Vector3 _scale;

		private bool _cleared;
		private bool _hasInterior;
		private bool _hasExterior;

		private int _margin;

		public ArrayVoxelData()
		{

		}

		public ArrayVoxelData( int subdivisions, NormalStyle normalStyle )
		{
			if ( subdivisions < 0 || subdivisions > MaxSubdivisions )
			{
				throw new ArgumentOutOfRangeException( nameof(subdivisions),
					$"Expected {nameof(subdivisions)} to be between 0 and {MaxSubdivisions}." );
			}

			Init( subdivisions, normalStyle );
		}

		private void Init( int subdivisions, NormalStyle normalStyle )
		{
			Subdivisions = subdivisions;
			NormalStyle = normalStyle;

			_cleared = true;
			_margin = normalStyle == NormalStyle.Flat ? 0 : 1;

			var resolution = 1 << Subdivisions;

			_renderedSize = resolution;
			_size = _renderedSize + _margin * 2 + 1;
			_scale = 1f / resolution;
		}

		public bool Clear()
		{
			if ( _cleared || _voxels == null ) return false;

			Array.Clear( _voxels, 0, _voxels.Length );

			_cleared = true;

			return true;
		}

		public void UpdateMesh( IVoxelMeshWriter writer, int lod, bool render, bool collision )
		{
			if ( _voxels == null || _cleared || !render && !collision ) return;
			if ( !_hasInterior || !_hasExterior ) return;

			writer.Write( _voxels, _size, _margin, _size - _margin, lod, render ? NormalStyle : NormalStyle.Flat, render, collision );
		}

		private bool PrepareVoxelsForEditing( BBox bounds, out Vector3i outerMin, out Vector3i outerMax )
		{
			outerMin = Vector3i.Max( Vector3i.Floor( bounds.Mins * _renderedSize ) - _margin - 1, 0 );
			outerMax = Vector3i.Min( Vector3i.Ceiling( bounds.Maxs * _renderedSize ) + 2 + _margin, _size );

			if ( outerMin.x >= outerMax.x || outerMin.y >= outerMax.y || outerMin.z >= outerMax.z )
			{
				return false;
			}

			_voxels ??= new Voxel[_size.x * _size.y * _size.z];

			return true;
		}

		public bool Add<T>( T sdf, BBox bounds, Matrix transform, Color color )
			where T : ISignedDistanceField
		{
			if ( !PrepareVoxelsForEditing( bounds, out var outerMin, out var outerMax ) )
			{
				return false;
			}

			var changed = false;
			var r = (byte)MathF.Round( color.r * byte.MaxValue );
			var g = (byte)MathF.Round( color.g * byte.MaxValue );
			var b = (byte)MathF.Round( color.b * byte.MaxValue );

			foreach ( var (index3, index) in _size.EnumerateArray3D( outerMin, outerMax ) )
			{
				var pos = transform.Transform( (index3 - _margin) * _scale );
				var prev = _voxels[index];
				var next = prev + new Voxel(sdf[pos], r, g, b);

				_voxels[index] = next;

				changed |= next != prev;
			}

			if ( changed )
			{
				_cleared = false;
			}

			return changed;
		}

		public bool Subtract<T>( T sdf, BBox bounds, Matrix transform )
			where T : ISignedDistanceField
		{
			if ( !PrepareVoxelsForEditing( bounds, out var outerMin, out var outerMax ) )
			{
				return false;
			}

			var changed = false;

			foreach ( var (index3, index) in _size.EnumerateArray3D( outerMin, outerMax ) )
			{
				var pos = transform.Transform( (index3 - _margin) * _scale );
				var prev = _voxels[index];
				var next = prev - new Voxel( sdf[pos], 0, 0, 0 );

				_voxels[index] = next;

				changed |= next != prev;
			}

			if ( changed )
			{
				_cleared = false;
			}

			return changed;
		}

		public float GetValue( Vector3 pos )
		{
			throw new NotImplementedException();
		}

		public void Read( ref NetRead read )
		{
			var subDivs = read.Read<int>();
			var normalStyle = read.Read<NormalStyle>();

			if ( Subdivisions != subDivs || NormalStyle != normalStyle )
			{
				Init( subDivs, normalStyle );
			}

			_hasInterior = read.Read<bool>();
			_hasExterior = read.Read<bool>();

			if ( _hasInterior && _hasExterior )
			{
				_voxels = read.ReadUnmanagedArray(_voxels);
			}

			_cleared = false;

			++NetReadCount;
		}

		public void Write( NetWrite write )
		{
			write.Write( Subdivisions );
			write.Write( NormalStyle );

			_hasInterior = false;
			_hasExterior = false;

			if ( _voxels != null )
			{
				for (var i = 0; i < _voxels.Length; i++)
				{
					var interior = _voxels[i].RawValue >= 128;

					_hasInterior |= interior;
					_hasExterior |= !interior;
				}
			}

			write.Write( _hasInterior );
			write.Write( _hasExterior );

			if ( _hasInterior && _hasExterior )
			{
				write.WriteUnmanagedArray( _voxels );
			}
		}

		public override string ToString()
		{
			return $"(Subdivisions: {Subdivisions}, {(_voxels != null ? "Has Data" :  "No Data")})";
		}
	}
}
