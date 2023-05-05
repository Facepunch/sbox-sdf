using System;
using Sandbox;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Sdf
{
	public partial class MarchingCubesEntity : Entity
	{
		public float ChunkSize { get; }
		public int ChunkSubdivisions { get; }
		public NormalStyle NormalStyle { get; }

		private readonly float _chunkScale;
		private readonly int _margin;

		private readonly Dictionary<Vector3i, MarchingCubesChunk> _chunks = new Dictionary<Vector3i, MarchingCubesChunk>();

		// ReSharper disable once UnusedMember.Global
		public MarchingCubesEntity()
		{

		}

		public MarchingCubesEntity( float chunkSize, int chunkSubdivisions = 4, NormalStyle normalStyle = NormalStyle.Smooth )
		{
			ChunkSize = chunkSize;
			ChunkSubdivisions = chunkSubdivisions;
			NormalStyle = normalStyle;

			_chunkScale = 1f / ChunkSize;

			_margin = normalStyle == NormalStyle.Flat ? 1 : 2;
		}

		protected override void OnDestroy()
		{
			Clear();
		}

		public void Clear()
		{
			foreach ( var pair in _chunks )
			{
				pair.Value.Delete();
			}

			_chunks.Clear();
		}

		public Matrix WorldToLocal => Matrix.CreateScale( 1f / Scale )
			* Matrix.CreateRotation( Rotation.Inverse )
			* Matrix.CreateTranslation( -Position );

		private void GetChunkBounds( Matrix transform, BBox bounds,
			out Matrix invChunkTransform, out BBox chunkBounds,
			out Vector3i minChunkIndex, out Vector3i maxChunkIndex )
		{
			var worldToLocal = Matrix.CreateScale(1f / Scale)
				* Matrix.CreateRotation(Rotation.Inverse)
				* Matrix.CreateTranslation(-Position);

			var localTransform = transform * worldToLocal;

			invChunkTransform = Matrix.CreateScale( ChunkSize )
				* localTransform.Inverted;

			chunkBounds = localTransform.Transform( bounds );
			chunkBounds = new BBox( chunkBounds.Mins - _margin - 1, chunkBounds.Maxs + _margin + 1 ) * _chunkScale;

			minChunkIndex = Vector3i.Floor( chunkBounds.Mins );
			maxChunkIndex = Vector3i.Ceiling( chunkBounds.Maxs ) + 1;
		}

		private MarchingCubesChunk GetOrCreateChunk( Vector3i index3 )
		{
			if ( _chunks.TryGetValue( index3, out var chunk ) ) return chunk;

			_chunks.Add( index3, chunk = new MarchingCubesChunk( new ArrayVoxelData( ChunkSubdivisions, NormalStyle ), ChunkSize ) );

			chunk.Name = $"Chunk {index3.x} {index3.y} {index3.z}";

			chunk.SetParent( this );
			chunk.LocalPosition = (Vector3)index3 * ChunkSize - ChunkSize / (1 << ChunkSubdivisions);

			return chunk;
		}

		public float GetValue( Vector3 pos )
		{
			var localPos = WorldToLocal.Transform( pos );
			var chunkIndex = Vector3i.Floor( localPos * _chunkScale );

			if ( !_chunks.TryGetValue( chunkIndex, out var chunk ) )
			{
				return -1f;
			}

			return chunk.Data.GetValue( localPos - (Vector3) chunkIndex * ChunkSize );
		}

		public async Task<bool> Add<T>( T sdf, Matrix transform, Color color )
			where T : ISignedDistanceField
		{
			GetChunkBounds( transform, sdf.Bounds,
				out var invChunkTransform, out var chunkBounds,
				out var minChunkIndex, out var maxChunkIndex );

			var tasks = new List<Task<bool>>();

			foreach ( var (indexOffset, _) in (maxChunkIndex - minChunkIndex).Enumerate() )
			{
				var chunkIndex = minChunkIndex + indexOffset;
				var chunk = GetOrCreateChunk( chunkIndex );

				tasks.Add( chunk.Add( sdf, chunkBounds + -chunkIndex,
					Matrix.CreateTranslation( chunkIndex ) * invChunkTransform,
					color ) );
			}

			return (await Task.WhenAll( tasks )).Any();
		}

		public async Task<bool> Subtract<T>( T sdf, Matrix transform )
			where T : ISignedDistanceField
		{
			GetChunkBounds( transform, sdf.Bounds,
				out var invChunkTransform, out var chunkBounds,
				out var minChunkIndex, out var maxChunkIndex );

			var tasks = new List<Task<bool>>();

            foreach ( var (indexOffset, _) in Helpers.Enumerate( maxChunkIndex - minChunkIndex ) )
			{
				var chunkIndex = minChunkIndex + indexOffset;

				if ( !_chunks.TryGetValue( chunkIndex, out var chunk ) ) continue;

				tasks.Add( chunk.Subtract( sdf, chunkBounds + -chunkIndex,
					Matrix.CreateTranslation( chunkIndex ) * invChunkTransform ) );
			}

            return (await Task.WhenAll( tasks )).Any();
        }
	}
}
