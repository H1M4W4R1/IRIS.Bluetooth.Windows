using System.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using IRIS.Bluetooth.Common;
using IRIS.Bluetooth.Common.Abstract;
using IRIS.Bluetooth.Common.Addressing;
using IRIS.Bluetooth.Common.Utility;
using IRIS.Bluetooth.Windows.Structure;
using IRIS.Communication;
using IRIS.Operations;
using IRIS.Operations.Abstract;
using IRIS.Operations.Connection;
using IRIS.Utility;

namespace IRIS.Bluetooth.Windows.Communication
{
    /// <summary>
    ///     Provides a Windows-specific implementation of the Bluetooth Low Energy (BLE) interface.
    ///     This class handles device discovery, connection management, and communication with BLE devices.
    ///     The implementation uses Windows.Devices.Bluetooth APIs for BLE operations.
    /// </summary>
    public sealed class WindowsBluetoothLEInterface(IBluetoothLEAddress deviceAddress)
        : IBluetoothLEInterface
    {
        /// <summary>
        ///     Internal list of currently connected Bluetooth LE devices.
        ///     Thread-safe access is managed through _devicesLock.
        /// </summary>
        private readonly List<IBluetoothLEDevice> _connectedDevices = [];

        /// <summary>
        ///     Internal list of discovered Bluetooth LE devices.
        ///     Thread-safe access is managed through _devicesLock.
        /// </summary>
        private readonly List<IBluetoothLEDevice> _discoveredDevices = [];

        /// <summary>
        ///     Internal list of currently processed Bluetooth LE devices
        ///      Thread-safe access is managed through _devicesLock.
        /// </summary>
        private readonly List<ulong> _devicesUnderProcessing = [];

        /// <summary>
        ///     Lock object for synchronizing access to device collections.
        /// </summary>
        private readonly object _devicesLock = new();

        /// <summary>
        ///     Gets a thread-safe read-only list of all discovered Bluetooth LE devices.
        ///     The returned list is a snapshot of the current state.
        /// </summary>
        public IReadOnlyList<IBluetoothLEDevice> DiscoveredDevices
        {
            get
            {
                lock (_devicesLock) return _discoveredDevices.ToList();
            }
        }

        /// <summary>
        ///     Gets a thread-safe read-only list of all currently connected Bluetooth LE devices.
        ///     The returned list is a snapshot of the current state.
        /// </summary>
        public IReadOnlyList<IBluetoothLEDevice> ConnectedDevices
        {
            get
            {
                lock (_devicesLock) return _connectedDevices.ToList();
            }
        }

        /// <summary>
        ///     Event raised when a new Bluetooth LE device is discovered during scanning.
        ///     The event provides both the interface instance and the discovered device.
        /// </summary>
        public event DeviceDiscoveredHandler? OnBluetoothDeviceDiscovered;

        /// <summary>
        ///     Event raised when a Bluetooth LE device is successfully connected.
        ///     The event provides both the interface instance and the connected device.
        /// </summary>
        public event DeviceConnectedHandler? OnBluetoothDeviceConnected;

        /// <summary>
        ///     Event raised when a Bluetooth LE device is explicitly disconnected.
        ///     The event provides both the interface instance and the disconnected device.
        /// </summary>
        public event DeviceDisconnectedHandler? OnBluetoothDeviceDisconnected;

        /// <summary>
        ///     Event raised when the connection to a Bluetooth LE device is unexpectedly lost.
        ///     The event provides both the interface instance and the device that lost connection.
        /// </summary>
        public event DeviceConnectionLostHandler? OnBluetoothDeviceConnectionLost;

        /// <summary>
        ///     Gets the Bluetooth address of the device that this interface is configured to search for.
        ///     This address is used to filter and validate discovered devices.
        /// </summary>
        public IBluetoothLEAddress DeviceBluetoothAddress { get; } = deviceAddress;

        /// <summary>
        ///     Check if device with specified address is discovered
        /// </summary>
        /// <param name="deviceBluetoothAddress">Address of BLE device</param>
        public bool IsDeviceDiscovered(ulong deviceBluetoothAddress) =>
            _devicesUnderProcessing.Contains(deviceBluetoothAddress) ||
            _discoveredDevices.Any(device => device.DeviceAddress == deviceBluetoothAddress);

        /// <summary>
        ///     Check if device with specified address is connected
        /// </summary>
        /// <param name="deviceBluetoothAddress">Address of BLE device</param>
        public bool IsDeviceConnected(ulong deviceBluetoothAddress) =>
            _connectedDevices.Any(device => device.DeviceAddress == deviceBluetoothAddress);

        /// <summary>
        ///     Gets a value indicating whether the interface is currently connected to any Bluetooth LE device.
        ///     Thread-safe check against the connected devices collection.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                lock (_devicesLock) return _connectedDevices.Count > 0;
            }
        }

        /// <summary>
        ///     Gets or sets a value indicating whether the interface is currently scanning for devices.
        ///     This property is used internally to track the scanning state.
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        ///     The Bluetooth LE advertisement watcher used for scanning for devices.
        ///     Configured with active scanning mode and signal strength filtering.
        ///     Signal strength thresholds are set to optimize device discovery:
        ///     - InRangeThresholdInDBm: -75 dBm
        ///     - OutOfRangeThresholdInDBm: -70 dBm
        ///     - OutOfRangeTimeout: 2 seconds
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
        ///     Initiates a connection to Bluetooth LE devices by starting the device discovery process.
        ///     This method sets up the advertisement watcher and begins scanning for devices.
        /// </summary>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        public ValueTask<IDeviceOperationResult> Connect(CancellationToken cancellationToken = default)
        {
            // Prevent if already running
            if (IsRunning)
            {
                Notify.Verbose(nameof(WindowsBluetoothLEInterface), "Interface is already running.");
                return DeviceOperation.VResult<DeviceAlreadyConnectedResult>();
            }

            try
            {
                _watcher.Received += OnAdvertisementReceived;
                _watcher.Stopped += OnWatcherStopped;
                _watcher.Start();
                IsRunning = true;
                return DeviceOperation.VResult<DeviceConnectedSuccessfullyResult>();
            }
            catch (Exception exception)
            {
                Notify.Error(nameof(WindowsBluetoothLEInterface),
                    $"Cannot start BLE Watcher due to exception: {exception}.");
                _watcher.Received -= OnAdvertisementReceived;
                _watcher.Stopped -= OnWatcherStopped;
                return DeviceOperation.VResult<DeviceConnectionFailedResult>();
            }
        }

        /// <summary>
        ///     Disconnects from all connected devices and stops the device discovery process.
        ///     This method ensures proper cleanup of all device connections and the advertisement watcher.
        /// </summary>
        public ValueTask<IDeviceOperationResult> Disconnect()
        {
            if (!IsRunning)
            {
                Notify.Verbose(nameof(WindowsBluetoothLEInterface), "Interface is not running.");
                return DeviceOperation.VResult<DeviceAlreadyDisconnectedResult>();
            }

            try
            {
                DisconnectAllDevices();
                StopWatcher();
                return DeviceOperation.VResult<DeviceDisconnectedSuccessfullyResult>();
            }
            catch (Exception exception)
            {
                Notify.Error(nameof(WindowsBluetoothLEInterface),
                    $"Cannot stop BLE Watcher due to exception: {exception}.");
                return DeviceOperation.VResult<DeviceDisconnectionFailedResult>();
            }
        }

        /// <summary>
        ///     Disconnects all currently connected devices and clears the device collections.
        ///     This method is called internally during the disconnect process.
        /// </summary>
        private void DisconnectAllDevices()
        {
            Notify.Verbose(nameof(WindowsBluetoothLEInterface), "Disconnecting all BLE devices.");
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

        /// <summary>
        ///     Stops the advertisement watcher and cleans up its event handlers.
        ///     This method is called internally during the disconnect process.
        /// </summary>
        private void StopWatcher()
        {
            _watcher.Received -= OnAdvertisementReceived;
            _watcher.Stopped -= OnWatcherStopped;
            _watcher.Stop();
            IsRunning = false;
        }

        /// <summary>
        ///     Event handler for when the advertisement watcher stops.
        ///     Triggers a disconnect operation to ensure proper cleanup.
        /// </summary>
        private async void OnWatcherStopped(
            BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            await Disconnect();
        }

        /// <summary>
        ///     Event handler for when a new advertisement is received.
        ///     Processes the advertisement and attempts to discover the device.
        /// </summary>
        private void OnAdvertisementReceived(
            BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementReceivedEventArgs args)
        {
            try
            {
                ProcessAdvertisement(args);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Error processing advertisement: {exception}");
            }
        }

        /// <summary>
        ///     Processes a received advertisement and attempts to discover the associated device.
        ///     Validates the device against the configured address and adds it to the discovered devices list.
        /// </summary>
        private async void ProcessAdvertisement(BluetoothLEAdvertisementReceivedEventArgs args)
        {
            lock (_devicesLock)
            {
                // Check if device is already discovered
                if (_discoveredDevices.Any(device => device.DeviceAddress == args.BluetoothAddress)) return;
                if (_devicesUnderProcessing.Any(device => device == args.BluetoothAddress)) return;

                _devicesUnderProcessing.Add(args.BluetoothAddress);
            }

            BluetoothLEDevice? device = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
            if (device == null)
            {
                _devicesUnderProcessing.Remove(args.BluetoothAddress);
                return;
            }

            Notify.Verbose(nameof(WindowsBluetoothLEInterface),
                $"Discovered BluetoothLE Device: {device.Name} ({device.BluetoothAddress:X})");
            WindowsBluetoothLEDevice bluetoothDevice = new(device);

            // Wait until device is configured
            await new WaitUntilBluetoothDeviceIsConfigured(bluetoothDevice, CancellationToken.None);

            // Ensure that device is valid for current address
            if (!DeviceBluetoothAddress.IsDeviceValid(bluetoothDevice))
            {
                Notify.Verbose(nameof(WindowsBluetoothLEInterface),
                    $"Device {bluetoothDevice.Name} ({bluetoothDevice.DeviceAddress:X}) is not valid for address {DeviceBluetoothAddress}");
                _devicesUnderProcessing.Remove(args.BluetoothAddress);
                return;
            }

            // Device has been discovered, proceed with events
            Notify.Success(nameof(WindowsBluetoothLEInterface),
                $"Discovered Valid BluetoothLE Device: {device.Name} ({device.BluetoothAddress:X})");

            // Swap processing to discovered
            lock (_devicesLock)
            {
                _discoveredDevices.Add(bluetoothDevice);
                _devicesUnderProcessing.Remove(args.BluetoothAddress);
            }

            device.ConnectionStatusChanged += OnDeviceConnectionStatusChanged;
            OnBluetoothDeviceDiscovered?.Invoke(this, bluetoothDevice);
        }

        /// <summary>
        ///     Handles changes in the connection status of a Bluetooth LE device.
        ///     When a device disconnects, it is removed from both connected and discovered device lists,
        ///     and appropriate cleanup is performed.
        /// </summary>
        /// <param name="sender">The Bluetooth LE device whose connection status changed</param>
        /// <param name="args">Event arguments (unused)</param>
        private void OnDeviceConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            Notify.Verbose(nameof(WindowsBluetoothLEInterface),
                $"Device {sender.Name} ({sender.BluetoothAddress:X}) status has changed to {sender.ConnectionStatus}");
            if (sender.ConnectionStatus != BluetoothConnectionStatus.Disconnected) return;

            IBluetoothLEDevice? disconnectedDevice = GetDeviceFromNative(sender);

            lock (_devicesLock)
            {
                _connectedDevices.RemoveAll(device => device.DeviceAddress == sender.BluetoothAddress);
                _discoveredDevices.RemoveAll(device => device.DeviceAddress == sender.BluetoothAddress);

                sender.ConnectionStatusChanged -= OnDeviceConnectionStatusChanged;
                sender.Dispose();
            }

            if (disconnectedDevice != null) OnBluetoothDeviceConnectionLost?.Invoke(this, disconnectedDevice);

            Notify.Verbose(nameof(WindowsBluetoothLEInterface),
                $"Lost connection with device {sender.Name} ({sender.BluetoothAddress:X})");
        }

        /// <summary>
        ///     Attempts to convert native device to IRIS one.
        /// </summary>
        /// <param name="sender">The Bluetooth LE device</param>
        private IBluetoothLEDevice? GetDeviceFromNative(BluetoothLEDevice sender)
        {
            IBluetoothLEDevice? device = null;
            lock (_devicesLock)
            {
                device = _discoveredDevices.FirstOrDefault(d
                    => d.DeviceAddress == sender.BluetoothAddress);
                if (device == null) return null;
            }

            return device;
        }

        /// <summary>
        ///     Claims a discovered Bluetooth LE device for use.
        ///     If no device is immediately available, waits for a device to be discovered.
        ///     The device must be configured before it can be claimed.
        /// </summary>
        /// <param name="deviceAddress">Address of exact device to find or null if any</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>The claimed Bluetooth LE device, or null if no device was available or the operation was cancelled.</returns>
        public async ValueTask<IBluetoothLEDevice?> ClaimDevice(
            IBluetoothLEAddress? deviceAddress = null,
            CancellationToken cancellationToken = default)
        {
            IBluetoothLEDevice? device;

            lock (_devicesLock)
            {
                device = _discoveredDevices.FirstOrDefault(d =>
                    !_connectedDevices.Contains(d) &&
                    (deviceAddress?.IsDeviceValid(d) ?? true));
            }

            while (device == null)
            {
                // Try to discover device if not found
                device ??= await new DiscoverNewBluetoothDevice(this, cancellationToken);
                if (device == null) continue;

                // Wait until valid device was discovered
                bool isDeviceValid = deviceAddress?.IsDeviceValid(device) ?? true;
                if (isDeviceValid) break;
            }

            // Wait until device is configured
            await new WaitUntilBluetoothDeviceIsConfigured(device, CancellationToken.None);

            // Register device
            RegisterDevice(device);
            return device;

            void RegisterDevice(IBluetoothLEDevice? deviceInstance)
            {
                if (deviceInstance == null) return;

                lock (_devicesLock)
                {
                    if (_connectedDevices.Contains(deviceInstance)) return;
                    _connectedDevices.Add(deviceInstance);
                }

                OnBluetoothDeviceConnected?.Invoke(this, deviceInstance);
                DeviceConnected?.Invoke(DeviceBluetoothAddress);

                Notify.Success(nameof(WindowsBluetoothLEInterface),
                    $"Claimed Device {deviceInstance.Name} ({deviceInstance.DeviceAddress:X})");

                device = deviceInstance;
            }
        }

        /// <summary>
        ///     Releases a previously claimed Bluetooth LE device.
        ///     Removes the device from the connected devices list and notifies listeners.
        /// </summary>
        /// <param name="deviceInstance">The Bluetooth LE device to release.</param>
        public ValueTask ReleaseDevice(IBluetoothLEDevice deviceInstance)
        {
            lock (_devicesLock)
            {
                if (!_connectedDevices.Contains(deviceInstance)) return ValueTask.CompletedTask;

                _connectedDevices.Remove(deviceInstance);
            }

            OnBluetoothDeviceDisconnected?.Invoke(this, deviceInstance);
            DeviceDisconnected?.Invoke(DeviceBluetoothAddress);

            Notify.Success(nameof(WindowsBluetoothLEInterface),
                $"Released Device {deviceInstance.Name} ({deviceInstance.DeviceAddress:X})");

            return ValueTask.CompletedTask;
        }

        /// <summary>
        ///     Event raised when a device is connected.
        ///     Maintained for API compatibility with legacy code.
        /// </summary>
        public event Delegates.DeviceConnectedHandler<IBluetoothLEAddress>? DeviceConnected;

        /// <summary>
        ///     Event raised when a device is disconnected.
        ///     Maintained for API compatibility with legacy code.
        /// </summary>
        public event Delegates.DeviceDisconnectedHandler<IBluetoothLEAddress>? DeviceDisconnected;

        /// <summary>
        ///     Event raised when a device connection is unexpectedly lost.
        ///     Maintained for API compatibility with legacy code.
        /// </summary>
        public event Delegates.DeviceConnectionLostHandler<IBluetoothLEAddress>? DeviceConnectionLost;
    }
}