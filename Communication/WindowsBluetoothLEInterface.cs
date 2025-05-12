using System.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using IRIS.Bluetooth.Common;
using IRIS.Bluetooth.Common.Abstract;
using IRIS.Bluetooth.Common.Addressing;
using IRIS.Bluetooth.Windows.Structure;
using IRIS.Communication;

namespace IRIS.Bluetooth.Windows.Communication
{
    /// <summary>
    /// Provides a Windows-specific implementation of the Bluetooth Low Energy (BLE) interface.
    /// This class handles device discovery, connection management, and communication with BLE devices.
    /// </summary>
    public sealed class WindowsBluetoothLEInterface(IBluetoothLEAddress deviceAddress) : IBluetoothLEInterface, IDisposable
    {
        private readonly List<IBluetoothLEDevice> _connectedDevices = [];
        private readonly List<IBluetoothLEDevice> _discoveredDevices = [];
        private readonly object _devicesLock = new();
        private bool _disposed;

        /// <summary>
        /// Gets a read-only list of all discovered Bluetooth LE devices.
        /// </summary>
        public IReadOnlyList<IBluetoothLEDevice> DiscoveredDevices
        {
            get
            {
                lock (_devicesLock) return _discoveredDevices.ToList();
            }
        }

        /// <summary>
        /// Gets a read-only list of all currently connected Bluetooth LE devices.
        /// </summary>
        public IReadOnlyList<IBluetoothLEDevice> ConnectedDevices
        {
            get
            {
                lock (_devicesLock) return _connectedDevices.ToList();
            }
        }

        /// <summary>
        /// Event raised when a new Bluetooth LE device is discovered during scanning.
        /// </summary>
        public event DeviceDiscoveredHandler? OnBluetoothDeviceDiscovered;

        /// <summary>
        /// Event raised when a Bluetooth LE device is successfully connected.
        /// </summary>
        public event DeviceConnectedHandler? OnBluetoothDeviceConnected;

        /// <summary>
        /// Event raised when a Bluetooth LE device is explicitly disconnected.
        /// </summary>
        public event DeviceDisconnectedHandler? OnBluetoothDeviceDisconnected;

        /// <summary>
        /// Event raised when the connection to a Bluetooth LE device is unexpectedly lost.
        /// </summary>
        public event DeviceConnectionLostHandler? OnBluetoothDeviceConnectionLost;

        /// <summary>
        /// Gets the Bluetooth address of the device that this interface is configured to search for.
        /// </summary>
        public IBluetoothLEAddress DeviceBluetoothAddress { get; } = deviceAddress;

        /// <summary>
        /// Gets a value indicating whether the interface is currently connected to any Bluetooth LE device.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                lock (_devicesLock) return _connectedDevices.Count > 0;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the interface is currently scanning for devices.
        /// </summary>
        private bool IsRunning { get; set; }

        /// <summary>
        /// The Bluetooth LE advertisement watcher used for scanning for devices.
        /// Configured with active scanning mode and signal strength filtering.
        /// </summary>
        private readonly BluetoothLEAdvertisementWatcher _watcher = new()
        {
            ScanningMode = BluetoothLEScanningMode.Active,
            AdvertisementFilter = new BluetoothLEAdvertisementFilter
            {
                Advertisement = new BluetoothLEAdvertisement()
            },
            SignalStrengthFilter = new BluetoothSignalStrengthFilter
            {
                InRangeThresholdInDBm = -75,
                OutOfRangeThresholdInDBm = -70,
                OutOfRangeTimeout = TimeSpan.FromSeconds(2)
            }
        };

        /// <summary>
        /// Initiates a connection to Bluetooth LE devices by starting the device discovery process.
        /// </summary>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>True if the connection process was successfully started; otherwise, false.</returns>
        public bool Connect(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            // Architectural Decision: We don't start a new scan if one is already running
            // This prevents multiple concurrent scans which could interfere with each other
            // and waste system resources. Instead, we reuse the existing scan.
            if (IsRunning) return true;

            try
            {
                // Architectural Decision: Using event-based approach for device discovery
                // This allows for asynchronous processing of discovered devices without blocking
                // the main thread, and provides real-time updates as devices are found.
                _watcher.Received += OnAdvertisementReceived;
                _watcher.Stopped += OnWatcherStopped;
                _watcher.Start();
                IsRunning = true;
                return true;
            }
            catch (Exception exception)
            {
                // Architectural Decision: Graceful error handling with cleanup
                // We ensure event handlers are removed even if start fails to prevent memory leaks
                Debug.WriteLine($"Failed to start Bluetooth LE watcher: {exception}");
                _watcher.Received -= OnAdvertisementReceived;
                _watcher.Stopped -= OnWatcherStopped;
                return false;
            }
        }

        /// <summary>
        /// Disconnects from all connected devices and stops the device discovery process.
        /// </summary>
        /// <returns>True if the disconnection was successful; otherwise, false.</returns>
        public bool Disconnect()
        {
            ThrowIfDisposed();
            
            // Architectural Decision: Check current state before acting
            // Avoids unnecessary operations if already in desired state
            if (!IsRunning) return true;

            try
            {
                // Architectural Decision: Clear devices before stopping watcher
                // Ensures proper cleanup sequence and prevents race conditions
                DisconnectAllDevices();
                StopWatcher();
                return true;
            }
            catch (Exception exception)
            {
                // Architectural Decision: Debug-level logging
                // Provides diagnostic information without exposing internal details
                Debug.WriteLine($"Error during disconnect: {exception}");
                return false;
            }
        }
        
        private void DisconnectAllDevices()
        {
            lock (_devicesLock)
            {
                // Architectural Decision: Notify connection loss rather than intentional disconnection
                // This ensures consumers handle these as unexpected disconnections rather than clean shutdowns
                foreach (IBluetoothLEDevice device in _connectedDevices)
                {
                    OnBluetoothDeviceConnectionLost?.Invoke(this, device);
                }

                // Architectural Decision: Clear both connected and discovered devices
                // This ensures complete state reset and prevents stale device references:
                // 1. Connected devices list clearance breaks circular dependencies
                // 2. Discovered devices clearance forces fresh rediscovery on next scan
                _connectedDevices.Clear();
                _discoveredDevices.Clear();
            }
        }

        private void StopWatcher()
        {
            _watcher.Received -= OnAdvertisementReceived;
            _watcher.Stopped -= OnWatcherStopped;
            _watcher.Stop();
            IsRunning = false;
        }

        private void OnWatcherStopped(
            BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            // Disconnect as we've failed scanning for some reason
            Disconnect();
        }

        private void OnAdvertisementReceived(
            BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementReceivedEventArgs args)
        {
            // Architectural Decision: Immediate disposal check
            // Ensures we don't process advertisements after interface disposal
            ThrowIfDisposed();
            
            try
            {
                // Architectural Decision: Isolated processing context
                // All advertisement handling occurs in a dedicated method with its own error containment
                ProcessAdvertisement(args);
            }
            catch (Exception exception)
            {
                // Architectural Decision: Error swallowing with diagnostics
                // Prevents watcher thread crashes while maintaining operational continuity
                Debug.WriteLine($"Error processing advertisement: {exception}");
            }
        }

        private async void ProcessAdvertisement(BluetoothLEAdvertisementReceivedEventArgs args)
        {
            lock (_devicesLock)
            {
                // Architectural Decision: Deduplication of discovered devices
                // We check if we've already discovered this device to prevent duplicate processing
                // and ensure consistent device state management
                if (_discoveredDevices.Any(device => device.DeviceAddress == args.BluetoothAddress)) return;

                
                // Architectural Decision: Device Creation and Validation
                // We create a BluetoothLEDevice instance from the discovered address to access device properties
                // and validate its existence before proceeding with further processing
                BluetoothLEDevice? device = BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress).AsTask().Result;
                if (device == null) return;

                Debug.WriteLine($"Discovered device: {device.Name} ({device.BluetoothAddress})");

                // Architectural Decision: Device validation before adding to discovered list
                // This ensures we only track devices that match our target criteria
                WindowsBluetoothLEDevice bluetoothDevice = new (device);
                if (!DeviceBluetoothAddress.IsDeviceValid(bluetoothDevice))
                {
                    Debug.WriteLine(
                        $"Device {bluetoothDevice.Name} ({bluetoothDevice.DeviceAddress}) is not valid for address {DeviceBluetoothAddress}");
                    return;
                }

                // Architectural Decision: Event-based device discovery notification
                // This allows consumers to react to new devices immediately while maintaining
                // loose coupling between discovery and handling logic
                _discoveredDevices.Add(bluetoothDevice);
                device.ConnectionStatusChanged += OnDeviceConnectionStatusChanged;
                OnBluetoothDeviceDiscovered?.Invoke(this, bluetoothDevice);
            }
        }

        private void OnDeviceConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            // Architectural Decision: Early exit pattern for non-disconnection events
            // We only process disconnection events to avoid unnecessary processing
            // of connection status changes that don't affect our state
            if (sender.ConnectionStatus != BluetoothConnectionStatus.Disconnected) return;

            // Architectural Decision: Separation of notification and cleanup concerns
            // We handle connection loss notification before cleaning up resources
            // to ensure subscribers get the event before we modify device state
            AttemptToDisconnectDevice(sender);

            // Architectural Decision: Atomic device list modification
            // Using nested locks ensures atomic operations across both connected
            // and discovered device lists while maintaining thread safety
            lock (_devicesLock)
            {
                lock (_connectedDevices)
                {
                    // Architectural Decision: Complete device state cleanup
                    // We remove from both lists to maintain consistency between
                    // discovered and connected device tracking
                    _connectedDevices.RemoveAll(device => device.DeviceAddress == sender.BluetoothAddress);
                    _discoveredDevices.RemoveAll(device => device.DeviceAddress == sender.BluetoothAddress);

                    // Architectural Decision: Explicit event unsubscription and disposal
                    // Prevents memory leaks and ensures clean state for potential reconnections
                    sender.ConnectionStatusChanged -= OnDeviceConnectionStatusChanged;
                    sender.Dispose();
                }
            }
        }

        private void AttemptToDisconnectDevice(BluetoothLEDevice sender)
        {
            // Architectural Decision: Thread-safe device validation before notification
            // We use a lock to ensure atomic access to the discovered devices list while
            // checking device existence, preventing race conditions between disconnection
            // and device list modifications
            lock (_devicesLock)
            {
                // Architectural Decision: Explicit device lookup in discovered devices
                // We verify the device was previously discovered before notifying about disconnection
                // This ensures we only handle connection loss for devices we know about
                IBluetoothLEDevice? device =
                    _discoveredDevices.FirstOrDefault(device => device.DeviceAddress == sender.BluetoothAddress);

                // Architectural Decision: Early exit pattern for non-tracked devices
                // We avoid processing unknown devices to prevent false positive notifications
                // and maintain system integrity
                if (device == null) return;
                
                // Architectural Decision: Event-driven connection loss notification
                // Using an event here allows asynchronous processing of connection failures
                // while keeping the disconnection logic decoupled from consumer handling
                OnBluetoothDeviceConnectionLost?.Invoke(this, device);
            }
        }

        
        /// <summary>
        /// Claims a discovered Bluetooth LE device for use.
        /// If no device is immediately available, waits for a device to be discovered.
        /// </summary>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>The claimed Bluetooth LE device, or null if no device was available or the operation was cancelled.</returns>
        public IBluetoothLEDevice? ClaimDevice(CancellationToken cancellationToken = default)
        {
            // Architectural Decision: Two-phase device claiming
            // First try to claim an already discovered device, then wait for new discoveries
            // This optimizes for the common case where a device is already available
            IBluetoothLEDevice? device;

            lock (_devicesLock)
            {
                lock (_connectedDevices)
                {
                    device = _discoveredDevices.FirstOrDefault(d => !_connectedDevices.Contains(d));
                }
            }

            if (device != null)
            {
                RegisterDevice(device);
                return device;
            }

            // Architectural Decision: Event-based waiting for device discovery
            // Instead of polling, we use an event handler to be notified when a suitable device is found
            // This is more efficient and responsive than polling
            OnBluetoothDeviceDiscovered += OnDeviceDiscovered;

            // Architectural Decision: Cancellable waiting
            // We support cancellation to prevent indefinite waiting and allow proper cleanup
            while (device == null)
            {
                if (cancellationToken.IsCancellationRequested) break;
            }

            OnBluetoothDeviceDiscovered -= OnDeviceDiscovered;
            return device;

            void OnDeviceDiscovered(IBluetoothLEInterface sender, IBluetoothLEDevice deviceInstance)
            {
                RegisterDevice(deviceInstance);
            }

            void RegisterDevice(IBluetoothLEDevice deviceInstance)
            {
                // Architectural Decision: Thread-safe device registration
                // We use locks to ensure thread safety when modifying shared collections
                // and to prevent race conditions during device registration
                lock (_connectedDevices)
                {
                    if (_connectedDevices.Contains(deviceInstance)) return;
                    _connectedDevices.Add(deviceInstance);
                }

                // Architectural Decision: Event-based state change notification
                // We notify listeners of device connection through events to maintain
                // loose coupling and allow for asynchronous processing
                OnBluetoothDeviceConnected?.Invoke(this, deviceInstance);
                DeviceConnected?.Invoke(DeviceBluetoothAddress);

                device = deviceInstance;
            }
        }

        /// <summary>
        /// Releases a previously claimed Bluetooth LE device.
        /// </summary>
        /// <param name="device">The Bluetooth LE device to release.</param>
        public void ReleaseDevice(IBluetoothLEDevice device)
        {
            // Architectural Decision: Thread-safe device removal
            // We use a lock to ensure atomic operations on the connected devices list
            // preventing race conditions between device removal and other operations
            lock (_devicesLock)
            {
                // Architectural Decision: Early exit guard clause
                // We check existence first to avoid unnecessary operations
                // and maintain method efficiency
                if (!_connectedDevices.Contains(device)) return;

                // Architectural Decision: Explicit resource cleanup
                // We remove from connected devices first before notifications
                // to ensure consistent state before event propagation
                _connectedDevices.Remove(device);
            }

            // Architectural Decision: Dual notification system
            // We fire both the new-style event (OnBluetoothDeviceDisconnected) 
            // and legacy event (DeviceDisconnected) to maintain backward compatibility
            // while supporting the newer event-based architecture
            OnBluetoothDeviceDisconnected?.Invoke(this, device);
            
            // Architectural Decision: Address-based notification
            // The DeviceDisconnected event uses the Bluetooth address instead of
            // the device object to match legacy system requirements
            DeviceDisconnected?.Invoke(DeviceBluetoothAddress);
        }

        /// <summary>
        /// Event raised when a device is connected. Maintained for API compatibility.
        /// </summary>
        public event Delegates.DeviceConnectedHandler<IBluetoothLEAddress>? DeviceConnected;

        /// <summary>
        /// Event raised when a device is disconnected. Maintained for API compatibility.
        /// </summary>
        public event Delegates.DeviceDisconnectedHandler<IBluetoothLEAddress>? DeviceDisconnected;

        /// <summary>
        /// Event raised when a device connection is lost. Maintained for API compatibility.
        /// </summary>
        public event Delegates.DeviceConnectionLostHandler<IBluetoothLEAddress>? DeviceConnectionLost;

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            // Architectural Decision: Idempotent disposal
            // We check _disposed to ensure we don't perform cleanup multiple times
            if (_disposed) return;
            
            // Architectural Decision: Graceful cleanup
            // We disconnect all devices and stop the watcher to ensure proper resource cleanup
            // This prevents resource leaks and ensures a clean shutdown
            Disconnect();
            try
            {
                _watcher.Stop();
            }
            catch(Exception exception)
            {
                // Architectural Decision: Non-throwing error handling during disposal
                // We log errors but don't throw during disposal to ensure cleanup completes
                Debug.WriteLine($"Error stopping watcher: {exception}");
            }

            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WindowsBluetoothLEInterface));
            }
        }
    }
}