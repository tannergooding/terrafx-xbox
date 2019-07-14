using System;
using System.Numerics;
using System.Runtime.InteropServices;
using TerraFX.Interop;
using Windows.Foundation;
using Windows.Graphics.Display;
using Windows.UI.Core;
using static TerraFX.Interop.D3D_FEATURE_LEVEL;
using static TerraFX.Interop.D3D12;
using static TerraFX.Interop.D3D12_COMMAND_LIST_TYPE;
using static TerraFX.Interop.D3D12_COMMAND_QUEUE_FLAGS;
using static TerraFX.Interop.D3D12_DESCRIPTOR_HEAP_FLAGS;
using static TerraFX.Interop.D3D12_DESCRIPTOR_HEAP_TYPE;
using static TerraFX.Interop.D3D12_DSV_DIMENSION;
using static TerraFX.Interop.D3D12_DSV_FLAGS;
using static TerraFX.Interop.D3D12_FENCE_FLAGS;
using static TerraFX.Interop.D3D12_HEAP_FLAGS;
using static TerraFX.Interop.D3D12_HEAP_TYPE;
using static TerraFX.Interop.D3D12_RESOURCE_FLAGS;
using static TerraFX.Interop.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.DX;
using static TerraFX.Interop.DXGI;
using static TerraFX.Interop.DXGI_ADAPTER_FLAG;
using static TerraFX.Interop.DXGI_ALPHA_MODE;
using static TerraFX.Interop.DXGI_FORMAT;
using static TerraFX.Interop.DXGI_MODE_ROTATION;
using static TerraFX.Interop.DXGI_SCALING;
using static TerraFX.Interop.DXGI_SWAP_EFFECT;
using static TerraFX.Interop.Kernel32;
using static TerraFX.Interop.Windows;
using static TerraFX.Utilities.ExceptionUtilities;

namespace TestApp
{
    // Controls all the DirectX device resources.
    public unsafe class DeviceResources : IDisposable
    {
        #region Constants
        public const uint FrameCount = 2;   // Use double buffering.
        #endregion

        #region Fields
        private uint _currentFrame;

        // Direct3D objects.
        private ID3D12Device* _d3dDevice;
        private IDXGIFactory4* _dxgiFactory;
        private IDXGISwapChain3* _swapChain;
        private RenderTargets_e__FixedBuffer _renderTargets;
        private ID3D12Resource* _depthStencil;
        private ID3D12DescriptorHeap* _rtvHeap;
        private ID3D12DescriptorHeap* _dsvHeap;
        private ID3D12CommandQueue* _commandQueue;
        private CommandAllocators_e__FixedBuffer _commandAllocators;
        private DXGI_FORMAT _backBufferFormat;
        private DXGI_FORMAT _depthBufferFormat;
        private D3D12_VIEWPORT _screenViewport;
        private uint _rtvDescriptorSize;
        private bool _deviceRemoved;

        // CPU/GPU Synchronization.
        private ID3D12Fence* _fence;
        private FenceValues_e__FixedBuffer _fenceValues;
        private IntPtr _fenceEvent;

        // Cached reference to the Window.
        private CoreWindow _window;

        // Cached device properties.
        private Size _d3dRenderTargetSize;
        private Size _outputSize;
        private Size _logicalSize;
        private DisplayOrientations _nativeOrientation;
        private DisplayOrientations _currentOrientation;
        private float _dpi;

        // This is the DPI that will be reported back to the app. It takes into account whether the app supports high resolution screens or not.
        private float _effectiveDpi;

        // Transforms used for display orientation.
        private Matrix4x4 _orientationTransform3D;
        #endregion

        #region Constructors
        // Constructor for DeviceResources.
        public DeviceResources(DXGI_FORMAT backBufferFormat = DXGI_FORMAT_B8G8R8A8_UNORM, DXGI_FORMAT depthBufferFormat = DXGI_FORMAT_D32_FLOAT)
        {
            _currentFrame = 0;
            _screenViewport = default;
            _rtvDescriptorSize = 0;
            _fenceEvent = IntPtr.Zero;
            _backBufferFormat = backBufferFormat;
            _depthBufferFormat = depthBufferFormat;
            _fenceValues = default;
            _d3dRenderTargetSize = default;
            _outputSize = default;
            _logicalSize = default;
            _nativeOrientation = DisplayOrientations.None;
            _currentOrientation = DisplayOrientations.None;
            _dpi = -1.0f;
            _effectiveDpi = -1.0f;
            _deviceRemoved = false;
   
            CreateDeviceIndependentResources();
            CreateDeviceResources();
        }

        ~DeviceResources()
        {
            Dispose();
        }
        #endregion

        #region Properties
        public float Dpi
        {
            get
            {
                return _effectiveDpi;
            }

            // This method is called in the event handler for the DpiChanged event.
            set
            {
                if (_dpi != value)
                {
                    _dpi = value;

                    // When the display DPI changes, the logical size of the window (measured in Dips) also changes and needs to be updated.
                    _logicalSize = new Size(_window.Bounds.Width, _window.Bounds.Height);

                    CreateWindowSizeDependentResources();
                }
            }
        }

        // The size of the render target, in dips.
        public Size LogicalSize
        {
            get
            {
                return _logicalSize;
            }

            // This method is called in the event handler for the SizeChanged event.
            set
            {
                if (_logicalSize != value)
                {
                    _logicalSize = value;
                    CreateWindowSizeDependentResources();
                }
            }
        }

        // The size of the render target, in pixels.
        public Size OutputSize
        {
            get
            {
                return _outputSize;
            }
        }

        public bool IsDeviceRemoved
        {
            get
            {
                return _deviceRemoved;
            }
        }

        // D3D Accessors.
        public ID3D12Device* D3DDevice
        {
            get
            {
                return _d3dDevice;
            }

            set
            {
                if (_d3dDevice != null)
                {
                    _d3dDevice->Release();
                }
                _d3dDevice = value;
            }
        }

        public IDXGISwapChain3* SwapChain
        {
            get
            {
                return _swapChain;
            }

            set
            {
                if (_swapChain != null)
                {
                    _swapChain->Release();
                }
                _swapChain = value;
            }
        }

        public ID3D12Resource* RenderTarget
        {
            get
            {
                return _renderTargets[(int)_currentFrame];
            }
        }

        public ID3D12Resource* DepthStencil
        {
            get
            {
                return _depthStencil;
            }

            set
            {
                if (_depthStencil != null)
                {
                    _depthStencil->Release();
                }
                _depthStencil = value;
            }
        }

        public ID3D12CommandQueue* CommandQueue
        {
            get
            {
                return _commandQueue;
            }

            set
            {
                if (_commandQueue != null)
                {
                    _commandQueue->Release();
                }
                _commandQueue = value;
            }
        }

        public ID3D12CommandAllocator* CommandAllocator
        {
            get
            {
                return _commandAllocators[(int)_currentFrame];
            }
        }

        public DXGI_FORMAT BackBufferFormat
        {
            get
            {
                return _backBufferFormat;
            }
        }

        public DXGI_FORMAT DepthBufferFormat
        {
            get
            {
                return _depthBufferFormat;
            }
        }

        public D3D12_VIEWPORT ScreenViewport
        {
            get
            {
                return _screenViewport;
            }
        }

        public Matrix4x4 OrientationTransform3D
        {
            get
            {
                return _orientationTransform3D;
            }
        }

        public uint CurrentFrameIndex
        {
            get
            {
                return _currentFrame;
            }
        }

        public D3D12_CPU_DESCRIPTOR_HANDLE RenderTargetView
        {
            get
            {
                var result = RtvHeap->GetCPUDescriptorHandleForHeapStart();
                result.ptr = (UIntPtr)((byte*)result.ptr + _currentFrame * _rtvDescriptorSize);
                return result;
            }
        }

        public D3D12_CPU_DESCRIPTOR_HANDLE DepthStencilView
        {
            get
            {
                return DsvHeap->GetCPUDescriptorHandleForHeapStart();
            }
        }

        public DisplayOrientations CurrentOrientation
        {
            get
            {
                return _currentOrientation;
            }

            // This method is called in the event handler for the OrientationChanged event.
            set
            {
                if (_currentOrientation != value)
                {
                    _currentOrientation = value;
                    CreateWindowSizeDependentResources();
                }
            }
        }

        public CoreWindow Window
        {
            get
            {
                return _window;
            }

            // This method is called when the CoreWindow is created (or re-created).
            set
            {
                var currentDisplayInformation = DisplayInformation.GetForCurrentView();

                _window = value;
                _logicalSize = new Size(value.Bounds.Width, value.Bounds.Height);
                _nativeOrientation = currentDisplayInformation.NativeOrientation;
                _currentOrientation = currentDisplayInformation.CurrentOrientation;
                _dpi = currentDisplayInformation.LogicalDpi;

                CreateWindowSizeDependentResources();
            }
        }

        private IDXGIFactory4* DxgiFactory
        {
            get
            {
                return _dxgiFactory;
            }

            set
            {
                if (_dxgiFactory != null)
                {
                    _dxgiFactory->Release();
                }
                _dxgiFactory = value;
            }
        }

        private ID3D12DescriptorHeap* RtvHeap
        {
            get
            {
                return _rtvHeap;
            }

            set
            {
                if (_rtvHeap != null)
                {
                    _rtvHeap->Release();
                }
                _rtvHeap = value;
            }
        }

        private ID3D12DescriptorHeap* DsvHeap
        {
            get
            {
                return _dsvHeap;
            }

            set
            {
                if (_dsvHeap != null)
                {
                    _dsvHeap->Release();
                }
                _dsvHeap = value;
            }
        }

        private ID3D12Fence* Fence
        {
            get
            {
                return _fence;
            }

            set
            {
                if (_fence != null)
                {
                    _fence->Release();
                }
                _fence = value;
            }
        }
        #endregion

        #region Methods
        // This method is called in the event handler for the DisplayContentsInvalidated event.
        public void ValidateDevice()
        {
            // The D3D Device is no longer valid if the default adapter changed since the device
            // was created or if the device has been removed.

            // First, get the LUID for the default adapter from when the device was created.

            DXGI_ADAPTER_DESC previousDesc;
            {
                IDXGIAdapter1* previousDefaultAdapter = null;

                try
                {
                    ThrowIfFailed(nameof(IDXGIFactory1.EnumAdapters1), DxgiFactory->EnumAdapters1(0, &previousDefaultAdapter));

                    ThrowIfFailed(nameof(IDXGIAdapter.GetDesc), previousDefaultAdapter->GetDesc(&previousDesc));
                }
                finally
                {
                    if (previousDefaultAdapter != null)
                    {
                        previousDefaultAdapter->Release();
                    }
                }
            }

            // Next, get the information for the current default adapter.

            DXGI_ADAPTER_DESC currentDesc;
            {
                IDXGIFactory4* currentDxgiFactory = null;
                IDXGIAdapter1* currentDefaultAdapter = null;

                try
                {
                    var iid = IID_IDXGIFactory4;
                    ThrowIfFailed(nameof(CreateDXGIFactory1), CreateDXGIFactory1(&iid, (void**)&currentDxgiFactory));

                    ThrowIfFailed(nameof(IDXGIFactory1.EnumAdapters1), currentDxgiFactory->EnumAdapters1(0, &currentDefaultAdapter));

                    ThrowIfFailed(nameof(IDXGIAdapter.GetDesc), currentDefaultAdapter->GetDesc(&currentDesc));
                }
                finally
                {
                    if (currentDefaultAdapter != null)
                    {
                        currentDefaultAdapter->Release();
                    }

                    if (currentDxgiFactory != null)
                    {
                        currentDxgiFactory->Release();
                    }
                }
            }

            // If the adapter LUIDs don't match, or if the device reports that it has been removed,
            // a new D3D device must be created.

            if (previousDesc.AdapterLuid.LowPart != currentDesc.AdapterLuid.LowPart ||
                previousDesc.AdapterLuid.HighPart != currentDesc.AdapterLuid.HighPart ||
                FAILED(D3DDevice->GetDeviceRemovedReason()))
            {
                _deviceRemoved = true;
            }
        }

        // Present the contents of the swap chain to the screen.
        public void Present()
        {
            // The first argument instructs DXGI to block until VSync, putting the application
            // to sleep until the next VSync. This ensures we don't waste any cycles rendering
            // frames that will never be displayed to the screen.
            int hr = SwapChain->Present(1, 0);

            // If the device was removed either by a disconnection or a driver upgrade, we
            // must recreate all device resources.
            if (hr == DXGI_ERROR_DEVICE_REMOVED || hr == DXGI_ERROR_DEVICE_RESET)
            {
                _deviceRemoved = true;
            }
            else
            {
                ThrowIfFailed(nameof(IDXGISwapChain.Present), hr);

                MoveToNextFrame();
            }
        }

        // Wait for pending GPU work to complete.
        public void WaitForGpu()
        {
            // Schedule a Signal command in the queue.
            ThrowIfFailed(nameof(ID3D12CommandQueue.Signal), CommandQueue->Signal(Fence, _fenceValues[(int)_currentFrame]));

            // Wait until the fence has been crossed.
            ThrowIfFailed(nameof(ID3D12Fence.SetEventOnCompletion), Fence->SetEventOnCompletion(_fenceValues[(int)_currentFrame], _fenceEvent));
            WaitForSingleObject(_fenceEvent, INFINITE);

            // Increment the fence value for the current frame.
            _fenceValues[(int)_currentFrame]++;
        }

        // Configures resources that don't depend on the Direct3D device.
        private void CreateDeviceIndependentResources()
        {
        }

        // Configures the Direct3D device, and stores handles to it and the device context.
        private void CreateDeviceResources()
        {
            Guid iid;
            var dxgiFactoryFlags = 0u;

#if DEBUG
            // If the project is in a debug build, enable debugging via SDK Layers.
            {
                ID3D12Debug* debugController = null;

                try
                {
                    iid = IID_ID3D12Debug;
                    if (SUCCEEDED(D3D12GetDebugInterface(&iid, (void**)&debugController)))
                    {
                        debugController->EnableDebugLayer();

                        // Enable additional debug layers.
                        dxgiFactoryFlags |= DXGI_CREATE_FACTORY_DEBUG;
                    }
                }
                finally
                {
                    if (debugController != null)
                    {
                        debugController->Release();
                    }
                }
            }
#endif

            IDXGIAdapter1* adapter = null;

            try
            {
                IDXGIFactory4* factory;
                iid = IID_IDXGIFactory4;
                ThrowIfFailed(nameof(CreateDXGIFactory1), CreateDXGIFactory2(dxgiFactoryFlags, &iid, (void**)&factory));
                DxgiFactory = factory;

                GetHardwareAdapter(&adapter);

                // Create the Direct3D 12 API device object
                ID3D12Device* device;
                iid = IID_ID3D12Device;
                int hr = D3D12CreateDevice((IUnknown*)adapter, D3D_FEATURE_LEVEL_11_0, &iid, (void**)&device);
                D3DDevice = device;

#if DEBUG
                if (FAILED(hr))
                {
                    // If the initialization fails, fall back to the WARP device.
                    // For more information on WARP, see: 
                    // https://go.microsoft.com/fwlink/?LinkId=286690

                    IDXGIAdapter* warpAdapter = null;

                    try
                    {
                        iid = IID_IDXGIAdapter;
                        ThrowIfFailed(nameof(IDXGIFactory4.EnumWarpAdapter), DxgiFactory->EnumWarpAdapter(&iid, (void**)(&warpAdapter)));
                        iid = IID_ID3D12Device;
                        hr = D3D12CreateDevice((IUnknown*)warpAdapter, D3D_FEATURE_LEVEL_11_0, &iid, (void**)&device);
                        D3DDevice = device;
                    }
                    finally
                    {
                        if (warpAdapter != null)
                        {
                            warpAdapter->Release();
                        }
                    }
                }
#endif

                ThrowIfFailed(nameof(D3D12CreateDevice), hr);

                // Create the command queue.
                var queueDesc = new D3D12_COMMAND_QUEUE_DESC
                {
                    Flags = D3D12_COMMAND_QUEUE_FLAG_NONE,
                    Type = D3D12_COMMAND_LIST_TYPE_DIRECT
                };

                ID3D12CommandQueue* commandQueue;
                iid = IID_ID3D12CommandQueue;
                ThrowIfFailed(nameof(ID3D12Device.CreateCommandQueue), D3DDevice->CreateCommandQueue(&queueDesc, &iid, (void**)&commandQueue));
                NameD3D12Object(CommandQueue = commandQueue, nameof(commandQueue));

                // Create descriptor heaps for render target views and depth stencil views.
                var rtvHeapDesc = new D3D12_DESCRIPTOR_HEAP_DESC
                {
                    NumDescriptors = FrameCount,
                    Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV,
                    Flags = D3D12_DESCRIPTOR_HEAP_FLAG_NONE
                };

                ID3D12DescriptorHeap* rtvHeap;
                iid = IID_ID3D12DescriptorHeap;
                ThrowIfFailed(nameof(ID3D12Device.CreateDescriptorHeap), D3DDevice->CreateDescriptorHeap(&rtvHeapDesc, &iid, (void**)&rtvHeap));
                NameD3D12Object(RtvHeap = rtvHeap, nameof(RtvHeap));

                _rtvDescriptorSize = D3DDevice->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);

                var dsvHeapDesc = new D3D12_DESCRIPTOR_HEAP_DESC
                {
                    NumDescriptors = 1,
                    Type = D3D12_DESCRIPTOR_HEAP_TYPE_DSV,
                    Flags = D3D12_DESCRIPTOR_HEAP_FLAG_NONE
                };

                ID3D12DescriptorHeap* dsvHeap;
                iid = IID_ID3D12DescriptorHeap;
                ThrowIfFailed(nameof(ID3D12Device.CreateDescriptorHeap), D3DDevice->CreateDescriptorHeap(&dsvHeapDesc, &iid, (void**)&dsvHeap));
                NameD3D12Object(DsvHeap = dsvHeap, nameof(DsvHeap));

                fixed (CommandAllocators_e__FixedBuffer* commandAllocatorsBuffer = &_commandAllocators)
                {
                    var commandAllocators = (ID3D12CommandAllocator**)commandAllocatorsBuffer;
                    iid = IID_ID3D12CommandAllocator;

                    for (var n = 0u; n < FrameCount; n++)
                    {
                        ThrowIfFailed(nameof(ID3D12Device.CreateCommandAllocator), D3DDevice->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, &iid, (void**)&commandAllocators[n]));
                    }
                }

                // Create synchronization objects.
                ID3D12Fence* fence;
                iid = IID_ID3D12Fence;
                ThrowIfFailed(nameof(ID3D12Device.CreateFence), D3DDevice->CreateFence(_fenceValues[(int)_currentFrame], D3D12_FENCE_FLAG_NONE, &iid, (void**)&fence));
                NameD3D12Object(Fence = fence, nameof(Fence));

                _fenceValues[(int)_currentFrame]++;

                _fenceEvent = CreateEvent(null, FALSE, FALSE, null);
                if (_fenceEvent == IntPtr.Zero)
                {
                    ThrowExternalExceptionForLastHRESULT(nameof(CreateEvent));
                }
            }
            finally
            {
                if (adapter != null)
                {
                    adapter->Release();
                }
            }
        }

        // Configures the Direct3D device, and stores handles to it and the device context.
        private void CreateWindowSizeDependentResources()
        {
            // Wait until all previous GPU work is complete.
            WaitForGpu();

            // Clear the previous window size specific content and update the tracked fence values.
            for (var n = 0u; n < FrameCount; n++)
            {
                _renderTargets[(int)n] = null;
                _fenceValues[(int)n] = _fenceValues[(int)_currentFrame];
            }

            UpdateRenderTargetSize();

            // The width and height of the swap chain must be based on the window's
            // natively-oriented width and height. If the window is not in the native
            // orientation, the dimensions must be reversed.
            var displayRotation = ComputeDisplayRotation();

            bool swapDimensions = displayRotation == DXGI_MODE_ROTATION_ROTATE90 || displayRotation == DXGI_MODE_ROTATION_ROTATE270;
            _d3dRenderTargetSize.Width = swapDimensions ? _outputSize.Height : _outputSize.Width;
            _d3dRenderTargetSize.Height = swapDimensions ? _outputSize.Width : _outputSize.Height;

            var backBufferWidth = (uint)_d3dRenderTargetSize.Width;
            var backBufferHeight = (uint)_d3dRenderTargetSize.Height;

            if (SwapChain != null)
            {
                // If the swap chain already exists, resize it.
                int hr = SwapChain->ResizeBuffers(FrameCount, backBufferWidth, backBufferHeight, BackBufferFormat, 0);

                if (hr == DXGI_ERROR_DEVICE_REMOVED || hr == DXGI_ERROR_DEVICE_RESET)
                {
                    // If the device was removed for any reason, a new device and swap chain will need to be created.
                    _deviceRemoved = true;

                    // Do not continue execution of this method. DeviceResources will be destroyed and re-created.
                    return;
                }
                else
                {
                    ThrowIfFailed(nameof(IDXGISwapChain.ResizeBuffers), hr);
                }
            }
            else
            {
                // Otherwise, create a new one using the same adapter as the existing Direct3D device.
                DXGI_SCALING scaling = DisplayMetrics.SupportHighResolutions ? DXGI_SCALING_NONE : DXGI_SCALING_STRETCH;
                var swapChainDesc = new DXGI_SWAP_CHAIN_DESC1
                {
                    Width = backBufferWidth,                      // Match the size of the window.
                    Height = backBufferHeight,
                    Format = BackBufferFormat,
                    Stereo = FALSE,
                    SampleDesc = new DXGI_SAMPLE_DESC
                    {
                        Count = 1,                         // Don't use multi-sampling.
                        Quality = 0
                    },
                    BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT,
                    BufferCount = FrameCount,                   // Use triple-buffering to minimize latency.
                    SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD,   // All Windows Universal apps must use _FLIP_ SwapEffects.
                    Flags = 0,
                    Scaling = scaling,
                    AlphaMode = DXGI_ALPHA_MODE_IGNORE
                };

                IDXGISwapChain1* swapChain = null;

                try
                {
                    ThrowIfFailed(nameof(IDXGIFactory2._CreateSwapChainForCoreWindow),
                        DxgiFactory->CreateSwapChainForCoreWindow(
                            (IUnknown*)CommandQueue,                               // Swap chains need a reference to the command queue in DirectX 12.
                            (IUnknown*)Marshal.GetIUnknownForObject(_window),
                            &swapChainDesc,
                            null,
                            &swapChain
                        )
                    );

                    IDXGISwapChain3* swapChain3;
                    var iid = IID_IDXGISwapChain3;
                    ThrowIfFailed(nameof(IUnknown.QueryInterface), swapChain->QueryInterface(&iid, (void**)&swapChain3));
                    SwapChain = swapChain3;
                }
                finally
                {
                    if (swapChain != null)
                    {
                        swapChain->Release();
                    }
                }
            }

            // Set the proper orientation for the swap chain, and generate
            // 3D matrix transformations for rendering to the rotated swap chain.
            // The 3D matrix is specified explicitly to avoid rounding errors.

            switch (displayRotation)
            {
                case DXGI_MODE_ROTATION_IDENTITY:
                    _orientationTransform3D = ScreenRotation.Rotation0;
                    break;

                case DXGI_MODE_ROTATION_ROTATE90:
                    _orientationTransform3D = ScreenRotation.Rotation270;
                    break;

                case DXGI_MODE_ROTATION_ROTATE180:
                    _orientationTransform3D = ScreenRotation.Rotation180;
                    break;

                case DXGI_MODE_ROTATION_ROTATE270:
                    _orientationTransform3D = ScreenRotation.Rotation90;
                    break;

                default:
                    throw new Exception();
            }

            ThrowIfFailed(nameof(IDXGISwapChain1._SetRotation), SwapChain->SetRotation(displayRotation));

            // Create render target views of the swap chain back buffer.
            {
                _currentFrame = SwapChain->GetCurrentBackBufferIndex();
                var rtvDescriptor = RtvHeap->GetCPUDescriptorHandleForHeapStart();

                fixed (RenderTargets_e__FixedBuffer* renderTargetsBuffer = &_renderTargets)
                {
                    var renderTargets = (ID3D12Resource**)renderTargetsBuffer;
                    var iid = IID_ID3D12Resource;

                    for (var n = 0u; n < FrameCount; n++)
                    {
                        ThrowIfFailed(nameof(IDXGISwapChain._GetBuffer), SwapChain->GetBuffer(n, &iid, (void**)&renderTargets[n]));
                        D3DDevice->CreateRenderTargetView(_renderTargets[(int)n], null, rtvDescriptor);
                        rtvDescriptor.ptr = (UIntPtr)((byte*)rtvDescriptor.ptr + _rtvDescriptorSize);

                        NameD3D12Object(renderTargets[n], $"{nameof(RenderTarget)}[{n}]");
                    }
                }
            }

            // Create a depth stencil and view.
            {
                var depthHeapProperties = new D3D12_HEAP_PROPERTIES(D3D12_HEAP_TYPE_DEFAULT);

                var depthResourceDesc = D3D12_RESOURCE_DESC.Tex2D(DepthBufferFormat, backBufferWidth, backBufferHeight, 1, 1);
                depthResourceDesc.Flags |= D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL;

                var depthOptimizedClearValue = new D3D12_CLEAR_VALUE(DepthBufferFormat, 1.0f, 0);

                ID3D12Resource * depthStencil;
                var iid = IID_ID3D12Resource;
                ThrowIfFailed(nameof(ID3D12Device._CreateCommittedResource), D3DDevice->CreateCommittedResource(
                    &depthHeapProperties,
                    D3D12_HEAP_FLAG_NONE,
                    &depthResourceDesc,
                    D3D12_RESOURCE_STATE_DEPTH_WRITE,
                    &depthOptimizedClearValue,
                    &iid,
                    (void**)&depthStencil
                ));
                NameD3D12Object(DepthStencil = depthStencil, nameof(DepthStencil));

                var dsvDesc = new D3D12_DEPTH_STENCIL_VIEW_DESC
                {
                    Format = DepthBufferFormat,
                    ViewDimension = D3D12_DSV_DIMENSION_TEXTURE2D,
                    Flags = D3D12_DSV_FLAG_NONE
                };

                var dsvHeapHandle = DsvHeap->GetCPUDescriptorHandleForHeapStart();
                D3DDevice->CreateDepthStencilView(DepthStencil, &dsvDesc, dsvHeapHandle);
            }

            // Set the 3D rendering viewport to target the entire window.
            _screenViewport = new D3D12_VIEWPORT() {
                TopLeftX = 0.0f,
                TopLeftY = 0.0f,
                Width = (float)_d3dRenderTargetSize.Width,
                Height = (float)_d3dRenderTargetSize.Height,
                MinDepth = 0.0f,
                MaxDepth = 1.0f
            };
        }

        // Determine the dimensions of the render target and whether it will be scaled down.
        private void UpdateRenderTargetSize()
        {
            _effectiveDpi = _dpi;

            // To improve battery life on high resolution devices, render to a smaller render target
            // and allow the GPU to scale the output when it is presented.
            if (!DisplayMetrics.SupportHighResolutions && _dpi > DisplayMetrics.DpiThreshold)
            {
                float width = ConvertDipsToPixels((float)_logicalSize.Width, _dpi);
                float height = ConvertDipsToPixels((float)_logicalSize.Height, _dpi);

                // When the device is in portrait orientation, height > width. Compare the
                // larger dimension against the width threshold and the smaller dimension
                // against the height threshold.
                if (MathF.Max(width, height) > DisplayMetrics.WidthThreshold && MathF.Min(width, height) > DisplayMetrics.HeightThreshold)
                {
                    // To scale the app we change the effective DPI. Logical size does not change.
                    _effectiveDpi /= 2.0f;
                }
            }

            // Calculate the necessary render target size in pixels.
            _outputSize.Width = ConvertDipsToPixels((float)_logicalSize.Width, _effectiveDpi);
            _outputSize.Height = ConvertDipsToPixels((float)_logicalSize.Height, _effectiveDpi);

            // Prevent zero size DirectX content from being created.
            _outputSize.Width = Math.Max(_outputSize.Width, 1);
            _outputSize.Height = Math.Max(_outputSize.Height, 1);
        }

        // Prepare to render the next frame.
        private void MoveToNextFrame()
        {
            // Schedule a Signal command in the queue.
            ulong currentFenceValue = _fenceValues[(int)_currentFrame];
            ThrowIfFailed(nameof(ID3D12CommandQueue.Signal), CommandQueue->Signal(Fence, currentFenceValue));

            // Advance the frame index.
            _currentFrame = SwapChain->GetCurrentBackBufferIndex();

            // Check to see if the next frame is ready to start.
            if (Fence->GetCompletedValue() < _fenceValues[(int)_currentFrame])
            {
                ThrowIfFailed(nameof(ID3D12Fence.SetEventOnCompletion), Fence->SetEventOnCompletion(_fenceValues[(int)_currentFrame], _fenceEvent));
                WaitForSingleObject(_fenceEvent, INFINITE);
            }

            // Set the fence value for the next frame.
            _fenceValues[(int)_currentFrame] = currentFenceValue + 1;
        }

        // This method determines the rotation between the display device's native Orientation and the
        // current display orientation.
        private DXGI_MODE_ROTATION ComputeDisplayRotation()
        {
            DXGI_MODE_ROTATION rotation = DXGI_MODE_ROTATION_UNSPECIFIED;

            // Note: NativeOrientation can only be Landscape or Portrait even though
            // the DisplayOrientations enum has other values.
            switch (_nativeOrientation)
            {
                case DisplayOrientations.Landscape:
                    switch (_currentOrientation)
                    {
                        case DisplayOrientations.Landscape:
                            rotation = DXGI_MODE_ROTATION_IDENTITY;
                            break;

                        case DisplayOrientations.Portrait:
                            rotation = DXGI_MODE_ROTATION_ROTATE270;
                            break;

                        case DisplayOrientations.LandscapeFlipped:
                            rotation = DXGI_MODE_ROTATION_ROTATE180;
                            break;

                        case DisplayOrientations.PortraitFlipped:
                            rotation = DXGI_MODE_ROTATION_ROTATE90;
                            break;
                    }
                    break;

                case DisplayOrientations.Portrait:
                    switch (_currentOrientation)
                    {
                        case DisplayOrientations.Landscape:
                            rotation = DXGI_MODE_ROTATION_ROTATE90;
                            break;

                        case DisplayOrientations.Portrait:
                            rotation = DXGI_MODE_ROTATION_IDENTITY;
                            break;

                        case DisplayOrientations.LandscapeFlipped:
                            rotation = DXGI_MODE_ROTATION_ROTATE270;
                            break;

                        case DisplayOrientations.PortraitFlipped:
                            rotation = DXGI_MODE_ROTATION_ROTATE180;
                            break;
                    }
                    break;
            }
            return rotation;
        }

        // This method acquires the first available hardware adapter that supports Direct3D 12.
        // If no such adapter can be found, *ppAdapter will be set to null.
        private void GetHardwareAdapter(IDXGIAdapter1** ppAdapter)
        {
            IDXGIAdapter1* adapter = null;
            *ppAdapter = null;
            var iid = IID_ID3D12Device;

            for (var adapterIndex = 0u; DXGI_ERROR_NOT_FOUND != DxgiFactory->EnumAdapters1(adapterIndex, &adapter); adapterIndex++)
            {
                DXGI_ADAPTER_DESC1 desc;
                adapter->GetDesc1(&desc);

                if ((desc.Flags & (uint)DXGI_ADAPTER_FLAG_SOFTWARE) != 0)
                {
                    // Don't select the Basic Render Driver adapter.
                }
                else if (SUCCEEDED(D3D12CreateDevice((IUnknown*)adapter, D3D_FEATURE_LEVEL_11_0, &iid, null)))
                {
                    // Check to see if the adapter supports Direct3D 12, but don't create the
                    // actual device yet.
                    break;
                }

                adapter->Release();
            }

            *ppAdapter = adapter;
        }
        #endregion

        #region System.IDisposable
        public void Dispose()
        {
            DepthStencil = null;

            for (var n = FrameCount - 1; n != uint.MaxValue; n--)
            {
                _renderTargets[(int)n] = null;
            }

            SwapChain = null;
            Fence = null;

            for (var n = FrameCount - 1; n != uint.MaxValue; n--)
            {
                _commandAllocators[(int)n] = null;
            }

            DsvHeap = null;
            RtvHeap = null;
            CommandQueue = null;
            D3DDevice = null;
            DxgiFactory = null;
        }
        #endregion

        #region Structs
        private unsafe struct CommandAllocators_e__FixedBuffer
        {
            #region Fields
#pragma warning disable CS0649
            public ID3D12CommandAllocator* e0;

            public ID3D12CommandAllocator* e1;
#pragma warning restore CS0649
            #endregion

            #region Properties
            public ID3D12CommandAllocator* this[int index]
            {
                get
                {
                    fixed (ID3D12CommandAllocator** e = &e0)
                    {
                        return e[index];
                    }
                }

                set
                {
                    fixed (ID3D12CommandAllocator** e = &e0)
                    {
                        if (e[index] != null)
                        {
                            e[index]->Release();
                        }
                        e[index] = value;
                    }
                }
            }
            #endregion
        }

        private unsafe struct FenceValues_e__FixedBuffer
        {
            #region Fields
#pragma warning disable CS0649
            public ulong e0;

            public ulong e1;
#pragma warning restore CS0649
            #endregion

            #region Properties
            public ulong this[int index]
            {
                get
                {
                    fixed (ulong* e = &e0)
                    {
                        return e[index];
                    }
                }

                set
                {
                    fixed (ulong* e = &e0)
                    {
                        e[index] = value;
                    }
                }
            }
            #endregion
        }

        private unsafe struct RenderTargets_e__FixedBuffer
        {
            #region Fields
#pragma warning disable CS0649
            public ID3D12Resource* e0;

            public ID3D12Resource* e1;
#pragma warning restore CS0649
            #endregion

            #region Properties
            public ID3D12Resource* this[int index]
            {
                get
                {
                    fixed (ID3D12Resource** e = &e0)
                    {
                        return e[index];
                    }
                }

                set
                {
                    fixed (ID3D12Resource** e = &e0)
                    {
                        if (e[index] != null)
                        {
                            e[index]->Release();
                        }
                        e[index] = value;
                    }
                }
            }
            #endregion
        }
        #endregion
    }
}
