﻿using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Voxelizer.Rendering
{
    /// <summary>
    /// Encapsulate resources needed for a specific mesh voxelization 
    /// and trigger its generation and its rendering.
    /// </summary>
    public class Voxelization : MonoBehaviour
    {
        private const string VOXELIZATION = "Voxelization";

        [SerializeField]
        [Tooltip("Voxelization global resources")]
        private VoxelizationResources _resources = null;

        [SerializeField]
        [Tooltip("Mesh to voxelize")]
        private Mesh _mesh = null;

        [SerializeField]
        [Tooltip("Number of voxels in largest Mesh dimension")]
        [Range(2, 1024)]
        private int _resolution = 2;

        // TODO: implement
        //[SerializeField]
        //[Range(0, 10)]
        //[Tooltip("Lod bias at rendering")]
        //private int _lod = 0;

        private VoxelsData _voxelsData;
        private ComputeBuffer _indirectDrawArgs;
        private Material _indirectDrawMaterial;
        private MaterialPropertyBlock _materialPropertyBlock;
        private CommandBuffer _commandBuffer;

        // TODO: not clean way to enable revoxelization from editor
        //       but helps debugging
        private bool _forceVoxelize = false;

        private void Update()
        {
            if (_forceVoxelize)
            {
                // dispose of previous resources
                _forceVoxelize = false;
                StopAllCoroutines();

                if (Camera.main != null && _commandBuffer != null)
                    Camera.main.RemoveCommandBuffer(CameraEvent.AfterEverything, _commandBuffer);

                DisposeAll();
                
                // revoxelize
                Voxelize();
            }

            if (_mesh != null && _indirectDrawArgs != null)
            {
                // get volume bounds so frustum culling works fine
                var instancedMesh = _resources.FilledVoxelInstanceMesh;
                var localToWorld = gameObject.transform.localToWorldMatrix;
                var volumeLocalBounds = new Bounds(_mesh.bounds.center, _voxelsData.VolumeSize);
                var volumeWorldBounds = GeometryUtility.CalculateBounds(
                    new Vector3[] {volumeLocalBounds.min, volumeLocalBounds.max}, 
                    localToWorld);
                
                // take into account this gameobject transform
                _indirectDrawMaterial.SetMatrix(VoxelizationResources.VOLUME_LOCAL_TO_WORLD, localToWorld);

                // draw all filled voxels instances
                Graphics.DrawMeshInstancedIndirect(instancedMesh, 
                    0,
                    _indirectDrawMaterial,
                    volumeWorldBounds, 
                    _indirectDrawArgs,
                    0,
                    _materialPropertyBlock);
            }
        }

        private void OnValidate()
        {
            // notify of a change
            // next update will draw everything
            _forceVoxelize = true;
        }

        private void OnEnable()
        {
            Voxelize();
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            DisposeAll();
        }

        private void DisposeAll()
        {
            if (_voxelsData != null) _voxelsData.Dispose();
            _voxelsData = null;

            if (_indirectDrawArgs != null) _indirectDrawArgs.Release();
            _indirectDrawArgs = null;

            DisposeCommandBuffer();
        }

        private void DisposeCommandBuffer()
        {
            if (_commandBuffer != null) _commandBuffer.Release();
            _commandBuffer = null;
        }

        private void Voxelize()
        {
            // bail out if data is not properly setup
            if (_mesh == null || _resources == null) return;

            // create resources whenever necessary
            if (_voxelsData == null)
                _voxelsData = VoxelizationUtils.CreateVoxelData(_mesh.bounds, _resolution, _mesh.name);

            if (_indirectDrawArgs == null) CreateIndirectDrawResources();

            if (_commandBuffer == null)
            {
                _commandBuffer = new CommandBuffer();
                _commandBuffer.name = VOXELIZATION;
            }

            VoxelizationUtils.VoxelizeSurface(_commandBuffer, _resources, _voxelsData, _mesh);

            // execute voxelization at the end of the regular rendering
            if (Camera.main != null)
                Camera.main.AddCommandBuffer(CameraEvent.AfterEverything, _commandBuffer);

            // since we do an indirect draw call, must recuperate desired instance count
            StartCoroutine(CopyCounter());
        }

        private IEnumerator CopyCounter()
        {
            yield return new WaitForEndOfFrame();

            ComputeBuffer.CopyCount(_voxelsData.FilledVoxelInstances, 
                _indirectDrawArgs, 
                sizeof(uint));

            // done voxelizing, remove command buffer
            if (Camera.main != null)
                Camera.main.RemoveCommandBuffer(CameraEvent.AfterEverything, _commandBuffer);

            // done with command buffer
            DisposeCommandBuffer();
        }

        private void CreateIndirectDrawResources()
        {
            _indirectDrawMaterial = new Material(_resources.FilledVoxelInstanceShader);
            _indirectDrawMaterial.enableInstancing = true;

            var volumeSize = _voxelsData.VolumeSize;
            _indirectDrawMaterial.SetVector(VoxelizationResources.VOLUME_SIZE, 
                new Vector4(volumeSize.x, volumeSize.y, volumeSize.z, _voxelsData.VoxelSize));

            _materialPropertyBlock = new MaterialPropertyBlock();
            _materialPropertyBlock.Clear();
            _materialPropertyBlock.SetBuffer(VoxelizationResources.FILLED_VOXELS_INSTANCES,
                _voxelsData.FilledVoxelInstances);

            var instancedMesh = _resources.FilledVoxelInstanceMesh;
            var args = new uint[5];
            args[0] = instancedMesh.GetIndexCount(0);
            args[1] = 0; // instance count
            args[2] = instancedMesh.GetIndexStart(0);
            args[3] = instancedMesh.GetBaseVertex(0);
            args[4] = 0; // start instance location

            _indirectDrawArgs = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            _indirectDrawArgs.SetData(args);
        }
    }
}