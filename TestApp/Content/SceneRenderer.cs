using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using Windows.Storage;
using static TerraFX.Interop.DirectX.D3DCOMPILE;
using static TerraFX.Interop.DirectX.D3D_PRIMITIVE_TOPOLOGY;
using static TerraFX.Interop.DirectX.D3D_ROOT_SIGNATURE_VERSION;
using static TerraFX.Interop.DirectX.D3D12_CLEAR_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_COMMAND_LIST_TYPE;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_RANGE_TYPE;
using static TerraFX.Interop.DirectX.D3D12_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_INPUT_CLASSIFICATION;
using static TerraFX.Interop.DirectX.D3D12_PRIMITIVE_TOPOLOGY_TYPE;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.DirectX.D3D12_ROOT_SIGNATURE_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_SHADER_VISIBILITY;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.DirectX.DX;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;
using static TerraFX.Interop.DirectX.PIX;
using static TerraFX.Interop.Windows.IID;
using static TestApp.DeviceResources;

namespace TestApp
{
    // This sample renderer instantiates a basic rendering pipeline.
    public unsafe class SceneRenderer : IDisposable
    {
        #region Constants
        // Indices into the application state map.
        private const string AngleKey = "Angle";
        private const string TrackingKey = "Tracking";
        #endregion

        #region Fields
        // Constant buffers must be 256-byte aligned.
        private static readonly uint AlignedConstantBufferSize = (uint)((Unsafe.SizeOf<ModelViewProjectionConstantBuffer>() + 255) & ~255);

        // Cached pointer to device resources.
        private DeviceResources _deviceResources;

        // Direct3D resources for cube geometry.
        private ID3D12GraphicsCommandList* _commandList;
        private ID3D12RootSignature* _rootSignature;
        private ID3D12PipelineState* _pipelineState;
        private ID3D12DescriptorHeap* _cbvHeap;
        private ID3D12Resource* _vertexBuffer;
        private ID3D12Resource* _indexBuffer;
        private ID3D12Resource* _constantBuffer;
        private ModelViewProjectionConstantBuffer _constantBufferData;
        private byte* _mappedConstantBuffer;
        private uint _cbvDescriptorSize;
        private RECT _scissorRect;
        private D3D12_VERTEX_BUFFER_VIEW _vertexBufferView;
        private D3D12_INDEX_BUFFER_VIEW _indexBufferView;

        // Variables used with the rendering loop.
        private bool _loadingComplete;
        private float _radiansPerSecond;
        private float _angle;
        private bool _tracking;
        #endregion

        #region Constructors
        // Loads vertex and pixel shaders from files and instantiates the cube geometry.
        public SceneRenderer(DeviceResources deviceResources)
        {
            _loadingComplete = false;
            _radiansPerSecond = MathF.PI / 4; // rotate 45 degrees per second
            _angle = 0;
            _tracking = false;
            _mappedConstantBuffer = null;
            _deviceResources = deviceResources;

            LoadState();
            _constantBufferData = default;

            CreateDeviceDependentResources();
            CreateWindowSizeDependentResources();
        }

        ~SceneRenderer()
        {
            Dispose();
        }
        #endregion

        #region Properties
        public bool IsTracking
        {
            get
            {
                return _tracking;
            }
        }

        private ID3D12GraphicsCommandList* CommandList
        {
            get
            {
                return _commandList;
            }

            set
            {
                if (_commandList != null)
                {
                    _commandList->Release();
                }
                _commandList = value;
            }
        }

        private ID3D12RootSignature* RootSignature
        {
            get
            {
                return _rootSignature;
            }

            set
            {
                if (_rootSignature != null)
                {
                    _rootSignature->Release();
                }
                _rootSignature = value;
            }
        }

        private ID3D12PipelineState* PipelineState
        {
            get
            {
                return _pipelineState;
            }

            set
            {
                if (_pipelineState != null)
                {
                    _pipelineState->Release();
                }
                _pipelineState = value;
            }
        }

        private ID3D12DescriptorHeap* CbvHeap
        {
            get
            {
                return _cbvHeap;
            }

            set
            {
                if (_cbvHeap != null)
                {
                    _cbvHeap->Release();
                }
                _cbvHeap = value;
            }
        }

        private ID3D12Resource* VertexBuffer
        {
            get
            {
                return _vertexBuffer;
            }

            set
            {
                if (_vertexBuffer != null)
                {
                    _vertexBuffer->Release();
                }
                _vertexBuffer = value;
            }
        }

        private ID3D12Resource* IndexBuffer
        {
            get
            {
                return _indexBuffer;
            }

            set
            {
                if (_indexBuffer != null)
                {
                    _indexBuffer->Release();
                }
                _indexBuffer = value;
            }
        }

        private ID3D12Resource* ConstantBuffer
        {
            get
            {
                return _constantBuffer;
            }

            set
            {
                if (_constantBuffer != null)
                {
                    _constantBuffer->Release();
                }
                _constantBuffer = value;
            }
        }
        #endregion

        #region Methods
        public void CreateDeviceDependentResources()
        {
            Guid iid;
            var device = _deviceResources.D3DDevice;

            // Create a root signature with a single constant buffer slot.
            {
                Unsafe.SkipInit(out D3D12_DESCRIPTOR_RANGE range);
                Unsafe.SkipInit(out D3D12_ROOT_PARAMETER parameter);

                range.Init(D3D12_DESCRIPTOR_RANGE_TYPE_CBV, 1, 0);
                parameter.InitAsDescriptorTable(1, &range, D3D12_SHADER_VISIBILITY_VERTEX);

                var rootSignatureFlags =
                    D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT | // Only the input assembler stage needs access to the constant buffer.
                    D3D12_ROOT_SIGNATURE_FLAG_DENY_DOMAIN_SHADER_ROOT_ACCESS |
                    D3D12_ROOT_SIGNATURE_FLAG_DENY_GEOMETRY_SHADER_ROOT_ACCESS |
                    D3D12_ROOT_SIGNATURE_FLAG_DENY_HULL_SHADER_ROOT_ACCESS |
                    D3D12_ROOT_SIGNATURE_FLAG_DENY_PIXEL_SHADER_ROOT_ACCESS;

                Unsafe.SkipInit(out D3D12_ROOT_SIGNATURE_DESC descRootSignature);
                descRootSignature.Init(1, &parameter, 0, null, rootSignatureFlags);

                ID3DBlob* pSignature = null;
                ID3DBlob* pError = null;

                try
                {
                    ThrowIfFailed(nameof(D3D12SerializeRootSignature), D3D12SerializeRootSignature(&descRootSignature, D3D_ROOT_SIGNATURE_VERSION_1, &pSignature, &pError));

                    ID3D12RootSignature* rootSignature;
                    iid = IID_ID3D12RootSignature;
                    ThrowIfFailed(nameof(ID3D12Device.CreateRootSignature), device->CreateRootSignature(0, pSignature->GetBufferPointer(), pSignature->GetBufferSize(), &iid, (void**)&rootSignature));
                    NameD3D12Object(RootSignature = rootSignature, nameof(RootSignature));
                }
                finally
                {
                    if (pError != null)
                    {
                        pError->Release();
                    }

                    if (pSignature != null)
                    {
                        pSignature->Release();
                    }
                }
            }

            {
                ID3DBlob* vertexShader = null;
                ID3DBlob* pixelShader = null;

                try
                {
#if DEBUG
                    // Enable better shader debugging with the graphics debugging tools.
                    uint compileFlags = D3DCOMPILE_DEBUG | D3DCOMPILE_SKIP_OPTIMIZATION;
#else
                    uint compileFlags = 0u;
#endif
                    fixed (char* pixelShaderFileName = @"Content\SamplePixelShader.hlsl")
                    fixed (char* vertexShaderFileName = @"Content\SampleVertexShader.hlsl")
                    {
                        var entryPoint = 0x00006E69614D5356;    // VSMain
                        var target = 0x0000305F355F7376;        // vs_5_0
                        ThrowIfFailed(nameof(D3DCompileFromFile), D3DCompileFromFile(vertexShaderFileName, null, null, (sbyte*)&entryPoint, (sbyte*)&target, compileFlags, 0, &vertexShader, null));

                        entryPoint = 0x00006E69614D5350;        // PSMain
                        target = 0x0000305F355F7370;            // ps_5_0
                        ThrowIfFailed(nameof(D3DCompileFromFile), D3DCompileFromFile(pixelShaderFileName, null, null, (sbyte*)&entryPoint, (sbyte*)&target, compileFlags, 0, &pixelShader, null));
                    }

                    // Create the pipeline state once the shaders are loaded.
                    var inputElementDescs = stackalloc D3D12_INPUT_ELEMENT_DESC[2];
                    {
                        var semanticName0 = stackalloc sbyte[9];
                        {
                            ((ulong*)semanticName0)[0] = 0x4E4F495449534F50;      // POSITION
                        }
                        inputElementDescs[0] = new D3D12_INPUT_ELEMENT_DESC
                        {
                            SemanticName = semanticName0,
                            SemanticIndex = 0,
                            Format = DXGI_FORMAT_R32G32B32_FLOAT,
                            InputSlot = 0,
                            AlignedByteOffset = 0,
                            InputSlotClass = D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                            InstanceDataStepRate = 0
                        };

                        var semanticName1 = 0x000000524F4C4F43;                     // COLOR
                        inputElementDescs[1] = new D3D12_INPUT_ELEMENT_DESC
                        {
                            SemanticName = (sbyte*)&semanticName1,
                            SemanticIndex = 0,
                            Format = DXGI_FORMAT_R32G32B32A32_FLOAT,
                            InputSlot = 0,
                            AlignedByteOffset = 12,
                            InputSlotClass = D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                            InstanceDataStepRate = 0
                        };
                    }

                    var state = new D3D12_GRAPHICS_PIPELINE_STATE_DESC
                    {
                        InputLayout = new D3D12_INPUT_LAYOUT_DESC
                        {
                            pInputElementDescs = inputElementDescs,
                            NumElements = 2
                        },
                        pRootSignature = RootSignature,
                        VS = new D3D12_SHADER_BYTECODE(vertexShader),
                        PS = new D3D12_SHADER_BYTECODE(pixelShader),
                        RasterizerState = D3D12_RASTERIZER_DESC.DEFAULT,
                        BlendState = D3D12_BLEND_DESC.DEFAULT,
                        DepthStencilState = D3D12_DEPTH_STENCIL_DESC.DEFAULT,
                        SampleMask = uint.MaxValue,
                        PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE,
                        NumRenderTargets = 1
                    };

                    state.RTVFormats[0] = _deviceResources.BackBufferFormat;
                    state.DSVFormat = _deviceResources.DepthBufferFormat;
                    state.SampleDesc.Count = 1;

                    ID3D12PipelineState* pipelineState;
                    iid = IID_ID3D12PipelineState;
                    ThrowIfFailed(nameof(ID3D12Device.CreateGraphicsPipelineState), _deviceResources.D3DDevice->CreateGraphicsPipelineState(&state, &iid, (void**)&pipelineState));
                    PipelineState = pipelineState;
                }
                finally
                {
                    // Shader data can be deleted once the pipeline state is created.

                    if (pixelShader != null)
                    {
                        pixelShader->Release();
                    }

                    if (vertexShader != null)
                    {
                        vertexShader->Release();
                    }
                }
            }

            // Create and upload cube geometry resources to the GPU.
            {
                ID3D12Resource* vertexBufferUpload = null;
                ID3D12Resource* indexBufferUpload = null;

                try
                {
                    // Create a command list.
                    ID3D12GraphicsCommandList* commandList;
                    iid = IID_ID3D12GraphicsCommandList;
                    ThrowIfFailed(nameof(ID3D12Device.CreateCommandList), device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, _deviceResources.CommandAllocator, PipelineState, &iid, (void**)&commandList));
                    NameD3D12Object(CommandList = commandList, nameof(CommandList));

                    // Cube vertices. Each vertex has a position and a color.
                    var cubeVertices = stackalloc VertexPositionColor[8]
                    {
                        new VertexPositionColor { pos = new Vector3(-0.5f, -0.5f, -0.5f), color = new Vector3(0.0f, 0.0f, 0.0f) },
                        new VertexPositionColor { pos = new Vector3(-0.5f, -0.5f, 0.5f), color = new Vector3(0.0f, 0.0f, 1.0f) },
                        new VertexPositionColor { pos = new Vector3(-0.5f, 0.5f, -0.5f), color = new Vector3(0.0f, 1.0f, 0.0f) },
                        new VertexPositionColor { pos = new Vector3(-0.5f, 0.5f, 0.5f), color = new Vector3(0.0f, 1.0f, 1.0f) },
                        new VertexPositionColor { pos = new Vector3(0.5f, -0.5f, -0.5f), color = new Vector3(1.0f, 0.0f, 0.0f) },
                        new VertexPositionColor { pos = new Vector3(0.5f, -0.5f, 0.5f), color = new Vector3(1.0f, 0.0f, 1.0f) },
                        new VertexPositionColor { pos = new Vector3(0.5f, 0.5f, -0.5f), color = new Vector3(1.0f, 1.0f, 0.0f) },
                        new VertexPositionColor { pos = new Vector3(0.5f, 0.5f, 0.5f), color = new Vector3(1.0f, 1.0f, 1.0f) },
                    };

                    uint vertexBufferSize = (uint)(Unsafe.SizeOf<VertexPositionColor>() * 8);

                    // Create the vertex buffer resource in the GPU's default heap and copy vertex data into it using the upload heap.
                    // The upload resource must not be released until after the GPU has finished using it.

                    var defaultHeapProperties = new D3D12_HEAP_PROPERTIES(D3D12_HEAP_TYPE_DEFAULT);
                    var vertexBufferDesc = D3D12_RESOURCE_DESC.Buffer(vertexBufferSize);

                    ID3D12Resource* vertexBuffer;
                    iid = IID_ID3D12Resource;
                    ThrowIfFailed(nameof(ID3D12Device.CreateCommittedResource), device->CreateCommittedResource(
                        &defaultHeapProperties,
                        D3D12_HEAP_FLAG_NONE,
                        &vertexBufferDesc,
                        D3D12_RESOURCE_STATE_COPY_DEST,
                        null,
                        &iid,
                        (void**)&vertexBuffer
                    ));

                    var uploadHeapProperties = new D3D12_HEAP_PROPERTIES(D3D12_HEAP_TYPE_UPLOAD);

                    iid = IID_ID3D12Resource;
                    ThrowIfFailed(nameof(ID3D12Device.CreateCommittedResource), device->CreateCommittedResource(
                        &uploadHeapProperties,
                        D3D12_HEAP_FLAG_NONE,
                        &vertexBufferDesc,
                        D3D12_RESOURCE_STATE_GENERIC_READ,
                        null,
                        &iid,
                        (void**)&vertexBufferUpload
                    ));
                    NameD3D12Object(VertexBuffer = vertexBuffer, nameof(VertexBuffer));

                    // Upload the vertex buffer to the GPU.
                    {
                        var vertexData = new D3D12_SUBRESOURCE_DATA
                        {
                            pData = cubeVertices,
                            RowPitch = (IntPtr)vertexBufferSize,
                            SlicePitch = (IntPtr)vertexBufferSize
                        };

                        UpdateSubresources(CommandList, VertexBuffer, vertexBufferUpload, 0, 0, 1, &vertexData);

                        var vertexBufferResourceBarrier = D3D12_RESOURCE_BARRIER.InitTransition(VertexBuffer, D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER);
                        CommandList->ResourceBarrier(1, &vertexBufferResourceBarrier);
                    }

                    // Load mesh indices. Each trio of indices represents a triangle to be rendered on the screen.
                    // For example: 0,2,1 means that the vertices with indexes 0, 2 and 1 from the vertex buffer compose the
                    // first triangle of this mesh.
                    ushort* cubeIndices = stackalloc ushort[36] {
                        0,
                        2,
                        1, // -x
                        1,
                        2,
                        3,

                        4,
                        5,
                        6, // +x
                        5,
                        7,
                        6,

                        0,
                        1,
                        5, // -y
                        0,
                        5,
                        4,

                        2,
                        6,
                        7, // +y
                        2,
                        7,
                        3,

                        0,
                        4,
                        6, // -z
                        0,
                        6,
                        2,

                        1,
                        3,
                        7, // +z
                        1,
                        7,
                        5,
                    };

                    uint indexBufferSize = sizeof(ushort) * 36;

                    // Create the index buffer resource in the GPU's default heap and copy index data into it using the upload heap.
                    // The upload resource must not be released until after the GPU has finished using it.

                    var indexBufferDesc = D3D12_RESOURCE_DESC.Buffer(indexBufferSize);

                    ID3D12Resource* indexBuffer;
                    iid = IID_ID3D12Resource;
                    ThrowIfFailed(nameof(ID3D12Device.CreateCommittedResource), device->CreateCommittedResource(
                        &defaultHeapProperties,
                        D3D12_HEAP_FLAG_NONE,
                        &indexBufferDesc,
                        D3D12_RESOURCE_STATE_COPY_DEST,
                        null,
                        &iid,
                        (void**)&indexBuffer
                    ));

                    iid = IID_ID3D12Resource;
                    ThrowIfFailed(nameof(ID3D12Device.CreateCommittedResource), device->CreateCommittedResource(
                        &uploadHeapProperties,
                        D3D12_HEAP_FLAG_NONE,
                        &indexBufferDesc,
                        D3D12_RESOURCE_STATE_GENERIC_READ,
                        null,
                        &iid,
                        (void**)&indexBufferUpload
                    ));
                    NameD3D12Object(IndexBuffer = indexBuffer, nameof(IndexBuffer));

                    // Upload the index buffer to the GPU.
                    {
                        var indexData = new D3D12_SUBRESOURCE_DATA
                        {
                            pData = (byte*)cubeIndices,
                            RowPitch = (IntPtr)indexBufferSize,
                            SlicePitch = (IntPtr)indexBufferSize
                        };

                        UpdateSubresources(CommandList, IndexBuffer, indexBufferUpload, 0, 0, 1, &indexData);

                        var indexBufferResourceBarrier = D3D12_RESOURCE_BARRIER.InitTransition(IndexBuffer, D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_INDEX_BUFFER);
                        CommandList->ResourceBarrier(1, &indexBufferResourceBarrier);
                    }

                    // Create a descriptor heap for the constant buffers.
                    {
                        var heapDesc = new D3D12_DESCRIPTOR_HEAP_DESC
                        {
                            NumDescriptors = FrameCount,
                            Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,
                            // This flag indicates that this descriptor heap can be bound to the pipeline and that descriptors contained in it can be referenced by a root table.
                            Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE
                        };

                        ID3D12DescriptorHeap* cbvHeap;
                        iid = IID_ID3D12DescriptorHeap;
                        ThrowIfFailed(nameof(ID3D12Device.CreateDescriptorHeap), device->CreateDescriptorHeap(&heapDesc, &iid, (void**)&cbvHeap));
                        NameD3D12Object(CbvHeap = cbvHeap, nameof(CbvHeap));
                    }

                    var constantBufferDesc = D3D12_RESOURCE_DESC.Buffer(FrameCount * AlignedConstantBufferSize);

                    ID3D12Resource* constantBuffer;
                    iid = IID_ID3D12Resource;
                    ThrowIfFailed(nameof(ID3D12Device.CreateCommittedResource), device->CreateCommittedResource(
                        &uploadHeapProperties,
                        D3D12_HEAP_FLAG_NONE,
                        &constantBufferDesc,
                        D3D12_RESOURCE_STATE_GENERIC_READ,
                        null,
                        &iid,
                        (void**)&constantBuffer
                    ));
                    NameD3D12Object(ConstantBuffer = constantBuffer, nameof(ConstantBuffer));

                    // Create constant buffer views to access the upload buffer.
                    var cbvGpuAddress = ConstantBuffer->GetGPUVirtualAddress();
                    var cbvCpuHandle = CbvHeap->GetCPUDescriptorHandleForHeapStart();
                    _cbvDescriptorSize = device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);

                    for (int n = 0; n < FrameCount; n++)
                    {
                        var desc = new D3D12_CONSTANT_BUFFER_VIEW_DESC
                        {
                            BufferLocation = cbvGpuAddress,
                            SizeInBytes = AlignedConstantBufferSize
                        };
                        device->CreateConstantBufferView(&desc, cbvCpuHandle);

                        cbvGpuAddress += desc.SizeInBytes;
                        cbvCpuHandle.Offset((int)(_cbvDescriptorSize));
                    }

                    // Map the constant buffers.
                    var readRange = new D3D12_RANGE(UIntPtr.Zero, UIntPtr.Zero);    // We do not intend to read from this resource on the CPU.
                    fixed (byte** mappedConstantBuffer = &_mappedConstantBuffer)
                    {
                        ThrowIfFailed(nameof(ID3D12Resource.Map), ConstantBuffer->Map(0, &readRange, (void**)mappedConstantBuffer));
                        Unsafe.InitBlock(_mappedConstantBuffer, 0, FrameCount * AlignedConstantBufferSize);
                    }
                    // We don't unmap this until the app closes. Keeping things mapped for the lifetime of the resource is okay.

                    // Close the command list and execute it to begin the vertex/index buffer copy into the GPU's default heap.
                    ThrowIfFailed(nameof(ID3D12GraphicsCommandList.Close), CommandList->Close());
                    ID3D12CommandList** ppCommandLists = stackalloc ID3D12CommandList*[1] { (ID3D12CommandList*)CommandList };
                    _deviceResources.CommandQueue->ExecuteCommandLists(1, ppCommandLists);

                    // Create vertex/index buffer views.
                    _vertexBufferView.BufferLocation = VertexBuffer->GetGPUVirtualAddress();
                    _vertexBufferView.StrideInBytes = (uint)Unsafe.SizeOf<VertexPositionColor>();
                    _vertexBufferView.SizeInBytes = vertexBufferSize;

                    _indexBufferView.BufferLocation = IndexBuffer->GetGPUVirtualAddress();
                    _indexBufferView.SizeInBytes = indexBufferSize;
                    _indexBufferView.Format = DXGI_FORMAT_R16_UINT;

                    // Wait for the command list to finish executing; the vertex/index buffers need to be uploaded to the GPU before the upload resources go out of scope.
                    _deviceResources.WaitForGpu();
                }
                finally
                {
                    if (indexBufferUpload != null)
                    {
                        indexBufferUpload->Release();
                    }

                    if (vertexBufferUpload != null)
                    {
                        vertexBufferUpload->Release();
                    }
                }
            }

            _loadingComplete = true;
        }

        // Initializes view parameters when the window size changes.
        public void CreateWindowSizeDependentResources()
        {
            var outputSize = _deviceResources.OutputSize;
            float aspectRatio = (float)(outputSize.Width / outputSize.Height);
            float fovAngleY = 70.0f * MathF.PI / 180.0f;

            var viewport = _deviceResources.ScreenViewport;
            _scissorRect = new RECT
            {
                left = 0,
                top = 0,
                right = (int)viewport.Width,
                bottom = (int)viewport.Height
            };

            // This is a simple example of change that can be made when the app is in
            // portrait or snapped view.
            if (aspectRatio < 1.0f)
            {
                fovAngleY *= 2.0f;
            }

            // Note that the OrientationTransform3D matrix is post-multiplied here
            // in order to correctly orient the scene to match the display orientation.
            // This post-multiplication step is required for any draw calls that are
            // made to the swap chain render target. For draw calls to other targets,
            // this transform should not be applied.

            // This sample makes use of a right-handed coordinate system using row-major matrices.
            var perspective = Matrix4x4.CreatePerspectiveFieldOfView(
                fovAngleY,
                aspectRatio,
                0.01f,
                100.0f
            );

            var orientation = _deviceResources.OrientationTransform3D;

            _constantBufferData.projection = Matrix4x4.Transpose(perspective * orientation);

            // Eye is at (0,0.7,1.5), looking at point (0,-0.1,0) with the up-vector along the y-axis.
            var eye = new Vector3(0.0f, 0.7f, 1.5f);
            var at = new Vector3(0.0f, -0.1f, 0.0f);
            var up = new Vector3(0.0f, 1.0f, 0.0f);

            _constantBufferData.view = Matrix4x4.Transpose(Matrix4x4.CreateLookAt(eye, at, up));
        }

        public void Update(StepTimer timer)
        {
            if (_loadingComplete)
            {
                if (!_tracking)
                {
                    // Rotate the cube a small amount.
                    _angle += 1 / 60.0f * _radiansPerSecond;
                    Rotate(_angle);
                }

                // Update the constant buffer resource.
                byte* destination = _mappedConstantBuffer + (_deviceResources.CurrentFrameIndex * AlignedConstantBufferSize);

                fixed (ModelViewProjectionConstantBuffer* constantBufferData = &_constantBufferData)
                {
                    Buffer.MemoryCopy(constantBufferData, destination, Unsafe.SizeOf<ModelViewProjectionConstantBuffer>(), Unsafe.SizeOf<ModelViewProjectionConstantBuffer>());
                }
            }
        }

        public bool Render()
        {
            // Loading is asynchronous. Only draw geometry after it's loaded.
            if (!_loadingComplete)
            {
                return false;
            }

            ThrowIfFailed(nameof(ID3D12CommandAllocator.Reset), _deviceResources.CommandAllocator->Reset());

            // The command list can be reset anytime after ExecuteCommandList() is called.
            ThrowIfFailed(nameof(ID3D12GraphicsCommandList.Reset), CommandList->Reset(_deviceResources.CommandAllocator, PipelineState));

            PIXBeginEvent(CommandList, 0, "Draw the cube");
            {
                // Set the graphics root signature and descriptor heaps to be used by this frame.
                CommandList->SetGraphicsRootSignature(RootSignature);
                var ppHeaps = stackalloc ID3D12DescriptorHeap*[1] { CbvHeap };
                CommandList->SetDescriptorHeaps(1, ppHeaps);

                // Bind the current frame's constant buffer to the pipeline.
                var gpuHandle = CbvHeap->GetGPUDescriptorHandleForHeapStart();
                gpuHandle.Offset((int)_deviceResources.CurrentFrameIndex, _cbvDescriptorSize);
                CommandList->SetGraphicsRootDescriptorTable(0, gpuHandle);

                // Set the viewport and scissor rectangle.
                fixed (RECT* scissorRect = &_scissorRect)
                {
                    var viewport = _deviceResources.ScreenViewport;
                    CommandList->RSSetViewports(1, &viewport);
                    CommandList->RSSetScissorRects(1, scissorRect);
                }

                // Indicate this resource will be in use as a render target.
                var renderTargetResourceBarrier = D3D12_RESOURCE_BARRIER.InitTransition(_deviceResources.RenderTarget, D3D12_RESOURCE_STATE_PRESENT, D3D12_RESOURCE_STATE_RENDER_TARGET);
                CommandList->ResourceBarrier(1, &renderTargetResourceBarrier);

                // Record drawing commands.
                var renderTargetView = _deviceResources.RenderTargetView;
                var depthStencilView = _deviceResources.DepthStencilView;

                float* cornflowerBlue = stackalloc float[] { 0.392156899f, 0.584313750f, 0.929411829f, 1.000000000f };
                CommandList->ClearRenderTargetView(renderTargetView, cornflowerBlue, 0, null);
                CommandList->ClearDepthStencilView(depthStencilView, D3D12_CLEAR_FLAG_DEPTH, 1.0f, 0, 0, null);

                CommandList->OMSetRenderTargets(1, &renderTargetView, 0, &depthStencilView);

                CommandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);

                fixed (D3D12_VERTEX_BUFFER_VIEW* vertexBufferView = &_vertexBufferView)
                {
                    CommandList->IASetVertexBuffers(0, 1, vertexBufferView);
                }

                fixed (D3D12_INDEX_BUFFER_VIEW* indexBufferView = &_indexBufferView)
                {
                    CommandList->IASetIndexBuffer(indexBufferView);
                }

                CommandList->DrawIndexedInstanced(36, 1, 0, 0, 0);

                // Indicate that the render target will now be used to present when the command list is done executing.
                var presentResourceBarrier = D3D12_RESOURCE_BARRIER.InitTransition(_deviceResources.RenderTarget, D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_PRESENT);
                CommandList->ResourceBarrier(1, &presentResourceBarrier);
            }
            PIXEndEvent(CommandList);

            ThrowIfFailed(nameof(ID3D12GraphicsCommandList.Close), CommandList->Close());

            // Execute the command list.
            ID3D12CommandList** ppCommandLists = stackalloc ID3D12CommandList*[] { (ID3D12CommandList*)CommandList };
            _deviceResources.CommandQueue->ExecuteCommandLists(1, ppCommandLists);

            return true;
        }

        // Saves the current state of the renderer.
        public void SaveState()
        {
            var state = ApplicationData.Current.LocalSettings.Values;

            if (state.ContainsKey(AngleKey))
            {
                state.Remove(AngleKey);
            }
            if (state.ContainsKey(TrackingKey))
            {
                state.Remove(TrackingKey);
            }

            state.Add(AngleKey, _angle);
            state.Add(TrackingKey, _tracking);
        }

        public void StartTracking()
        {
            _tracking = true;
        }

        // When tracking, the 3D cube can be rotated around its Y axis by tracking pointer position relative to the output screen width.
        public void TrackingUpdate(float positionX)
        {
            if (_tracking)
            {
                float radians = MathF.PI * 2 * 2.0f * positionX / (float)_deviceResources.OutputSize.Width;
                Rotate(radians);
            }
        }

        public void StopTracking()
        {
            _tracking = false;
        }

        // Restores the previous state of the renderer.
        private void LoadState()
        {
            var state = ApplicationData.Current.LocalSettings.Values;

            if (state.ContainsKey(AngleKey))
            {
                _angle = (float)state[AngleKey];
                state.Remove(AngleKey);
            }
            if (state.ContainsKey(TrackingKey))
            {
                _tracking = (bool)state[TrackingKey];
                state.Remove(TrackingKey);
            }
        }

        // Rotate the 3D cube model a set amount of radians.
        private void Rotate(float radians)
        {
            // Prepare to pass the updated model matrix to the shader.
            _constantBufferData.model = Matrix4x4.Transpose(Matrix4x4.CreateRotationY(radians));
        }
        #endregion

        #region System.IDisposable
        public void Dispose()
        {
            ConstantBuffer->Unmap(0, null);
            _mappedConstantBuffer = null;

            ConstantBuffer = null;
            CbvHeap = null;
            IndexBuffer = null;
            VertexBuffer = null;
            CommandList = null;
            PipelineState = null;
            RootSignature = null;
        }
        #endregion
    }
}
