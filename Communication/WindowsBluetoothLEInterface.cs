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
    public sealed class WindowsBluetoothLEInterface(IBluetoothLEAddress deviceAddress)
        : IBluetoothLEInterface, IDisposable
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
        public ValueTask<bool> Connect(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            // Prevent if already running
            if (IsRunning) return ValueTask.FromResult(true);

            try
            {
                _watcher.Received += OnAdvertisementReceived;
                _watcher.Stopped += OnWatcherStopped;
                _watcher.Start();
                IsRunning = true;
                return ValueTask.FromResult(true);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Failed to start Bluetooth LE watcher: {exception}");
                _watcher.Received -= OnAdvertisementReceived;
                _watcher.Stopped -= OnWatcherStopped;
                return ValueTask.FromResult(false);
            }
        }

        /// <summary>
        /// Disconnects from all connected devices and stops the device discovery process.
        /// </summary>
        /// <returns>True if the disconnection was successful; otherwise, false.</returns>
        public ValueTask<bool> Disconnect()
        {
            ThrowIfDisposed();

            if (!IsRunning) return ValueTask.FromResult(true);

            try
            {
                DisconnectAllDevices();
                StopWatcher();
                return ValueTask.FromResult(true);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Error during disconnect: {exception}");
                return ValueTask.FromResult(false);
            }
        }

        private void DisconnectAllDevices()
        {
            lock (_devicesLock)
            {
                foreach (IBluetoothLEDevice device in _connectedDevices)
                {
                    OnBluetoothDeviceConnectionLost?.Invoke(this, device);
                }

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

        private async void OnWatcherStopped(
            BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            await Disconnect();
        }

        private void OnAdvertisementReceived(
            BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementReceivedEventArgs args)
        {
            ThrowIfDisposed();

            try
            {
                ProcessAdvertisement(args);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Error processing advertisement: {exception}");
            }
        }

        private async void ProcessAdvertisement(BluetoothLEAdvertisementReceivedEventArgs args)
        {
            lock (_devicesLock)
            {
                if (_discoveredDevices.Any(device => device.DeviceAddress == args.BluetoothAddress)) return;
            }

            BluetoothLEDevice? device = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
            if (device == null) return;

            Debug.WriteLine($"Discovered device: {device.Name} ({device.BluetoothAddress})");

            WindowsBluetoothLEDevice bluetoothDevice = new(device);
            if (!DeviceBluetoothAddress.IsDeviceValid(bluetoothDevice))
            {
                Debug.WriteLine(
                    $"Device {bluetoothDevice.Name} ({bluetoothDevice.DeviceAddress}) is not valid for address {DeviceBluetoothAddress}");
                return;
            }

            lock (_devicesLock) _discoveredDevices.Add(bluetoothDevice);
            device.ConnectionStatusChanged += OnDeviceConnectionStatusChanged;
            OnBluetoothDeviceDiscovered?.Invoke(this, bluetoothDevice);
        }

        private void OnDeviceConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            ThrowIfDisposed();

            if (sender.ConnectionStatus != BluetoothConnectionStatus.Disconnected) return;
            AttemptToDisconnectDevice(sender);

            lock (_devicesLock)
            {
                _connectedDevices.RemoveAll(device => device.DeviceAddress == sender.BluetoothAddress);
                _discoveredDevices.RemoveAll(device => device.DeviceAddress == sender.BluetoothAddress);

                sender.ConnectionStatusChanged -= OnDeviceConnectionStatusChanged;
                sender.Dispose();
            }
        }

        private void AttemptToDisconnectDevice(BluetoothLEDevice sender)
        {
            lock (_devicesLock)
            {
                IBluetoothLEDevice? device =
                    _discoveredDevices.FirstOrDefault(device => device.DeviceAddress == sender.BluetoothAddress);
                if (device == null) return;

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
            IBluetoothLEDevice? device;

            lock (_devicesLock)
            {
                device = _discoveredDevices.FirstOrDefault(d => !_connectedDevices.Contains(d));
            }

            if (device != null)
            {
                RegisterDevice(device);
                return device;
            }

            OnBluetoothDeviceDiscovered += OnDeviceDiscovered;

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
                lock (_devicesLock)
                {
                    if (_connectedDevices.Contains(deviceInstance)) return;
                    _connectedDevices.Add(deviceInstance);
                }

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
            lock (_devicesLock)
            {
                if (!_connectedDevices.Contains(device)) return;

                _connectedDevices.Remove(device);
            }

            OnBluetoothDeviceDisconnected?.Invoke(this, device);
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
        public async void Dispose()
        {
            if (_disposed) return;

            await Disconnect();
            try
            {
                StopWatcher();
            }
            catch (Exception exception)
            {
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